---
sidebar_position: 4
title: Features
---

# Features

Complete feature reference for Valkarn Tasks.

---

## Core async primitive

### ValkarnTask struct

A zero-allocation, struct-based async return type that replaces both `UniTask` and Unity's `Awaitable`.

```csharp
async ValkarnTask LoadLevel() { ... }
async ValkarnTask<int> CountEnemies() { ... }
```

- **Zero-allocation synchronous fast path** — if the method completes without ever suspending, zero heap allocations occur.
- **Pooled async path** — if the method suspends, the state machine runner is taken from a bounded, shrinkable pool. Zero boxing, bounded pools with automatic trimming.
- **IL2CPP-first pooling** — pool operations on the main thread use zero atomics. IL2CPP penalizes `Volatile` by 9.2× and `Interlocked` by 2.9× vs Mono; Valkarn avoids both on the hot path.
- **Generational token safety** — a `uint` generation counter per pool slot. 4,294,967,296 cycles per slot before collision — impossible in practice. (UniTask uses a `short` token: collision after ~18 minutes of active async work.)

### Result\<T\> — exception-free error handling

```csharp
var result = await loadTask.AsResult();

if (result.Succeeded)    Use(result.Value);
else if (result.IsCanceled) ShowRetry();
else                         Debug.LogError(result.Error);
```

`Result<T>` and `Result` are `readonly struct` values that represent the outcome of a task without throwing. Both support implicit conversion to `bool` (true when succeeded).

### Manual completion sources

```csharp
var promise = new ValkarnTask.Promise<string>();
promise.TrySetResult("done");
string value = await promise.Task;
```

Supports `TrySetResult`, `TrySetException`, and `TrySetCanceled`. Finalizer-based unobserved exception reporting ensures errors are never silently lost.

### Pooled completion sources

Auto-reset pooled variants for repeating patterns (used internally by channels and combinators):

```csharp
var source = ValkarnTask.PooledPromise<int>.Create(out uint token);
source.TrySetResult(42);
int value = await source.Task; // source returns to pool automatically
```

---

## Awaitable bridge

Transparent interop with Unity's `Awaitable` — no manual conversion:

```csharp
async ValkarnTask LoadGame()
{
    await SceneManager.LoadSceneAsync("Level2"); // Unity Awaitable — works directly
    await Resources.LoadAsync<Texture2D>("hero"); // Unity Awaitable — works directly
    await ValkarnTask.Delay(1000);                // Valkarn native
}
```

The source generator detects `Awaitable` awaits and generates the adapter automatically.

---

## Lifecycle cancellation

### Automatic (source-generated)

Mark the class `partial` — the source generator does the rest:

```csharp
public partial class EnemySpawner : MonoBehaviour
{
    async ValkarnTask Start()
    {
        while (true)
        {
            await ValkarnTask.Delay(3000);
            SpawnWave();
            // Automatically cancelled when this GameObject is destroyed.
        }
    }
}
```

- **MonoBehaviour** — bound to `destroyCancellationToken`
- **ScriptableObject** — bound to application lifetime
- **Plain class** — no automatic binding (manual token required)

### Opt-out

```csharp
[NoAutoCancel]
async ValkarnTask BackgroundSync()
{
    await SyncToServer(); // not auto-cancelled on destroy
}
```

### Manual CancellationToken override

Passing an explicit `CancellationToken` overrides the auto-injected lifecycle token:

```csharp
async ValkarnTask DoWork(CancellationToken ct)
{
    await ValkarnTask.Delay(1000, ct);
}
```

### No sibling cancellation

Tasks never cancel sibling tasks. `WhenAll` waits for ALL tasks; `WhenAny` returns the first result but losing tasks continue running. This prevents data corruption when tasks have side effects.

---

## Critical sections

For operations that must not be interrupted by lifecycle cancellation:

```csharp
async ValkarnTask SaveProgress()
{
    var data = await LoadPlayerData();        // cancellable

    await using (ValkarnTask.Critical())
    {
        await db.Insert(data);               // NOT cancelled even if GO is destroyed
        await db.Commit();
    }                                        // pending cancellation applies here

    await SendNotification();                // cancellable again
}
```

Inside a critical section, cancellation is **deferred** — not ignored. When the section ends, pending cancellation is applied.

---

## Combinators

### WhenAll (typed)

```csharp
// Direct — throws if any task fails
var (enemies, map) = await ValkarnTask.WhenAll(LoadEnemies(), LoadMap());

// Safe — wrap with AsResult() for non-throwing behavior
var (a, b) = await ValkarnTask.WhenAll(
    LoadEnemies().AsResult(), LoadMap().AsResult());
```

Zero-allocation sync fast path when all tasks are already completed. `IEnumerable<ValkarnTask<T>>` overload uses `ArrayPool<T>` for internal arrays.

### WhenAll (void)

```csharp
await ValkarnTask.WhenAll(SaveA(), SaveB(), SaveC());
await ValkarnTask.WhenAll(taskList); // IEnumerable<ValkarnTask>
```

### WhenAny

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(DownloadFromA(), DownloadFromB());
```

Returns the first completed result. Losing tasks continue running naturally.

### Fire-and-forget

```csharp
SendAnalytics("event").Forget();

[FireAndForget]
async ValkarnTask SendAnalytics(string eventName) { ... }
// Callers need no .Forget() — no warning generated
```

`.Forget()` routes errors to `ValkarnTask.UnobservedException`. Never silently swallowed.

### AsNonGeneric

```csharp
ValkarnTask voidTask = typedTask.AsNonGeneric();
```

---

## Time and delay

```csharp
await ValkarnTask.Delay(1000);                                    // milliseconds
await ValkarnTask.Delay(TimeSpan.FromSeconds(2));                  // TimeSpan
await ValkarnTask.Delay(1000, DelayType.UnscaledDeltaTime);       // ignore timescale
await ValkarnTask.Delay(1000, DelayType.Realtime);                 // Stopwatch-based

await ValkarnTask.Yield();                                         // next PlayerLoop tick
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);             // specific timing
await ValkarnTask.NextFrame();                                      // guaranteed next frame
await ValkarnTask.DelayFrame(5);                                    // N frames

await ValkarnTask.WaitUntil(() => player.IsReady);
await ValkarnTask.WaitWhile(() => isLoading);
```

---

## Thread switching

```csharp
async ValkarnTask ProcessData()
{
    var raw = await DownloadData();

    await ValkarnTask.SwitchToThreadPool();
    var processed = HeavyComputation(raw);  // background thread

    await ValkarnTask.SwitchToMainThread();
    ApplyToGameObject(processed);           // main thread
}
```

---

## 16 PlayerLoop timings

| Group | Timings |
|-------|---------|
| Initialization | `Initialization`, `LastInitialization` |
| EarlyUpdate | `EarlyUpdate`, `LastEarlyUpdate` |
| FixedUpdate | `FixedUpdate`, `LastFixedUpdate` |
| PreUpdate | `PreUpdate`, `LastPreUpdate` |
| Update | `Update`, `LastUpdate` |
| PreLateUpdate | `PreLateUpdate`, `LastPreLateUpdate` |
| PostLateUpdate | `PostLateUpdate`, `LastPostLateUpdate` |
| TimeUpdate | `TimeUpdate`, `LastTimeUpdate` |

All operations default to `PlayerLoopTiming.Update` unless specified otherwise.

---

## Channels

```csharp
// Unbounded
var channel = ValkarnTask.Channel.CreateUnbounded<EnemySpawnRequest>();

// Bounded — backpressure when full
var channel = ValkarnTask.Channel.CreateBounded<LogEntry>(capacity: 100);

// Multi-consumer
var channel = ValkarnTask.Channel.CreateUnbounded<WorkItem>(multiConsumer: true);

// Producer
await channel.Writer.WriteAsync(entry);
bool accepted = channel.Writer.TryWrite(entry);

// Consumer
await foreach (var item in channel.Reader.ReadAllAsync())
    Process(item);

// Completion
channel.Writer.Complete();
await channel.Reader.Completion;
```

---

## Deterministic testing (TestClock)

```csharp
[Test]
public void Respawn_WaitsThreeSeconds()
{
    var clock = new TestClock();
    var task = spawner.RespawnEnemy();

    clock.Advance(TimeSpan.FromSeconds(2));
    Assert.IsFalse(task.IsCompleted);

    clock.Advance(TimeSpan.FromSeconds(1));
    Assert.IsTrue(task.IsCompleted);
}
```

All time-dependent operations read from `TimeProvider.Current`. In tests, replace it with `TestClock`. `AdvanceFrame()` simulates a single player loop tick.

---

## Job System bridge

```csharp
var job = new PathfindingJob { start = a, end = b, results = results };
await job.ScheduleAsync();
// results NativeArray is readable immediately after await

await job.ScheduleParallelAsync(dataCount, batchSize); // IJobParallelFor

// Cancellation — complete the job handle before reporting cancellation (no job leak)
await job.ScheduleAsync(cancellationToken);
```

---

## Compile-time diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| TT001 | Warning | Double-await / use-after-free on a `ValkarnTask` |
| TT002 | Error | `async ValkarnTask` result used as expression statement — must be awaited or `.Forget()` |
| TT010 | Info | Async method in MonoBehaviour auto-cancelled on Destroy |
| TT011 | Warning | `WhenAll` contains tasks with different lifetimes |
| TT012 | Warning | Async loop without cancellation check (potential zombie loop) |
| TT013 | Warning | `ValkarnTask` returned but never awaited and not explicitly discarded |
| TT014 | Warning | `[NoAutoCancel]` without manual `CancellationToken` parameter |
| TT015 | Info | Awaiting `Awaitable` inside `ValkarnTask` — bridge adapter generated |
| TT016 | Warning | Async method with no await expression |
| TT017 | Warning | `[FireAndForget]` on `ValkarnTask<T>` — discards return value |

---

## Pool management

Every async method runner is pooled via `ValkarnPool<T>`:

- **Main thread** — lock-free stack, zero atomics
- **Background threads** — Treiber lock-free stack with CAS operations
- **Frame-based trimming** — every 300 frames (~5s at 60fps), excess objects are released gradually
- **Never shrinks** below configurable minimum (default: 8)

Monitor at runtime:

```csharp
foreach (var (type, size, maxSize) in ValkarnTask.GetPoolInfo())
    Debug.Log($"{type}: {size}/{maxSize}");
```

---

## ValkarnTaskSettings

Configure via `ScriptableObject` (`Assets > Create > Valkarn > Tasks > Task Settings`, place in `Resources/`):

| Setting | Default | Description |
|---------|---------|-------------|
| `DefaultMaxPoolSize` | 256 | Max items per pool type |
| `MinPoolSize` | 8 | Never trim below this |
| `TrimCheckInterval` | 300 | Frames between trim checks |
| `TrimHysteresisCount` | 2 | Consecutive checks before trimming |
| `TrimReleaseRatio` | 0.25 | Fraction of excess released per cycle |
| `EnableAutoCancel` | true | Auto-bind MonoBehaviour tasks to destroyCancellationToken |
| `LogUnobservedCancellations` | false | Log unobserved cancellations as warnings |
| `MaxExceptionLogsPerFrame` | 10 | Cap exception logs per frame |

---

## Error handling

```csharp
// Unobserved exceptions — fired deterministically at pool return
ValkarnTask.UnobservedException += ex =>
    Debug.LogError($"Unobserved: {ex}");
```

- `WhenAll` — throws first exception, routes additional ones to `UnobservedException`
- `WhenAny` — winner's exception is thrown; loser faults go to `UnobservedException`
- Lifecycle cancellation — `OperationCanceledException` suppressed by default (configurable)

### Factory methods

```csharp
ValkarnTask.CompletedTask          // void, zero-alloc
ValkarnTask.FromResult<T>(value)   // typed, zero-alloc
ValkarnTask.FromException(ex)      // faulted
ValkarnTask.FromException<T>(ex)
ValkarnTask.FromCanceled()         // canceled
ValkarnTask.FromCanceled<T>()
ValkarnTask.Never                  // never completes (sentinel for WhenAny)
```
