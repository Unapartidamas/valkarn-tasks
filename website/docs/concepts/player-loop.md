---
sidebar_position: 1
title: Unity PlayerLoop Integration
---

# Unity PlayerLoop Integration

ValkarnTasks does not use threads or background schedulers to resume your `async` methods. Everything runs on Unity's main thread, driven by Unity's own PlayerLoop system. Understanding how this works will help you pick the right timing for your use case and reason about exactly when your code resumes after an `await`.

## What the Unity PlayerLoop Is

Unity's PlayerLoop is the internal engine loop that drives every frame. It is not a single `Update()` call — it is a hierarchical sequence of phases that run in a defined order every frame:

```
Initialization
EarlyUpdate
FixedUpdate      (repeated if physics is stepping)
PreUpdate
Update
PreLateUpdate
PostLateUpdate
TimeUpdate
```

Each of these top-level phases contains sub-systems that Unity (and third-party packages) insert to run their logic at specific points. `MonoBehaviour.Update()`, for example, runs inside the `Update` phase. `MonoBehaviour.LateUpdate()` runs inside `PreLateUpdate`.

Because ValkarnTasks hooks directly into this loop, `await ValkarnTask.Yield()` does not park a thread — it registers a callback that Unity calls at the next tick of the chosen phase, then returns immediately.

## The 16 PlayerLoop Timings

ValkarnTasks injects at 16 points: one at the **start** and one at the **end** of each of the 8 Unity PlayerLoop phases. The `Last` variants are appended at the tail of their parent phase's sub-system list; the plain variants are prepended at the head.

| Value | Enum integer | Parent phase | Position in parent |
|---|---|---|---|
| `Initialization` | 0 | `UnityEngine.PlayerLoop.Initialization` | First |
| `LastInitialization` | 1 | `UnityEngine.PlayerLoop.Initialization` | Last |
| `EarlyUpdate` | 2 | `UnityEngine.PlayerLoop.EarlyUpdate` | First |
| `LastEarlyUpdate` | 3 | `UnityEngine.PlayerLoop.EarlyUpdate` | Last |
| `FixedUpdate` | 4 | `UnityEngine.PlayerLoop.FixedUpdate` | First |
| `LastFixedUpdate` | 5 | `UnityEngine.PlayerLoop.FixedUpdate` | Last |
| `PreUpdate` | 6 | `UnityEngine.PlayerLoop.PreUpdate` | First |
| `LastPreUpdate` | 7 | `UnityEngine.PlayerLoop.PreUpdate` | Last |
| `Update` | 8 | `UnityEngine.PlayerLoop.Update` | First |
| `LastUpdate` | 9 | `UnityEngine.PlayerLoop.Update` | Last |
| `PreLateUpdate` | 10 | `UnityEngine.PlayerLoop.PreLateUpdate` | First |
| `LastPreLateUpdate` | 11 | `UnityEngine.PlayerLoop.PreLateUpdate` | Last |
| `PostLateUpdate` | 12 | `UnityEngine.PlayerLoop.PostLateUpdate` | First |
| `LastPostLateUpdate` | 13 | `UnityEngine.PlayerLoop.PostLateUpdate` | Last |
| `TimeUpdate` | 14 | `UnityEngine.PlayerLoop.TimeUpdate` | First |
| `LastTimeUpdate` | 15 | `UnityEngine.PlayerLoop.TimeUpdate` | Last |

`Update` (value 8) is the **default** for all ValkarnTasks operations — `Yield()`, `Delay()`, `WaitUntil()`, `WaitWhile()`, `NextFrame()`, and `DelayFrame()`.

## Marker Structs and Idempotent Injection

To inject into Unity's PlayerLoop, ValkarnTasks creates one `PlayerLoopSystem` per timing. Each system is identified by a unique marker struct defined inside `PlayerLoopHelper`:

```csharp
struct ValkarnTaskInitialization     { }
struct ValkarnTaskLastInitialization { }
struct ValkarnTaskEarlyUpdate        { }
struct ValkarnTaskLastEarlyUpdate    { }
struct ValkarnTaskFixedUpdate        { }
struct ValkarnTaskLastFixedUpdate    { }
struct ValkarnTaskPreUpdate          { }
struct ValkarnTaskLastPreUpdate      { }
struct ValkarnTaskUpdate             { }
struct ValkarnTaskLastUpdate         { }
struct ValkarnTaskPreLateUpdate      { }
struct ValkarnTaskLastPreLateUpdate  { }
struct ValkarnTaskPostLateUpdate     { }
struct ValkarnTaskLastPostLateUpdate { }
struct ValkarnTaskTimeUpdate         { }
struct ValkarnTaskLastTimeUpdate     { }
```

Before injecting, `PlayerLoopHelper` checks whether `ValkarnTaskUpdate` already appears anywhere in the current PlayerLoop tree. If it does, injection is skipped. This makes the injection **idempotent** — calling `Init()` multiple times (which can happen in the editor) never results in duplicate systems being registered.

Injection always reads the **current** PlayerLoop with `PlayerLoop.GetCurrentPlayerLoop()`, never `GetDefaultPlayerLoop()`. This means any systems previously installed by other packages are preserved.

## ContinuationQueue — One-Shot Callbacks

When you `await ValkarnTask.Yield()` (or anything that suspends for exactly one tick), the compiler-generated state machine calls `OnCompleted` on the awaiter. The awaiter calls `PlayerLoopHelper.AddContinuation(timing, action, state)`, which enqueues the callback into the `ContinuationQueue` for that timing.

`ContinuationQueue` uses a **double-buffer** design:

1. **Active buffer** (`actionList`): holds continuations queued before and during the current drain.
2. **Waiting buffer** (`waitingList`): catches continuations enqueued _while_ the active buffer is being drained (i.e., re-entrant enqueues from within a continuation itself).
3. **Cross-thread Treiber stack** (`crossThreadHead`): continuations posted from background threads land here via a lock-free compare-and-swap. At the start of each drain, the entire stack is atomically claimed and moved into the active buffer.

Each PlayerLoop tick, `ContinuationQueue.Run()` executes this sequence:

```
1. Drain crossThreadHead → actionList     (lock-free atomic take, then copy under SpinLock)
2. Snapshot count, set isDraining = true  (under SpinLock)
3. Execute all callbacks                  (outside lock, refs cleared for GC)
4. Swap actionList ↔ waitingList          (under SpinLock, set isDraining = false)
```

After the warmup of roughly one or two frames, the internal arrays stabilize at their high-water mark and the queue runs with **zero allocations**.

Cross-thread nodes are pooled in a bounded lock-free pool (max 1,024 nodes) to avoid allocating on every background-thread enqueue.

## PlayerLoopRunner — Recurring Items

Some operations must be checked on every tick until they complete: `Delay()`, `DelayFrame()`, `WaitUntil()`, `WaitWhile()`, and `NextFrame()`. These implement the `IPlayerLoopItem` interface:

```csharp
internal interface IPlayerLoopItem
{
    // Return true to keep running; return false to remove from the runner.
    bool MoveNext();
}
```

Items are added to `PlayerLoopRunner` via `PlayerLoopHelper.AddAction(timing, item)`. Each tick, `PlayerLoopRunner.Run()` iterates all registered items and calls `MoveNext()` on each one. Items that return `false` are removed via **in-place compaction** — the array is compacted in a single pass, preserving insertion order. Items added during a `Run()` call are safely appended and will be picked up next tick.

```
Tick N:
  ContinuationQueue.Run()   → resume all one-shot awaiters
  PlayerLoopRunner.Run()    → tick all recurring items (Delay, WaitUntil, ...)
```

Both run for every one of the 16 timings, in the order the engine calls them.

The `Update` timing also tracks the global frame count and periodically triggers object pool trimming (every `ValkarnTask.TrimCheckInterval` frames).

## Initialization — RuntimeInitializeOnLoadMethod

ValkarnTasks initializes at `RuntimeInitializeLoadType.SubsystemRegistration`, the earliest hook Unity provides, which fires before `Awake()` on any scene object:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void Init()
{
    // 1. Capture the main thread ID for thread-safety checks
    // 2. Create ContinuationQueue and PlayerLoopRunner for all 16 timings
    // 3. Inject into PlayerLoop (idempotent)
    // 4. Register play-mode-state-changed callback (editor only)
}
```

The main thread ID is captured at this point and shared with all pool and queue subsystems so they can distinguish main-thread enqueues (SpinLock path) from cross-thread enqueues (Treiber stack path).

## Domain Reload Handling (Editor)

In the Unity Editor, entering or exiting Play Mode triggers a domain reload. Static state from the previous play session would otherwise persist and cause stale references.

ValkarnTasks handles this with an `EditorApplication.playModeStateChanged` callback registered during `Init()`. When the state transitions to `ExitingPlayMode` or `EnteredEditMode`, the following cleanup runs:

```csharp
// Reset all queues and runners
for (int i = 0; i < 16; i++)
{
    s_continuationQueues[i] = null;
    s_playerLoopRunners[i] = null;
}

// Reset other subsystems
ValkarnTask.ResetStatics();
TimeProvider.ResetToDefault();   // reverts to UnityTimeProvider
PoolRegistry.Clear();
ContinuationQueue.ContinuationNode.ResetPool();
```

When Play Mode is subsequently entered again, `Init()` fires via `RuntimeInitializeOnLoadMethod` and fresh queues and runners are allocated. The PlayerLoop injection check (`HasValkarnTaskSystems`) prevents the marker systems from being inserted a second time if the domain was not fully unloaded.

## Choosing the Right Timing

Most code should use the default `Update` timing. Only reach for others when you have a specific reason.

| Timing | When to use it |
|---|---|
| `Initialization` / `LastInitialization` | Very early frame setup; rarely needed in game code |
| `EarlyUpdate` / `LastEarlyUpdate` | Input sampling; runs before physics and before `Update` |
| `FixedUpdate` / `LastFixedUpdate` | Physics-synchronized logic; matches `MonoBehaviour.FixedUpdate` cadence |
| `PreUpdate` / `LastPreUpdate` | Before Unity's main update dispatch; useful for custom scheduler pre-pass |
| **`Update`** (default) | **Standard game logic; matches `MonoBehaviour.Update`** |
| `LastUpdate` | Logic that must run after all `MonoBehaviour.Update` calls |
| `PreLateUpdate` / `LastPreLateUpdate` | Matches `MonoBehaviour.LateUpdate`; camera and transform follow |
| `PostLateUpdate` / `LastPostLateUpdate` | After rendering submission; UI finalization, screenshot capture |
| `TimeUpdate` / `LastTimeUpdate` | Time value synchronization; very rarely needed |

```csharp
// Resume next Update tick (default)
await ValkarnTask.Yield();

// Resume at the start of the next FixedUpdate (physics-safe)
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// Wait 2 seconds, advancing with unscaled time, checked in LateUpdate
await ValkarnTask.Delay(
    TimeSpan.FromSeconds(2),
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.PreLateUpdate);

// Wait until a condition is true, checked after all LateUpdate calls
await ValkarnTask.WaitUntil(() => _ready, PlayerLoopTiming.LastPreLateUpdate);
```
