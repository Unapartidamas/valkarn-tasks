---
sidebar_position: 2
title: Puentes de Job y Awaitable
---

# Puentes de Job y Awaitable

Valkarn Tasks proporciona un conjunto de tipos puente que conectan el Sistema de Trabajos de Unity y la API `Awaitable` con la tubería `VlkTask`. Cada puente es una capa delgada que minimiza las asignaciones; no hay magia oculta.

Todos los tipos descritos aquí están en el espacio de nombres `UnaPartidaMas.Valkarn.Tasks.Bridge` y están protegidos por `#if UNITY_5_3_OR_NEWER` (o `#if UNITY_2023_1_OR_NEWER` para el soporte de Awaitable).

---

## JobHandleExtensions — esperar un único JobHandle

El puente más simple. Llama a `.ToVlkTask()` en cualquier `JobHandle` para obtener un `VlkTask` que se completa cuando el trabajo termina.

```csharp
public static VlkTask ToVlkTask(
    this JobHandle handle,
    PlayerLoopTiming timing          = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

### Cómo funciona

1. **Camino rápido.** Si `handle.IsCompleted` ya es verdadero, se llama a `handle.Complete()` inmediatamente y se devuelve `VlkTask.CompletedTask` — cero asignaciones, sin registro en el PlayerLoop.
2. **Camino normal.** Se alquila un `JobHandlePromise` agrupado, se registra en el PlayerLoop en el `timing` dado y se devuelve envuelto en un `VlkTask`. Cada fotograma `MoveNext()` llama a `JobHandle.ScheduleBatchedJobs()` (para vaciar la cola de trabajos en modo editor y modo batch) y luego verifica `handle.IsCompleted`. Cuando el handle está listo, la promesa completa la tarea y se devuelve al grupo.
3. **Cancelación.** Si el `CancellationToken` se dispara, el handle se completa forzosamente (`handle.Complete()` siempre se llama para evitar fugas en el sistema de trabajos) y la tarea pasa al estado cancelado.

### Uso básico

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Jobs;

// Programar un trabajo y esperarlo inmediatamente.
var handle = myJob.Schedule();
await handle.ToVlkTask();

// Con timing no predeterminado y cancelación.
await handle.ToVlkTask(PlayerLoopTiming.PreUpdate, destroyCancellationToken);
```

---

## JobHandleWhenAll — esperar múltiples JobHandles en paralelo

Cuando necesitas programar varios trabajos independientes y reanudarte solo después de que todos se completen, usa `JobHandleExtensions.WhenAll`.

```csharp
// Sobrecarga más simple: espera todos los handles en timing Update.
public static VlkTask WhenAll(params JobHandle[] handles)

// Sobrecarga completa: timing y cancelación configurables.
public static VlkTask WhenAll(
    JobHandle[]       handles,
    PlayerLoopTiming  timing,
    CancellationToken cancellationToken = default)

// Alias de método de extensión para la sobrecarga completa.
public static VlkTask ToVlkTask(
    this JobHandle[]  handles,
    PlayerLoopTiming  timing              = PlayerLoopTiming.Update,
    CancellationToken cancellationToken   = default)
```

### Cómo funciona

- **Camino rápido.** Si cada handle en el array ya está completado, todos los handles se finalizan y se devuelve `VlkTask.CompletedTask` inmediatamente.
- **Array vacío.** Devuelve `VlkTask.CompletedTask`.
- **Camino normal.** Se crea un `JobHandleArrayPromise` agrupado. Internamente alquila un `JobHandle[]` de `ArrayPool<JobHandle>.Shared` (evitando asignaciones en el montón por llamada), copia los handles de entrada, y se registra en el PlayerLoop. Cada fotograma itera solo los handles aún pendientes usando un bucle compacto de intercambio y reducción, y llama a `JobHandle.ScheduleBatchedJobs()` para mantener los trabajadores en ejecución.
- **Cancelación.** Todos los handles restantes se completan forzosamente y la tarea se cancela.

### Uso

```csharp
var handleA = jobA.Schedule();
var handleB = jobB.Schedule();
var handleC = jobC.Schedule();

// Esperar los tres.
await JobHandleExtensions.WhenAll(handleA, handleB, handleC);

// O usando el método de extensión en un array.
JobHandle[] handles = { handleA, handleB, handleC };
await handles.ToVlkTask();
```

---

## TempNativeArrayScope — tiempo de vida de NativeArray a través de puntos await

### El problema

`NativeArray<T>` asignado con `Allocator.TempJob` tiene un tiempo de vida corto. Si asignas uno, programas un trabajo, `await` el handle del trabajo, y luego olvidas desechar el array, el sistema de seguridad de Unity reportará una fuga de memoria. Usar un `try`/`finally` simple funciona pero es fácil de equivocar en un método async largo.

`TempNativeArrayScope<T>` es un struct que envuelve un `NativeArray<T>` y lo desecha cuando el scope termina, usando la instrucción `using` — el patrón RAII aplicado a la memoria nativa.

### API

```csharp
public struct TempNativeArrayScope<T> : IDisposable where T : struct
{
    // Accede al array envuelto. Lanza ObjectDisposedException si ya fue desechado.
    public NativeArray<T> Array { get; }

    // Verdadero si el scope no ha sido desechado y el array está creado.
    public bool IsCreated { get; }

    // Asigna un nuevo NativeArray<T> con Allocator.TempJob y toma posesión.
    public static TempNativeArrayScope<T> Create(int length);

    // Toma posesión de un NativeArray<T> ya asignado.
    public static TempNativeArrayScope<T> Wrap(NativeArray<T> existing);

    // Desecha el array. Idempotente: seguro de llamar múltiples veces.
    public void Dispose();
}

// Ayudante no genérico (conveniencia de inferencia de tipos).
public static class TempNativeArrayScope
{
    public static TempNativeArrayScope<T> Create<T>(int length) where T : struct;
    public static TempNativeArrayScope<T> Wrap<T>(NativeArray<T> existing) where T : struct;
}
```

`Dispose` usa una bandera `int` simple en lugar de `Interlocked` porque el scope está diseñado para uso de un solo hilo en el hilo principal vía `using var`.

### Uso

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Collections;
using Unity.Jobs;

async VlkTask ProcessDataAsync(int count, CancellationToken ct)
{
    // La instrucción using garantiza que Dispose() se llame cuando el scope salga,
    // ya sea por completado normal, excepción o cancelación.
    using var inputScope  = TempNativeArrayScope.Create<float>(count);
    using var outputScope = TempNativeArrayScope.Create<float>(count);

    NativeArray<float> input  = inputScope.Array;
    NativeArray<float> output = outputScope.Array;

    // Rellenar input, programar el trabajo.
    var job = new MyProcessingJob { Input = input, Output = output };
    var handle = job.Schedule(count, 64);

    // Esperar sin bloquear el hilo principal.
    // Los NativeArrays permanecen válidos — el trabajo aún está ejecutándose.
    await handle.ToVlkTask(cancellationToken: ct);

    // El trabajo está listo. Leer resultados aquí.
    float total = 0f;
    for (int i = 0; i < count; i++)
        total += output[i];

    UnityEngine.Debug.Log($"Suma: {total}");

    // inputScope.Dispose() y outputScope.Dispose() se ejecutan automáticamente aquí.
}
```

También puedes envolver un array que ya asignaste:

```csharp
var existing = new NativeArray<int>(1024, Allocator.TempJob);
using var scope = TempNativeArrayScope.Wrap(existing);
// scope es propietario de existing y lo desechará.
```

### Trampa común: tiempo de vida de NativeArray sin un scope

Este patrón está roto y causará un error del sistema de seguridad:

```csharp
// INCORRECTO: el array puede sobrevivir al trabajo o tener una fuga si ocurre una excepción.
var array = new NativeArray<float>(1024, Allocator.TempJob);
var handle = job.Schedule(1024, 64);
await handle.ToVlkTask(); // punto de suspensión — el array debe permanecer vivo
array.Dispose();          // nunca alcanzado si se lanza una excepción arriba
```

Usa `TempNativeArrayScope` o un `try`/`finally` para garantizar el desecho en todos los caminos de código.

---

## AwaitableBridge — convertir Unity Awaitable a VlkTask

`AwaitableBridge` proporciona métodos de extensión para convertir los tipos `Awaitable` y `Awaitable<T>` de Unity (disponibles desde Unity 2023.1) en awaiters compatibles con `VlkTask`.

**Nota:** `Awaitable` también tiene su propio `GetAwaiter()`. Dado que la resolución de sobrecargas de C# siempre prefiere los métodos de instancia sobre los métodos de extensión, escribir `await myAwaitable` dentro de un método `async VlkTask` ya funciona correctamente — el awaiter de Unity implementa `ICriticalNotifyCompletion` y el constructor de `VlkTask` lo acepta. Los métodos de extensión `.AsVlkTask()` solo se necesitan cuando quieres pasar un `Awaitable` a un combinador (`VlkTask.WhenAll`, `VlkTask.WhenAny`) o almacenarlo como variable `VlkTask`.

Este archivo está protegido por `#if UNITY_2023_1_OR_NEWER`.

### API

```csharp
// Convertir Awaitable a un awaiter compatible con VlkTask.
public static AwaitableVlkTaskAwaiter   AsVlkTask(this Awaitable awaitable)

// Convertir Awaitable<T> a un awaiter compatible con VlkTask.
public static AwaitableVlkTaskAwaiter<T> AsVlkTask<T>(this Awaitable<T> awaitable)
```

Ambos awaiters implementan `ICriticalNotifyCompletion`, que omite la captura de `ExecutionContext`. Delegan `IsCompleted`, `GetResult` y `OnCompleted` directamente al awaiter de Unity envuelto.

### Uso

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnityEngine;

// Await directo — funciona sin conversión en un método async VlkTask.
async VlkTask DirectExample()
{
    await Awaitable.NextFrameAsync();       // No se necesita conversión.
    await Awaitable.WaitForSecondsAsync(1f);
}

// Conversión explícita — necesaria para combinadores y almacenamiento.
async VlkTask CombinatorExample()
{
    VlkTask a = Awaitable.NextFrameAsync().AsVlkTask();
    VlkTask b = Awaitable.WaitForSecondsAsync(2f).AsVlkTask();
    await VlkTask.WhenAll(a, b);
}

// Versión genérica con tipo de resultado.
async VlkTask<Texture2D> LoadTextureExample(string path)
{
    Awaitable<Texture2D> loadOp = LoadTextureAsync(path);
    return await loadOp.AsVlkTask();
}
```

---

## JobBridge — el envoltorio generado por código fuente

`JobBridge.cs` define `JobPromise<TJob>`, el tipo de promesa agrupada genérica usado por el generador de código fuente. Es un detalle de implementación; normalmente no lo instanciarás tú mismo.

```csharp
// Hace polling de un JobHandle cada fotograma. Usado por los métodos ScheduleAsync generados por código fuente.
public sealed class JobPromise<TJob> : VlkTask.ISource, IPlayerLoopItem, IPoolNode<JobPromise<TJob>>
    where TJob : struct
{
    public static JobPromise<TJob> Create(JobHandle handle, CancellationToken ct, out uint token);
}
```

El comportamiento es idéntico a `JobHandlePromise` (ver `JobHandleExtensions`), excepto que es genérico sobre el tipo de trabajo para el aislamiento del grupo — cada tipo de trabajo obtiene su propio grupo.

---

## Generador de código fuente: JobBridgeGenerator

El `JobBridgeGenerator` es un generador de código fuente incremental de Roslyn (clase `UnaPartidaMas.Valkarn.Tasks.SourceGen.Generators.JobBridgeGenerator`) que produce automáticamente métodos de extensión `ScheduleAsync` para tus tipos de trabajo.

### Qué detecta

El generador escanea todos los structs **públicos** en la compilación que implementan uno de:
- `Unity.Jobs.IJob`
- `Unity.Jobs.IJobParallelFor`
- `Unity.Jobs.IJobFor`

Los structs de trabajo privados e internos se omiten. Si un struct está anidado dentro de un tipo no público, también se omite.

El generador no hace nada si `UnaPartidaMas.Valkarn.Tasks.ValkarnTask` no se encuentra en la compilación, por lo que es seguro en ensamblados que no hacen referencia a Valkarn Tasks.

### Qué genera

El archivo de salida es `ValkarnTask.JobBridge.Generated.g.cs`. Para cada tipo de trabajo detectado emite una clase `public static __<TypeName>_AsyncExt` que contiene:

| Interfaz de trabajo | Firma de método generado |
|---|---|
| `IJob` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, CancellationToken ct = default)` |
| `IJobParallelFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleParallelAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |

Cada método generado programa el trabajo usando los métodos de extensión estándar de Unity, luego envuelve el `JobHandle` resultante en un `JobPromise<TJob>` y devuelve un `ValkarnTask`.

Para tipos anidados (por ejemplo, un struct de trabajo dentro de una clase exterior), el nombre de clase generado usa guiones bajos: `__Outer_Inner_AsyncExt`.

### Uso de los métodos generados

```csharp
// Ejemplo IJob
public struct MyCalculationJob : IJob
{
    public NativeArray<float> Data;
    public void Execute() { /* ... */ }
}

// El generador produce:
//   public static ValkarnTask ScheduleAsync(this ref MyCalculationJob job, CancellationToken ct = default)

async VlkTask RunCalculation(CancellationToken ct)
{
    using var scope = TempNativeArrayScope.Create<float>(1024);
    var job = new MyCalculationJob { Data = scope.Array };
    await job.ScheduleAsync(ct);  // método de extensión generado
    // Leer resultados de scope.Array aquí.
}

// Ejemplo IJobParallelFor
public struct MyParallelJob : IJobParallelFor
{
    public NativeArray<float> Input;
    public NativeArray<float> Output;
    public void Execute(int index) { Output[index] = Input[index] * 2f; }
}

async VlkTask RunParallel(int length, CancellationToken ct)
{
    using var inputScope  = TempNativeArrayScope.Create<float>(length);
    using var outputScope = TempNativeArrayScope.Create<float>(length);
    var job = new MyParallelJob { Input = inputScope.Array, Output = outputScope.Array };
    await job.ScheduleAsync(length, innerLoopBatchCount: 64, ct);
}
```

---

## Generador de código fuente: AwaitableBridgeGenerator

El `AwaitableBridgeGenerator` detecta si `UnityEngine.Awaitable` y `UnityEngine.Awaitable<T>` están presentes en la compilación y emite métodos de extensión `AsValkarnTask()` cuando lo están.

El archivo de salida es `ValkarnTask.AwaitableBridge.Generated.g.cs`. El código generado vive en `namespace UnaPartidaMas.Valkarn.Tasks.Bridge` bajo la clase `AwaitableBridgeExtensions`.

Métodos generados:

```csharp
// Emitido cuando se encuentra UnityEngine.Awaitable:
public static async ValkarnTask AsValkarnTask(this Awaitable awaitable)
{
    await awaitable;
}

// Emitido cuando se encuentra UnityEngine.Awaitable<T>:
public static async ValkarnTask<T> AsValkarnTask<T>(this Awaitable<T> awaitable)
{
    return await awaitable;
}
```

Estos son métodos `async ValkarnTask`, por lo que pasan por el constructor async agrupado de Valkarn Tasks — en un grupo caliente son de cero asignaciones.

El generador está protegido: si `ValkarnTask` no está en la compilación, no se emite código. Esto evita errores CS0246 en ensamblados que hacen referencia a Unity pero no a Valkarn Tasks.

---

## Ejemplo funcional completo: Puente de trabajos en un sistema ECS

Lo siguiente es de `Samples~/ECS/JobBridgeExample.cs`. Muestra el patrón completo para programar un trabajo paralelo de Burst desde un `ISystem`, esperarlo sin bloquear, y escribir los resultados de vuelta.

```csharp
#if UNITY_5_3_OR_NEWER && VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnaPartidaMas.Valkarn.Tasks.ECS;

public partial struct JobBridgeExample : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<HealthData>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var worldCt = state.World.GetWorldCancellationToken();

        // Extraer todos los datos de entidad sincrónicamente dentro de OnUpdate.
        // Los métodos async no pueden tener parámetros ref (CS1988), así que los datos
        // deben copiarse aquí y pasarse por valor al método async.
        var query       = SystemAPI.QueryBuilder().WithAll<HealthData>().Build();
        var entityCount = query.CalculateEntityCount();
        if (entityCount == 0) return;

        var entities    = query.ToEntityArray(Allocator.TempJob);
        var healthArray = query.ToComponentDataArray<HealthData>(Allocator.TempJob);
        var results     = new NativeArray<float>(entityCount, Allocator.TempJob);

        // El método async toma posesión de los NativeArrays y los desecha.
        ProcessHealthAsync(state.EntityManager, entities, healthArray, results, worldCt).Forget();
        state.Enabled = false;
    }

    public void OnDestroy(ref SystemState state) { }

    static async VlkTask ProcessHealthAsync(
        EntityManager entityManager,
        NativeArray<Entity> entities,
        NativeArray<HealthData> healthArray,
        NativeArray<float> results,
        CancellationToken ct)
    {
        try
        {
            // Fase 1: Programar el trabajo Burst.
            var job = new HealthProcessingJob
            {
                HealthInputs     = healthArray,
                ProcessedOutputs = results,
            };
            var handle = job.Schedule(entities.Length, batchSize: 64);

            // Fase 2: Esperar la completación sin bloquear el hilo principal.
            await handle.ToVlkTask(cancellationToken: ct);

            // Fase 3: Aplicar resultados. Estamos de vuelta en el hilo principal.
            ct.ThrowIfCancellationRequested();

            for (int i = 0; i < entities.Length; i++)
            {
                // La entidad puede haber sido destruida mientras el trabajo se ejecutaba.
                if (!entityManager.SafeEntityExists(entities[i]))
                    continue;

                entityManager.SetComponentData(entities[i], new HealthData
                {
                    CurrentHealth = results[i],
                });
            }
        }
        finally
        {
            // Siempre desechar los NativeArrays — se ejecuta en éxito, excepción o cancelación.
            if (entities.IsCreated)    entities.Dispose();
            if (healthArray.IsCreated) healthArray.Dispose();
            if (results.IsCreated)     results.Dispose();
        }
    }

    [BurstCompile]
    struct HealthProcessingJob : IJobParallelFor
    {
        [ReadOnly]  public NativeArray<HealthData> HealthInputs;
        [WriteOnly] public NativeArray<float>      ProcessedOutputs;

        public void Execute(int index)
        {
            var h = HealthInputs[index];
            var newHealth = h.CurrentHealth + h.RegenRate;
            if (newHealth > h.MaxHealth) newHealth = h.MaxHealth;
            ProcessedOutputs[index] = newHealth;
        }
    }

    struct HealthData : IComponentData
    {
        public float CurrentHealth;
        public float MaxHealth;
        public float RegenRate;
    }
}
#endif
```

---

## Resumen de tipos de puente

| Tipo | Propósito | Asignación |
|---|---|---|
| `JobHandleExtensions.ToVlkTask()` | Esperar un único `JobHandle` | Cero en camino rápido; promesa agrupada en caso contrario |
| `JobHandleExtensions.WhenAll()` | Esperar múltiples `JobHandle` en paralelo | Cero en camino rápido; promesa agrupada + alquiler de `ArrayPool` en caso contrario |
| `TempNativeArrayScope<T>` | Gestión de tiempo de vida RAII para `NativeArray` | Ninguna (struct) |
| `AwaitableBridge.AsVlkTask()` | Convertir `Awaitable`/`Awaitable<T>` a VlkTask | Ninguna (awaiter struct) |
| `ScheduleAsync()` generado | Esperar un trabajo tipado directamente | `JobPromise<TJob>` agrupado |
