---
sidebar_position: 1
title: Tareas Struct
---

# Tareas Struct

`ValkarnTask` y `ValkarnTask<T>` son los tipos de retorno async principales en Valkarn Tasks. A diferencia de `System.Threading.Tasks.Task`, que es un tipo por referencia que siempre se asigna en el montón, ambos tipos de tarea de Valkarn son valores `readonly struct`. Esta página explica qué significa eso en la práctica, cómo funciona el camino rápido de cero asignaciones y cómo el compilador se integra con la maquinaria async/await.

---

## ¿Por qué un `readonly struct`?

Una tarea basada en clase como `Task<T>` debe asignarse en el montón cada vez que se llama a un método async, incluso para métodos que se completan sincrónicamente. En un bucle de juego de Unity que corre a 60 fps, cientos de pequeñas operaciones async por fotograma pueden acumular una presión de GC considerable.

`ValkarnTask` y `ValkarnTask<T>` se declaran como `readonly partial struct`:

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncValkarnTaskMethodBuilder))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct ValkarnTask
{
    internal readonly ISource source;
    internal readonly uint token;
}
```

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncValkarnTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
{
    internal readonly ValkarnTask.ISource<T> source;
    internal readonly T result;
    internal readonly uint token;
}
```

Ser un struct significa que el valor de la tarea en sí vive en la pila (o en línea en su objeto padre) en lugar del montón. El modificador `readonly` asegura que el compilador pueda razonar sobre la inmutabilidad y evita errores accidentales de copia. `StructLayout.Auto` permite que el runtime optimice el orden de los campos para la plataforma de destino.

### El invariante clave: `source == null`

El diseño está construido alrededor de un único invariante:

> Cuando `source` es `null`, la tarea se completó sincrónicamente sin error. No hay ningún objeto en el montón involucrado.

`ValkarnTask.CompletedTask` es `default(ValkarnTask)` — su campo `source` es nulo, por lo que no cuesta nada. `ValkarnTask<T>` lleva su resultado en línea en el campo `result`, lo que también hace de `ValkarnTask.FromResult(value)` una llamada de cero asignaciones:

```csharp
// Cero asignaciones — source es null, resultado almacenado en línea
ValkarnTask<int> task = ValkarnTask.FromResult(42);

// También cero asignaciones — source es null
ValkarnTask done = ValkarnTask.CompletedTask;
```

---

## El camino rápido de cero asignaciones

Cuando un método `async` se completa sin suspenderse nunca (ningún `await` cede a una operación incompleta), el método completo se ejecuta sincrónicamente en el hilo que lo llama. El constructor detecta esto y devuelve una tarea con `source == null`.

El awaiter lo verifica inmediatamente:

```csharp
public bool IsCompleted
{
    get
    {
        var s = task.source;
        return s == null || s.GetStatus(task.token).IsCompleted();
    }
}
```

Cuando `IsCompleted` es verdadero antes de que `OnCompleted` sea llamado, la máquina de estados no registra una continuación. `GetResult` se llama inmediatamente, y para `ValkarnTask<T>` con `source == null`, el resultado se lee del campo `result` en línea del struct:

```csharp
public T GetResult()
{
    var s = task.source;
    if (s == null)
        return task.result;   // en línea, sin llamada a ISource
    return s.GetResult(task.token);
}
```

No se crea ningún objeto, no se produce despacho de interfaz, y no se asigna ningún delegado de continuación. Todo el await se resuelve como una lectura directa de valor.

### Cuando se necesita un source

Si un método async se suspende (espera algo que aún no está completo), el constructor asigna un objeto `AsyncValkarnTaskRunner<TStateMachine>` agrupado (o `AsyncValkarnTaskRunner<TStateMachine, TResult>` para la variante genérica). Este objeto sirve doble propósito: contiene la máquina de estados generada por el compilador por valor e implementa `ValkarnTask.ISource`, por lo que puede usarse directamente como el source de respaldo de la tarea. La tarea devuelta a los llamadores envuelve este runner junto con un `uint` token generacional.

Al completarse, cuando el llamador llama a `GetResult` en el awaiter, el runner se reinicia y vuelve a su grupo — por lo que la asignación se amortiza a través de muchas invocaciones de métodos.

---

## La interfaz `ISource`

El contrato entre un struct `ValkarnTask` y su objeto de respaldo asíncrono es `ValkarnTask.ISource`:

```csharp
public interface ISource
{
    ValkarnTask.Status GetStatus(uint token);
    void GetResult(uint token);
    void OnCompleted(Action<object> continuation, object state, uint token);
    ValkarnTask.Status UnsafeGetStatus();
}

public interface ISource<out T> : ISource
{
    new T GetResult(uint token);
}
```

Cualquier objeto que implemente `ISource` puede respaldar un `ValkarnTask`. La biblioteca incluye varias implementaciones:

| Tipo | Propósito |
|------|-----------|
| `AsyncValkarnTaskRunner<TStateMachine>` | Respalda cada método `async ValkarnTask` (interno) |
| `AsyncValkarnTaskRunner<TStateMachine, TResult>` | Respalda cada método `async ValkarnTask<T>` (interno) |
| `ValkarnTask.PooledPromise` | Fuente de completado manual con retorno automático al grupo |
| `ValkarnTask.PooledPromise<T>` | Variante genérica de lo anterior |
| `ValkarnTask.Promise` | Fuente de completado manual sin agrupamiento (operaciones de larga duración) |
| `ValkarnTask.Promise<T>` | Variante genérica de lo anterior |

El parámetro `uint token` es una protección generacional. Cuando un source agrupado se reinicia para reutilización, su contador de generación se incrementa. Cualquier struct `ValkarnTask` que contenga el token antiguo recibirá inmediatamente una `InvalidOperationException` en lugar de leer silenciosamente el estado reciclado.

---

## `ValkarnTask` vs `ValkarnTask<T>`

| Característica | `ValkarnTask` | `ValkarnTask<T>` |
|----------------|-----------|-------------|
| Valor de retorno | Ninguno (equivalente a void) | `T` |
| Almacenamiento de resultado en línea | Sin campo `result` | Campo `result` (tipo `T`) |
| `GetResult` del awaiter | `void` | Devuelve `T` |
| Tipo de constructor | `AsyncValkarnTaskMethodBuilder` | `AsyncValkarnTaskMethodBuilder<TResult>` |
| Valor completado sincrónicamente | `ValkarnTask.CompletedTask` | `ValkarnTask.FromResult(value)` |
| Convertir a no genérico | No aplica | `.AsNonGeneric()` |

Usa `ValkarnTask` cuando un método async no tiene un valor de retorno significativo, y `ValkarnTask<T>` cuando produce un resultado. Siempre puedes convertir un `ValkarnTask<T>` a un `ValkarnTask` vía `AsNonGeneric()` cuando necesitas mezclar tareas tipadas y no tipadas en combinadores como `WhenAll`.

---

## Cómo funciona el constructor de métodos async

El compilador de C# busca el tipo nombrado en `[AsyncMethodBuilder(...)]` en el tipo de retorno. Para `ValkarnTask`, ese es `AsyncValkarnTaskMethodBuilder`. Para `ValkarnTask<T>`, es `AsyncValkarnTaskMethodBuilder<TResult>`.

El constructor en sí mismo es un struct para evitar una asignación en el montón solo para el objeto constructor. Tiene dos campos (tres para la variante genérica):

```csharp
public struct AsyncValkarnTaskMethodBuilder
{
    IStateMachineRunnerPromise runner;   // null hasta la primera suspensión
    Exception syncException;            // solo se establece en el camino de fallo síncrono
}

public struct AsyncValkarnTaskMethodBuilder<TResult>
{
    IStateMachineRunnerPromise<TResult> runner;
    Exception syncException;
    TResult result;                     // solo se establece en el camino de éxito síncrono
}
```

### Ciclo de vida del constructor

El compilador llama a estos métodos en orden:

**1. `Create()`** — devuelve un constructor predeterminado (todos los campos nulos/predeterminados). Sin asignación.

**2. `Start(ref stateMachine)`** — llama a `stateMachine.MoveNext()` sincrónicamente. Si el método se completa sin alcanzar un `await` incompleto, se llama a `SetResult`/`SetException` y `runner` permanece nulo.

**3. `AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine)`** — se llama cuando el método encuentra un `await` incompleto. Si `runner` es nulo (primera suspensión), alquila o crea un `AsyncValkarnTaskRunner` y copia la máquina de estados en él. Luego llama a `awaiter.UnsafeOnCompleted(runner.MoveNextAction)` para registrar la continuación de la máquina de estados.

**4. `SetResult()` / `SetException(exception)`** — señala la completación al `ValkarnTaskCompletionCore` del runner, que despierta cualquier awaiter registrado.

**5. Propiedad `Task`** — verificada por el llamador para obtener el valor `ValkarnTask`. En el camino de éxito síncrono (`runner == null && syncException == null`), devuelve `default` (o `new ValkarnTask<T>(result)` para la variante genérica) — cero asignaciones. En el camino asíncrono, envuelve el runner como el source.

La optimización crítica es que `runner` se asigna de forma diferida. Si un método se completa sincrónicamente (el caso común para aciertos de caché, guardas, retornos tempranos), nunca se alquila ningún objeto agrupado.

---

## Estados de `ValkarnTaskStatus`

El estado se representa mediante un enum de tamaño `byte` anidado dentro de `ValkarnTask`:

```csharp
public enum Status : byte
{
    Pending   = 0,   // aún no completado
    Succeeded = 1,   // completado normalmente
    Faulted   = 2,   // completado con una excepción no manejada
    Canceled  = 3    // completado vía OperationCanceledException
}
```

Puedes verificar el estado directamente:

```csharp
ValkarnTask task = SomeOperation();
ValkarnTask.Status status = task.GetStatus();

switch (status)
{
    case ValkarnTask.Status.Pending:
        // Aún en ejecución — no se puede llamar a GetResult
        break;
    case ValkarnTask.Status.Succeeded:
        // Completado normalmente
        break;
    case ValkarnTask.Status.Faulted:
        // Completado con excepción — GetResult relanzará
        break;
    case ValkarnTask.Status.Canceled:
        // Completado con OperationCanceledException
        break;
}
```

Para el camino rápido completado sincrónicamente (donde `source == null`), `GetStatus()` devuelve `Succeeded` sin ninguna llamada de interfaz:

```csharp
public Status GetStatus()
{
    if (source == null) return Status.Succeeded;
    return source.GetStatus(token);
}
```

La propiedad `IsCompleted` sigue el mismo patrón y devuelve `true` para cualquier estado que no sea `Pending`.

---

## Implicaciones de IL2CPP

IL2CPP compila C# a C++ antes de construir código nativo. Los tipos de valor genéricos — incluyendo los structs — están completamente especializados en el código generado, lo que tiene consecuencias importantes para esta biblioteca.

**Especialización de máquinas de estado.** El compilador genera una máquina de estado struct única por método async. `AsyncValkarnTaskRunner<TStateMachine>` es por lo tanto también único por método async, y `ValkarnTaskPool<AsyncValkarnTaskRunner<TStateMachine>>` es un grupo separado por método. Esto es en realidad beneficioso: el grupo nunca se comparte entre tipos incompatibles, eliminando cualquier riesgo de confusión de tipos.

**Sin boxing de la máquina de estados.** La máquina de estados se almacena por valor dentro del objeto runner, sin boxing. IL2CPP maneja esto correctamente porque el runner es una `sealed class` con un campo `TStateMachine` concreto.

**Protección contra eliminación.** El atributo `[AsyncMethodBuilder]` mantiene vivos los tipos del constructor. Sin embargo, si usas `ValkarnTask.ISource` a través de una referencia de interfaz en IL2CPP con eliminación agresiva, agrega una entrada `link.xml` que preserve el ensamblado `UnaPartidaMas.Valkarn.Tasks`:

```xml
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```

**`ICriticalNotifyCompletion`.** Los structs awaiter implementan `ICriticalNotifyCompletion`, que le dice al compilador que llame a `UnsafeOnCompleted` en lugar de `OnCompleted`. La variante "unsafe" omite intencionalmente la captura de `ExecutionContext`. Esto es correcto para Unity — no hay `SynchronizationContext` en la configuración predeterminada de Unity, y capturar uno añadiría sobrecarga sin beneficio. Bajo IL2CPP, esto también evita la sobrecarga del camino `ExecutionContext.Run` que `Task` estándar siempre paga.

---

## Ejemplos prácticos

### Retorno temprano sin asignación

```csharp
// async ValkarnTask<int> que se completa sincrónicamente en el camino caliente
async ValkarnTask<int> GetCachedValue(string key)
{
    if (_cache.TryGetValue(key, out var value))
        return value;            // el compilador llama a SetResult(value); source permanece null

    var result = await FetchFromDatabaseAsync(key);
    _cache[key] = result;
    return result;
}
```

Cuando el valor está en caché, el método nunca se suspende. El `ValkarnTask<int>` devuelto tiene `source == null` y lleva el resultado en línea. No ocurre ninguna asignación en el montón en este camino.

### Verificar IsCompleted antes de esperar

```csharp
ValkarnTask<Texture2D> loadTask = LoadTextureAsync("sprites/hero.png");

if (loadTask.IsCompleted)
{
    // Ya terminado — GetAwaiter().GetResult() lee el resultado en línea sin llamada a ISource
    Texture2D tex = loadTask.GetAwaiter().GetResult();
    ApplyTexture(tex);
}
else
{
    // Genuinamente asíncrono — registrar continuación
    ApplyTextureAsync(loadTask).Forget();
}
```

### Observar excepciones no manejadas

Las tareas fallidas que nunca se esperan (patrones fire-and-forget) reportan sus excepciones a través del evento `ValkarnTask.UnobservedException`. Esto se genera determinísticamente en el momento de retorno al grupo para sources agrupados, o desde el finalizador para tareas respaldadas por `Promise`.

```csharp
ValkarnTask.UnobservedException += ex =>
{
    Debug.LogError($"[ValkarnTask] No observado: {ex}");
};
```

El evento es seguro para hilos; los manejadores pueden añadirse o eliminarse desde cualquier hilo usando un bucle compare-exchange sin bloqueo.
