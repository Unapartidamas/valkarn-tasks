---
sidebar_position: 2
title: Job & Awaitable Bridges
---

# Job & Awaitable Bridges

Valkarn Tasks provides a set of bridge types that connect Unity's Job System and the `Awaitable` API to the `ValkarnTask` pipeline. Each bridge is a thin, allocation-minimizing layer; there is no hidden magic.

All types described here are in the namespace `UnaPartidaMas.Valkarn.Tasks.Bridge` and are guarded by `#if UNITY_5_3_OR_NEWER` (or `#if UNITY_2023_1_OR_NEWER` for Awaitable support).

---

## JobHandleExtensions — awaiting a single JobHandle

The simplest bridge. Call `.ToValkarnTask()` on any `JobHandle` to get back a `ValkarnTask` that completes when the job finishes.

```csharp
public static ValkarnTask ToValkarnTask(
    this JobHandle handle,
    PlayerLoopTiming timing          = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

### How it works

1. **Fast path.** If `handle.IsCompleted` is already true, `handle.Complete()` is called immediately and `ValkarnTask.CompletedTask` is returned — zero allocation, no PlayerLoop registration.
2. **Normal path.** A pooled `JobHandlePromise` is rented, registered on the PlayerLoop at the given `timing`, and returned wrapped in a `ValkarnTask`. Each frame `MoveNext()` calls `JobHandle.ScheduleBatchedJobs()` (to flush the job queue in edit mode and batch mode) and then checks `handle.IsCompleted`. When the handle is done, the promise completes the task and returns itself to the pool.
3. **Cancellation.** If the `CancellationToken` fires, the handle is forcibly completed (`handle.Complete()` is always called to prevent job system leaks) and the task transitions to the canceled state.

### Basic usage

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Jobs;

// Schedule a job and immediately await it.
var handle = myJob.Schedule();
await handle.ToValkarnTask();

// With a non-default timing and cancellation.
await handle.ToValkarnTask(PlayerLoopTiming.PreUpdate, destroyCancellationToken);
```

---

## JobHandleWhenAll — awaiting multiple JobHandles in parallel

When you need to schedule several independent jobs and resume only after all of them complete, use `JobHandleExtensions.WhenAll`.

```csharp
// Simplest overload: waits for all handles at Update timing.
public static ValkarnTask WhenAll(params JobHandle[] handles)

// Full overload: configurable timing and cancellation.
public static ValkarnTask WhenAll(
    JobHandle[]       handles,
    PlayerLoopTiming  timing,
    CancellationToken cancellationToken = default)

// Extension method alias for the full overload.
public static ValkarnTask ToValkarnTask(
    this JobHandle[]  handles,
    PlayerLoopTiming  timing              = PlayerLoopTiming.Update,
    CancellationToken cancellationToken   = default)
```

### How it works

- **Fast path.** If every handle in the array is already completed, all handles are finalized and `ValkarnTask.CompletedTask` is returned immediately.
- **Empty array.** Returns `ValkarnTask.CompletedTask`.
- **Normal path.** A pooled `JobHandleArrayPromise` is created. Internally it rents a `JobHandle[]` from `ArrayPool<JobHandle>.Shared` (avoiding per-call heap allocation), copies the input handles in, and registers on the PlayerLoop. Each frame it iterates only the still-pending handles using a compact swap-and-shrink loop, and calls `JobHandle.ScheduleBatchedJobs()` to keep workers running.
- **Cancellation.** All remaining handles are force-completed and the task is canceled.

### Usage

```csharp
var handleA = jobA.Schedule();
var handleB = jobB.Schedule();
var handleC = jobC.Schedule();

// Wait for all three.
await JobHandleExtensions.WhenAll(handleA, handleB, handleC);

// Or using the extension method on an array.
JobHandle[] handles = { handleA, handleB, handleC };
await handles.ToValkarnTask();
```

---

## TempNativeArrayScope — NativeArray lifetime across await points

### The problem

`NativeArray<T>` allocated with `Allocator.TempJob` has a short lifetime. If you allocate one, schedule a job, `await` the job handle, and then forget to dispose the array, Unity's safety system will report a memory leak. Using a plain `try`/`finally` works but is easy to get wrong in a long async method.

`TempNativeArrayScope<T>` is a struct that wraps a `NativeArray<T>` and disposes it when the scope ends, using the `using` statement — the RAII pattern applied to native memory.

### API

```csharp
public struct TempNativeArrayScope<T> : IDisposable where T : struct
{
    // Accesses the wrapped array. Throws ObjectDisposedException if already disposed.
    public NativeArray<T> Array { get; }

    // True if the scope has not been disposed and the array is created.
    public bool IsCreated { get; }

    // Allocates a new NativeArray<T> with Allocator.TempJob and takes ownership.
    public static TempNativeArrayScope<T> Create(int length);

    // Takes ownership of an already-allocated NativeArray<T>.
    public static TempNativeArrayScope<T> Wrap(NativeArray<T> existing);

    // Disposes the array. Idempotent: safe to call multiple times.
    public void Dispose();
}

// Non-generic helper (type inference convenience).
public static class TempNativeArrayScope
{
    public static TempNativeArrayScope<T> Create<T>(int length) where T : struct;
    public static TempNativeArrayScope<T> Wrap<T>(NativeArray<T> existing) where T : struct;
}
```

`Dispose` uses a plain `int` flag rather than `Interlocked` because the scope is designed for single-threaded use on the main thread via `using var`.

### Usage

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Collections;
using Unity.Jobs;

async ValkarnTask ProcessDataAsync(int count, CancellationToken ct)
{
    // The using statement guarantees Dispose() is called when the scope exits,
    // whether by normal completion, exception, or cancellation.
    using var inputScope  = TempNativeArrayScope.Create<float>(count);
    using var outputScope = TempNativeArrayScope.Create<float>(count);

    NativeArray<float> input  = inputScope.Array;
    NativeArray<float> output = outputScope.Array;

    // Populate input, schedule the job.
    var job = new MyProcessingJob { Input = input, Output = output };
    var handle = job.Schedule(count, 64);

    // Await without blocking the main thread.
    // The NativeArrays remain valid — job is still running.
    await handle.ToValkarnTask(cancellationToken: ct);

    // The job is done. Read results here.
    float total = 0f;
    for (int i = 0; i < count; i++)
        total += output[i];

    UnityEngine.Debug.Log($"Sum: {total}");

    // inputScope.Dispose() and outputScope.Dispose() run automatically here.
}
```

You can also wrap an array you already allocated:

```csharp
var existing = new NativeArray<int>(1024, Allocator.TempJob);
using var scope = TempNativeArrayScope.Wrap(existing);
// scope owns existing and will dispose it.
```

### Common pitfall: NativeArray lifetime without a scope

This pattern is broken and will cause a safety system error:

```csharp
// WRONG: array may outlive the job or be leaked if an exception occurs.
var array = new NativeArray<float>(1024, Allocator.TempJob);
var handle = job.Schedule(1024, 64);
await handle.ToValkarnTask(); // suspension point — array must remain alive
array.Dispose();          // never reached if an exception fires above
```

Use `TempNativeArrayScope` or a `try`/`finally` to guarantee disposal in all code paths.

---

## AwaitableBridge — converting Unity Awaitable to ValkarnTask

`AwaitableBridge` provides extension methods for converting Unity's `Awaitable` and `Awaitable<T>` types (available since Unity 2023.1) into `ValkarnTask`-compatible awaiters.

**Note:** `Awaitable` also has its own `GetAwaiter()`. Because C# overload resolution always prefers instance methods over extension methods, writing `await myAwaitable` inside an `async ValkarnTask` method already works correctly — Unity's awaiter implements `ICriticalNotifyCompletion` and the `ValkarnTask` builder accepts it. The `.AsValkarnTask()` extension methods are needed only when you want to pass an `Awaitable` to a combinator (`ValkarnTask.WhenAll`, `ValkarnTask.WhenAny`) or store it as a `ValkarnTask` variable.

This file is guarded by `#if UNITY_2023_1_OR_NEWER`.

### API

```csharp
// Convert Awaitable to a ValkarnTask-compatible awaiter.
public static AwaitableValkarnTaskAwaiter   AsValkarnTask(this Awaitable awaitable)

// Convert Awaitable<T> to a ValkarnTask-compatible awaiter.
public static AwaitableValkarnTaskAwaiter<T> AsValkarnTask<T>(this Awaitable<T> awaitable)
```

Both awaiters implement `ICriticalNotifyCompletion`, which skips `ExecutionContext` capture. They delegate `IsCompleted`, `GetResult`, and `OnCompleted` directly to the wrapped Unity awaiter.

### Usage

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnityEngine;

// Direct await — works without conversion in an async ValkarnTask method.
async ValkarnTask DirectExample()
{
    await Awaitable.NextFrameAsync();       // No conversion needed.
    await Awaitable.WaitForSecondsAsync(1f);
}

// Explicit conversion — needed for combinators and storage.
async ValkarnTask CombinatorExample()
{
    ValkarnTask a = Awaitable.NextFrameAsync().AsValkarnTask();
    ValkarnTask b = Awaitable.WaitForSecondsAsync(2f).AsValkarnTask();
    await ValkarnTask.WhenAll(a, b);
}

// Generic version with a result type.
async ValkarnTask<Texture2D> LoadTextureExample(string path)
{
    Awaitable<Texture2D> loadOp = LoadTextureAsync(path);
    return await loadOp.AsValkarnTask();
}
```

---

## JobBridge — the source-generated wrapper

`JobBridge.cs` defines `JobPromise<TJob>`, the generic pooled promise type used by the source generator. It is an implementation detail; you will not normally instantiate it yourself.

```csharp
// Polls a JobHandle each frame. Used by the source-generated ScheduleAsync methods.
public sealed class JobPromise<TJob> : ValkarnTask.ISource, IPlayerLoopItem, IPoolNode<JobPromise<TJob>>
    where TJob : struct
{
    public static JobPromise<TJob> Create(JobHandle handle, CancellationToken ct, out uint token);
}
```

Behaviour is identical to `JobHandlePromise` (see `JobHandleExtensions`), except it is generic over the job type for pool isolation — each job type gets its own pool.

---

## Source generator: JobBridgeGenerator

The `JobBridgeGenerator` is a Roslyn incremental source generator (class `UnaPartidaMas.Valkarn.Tasks.SourceGen.Generators.JobBridgeGenerator`) that automatically produces `ScheduleAsync` extension methods for your job types.

### What it detects

The generator scans all **public** structs in the compilation that implement one of:
- `Unity.Jobs.IJob`
- `Unity.Jobs.IJobParallelFor`
- `Unity.Jobs.IJobFor`

Private and internal job structs are skipped. If a struct is nested inside a non-public type, it is also skipped.

The generator does nothing if `UnaPartidaMas.Valkarn.Tasks.ValkarnTask` is not found in the compilation, so it is safe in assemblies that do not reference Valkarn Tasks.

### What it generates

The output file is `ValkarnTask.JobBridge.Generated.g.cs`. For each detected job type it emits a `public static class __<TypeName>_AsyncExt` containing:

| Job interface | Generated method signature |
|---|---|
| `IJob` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, CancellationToken ct = default)` |
| `IJobParallelFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleParallelAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |

Each generated method schedules the job using the standard Unity extension methods, then wraps the resulting `JobHandle` in a `JobPromise<TJob>` and returns a `ValkarnTask`.

For nested types (e.g. a job struct inside an outer class), the generated class name uses underscores: `__Outer_Inner_AsyncExt`.

### Usage of generated methods

```csharp
// IJob example
public struct MyCalculationJob : IJob
{
    public NativeArray<float> Data;
    public void Execute() { /* ... */ }
}

// The generator produces:
//   public static ValkarnTask ScheduleAsync(this ref MyCalculationJob job, CancellationToken ct = default)

async ValkarnTask RunCalculation(CancellationToken ct)
{
    using var scope = TempNativeArrayScope.Create<float>(1024);
    var job = new MyCalculationJob { Data = scope.Array };
    await job.ScheduleAsync(ct);  // generated extension method
    // Read results from scope.Array here.
}

// IJobParallelFor example
public struct MyParallelJob : IJobParallelFor
{
    public NativeArray<float> Input;
    public NativeArray<float> Output;
    public void Execute(int index) { Output[index] = Input[index] * 2f; }
}

async ValkarnTask RunParallel(int length, CancellationToken ct)
{
    using var inputScope  = TempNativeArrayScope.Create<float>(length);
    using var outputScope = TempNativeArrayScope.Create<float>(length);
    var job = new MyParallelJob { Input = inputScope.Array, Output = outputScope.Array };
    await job.ScheduleAsync(length, innerLoopBatchCount: 64, ct);
}
```

---

## Source generator: AwaitableBridgeGenerator

The `AwaitableBridgeGenerator` detects whether `UnityEngine.Awaitable` and `UnityEngine.Awaitable<T>` are present in the compilation and emits `AsValkarnTask()` extension methods when they are.

The output file is `ValkarnTask.AwaitableBridge.Generated.g.cs`. The generated code lives in `namespace UnaPartidaMas.Valkarn.Tasks.Bridge` under the class `AwaitableBridgeExtensions`.

Generated methods:

```csharp
// Emitted when UnityEngine.Awaitable is found:
public static async ValkarnTask AsValkarnTask(this Awaitable awaitable)
{
    await awaitable;
}

// Emitted when UnityEngine.Awaitable<T> is found:
public static async ValkarnTask<T> AsValkarnTask<T>(this Awaitable<T> awaitable)
{
    return await awaitable;
}
```

These are `async ValkarnTask` methods, so they go through Valkarn Tasks' pooled async builder — on a warm pool they are zero-allocation.

The generator is guarded: if `ValkarnTask` is not in the compilation, no code is emitted. This prevents CS0246 errors in assemblies that reference Unity but not Valkarn Tasks.

---

## Full working example: Job bridge in an ECS system

The following is from `Samples~/ECS/JobBridgeExample.cs`. It shows the complete pattern for scheduling a Burst parallel job from an `ISystem`, awaiting it without blocking, and writing results back.

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

        // Extract all entity data synchronously inside OnUpdate.
        // Async methods cannot have ref parameters (CS1988), so data
        // must be copied here and passed by value to the async method.
        var query       = SystemAPI.QueryBuilder().WithAll<HealthData>().Build();
        var entityCount = query.CalculateEntityCount();
        if (entityCount == 0) return;

        var entities    = query.ToEntityArray(Allocator.TempJob);
        var healthArray = query.ToComponentDataArray<HealthData>(Allocator.TempJob);
        var results     = new NativeArray<float>(entityCount, Allocator.TempJob);

        // The async method takes ownership of the NativeArrays and disposes them.
        ProcessHealthAsync(state.EntityManager, entities, healthArray, results, worldCt).Forget();
        state.Enabled = false;
    }

    public void OnDestroy(ref SystemState state) { }

    static async ValkarnTask ProcessHealthAsync(
        EntityManager entityManager,
        NativeArray<Entity> entities,
        NativeArray<HealthData> healthArray,
        NativeArray<float> results,
        CancellationToken ct)
    {
        try
        {
            // Phase 1: Schedule the Burst job.
            var job = new HealthProcessingJob
            {
                HealthInputs     = healthArray,
                ProcessedOutputs = results,
            };
            var handle = job.Schedule(entities.Length, batchSize: 64);

            // Phase 2: Await completion without blocking the main thread.
            await handle.ToValkarnTask(cancellationToken: ct);

            // Phase 3: Apply results. We are back on the main thread.
            ct.ThrowIfCancellationRequested();

            for (int i = 0; i < entities.Length; i++)
            {
                // Entity may have been destroyed while the job was running.
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
            // Always dispose NativeArrays — runs on success, exception, or cancellation.
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

## Summary of bridge types

| Type | Purpose | Allocation |
|---|---|---|
| `JobHandleExtensions.ToValkarnTask()` | Await a single `JobHandle` | Zero on fast path; pooled promise otherwise |
| `JobHandleExtensions.WhenAll()` | Await multiple `JobHandle` in parallel | Zero on fast path; pooled promise + `ArrayPool` rent otherwise |
| `TempNativeArrayScope<T>` | RAII lifetime management for `NativeArray` | None (struct) |
| `AwaitableBridge.AsValkarnTask()` | Convert `Awaitable`/`Awaitable<T>` to ValkarnTask | None (struct awaiter) |
| Generated `ScheduleAsync()` | Await a typed job directly | Pooled `JobPromise<TJob>` |
