---
sidebar_position: 1
title: Burst & ECS Integration
---

# Burst & ECS Integration

Valkarn Tasks includes optional integration with Unity's Burst compiler, Unity Collections, and the Entities (ECS) package. All of this functionality is conditionally compiled — it is only active when the required packages are present and the corresponding scripting define symbols are set.

## Requirements

| Feature | Required package | Scripting define |
|---|---|---|
| `NativeTimerHeap`, `NativeScheduler`, `BurstSchedulerRunner` | Unity Burst 1.8+, Unity Collections 2.0+ | `VTASKS_HAS_BURST` and `VTASKS_HAS_COLLECTIONS` |
| `AsyncSystemUtilities` | Unity Entities 1.0+ | `VTASKS_HAS_ENTITIES` |

All Burst/ECS source files are wrapped in `#if` guards matching these defines. Nothing in these files compiles or links unless the defines are present.

### Setup

1. Install the required packages via the Unity Package Manager:
   - `com.unity.burst` 1.8 or later
   - `com.unity.collections` 2.0 or later
   - `com.unity.entities` 1.0 or later (for ECS utilities only)

2. Add the scripting define symbols to **Project Settings > Player > Scripting Define Symbols**:
   ```
   VTASKS_HAS_BURST;VTASKS_HAS_COLLECTIONS;VTASKS_HAS_ENTITIES
   ```
   You only need to add the defines for the packages you have installed.

---

## NativeTimerHeap

**Namespace:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Guard:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeTimerHeap` is a Burst-compatible binary min-heap for scheduling timers. It stores `TimerEntry` values ordered by deadline, giving O(log n) insert and O(log n) removal per expired timer.

### Key types

```csharp
// Entry stored in the heap. Ordered by Deadline.
public struct TimerEntry : IComparable<TimerEntry>
{
    public long Deadline;
    public int  Id;
}

public struct NativeTimerHeap : IDisposable
{
    public bool IsCreated { get; }
    public int  Count     { get; }

    // Creates the heap. Use Allocator.Persistent for long-lived heaps.
    public NativeTimerHeap(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // Inserts a new timer. Returns the timer ID (used to identify the callback).
    // The deadline and the value passed to DrainExpired must use the same unit
    // (BurstSchedulerRunner uses DateTime ticks via Time.realtimeSinceStartupAsDouble).
    [BurstCompile]
    public int Schedule(long deadline);

    // Removes and appends IDs of all timers whose Deadline <= currentTimestamp.
    // Returns the number of timers drained.
    [BurstCompile]
    public int DrainExpired(long currentTimestamp, NativeList<int> expiredIds);

    public void Dispose();
}
```

`NativeTimerHeap` is an unmanaged struct. It cannot store managed delegates — IDs are matched to managed callbacks in the `BurstSchedulerRunner` dictionary on the main thread.

---

## NativeScheduler

**Namespace:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Guard:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeScheduler` is a Burst-compatible work queue backed by `NativeQueue<ScheduledWork>`. Burst-compiled jobs enqueue work items; the main thread drains them each frame.

### Key types

```csharp
public enum WorkType : byte
{
    TimerExpired  = 0,
    JobCompleted  = 1,
    Custom        = 2
}

public struct ScheduledWork
{
    public int      Id;
    public WorkType Type;
    public long     Payload;
}

public struct NativeScheduler : IDisposable
{
    public bool IsCreated { get; }

    public NativeScheduler(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // Enqueues a work item from a Burst-compiled job.
    [BurstCompile]
    public void Enqueue(ScheduledWork work);

    // Drains all pending work into `results`. Call from the main thread only.
    // Returns the number of items drained.
    public int Drain(NativeList<ScheduledWork> results);

    // Returns a parallel writer suitable for use in IJobParallelFor jobs.
    public NativeQueue<ScheduledWork>.ParallelWriter AsParallelWriter();

    public void Dispose();
}
```

The queue is the crossing point between the Burst world and the managed world. `Enqueue` is Burst-callable; `Drain` is main-thread-only.

---

## BurstSchedulerRunner

**Namespace:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Guard:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`BurstSchedulerRunner` is the managed bridge between the native scheduler/timer heap and the rest of your game. It implements `IPlayerLoopItem`, so Unity calls `MoveNext()` once per frame at the registered timing. Each frame it:

1. Drains `NativeScheduler` and fires any registered managed callbacks matched by work ID.
2. Drains `NativeTimerHeap` for expired timers and fires their managed callbacks.

Exceptions thrown by callbacks are forwarded to `ValkarnTask.PublishUnobservedException` rather than propagating up through the PlayerLoop.

### API

```csharp
public sealed class BurstSchedulerRunner : IPlayerLoopItem, IDisposable
{
    // Creates a runner and registers it on the PlayerLoop. Returns the instance.
    // Dispose the returned runner when it is no longer needed.
    public static BurstSchedulerRunner Create(
        PlayerLoopTiming timing       = PlayerLoopTiming.Update,
        int initialCapacity           = 64);

    // Direct access to the NativeScheduler for enqueueing from Burst jobs.
    public NativeScheduler Scheduler { get; }

    // Schedules a managed timer callback. Must be called from the main thread.
    // Returns a timer ID (for identification only; there is no cancel API).
    public int ScheduleTimer(TimeSpan delay, Action callback);

    // Associates a managed callback with a work ID enqueued by a Burst job.
    // Must be called from the main thread, before or during the frame the job completes.
    public void RegisterCallback(int workId, Action callback);

    // Disposes all native containers and unregisters from the PlayerLoop.
    public void Dispose();
}
```

### How BurstSchedulerRunner differs from the default scheduler

The default Valkarn Tasks scheduler integrates directly with `async/await` state machines and manages continuation dispatch via `PlayerLoopHelper`. `BurstSchedulerRunner` adds a separate lane specifically for signalling from Burst-compiled jobs:

| | Default scheduler | BurstSchedulerRunner |
|---|---|---|
| Continuation type | Managed `Action` via `ISource` | Managed `Action` registered by ID |
| Signal source | C# awaiter pattern | Unmanaged `NativeScheduler.Enqueue` |
| Timer source | `ValkarnTask.Delay` (managed) | `NativeTimerHeap` (unmanaged) |
| Thread safety | Main-thread continuations | Enqueue is Burst-safe; drain is main-thread-only |

### Usage pattern

```csharp
// 1. Create the runner once (e.g. in a bootstrap MonoBehaviour or ISystem.OnCreate).
var runner = BurstSchedulerRunner.Create(PlayerLoopTiming.Update, initialCapacity: 128);

// 2. Schedule a timer callback (main thread).
runner.ScheduleTimer(TimeSpan.FromSeconds(2.0), () =>
{
    Debug.Log("Two seconds elapsed (unmanaged timer).");
});

// 3. From a Burst job, enqueue a work item.
//    The NativeScheduler.ParallelWriter is safe to use from IJobParallelFor.
var writer = runner.Scheduler.AsParallelWriter();
// Inside Execute(int index):
writer.Enqueue(new ScheduledWork { Id = myWorkId, Type = WorkType.Custom });

// 4. Register the managed callback on the main thread before the job completes.
runner.RegisterCallback(myWorkId, () =>
{
    Debug.Log($"Job completed signal received for work {myWorkId}.");
});

// 5. Dispose the runner when done (e.g. OnDestroy or domain reload).
runner.Dispose();
```

---

## AsyncSystemUtilities

**Namespace:** `UnaPartidaMas.Valkarn.Tasks.ECS`
**Guard:** `#if VTASKS_HAS_ENTITIES`

`AsyncSystemUtilities` provides two extension helpers for writing async ECS systems.

### GetWorldCancellationToken

```csharp
public static CancellationToken GetWorldCancellationToken(
    this World world,
    PlayerLoopTiming timing = PlayerLoopTiming.Update)
```

Returns a `CancellationToken` that is automatically canceled when the given `World` is destroyed. Internally it starts a fire-and-forget `ValkarnTask` that calls `ValkarnTask.Yield(timing)` each frame while `world.IsCreated` is true, then cancels a `CancellationTokenSource` when the loop exits.

If the world is already destroyed when you call this method, it returns a token that is already in the canceled state.

Pass this token to every async method you launch from a system so that in-flight work is automatically stopped when the world goes away.

### SafeEntityExists

```csharp
public static bool SafeEntityExists(this EntityManager entityManager, Entity entity)
```

Calls `entityManager.Exists(entity)` and returns `false` if an `ObjectDisposedException` is thrown. This can happen when the `EntityManager` is accessed after the world has been disposed, which is a real race condition in async code that survives across frame boundaries.

Use this after every `await` point before writing back to an entity.

---

## Working example: Async ECS system

The following example is from `Samples~/ECS/AsyncLoadSystem.cs`. It demonstrates the canonical pattern for one-shot async initialization from an `ISystem`.

```csharp
#if VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Entities;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.ECS;

public partial struct AsyncLoadSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // Obtain a cancellation token tied to this World's lifetime.
        // If the World is destroyed, all async work launched with this token
        // will be automatically canceled.
        var worldCt = state.World.GetWorldCancellationToken();

        // Launch the async initialization and forget the task.
        // Forget() routes any unhandled exception to ValkarnTask.PublishUnobservedException.
        InitializeAsync(state.WorldUnmanaged, worldCt).Forget();
    }

    public void OnUpdate(ref SystemState state) { }
    public void OnDestroy(ref SystemState state) { }

    static async ValkarnTask InitializeAsync(WorldUnmanaged world, CancellationToken ct)
    {
        // Phase 1: Load data on a background thread.
        // RunOnThreadPool switches to a worker thread, runs the delegate,
        // and returns to the main thread automatically.
        var configData = await ValkarnTask.RunOnThreadPool(
            () => LoadFromDisk(),
            cancellationToken: ct);

        // Phase 2: Apply results on the main thread.
        // Check cancellation in case the World was destroyed during loading.
        ct.ThrowIfCancellationRequested();
        ApplyConfiguration(world, configData);
    }

    static ConfigData LoadFromDisk()
    {
        // Pure C# only — no Unity or ECS API calls allowed here.
        return new ConfigData { MaxEnemies = 100, SpawnRadius = 50f };
    }

    static void ApplyConfiguration(WorldUnmanaged world, ConfigData data)
    {
        // Safe: we are on the main thread.
        UnityEngine.Debug.Log($"Loaded config: MaxEnemies={data.MaxEnemies}");
    }

    struct ConfigData
    {
        public int   MaxEnemies;
        public float SpawnRadius;
    }
}
#endif
```

The AI throttling example (`Samples~/ECS/AISystemExample.cs`) builds on this pattern and adds `AsyncThrottle` to cap how many concurrent async tasks are in flight. See the Throttling documentation for details on that pattern.

---

## Limitations

The following constraints apply to all Burst/ECS async code. Violating them will produce editor errors, job safety violations, or silent data corruption.

### Inside Burst-compiled code

- **No managed types.** Burst cannot compile code that allocates, accesses, or references managed objects (classes, delegates, arrays, strings, `List<T>`, etc.). Only blittable structs and native containers are allowed.
- **No exceptions.** Burst does not support `try`/`catch`/`throw`. Use return codes or flags to communicate errors.
- **No `async`/`await`.** C# async state machines are managed and cannot be compiled by Burst. The `NativeScheduler` and `NativeTimerHeap` provide a side-channel for signalling managed continuations, but the continuations themselves run on the main thread.
- **No static mutable managed state.** Burst jobs can read static readonly fields but must not write to managed statics.

### Across await points in ECS systems

- **Entity lifetime.** Entities can be destroyed while an async method is suspended. Always call `entityManager.SafeEntityExists(entity)` after every `await` before writing back.
- **ComponentLookup staleness.** `ComponentLookup`, `RefRW`, and other chunk-pointer types become invalid after structural changes, which can occur on any frame. Do not cache these across `await` points. Re-acquire from `SystemState` after resuming, or use `EntityManager` directly.
- **`ref` parameters.** Async methods cannot have `ref`, `in`, or `out` parameters (C# error CS1988). Extract all ECS data synchronously in the synchronous `OnUpdate` method and pass it to the async method by value.
- **`SystemAPI` in async methods.** `SystemAPI` is source-generated and only works inside partial `ISystem` methods. It is not available in `async` methods. Perform all `SystemAPI` queries before the first `await`.
- **Thread safety.** `EntityManager`, `ComponentLookup`, and structural changes are main-thread-only. Use `ValkarnTask.RunOnThreadPool` only for pure C# computation with no Unity or ECS API calls.
