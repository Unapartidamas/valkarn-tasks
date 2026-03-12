---
sidebar_position: 2
title: ValkarnTask<T>
---

# `ValkarnTask<T>`

`ValkarnTask<T>` es el tipo de tarea async que devuelve valores en Valkarn Tasks. Es un `readonly struct`, que lleva un resultado en línea (cuando se completa sincrónicamente) o una referencia a un objeto fuente agrupado (cuando se completa asincrónicamente).

**Espacio de nombres:** `UnaPartidaMas.Valkarn.Tasks`

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncValkarnTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
```

No hay restricciones genéricas en `T`. Cualquier tipo — tipo de valor, tipo de referencia, struct o clase — es válido.

---

## Crear instancias

### Tareas completadas sincrónicamente

Estos métodos de fábrica devuelven un `ValkarnTask<T>` sin objeto fuente de respaldo. Cero asignaciones.

#### `ValkarnTask.FromResult<T>(T value)`

Devuelve un `ValkarnTask<T>` completado que lleva `value` en línea. Declarado como método estático en el tipo `ValkarnTask` no genérico.

```csharp
public static ValkarnTask<T> FromResult<T>(T value)
```

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(42);
ValkarnTask<string> name = ValkarnTask.FromResult("Valkarn");
ValkarnTask<Vector3> pos = ValkarnTask.FromResult(transform.position);
```

El struct devuelto tiene `source == null`. Esperarlo no incurre en asignación de continuación — el compilador ve `IsCompleted == true` inmediatamente.

#### `ValkarnTask.FromException<T>(Exception exception)`

Devuelve un `ValkarnTask<T>` fallido. Esperarlo relanza la excepción con su traza de pila original preservada vía `ExceptionDispatchInfo`.

```csharp
public static ValkarnTask<T> FromException<T>(Exception exception)
```

```csharp
ValkarnTask<Texture2D> LoadTexture(string path)
{
    if (string.IsNullOrEmpty(path))
        return ValkarnTask.FromException<Texture2D>(
            new ArgumentException("La ruta no debe estar vacía.", nameof(path)));

    return LoadTextureAsync(path);
}
```

#### `ValkarnTask.FromCanceled<T>(CancellationToken ct = default)`

Devuelve un `ValkarnTask<T>` cancelado. Esperarlo lanza `OperationCanceledException`.

```csharp
public static ValkarnTask<T> FromCanceled<T>(CancellationToken ct = default)
```

```csharp
ValkarnTask<byte[]> Download(string url, CancellationToken ct)
{
    if (ct.IsCancellationRequested)
        return ValkarnTask.FromCanceled<byte[]>(ct);

    return DownloadAsync(url, ct);
}
```

### Vía métodos `async`

Cualquier método `async` declarado para devolver `ValkarnTask<T>` usa `AsyncValkarnTaskMethodBuilder<TResult>` automáticamente:

```csharp
async ValkarnTask<int> ComputeAsync()
{
    await ValkarnTask.Yield();
    return 42;
}
```

El compilador genera una máquina de estados. Si el método se completa sincrónicamente (nunca se suspende), `AsyncValkarnTaskMethodBuilder<T>.Task` devuelve `new ValkarnTask<T>(result)` con `source == null` — cero asignaciones.

---

## Ejecutar trabajo en el grupo de hilos

Estos métodos ejecutan un delegado en el grupo de hilos de .NET y devuelven el resultado en el hilo principal (en el `PlayerLoopTiming` especificado). Son envoltorios de conveniencia sobre las variantes con nombre más largo `RunOnThreadPool`.

#### `ValkarnTask.Run<T>(Func<T> func, PlayerLoopTiming timing, CancellationToken ct)`

Ejecuta un `Func<T>` síncrono en el grupo de hilos.

```csharp
public static ValkarnTask<T> Run<T>(
    Func<T> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
// Calcular en el grupo de hilos, resultado llega de vuelta en el hilo principal en el próximo Update
int hash = await ValkarnTask.Run(() => ComputeExpensiveHash(data));
```

#### `ValkarnTask.Run<T>(Func<ValkarnTask<T>> func, PlayerLoopTiming timing, CancellationToken ct)`

Ejecuta un `Func<ValkarnTask<T>>` async en el grupo de hilos. Úsalo cuando el trabajo en sí es async (por ejemplo, E/S de archivos).

```csharp
public static ValkarnTask<T> Run<T>(
    Func<ValkarnTask<T>> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
string json = await ValkarnTask.Run(async () =>
{
    using var reader = File.OpenText("data.json");
    return await reader.ReadToEndAsync();
});
```

Ambas sobrecargas `Run` cancelan temprano si el token ya está cancelado, y cambian de vuelta al hilo principal en `timing` después de que el trabajo se completa.

---

## Miembros de instancia

### `IsCompleted`

```csharp
public bool IsCompleted { get; }
```

Devuelve `true` si la tarea se ha completado en cualquier estado terminal (Succeeded, Faulted o Canceled). Para tareas completadas sincrónicamente (`source == null`), siempre devuelve `true` sin despacho de interfaz.

```csharp
var task = SomeLongOperation();
if (task.IsCompleted)
{
    int result = task.GetAwaiter().GetResult();
    Use(result);
}
```

### `GetStatus()`

```csharp
public ValkarnTask.Status GetStatus()
```

Devuelve el `ValkarnTask.Status` actual. Valores posibles: `Pending`, `Succeeded`, `Faulted`, `Canceled`. Para `source == null`, siempre devuelve `Succeeded`.

### `GetAwaiter()`

```csharp
public Awaiter GetAwaiter()
```

Devuelve un struct `Awaiter`. Usado por el compilador para implementar `await`. También puedes llamarlo directamente para obtener el resultado sincrónicamente (solo seguro cuando `IsCompleted` es verdadero).

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(10);
int value = task.GetAwaiter().GetResult(); // seguro — completado sincrónicamente
```

Llamar a `GetResult()` en una tarea pendiente lanza una `InvalidOperationException`.

### `AsNonGeneric()`

```csharp
public ValkarnTask AsNonGeneric()
```

Convierte este `ValkarnTask<T>` a un `ValkarnTask` no genérico, descartando el tipo de resultado. La tarea resultante comparte el mismo source subyacente y token, por lo que se completa al mismo tiempo.

```csharp
ValkarnTask<int> typedTask = ComputeAsync();
ValkarnTask voidTask = typedTask.AsNonGeneric();
await voidTask;   // espera la completación, ignora el resultado
```

Esto es útil cuando se pasan tareas de tipos mixtos a combinadores o cuando solo te importa el timing de completado, no el valor.

---

## Combinadores que devuelven `ValkarnTask<T>`

### `WhenAll` — sobrecarga tipada de dos tareas

```csharp
public static ValkarnTask<(T1, T2)> WhenAll<T1, T2>(
    ValkarnTask<T1> task1, ValkarnTask<T2> task2)
```

Ejecuta ambas tareas concurrentemente y devuelve una tupla de resultados. Si cualquier tarea falla o se cancela, la primera excepción gana y el error de la otra se reporta a través de `ValkarnTask.UnobservedException`.

**La desestructuración de tuplas** funciona naturalmente con la deconstrucción de C#:

```csharp
var (profile, inventory) = await ValkarnTask.WhenAll(
    FetchProfileAsync(userId),
    FetchInventoryAsync(userId)
);
```

**Camino rápido de cero asignaciones:** si ambas tareas están completadas sincrónicamente en el punto de llamada, no se crea ningún objeto agrupado y la tupla de resultado se devuelve en línea.

### `WhenAll` — sobrecarga de colección tipada

```csharp
public static ValkarnTask<T[]> WhenAll<T>(IEnumerable<ValkarnTask<T>> tasks)
```

Espera todas las tareas en la colección concurrentemente y devuelve un `T[]` en orden de índice.

```csharp
var urls = new[] { "https://a.com", "https://b.com", "https://c.com" };
ValkarnTask<string>[] downloads = urls.Select(u => DownloadAsync(u)).ToArray();
string[] results = await ValkarnTask.WhenAll(downloads);
```

Si la colección está vacía, devuelve `ValkarnTask.FromResult(Array.Empty<T>())` — cero asignaciones. Si todas las tareas ya están completadas sincrónicamente, se construye un array de resultado en línea sin crear una promesa de combinador.

### `WhenAny` — sobrecarga tipada de dos tareas

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    ValkarnTask<T> task1, ValkarnTask<T> task2)
```

Retorna tan pronto como cualquiera de las tareas se completa. La tupla de resultado contiene el índice basado en 0 del ganador y su valor. Las tareas perdedoras siguen ejecutándose; sus errores (si los hay) se reportan a través de `ValkarnTask.UnobservedException`. Las cancelaciones de los perdedores intencionalmente no se reportan.

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(
    FetchFromCacheAsync(key),
    FetchFromNetworkAsync(key)
);

if (winnerIndex == 0)
    Debug.Log("El caché ganó");
```

### `WhenAny` — sobrecarga de colección tipada

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    IEnumerable<ValkarnTask<T>> tasks)
```

Misma semántica que la sobrecarga de dos tareas, extendida a cualquier número de tareas. Requiere al menos una tarea; lanza `ArgumentException` para colecciones vacías.

```csharp
var tasks = servers.Select(s => s.FetchAsync(query)).ToArray();
var (winnerIndex, data) = await ValkarnTask.WhenAny(tasks);
Debug.Log($"El servidor {winnerIndex} respondió primero");
```

---

## Métodos de fábrica de conveniencia

### `ValkarnTask.Create<T>(Func<ValkarnTask<T>> factory)`

Invoca el delegado de fábrica y espera la tarea resultante. Útil cuando quieres diferir la construcción de una operación async.

```csharp
public static async ValkarnTask<T> Create<T>(Func<ValkarnTask<T>> factory)
```

```csharp
var result = await ValkarnTask.Create(() => LoadLevelDataAsync(levelId));
```

---

## Struct `Awaiter` (anidado)

`ValkarnTask<T>.Awaiter` es el awaiter orientado al compilador. Es un `readonly struct` que implementa `ICriticalNotifyCompletion`. Normalmente no interactúas con él directamente.

```csharp
public readonly struct Awaiter : ICriticalNotifyCompletion
{
    public bool IsCompleted { get; }
    public T GetResult();
    public void OnCompleted(Action continuation);
    public void UnsafeOnCompleted(Action continuation);
}
```

`UnsafeOnCompleted` es el camino usado por `AsyncValkarnTaskMethodBuilder<T>`. La etiqueta "unsafe" significa que `ExecutionContext` no se captura — esto es intencional para Unity donde no hay `SynchronizationContext` en efecto.

Cuando `IsCompleted` es verdadero, llamar a `GetResult()` lee el campo `result` en línea (para tareas síncronas) o llama a través de la interfaz fuente (para tareas asíncronas). Ambos caminos son `[MethodImpl(MethodImplOptions.AggressiveInlining)]`.

---

## Métodos de extensión en `ValkarnTask<T>`

### `AsResult<T>()`

```csharp
public static ValkarnTask<Result<T>> AsResult<T>(this ValkarnTask<T> task)
```

Envuelve un `ValkarnTask<T>` en un `ValkarnTask<Result<T>>`, capturando cualquier excepción o cancelación y codificándola en el valor `Result<T>`. Esto evita try/catch en el punto de llamada.

```csharp
Result<string> result = await FetchDataAsync(url).AsResult();

if (result.IsSuccess)
    Process(result.Value);
else if (result.IsCanceled)
    Debug.Log("Cancelado");
else
    Debug.LogError(result.Exception);
```

**Camino rápido síncrono:** si la tarea fuente ya está completada sincrónicamente, `AsResult` devuelve sincrónicamente sin maquinaria async involucrada.

---

## `Promise<T>` — fuente de completado manual

`ValkarnTask.Promise<T>` es una fuente de completado manual asignada en el montón para casos donde necesitas controlar cuándo se completa un `ValkarnTask<T>`, y el tiempo de vida no está acotado por una única operación async.

```csharp
public class Promise<T>
{
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

```csharp
// Envolver una API basada en callbacks
var promise = new ValkarnTask.Promise<string>();

SomeCallbackApi.OnComplete += value => promise.TrySetResult(value);
SomeCallbackApi.OnError   += ex    => promise.TrySetException(ex);

string result = await promise.Task;
```

A diferencia de `PooledPromise<T>`, `Promise<T>` no está agrupado. Usa un finalizador para detectar y reportar excepciones no observadas si la tarea falla y el llamador nunca la espera.

Para patrones de alta frecuencia (bucles productor/consumidor, operaciones por fotograma), prefiere `ValkarnTask.PooledPromise<T>`, que vuelve automáticamente al grupo después de que se llama a `GetResult`.

---

## `PooledPromise<T>` — fuente de completado manual agrupada

```csharp
public sealed class PooledPromise<T> : ValkarnTask.ISource<T>, IPoolNode<PooledPromise<T>>
{
    public static PooledPromise<T> Create(out uint token);
    public static PooledPromise<T> CreateCompleted(T result, out uint token);
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

Después de que `GetResult` se llama en la tarea de respaldo, la promesa reinicia su `ValkarnTaskCompletionCore<T>` y se devuelve al grupo. Una protección de doble retorno asegura que esto ocurra como máximo una vez incluso si `GetResult` se llama concurrentemente.

```csharp
// Patrón: producir un ValkarnTask<T> que se completa cuando esté listo
var promise = ValkarnTask.PooledPromise<int>.Create(out uint token);
ValkarnTask<int> task = promise.Task;

// Despachar trabajo de forma asíncrona
ThreadPool.QueueUserWorkItem(_ =>
{
    int result = DoWork();
    promise.TrySetResult(result);
});

// El consumidor espera; al completarse la promesa vuelve al grupo automáticamente
int value = await task;
```

---

## Resumen de formas de obtener un `ValkarnTask<T>`

| Método | Cuándo usarlo |
|--------|--------------|
| `return value` dentro de `async ValkarnTask<T>` | Métodos async normales |
| `ValkarnTask.FromResult(value)` | Retornos rápidos síncronos |
| `ValkarnTask.FromException<T>(ex)` | Tareas pre-fallidas |
| `ValkarnTask.FromCanceled<T>(ct)` | Tareas pre-canceladas |
| `ValkarnTask.Run<T>(Func<T>, ...)` | Descarga al grupo de hilos |
| `ValkarnTask.Run<T>(Func<ValkarnTask<T>>, ...)` | Trabajo async en grupo de hilos |
| `ValkarnTask.WhenAll<T1,T2>(t1, t2)` | Esperar dos tareas tipadas, obtener tupla |
| `ValkarnTask.WhenAll<T>(IEnumerable<...>)` | Esperar N tareas tipadas, obtener array |
| `ValkarnTask.WhenAny<T>(t1, t2)` | Primera de dos tareas tipadas |
| `ValkarnTask.WhenAny<T>(IEnumerable<...>)` | Primera de N tareas tipadas |
| `task.AsResult<T>()` | Envolvimiento seguro ante excepciones |
| `new ValkarnTask.Promise<T>()` → `.Task` | Completado manual de larga duración |
| `ValkarnTask.PooledPromise<T>.Create(...)` → `.Task` | Completado manual de alta frecuencia |
