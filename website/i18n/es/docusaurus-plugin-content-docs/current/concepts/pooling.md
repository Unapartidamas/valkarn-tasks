---
sidebar_position: 2
title: Agrupamiento de Objetos
---

# Agrupamiento de Objetos

Valkarn Tasks elimina las asignaciones de GC en los caminos async comunes agrupando los objetos que respaldan cada `ValkarnTask`. Esta página explica la arquitectura del grupo — cómo se almacenan los objetos, cómo se adquieren y devuelven, y qué garantías de ciclo de vida proporciona el sistema.

---

## Descripción general

Cuando un método `async` se suspende, la biblioteca necesita un lugar para almacenar la máquina de estados generada por el compilador y un mecanismo de completado al que el awaiter pueda suscribirse. En `System.Threading.Tasks`, este es el propio objeto `Task` — una asignación en el montón por llamada. En Valkarn Tasks, este rol lo juegan objetos agrupados que implementan `ValkarnTask.ISource`.

El diseño del grupo tiene tres objetivos:

1. **Cero operaciones atómicas en el camino caliente del hilo principal.** El bucle de juego de Unity es de un solo hilo por convención. El alquiler y retorno desde el hilo principal deben ser lecturas y escrituras simples.
2. **Acceso seguro entre hilos.** Las tareas en segundo plano que usan `ValkarnTask.Run` operan en hilos del grupo de hilos. El grupo debe manejar el alquiler/retorno concurrente correctamente.
3. **Crecimiento acotado con recorte adaptativo.** Los grupos no deben crecer sin límite después de un pico de tráfico, pero no deben reducirse tan agresivamente que re-asignen constantemente.

---

## `ValkarnTaskPool<T>`

`ValkarnTaskPool<T>` es la clase principal del grupo. Es `internal sealed` — no interactúas con ella directamente, pero entenderla explica dónde van tus asignaciones.

```
ValkarnTaskPool<T>
  |
  +-- fastItem: T            (caché de una ranura, solo hilo principal, lectura/escritura simple)
  |
  +-- stackHead: T           (cabeza de la pila Treiber, basada en CAS para seguridad entre hilos)
  +-- stackSize: int
  |
  +-- maxSize: int           (acotado por ValkarnTask.DefaultMaxPoolSize)
  +-- totalCreated: int      (rastrea asignaciones de por vida para la ratio de recorte)
```

### Ranura rápida (hilo principal)

El campo `fastItem` es una única ranura reservada para el objeto devuelto más recientemente. En el hilo principal, el alquiler y retorno son una lectura y escritura simples — sin operaciones atómicas, sin spinning. Esto cubre la gran mayoría de las operaciones del bucle de juego de Unity.

```
Alquiler (hilo principal):
  fastItem != null  →  tomarlo (fastItem = null), devolverlo   [cero operaciones atómicas]
  fastItem == null  →  continuar a la pila Treiber

Retorno (hilo principal):
  fastItem == null  →  fastItem = item                        [cero operaciones atómicas]
  fastItem != null  →  continuar a la pila Treiber
```

### Pila Treiber (desbordamiento / hilos en segundo plano)

Cuando la ranura rápida está ocupada (o cuando el hilo que llama no es el hilo principal), el grupo usa una pila Treiber sin bloqueo — una lista enlazada intrusiva clásica usando compare-and-swap (CAS):

```
Alquiler (cualquier hilo):
  while (true):
    head = Volatile.Read(stackHead)
    if head == null: return null (grupo vacío)
    next = head.NextNode
    if CAS(stackHead, next, head) == head: return head  // ganó la carrera
    spinner.SpinOnce()                                   // perdió, reintentar

Retorno (cualquier hilo):
  if stackSize >= maxSize: return false (grupo lleno, descartar elemento)
  while (true):
    head = Volatile.Read(stackHead)
    item.NextNode = head
    if CAS(stackHead, item, head) == head: stackSize++; return true
    spinner.SpinOnce()
```

La pila es intrusiva: cada objeto agrupado almacena su propio puntero `NextNode`, por lo que no se necesita ningún nodo envolvente externo. Esto es aplicado por la interfaz `IPoolNode<T>`.

### Enrutamiento de hilos

```csharp
internal static class ValkarnTaskPoolShared
{
    internal static volatile int MainThreadId;
}
```

Todas las instancias del grupo comparten un único `MainThreadId`. Las operaciones de alquiler/retorno verifican `Thread.CurrentThread.ManagedThreadId == MainThreadId` para enrutar al camino correcto. El campo `volatile` asegura la visibilidad entre hilos después de que el ID se publique al inicio.

---

## `IPoolNode<T>`

Cualquier tipo que participe en el grupo debe implementar esta interfaz:

```csharp
internal interface IPoolNode<T> where T : class
{
    ref T NextNode { get; }
}
```

`ref T NextNode` devuelve una referencia al campo dentro del objeto que almacena el siguiente puntero. El grupo escribe directamente en este campo a través de la ref, eliminando cualquier nodo envolvente separado. Todos los tipos agrupados en la biblioteca — runners, promesas, combinadores — implementan esta interfaz declarando un campo privado y exponiéndolo:

```csharp
// Ejemplo de PooledPromise<T>
PooledPromise<T> nextNode;
public ref PooledPromise<T> NextNode => ref nextNode;
```

---

## Ciclo de vida del grupo: adquirir, usar, devolver

El ciclo de vida completo de un objeto agrupado es:

```
El llamador invoca el método async
  |
  +--> AsyncValkarnTaskMethodBuilder.AwaitUnsafeOnCompleted()
        |
        +--> runner == null? SÍ --> Pool.TryRent() ?? Pool.TrackNew(new Runner())
              |
              +--> runner almacenado en el constructor; máquina de estados copiada al runner
              |
              +--> awaiter.UnsafeOnCompleted(runner.MoveNextAction)
              |
              [... trabajo asíncrono continúa ...]
              |
              +--> runner.SetResult() / runner.SetException()
                    |
                    +--> ValkarnTaskCompletionCore.TrySetResult() --> continuación invocada
                          |
                          +--> el awaiter del llamador llama a GetResult(token)
                                |
                                +--> core.GetResult(token) -- lee resultado o relanza
                                |
                                +--> TryReturn():
                                      stateMachine = default
                                      core.Reset()           // incrementa generación
                                      Pool.TryReturn(this)
```

El método `TryReturn` siempre borra la máquina de estados antes de llamar a `core.Reset()`. Este orden importa: `Reset()` incrementa el contador de generación, haciendo que la ranura sea visible como disponible para los arrendatarios concurrentes. Si la máquina de estados se borrara después de `Reset()`, un arrendatario en otro hilo podría obtener la ranura y tener su máquina de estados sobreescrita.

---

## `ValkarnTaskCompletionCore<TResult>`

`ValkarnTaskCompletionCore<TResult>` es un `struct` interno incrustado dentro de cada objeto agrupado. Es la máquina de estados real para la promesa — rastreando el estado de completado, almacenando resultados y errores, y resolviendo la carrera entre `OnCompleted` (registrar una continuación) y `TrySetResult` (señalar el completado).

```
Campos:
  result:          TResult     -- el valor de éxito
  error:           object      -- ExceptionDispatchInfo o OperationCanceledException
  errorKind:       byte        -- 0=ninguno, 1=fallido, 2=cancelado(OCE), 3=cancelado(EDI)
  generation:      int         -- monotónicamente creciente; convertido a uint para comparación de token
  completedCount:  int         -- 0=pendiente, 1=reclamado, 2=completado (publicación en dos fases)
  continuation:    Action<object>
  continuationState: object
  hasUnhandledError: bool
```

### Protocolo de completado en dos fases

El completado usa un CAS en dos fases para ser seguro en ARM64 donde se necesitan pares store-release / load-acquire:

```
TrySetResult(value):
  Fase 1: CAS(completedCount, 0 -> 1)   -- reclamar propiedad exclusiva
  Fase 2: escribir resultado
  Fase 3: Volatile.Write(completedCount, 2)  -- publicar con semántica de liberación
  Fase 4: InvokeContinuation()
```

Los lectores usan `Volatile.Read(completedCount)` (semántica de adquisición) antes de leer el resultado, asegurando que vean el valor escrito en la Fase 2.

### Resolución de carrera entre `OnCompleted` y `TrySetResult`

Pueden ocurrir tres patrones:

```
Patrón A — OnCompleted primero:
  OnCompleted almacena la continuación vía CAS(continuation, null -> cont)
  TrySetResult lee una continuación no nula -> la invoca

Patrón B — TrySetResult primero (camino rápido síncrono):
  TrySetResult coloca ContinuationSentinel vía CAS(continuation, null -> sentinel)
  OnCompleted lee el sentinel -> invoca la continuación inmediatamente en línea

Patrón C (carrera concurrente):
  C.1: OnCompleted gana el CAS -> TrySetResult lo lee -> invoca
  C.2: TrySetResult gana el CAS (coloca sentinel) -> OnCompleted detecta el sentinel -> invoca en línea
```

El sentinel es un objeto `Action<object>` estático usado puramente como valor marcador — nunca se invoca realmente como delegado.

### Validación de token y seguridad ABA

Cada llamada a `GetStatus`, `GetResult` y `OnCompleted` valida el `uint token` contra la `generation` actual. Cuando `Reset()` llama a `Interlocked.Increment(ref generation)`, cualquier struct `ValkarnTask` que tenga el token antiguo recibirá una `InvalidOperationException` en lugar de operar silenciosamente sobre el estado reciclado. Un contador de generación de 32 bits que se desborda (requiriendo ~4 mil millones de reusos de una única ranura) se considera prácticamente imposible.

### Reinicio y reporte de errores no observados

`Reset()` se llama en el momento de retorno al grupo. Antes de incrementar la generación, verifica si un error fue almacenado pero nunca observado (es decir, `GetResult` nunca fue llamado después de un fallo). Si es así, publica la excepción a través de `ValkarnTask.UnobservedException`. Los errores de cancelación solo se reportan si `LogUnobservedCancellations` está habilitado en `ValkarnTaskSettings`, ya que la cancelación suele ser intencional.

Para objetos `Promise` y `Promise<T>` no agrupados, el reporte de errores no observados ocurre desde el finalizador a través de `ReportUnobservedIfNeeded()`, que sigue la misma lógica sin borrar el estado.

---

## Configuración del grupo

Tres configuraciones controlan el tamaño del grupo. En compilaciones de Unity se leen desde un activo ScriptableObject `ValkarnTaskSettings` (con valores predeterminados de reserva), y pueden sobreescribirse en tiempo de ejecución a través de propiedades estáticas:

```csharp
// Máximo de objetos por tipo de grupo (por TStateMachine o por tipo de promesa)
ValkarnTask.DefaultMaxPoolSize = 256;   // predeterminado: 256

// Cuántos fotogramas entre verificaciones de recorte (fotogramas del PlayerLoop de Unity)
ValkarnTask.TrimCheckInterval = 300;    // predeterminado: 300 (~5 segundos a 60fps)

// Mínimo de objetos a mantener después de un pase de recorte
ValkarnTask.MinPoolSize = 8;            // predeterminado: 8
```

`DefaultMaxPoolSize` es el techo aplicado en el momento de construcción del grupo. Se aplica por instancia de grupo, no globalmente — un grupo para `AsyncValkarnTaskRunner<LoadSceneStateMachine>` y un grupo para `AsyncValkarnTaskRunner<FetchDataStateMachine>` tienen cada uno su propio techo.

### Recorte del grupo

El `PlayerLoopHelper` invoca `PoolRegistry.TrimAll(minPoolSize)` cada `TrimCheckInterval` fotogramas en el hilo principal. Cada grupo usa una estrategia de histéresis:

```
Cada verificación de recorte:
  currentSize = fastItem.count + stackSize
  if currentSize <= MinPoolSize: reiniciar conteo consecutivo, omitir

  ratio = currentSize / totalCreated
  if ratio > 0.5 (el grupo contiene > 50% de todos los objetos creados):
    trimConsecutiveCount++
    if trimConsecutiveCount >= hysteresisThreshold:
      liberar cierta fracción (releaseRatio) de objetos en exceso de la pila
      (fastItem se preserva — es la ranura más amigable con la caché)
  else:
    reiniciar trimConsecutiveCount
```

La histéresis evita que un breve pico de tráfico cause inmediatamente que todos los objetos sean asignados y luego recortados inmediatamente. La ranura rápida siempre se preserva durante el recorte porque representa el elemento más recientemente usado y, por lo tanto, es el más probable de ser necesario nuevamente.

---

## `PoolRegistry` y monitoreo

Cada `ValkarnTaskPool<T>` se registra con el `PoolRegistry` global en el momento de construcción. El registro mantiene una lista de referencias `IPoolInfo`, que exponen:

```csharp
internal interface IPoolInfo
{
    Type PooledType { get; }
    int Size { get; }
    int MaxSize { get; }
    bool IsAlive { get; }
    int Trim(int minPoolSize);
}
```

Puedes enumerar todos los grupos activos en tiempo de ejecución usando la API pública:

```csharp
foreach (var (type, size, maxSize) in ValkarnTask.GetPoolInfo())
{
    Debug.Log($"{type.Name}: {size}/{maxSize}");
}
```

Estos son los mismos datos que se muestran en la ventana **Task Tracker** en el Editor de Unity. La ventana consulta `GetPoolInfo()` y muestra una tabla en vivo del estado de ocupación del grupo, permitiéndote ver si los grupos están calentados, si algún tipo está alcanzando consistentemente su techo y si el recorte está funcionando como se espera.

Las entradas de grupo muertas (donde `IsAlive` devuelve false) se eliminan de forma diferida de la lista del registro durante las llamadas a `GetAll()` y `TrimAll()`, evitando que el registro crezca indefinidamente si las instancias de grupo son recolectadas por el GC.

---

## `PooledPromise` y `PooledPromise<T>`

Estas son las fuentes de completado agrupadas destinadas a su uso en patrones async personalizados — por ejemplo, envolver una API basada en callbacks o un canal de productor/consumidor repetitivo.

```csharp
// Adquirir una promesa pendiente del grupo
var promise = ValkarnTask.PooledPromise<string>.Create(out uint token);
ValkarnTask<string> task = promise.Task;

// Pasar la tarea a un consumidor
// ... más tarde, desde cualquier hilo ...
promise.TrySetResult("hello");

// Cuando el consumidor espera la tarea y se llama a GetResult,
// la promesa se reinicia y vuelve al grupo automáticamente.
```

Características clave:

- `Create(out uint token)` alquila del grupo o asigna una nueva instancia rastreada por el grupo.
- `CreateCompleted(T result, out uint token)` hace lo mismo pero señala inmediatamente el resultado, por lo que la tarea ya está completa cuando se devuelve.
- Después de que `GetResult` se llama en la tarea de respaldo, `TryReturn()` se dispara: la promesa llama a `core.Reset()` y se devuelve al grupo.
- Una protección de doble retorno (`Interlocked.Exchange(ref returned, 1)`) evita la corrupción del grupo si `GetResult` se llama dos veces.

**Alternativa no agrupada: `Promise` y `Promise<T>`.** Estas son clases asignadas en el montón que no se devuelven a un grupo. Úsalas para operaciones de larga duración donde el tiempo de vida es impredecible o donde la misma fuente debe sobrevivir múltiples ciclos de espera. Se basan en un finalizador para reportar excepciones no observadas.

---

## Grupos de combinadores

Los combinadores `WhenAll` y `WhenAny` también usan grupos. Cada aridad y combinación de tipos tiene su propio grupo:

| Combinador | Tipo de grupo |
|-----------|---------------|
| `WhenAll(task1, task2)` (tipado) | `ValkarnTaskPool<WhenAllPromise<T1, T2>>` |
| `WhenAll(IEnumerable<ValkarnTask<T>>)` | `ValkarnTaskPool<WhenAllArrayPromise<T>>` |
| `WhenAll(task1, task2)` (void) | `ValkarnTaskPool<WhenAllVoidPromise2>` |
| `WhenAny(task1, task2)` (tipado) | `ValkarnTaskPool<WhenAnyPromise2<T>>` |
| `WhenAny(IEnumerable<ValkarnTask<T>>)` | `ValkarnTaskPool<WhenAnyArrayPromise<T>>` |

Los combinadores basados en arrays (`WhenAll<T>(IEnumerable<...>)` y `WhenAny<T>(IEnumerable<...>)`) usan `System.Buffers.ArrayPool<T>.Shared` para sus arrays internos de source/token, por lo que esos arrays también se reciclan en lugar de asignarse nuevamente por llamada.

Todos los combinadores aplican el mismo cortocircuito de cero asignaciones: si todas las entradas están completadas sincrónicamente en el punto donde se llama a `WhenAll` o `WhenAny`, nunca se crea un nuevo objeto agrupado.

```csharp
// Cero asignaciones — ambas tareas están completadas sincrónicamente
var t1 = ValkarnTask.FromResult(1);
var t2 = ValkarnTask.FromResult(2);
ValkarnTask<(int, int)> combined = ValkarnTask.WhenAll(t1, t2);
// combined.source == null; resultado es (1, 2) almacenado en línea
```
