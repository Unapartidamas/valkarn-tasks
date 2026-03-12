---
sidebar_position: 2
title: Object Pooling
---

# Object Pooling

Valkarn Tasks eliminates GC allocations on common async paths by pooling the objects that back each `ValkarnTask`. This page explains the pool architecture — how objects are stored, how they are acquired and returned, and what lifecycle guarantees the system provides.

---

## Overview

When an `async` method suspends, the library needs a place to store the compiler-generated state machine and a completion mechanism that the awaiter can subscribe to. In `System.Threading.Tasks`, this is the `Task` object itself — one heap allocation per call. In Valkarn Tasks, this role is played by pooled objects that implement `ValkarnTask.ISource`.

The pool design has three goals:

1. **Zero atomics on the main thread hot path.** Unity's game loop is single-threaded by convention. Rent and return from the main thread should be plain reads and writes.
2. **Safe cross-thread access.** Background tasks using `ValkarnTask.Run` operate on thread-pool threads. The pool must handle concurrent rent/return correctly.
3. **Bounded growth with adaptive trimming.** Pools should not grow without limit after a traffic spike, but they should not shrink so aggressively that they re-allocate constantly.

---

## `ValkarnTaskPool<T>`

`ValkarnTaskPool<T>` is the core pool class. It is `internal sealed` — you do not interact with it directly, but understanding it explains where your allocations go.

```
ValkarnTaskPool<T>
  |
  +-- fastItem: T            (single-slot cache, main thread only, plain read/write)
  |
  +-- stackHead: T           (Treiber stack head, CAS-based for cross-thread safety)
  +-- stackSize: int
  |
  +-- maxSize: int           (bounded by ValkarnTask.DefaultMaxPoolSize)
  +-- totalCreated: int      (tracks lifetime allocations for trim ratio)
```

### Fast slot (main thread)

The `fastItem` field is a single reserved slot for the most recently returned object. On the main thread, rent and return are a plain read and write — no atomics, no spinning. This covers the overwhelming majority of Unity game-loop operations.

```
Rent (main thread):
  fastItem != null  →  take it (fastItem = null), return it   [zero atomics]
  fastItem == null  →  fall through to Treiber stack

Return (main thread):
  fastItem == null  →  fastItem = item                        [zero atomics]
  fastItem != null  →  fall through to Treiber stack
```

### Treiber stack (overflow / background threads)

When the fast slot is occupied (or when the calling thread is not the main thread), the pool uses a lock-free Treiber stack — a classic intrusive linked list using compare-and-swap (CAS):

```
Rent (any thread):
  while (true):
    head = Volatile.Read(stackHead)
    if head == null: return null (pool empty)
    next = head.NextNode
    if CAS(stackHead, next, head) == head: return head  // won the race
    spinner.SpinOnce()                                   // lost, retry

Return (any thread):
  if stackSize >= maxSize: return false (pool full, drop item)
  while (true):
    head = Volatile.Read(stackHead)
    item.NextNode = head
    if CAS(stackHead, item, head) == head: stackSize++; return true
    spinner.SpinOnce()
```

The stack is intrusive: each pooled object stores its own `NextNode` pointer, so no external wrapper node is needed. This is enforced by the `IPoolNode<T>` interface.

### Thread routing

```csharp
internal static class ValkarnTaskPoolShared
{
    internal static volatile int MainThreadId;
}
```

All pool instances share a single `MainThreadId`. Rent/return operations check `Thread.CurrentThread.ManagedThreadId == MainThreadId` to route to the correct path. The `volatile` field ensures cross-thread visibility after the ID is published at startup.

---

## `IPoolNode<T>`

Any type that participates in the pool must implement this interface:

```csharp
internal interface IPoolNode<T> where T : class
{
    ref T NextNode { get; }
}
```

`ref T NextNode` returns a reference to the field inside the object that stores the next-pointer. The pool writes directly to this field via the ref, eliminating any separate wrapper node. All pooled types in the library — runners, promises, combinators — implement this interface by declaring a private field and exposing it:

```csharp
// Example from PooledPromise<T>
PooledPromise<T> nextNode;
public ref PooledPromise<T> NextNode => ref nextNode;
```

---

## Pool lifecycle: acquire, use, return

The full lifecycle of a pooled object is:

```
Caller invokes async method
  |
  +--> AsyncValkarnTaskMethodBuilder.AwaitUnsafeOnCompleted()
        |
        +--> runner == null? YES --> Pool.TryRent() ?? Pool.TrackNew(new Runner())
              |
              +--> runner stored in builder; state machine copied into runner
              |
              +--> awaiter.UnsafeOnCompleted(runner.MoveNextAction)
              |
              [... async work proceeds ...]
              |
              +--> runner.SetResult() / runner.SetException()
                    |
                    +--> ValkarnTaskCompletionCore.TrySetResult() --> continuation invoked
                          |
                          +--> caller's awaiter calls GetResult(token)
                                |
                                +--> core.GetResult(token) -- reads result or rethrows
                                |
                                +--> TryReturn():
                                      stateMachine = default
                                      core.Reset()           // increments generation
                                      Pool.TryReturn(this)
```

The `TryReturn` method always clears the state machine before calling `core.Reset()`. This ordering matters: `Reset()` increments the generation counter, making the slot visible as available to concurrent renters. If the state machine were cleared after `Reset()`, a renter on another thread could obtain the slot and have its state machine overwritten.

---

## `ValkarnTaskCompletionCore<TResult>`

`ValkarnTaskCompletionCore<TResult>` is an `internal struct` embedded inside every pooled object. It is the actual state machine for the promise — tracking completion state, storing results and errors, and resolving the race between `OnCompleted` (registering a continuation) and `TrySetResult` (signaling completion).

```
Fields:
  result:          TResult     -- the success value
  error:           object      -- ExceptionDispatchInfo or OperationCanceledException
  errorKind:       byte        -- 0=none, 1=faulted, 2=canceled(OCE), 3=canceled(EDI)
  generation:      int         -- monotonically increasing; cast to uint for token comparison
  completedCount:  int         -- 0=pending, 1=claimed, 2=completed (two-phase publish)
  continuation:    Action<object>
  continuationState: object
  hasUnhandledError: bool
```

### Two-phase completion protocol

Completion uses a two-phase CAS to be safe on ARM64 where store-release / load-acquire pairs are needed:

```
TrySetResult(value):
  Phase 1: CAS(completedCount, 0 -> 1)   -- claim exclusive ownership
  Phase 2: write result
  Phase 3: Volatile.Write(completedCount, 2)  -- publish with release semantics
  Phase 4: InvokeContinuation()
```

Readers use `Volatile.Read(completedCount)` (acquire semantics) before reading the result, ensuring they see the value written in Phase 2.

### Race resolution between `OnCompleted` and `TrySetResult`

Three patterns can occur:

```
Pattern A — OnCompleted first:
  OnCompleted stores continuation via CAS(continuation, null -> cont)
  TrySetResult reads non-null continuation -> invokes it

Pattern B — TrySetResult first (sync fast path):
  TrySetResult places ContinuationSentinel via CAS(continuation, null -> sentinel)
  OnCompleted reads sentinel -> invokes continuation inline immediately

Pattern C (concurrent race):
  C.1: OnCompleted wins CAS -> TrySetResult reads it -> invokes
  C.2: TrySetResult wins CAS (places sentinel) -> OnCompleted detects sentinel -> invokes inline
```

The sentinel is a static `Action<object>` object used purely as a marker value — it is never actually invoked as a delegate.

### Token validation and ABA safety

Every call to `GetStatus`, `GetResult`, and `OnCompleted` validates the `uint token` against the current `generation`. When `Reset()` calls `Interlocked.Increment(ref generation)`, any outstanding `ValkarnTask` struct holding the old token will receive an `InvalidOperationException` rather than silently operating on recycled state. A 32-bit generation counter wrapping around (requiring ~4 billion reuses of a single slot) is considered effectively impossible in practice.

### Reset and unobserved error reporting

`Reset()` is called at pool-return time. Before incrementing the generation, it checks whether an error was stored but never observed (i.e., `GetResult` was never called after a fault). If so, it publishes the exception through `ValkarnTask.UnobservedException`. Cancellation errors are only reported if `LogUnobservedCancellations` is enabled in `ValkarnTaskSettings`, since cancellation is often intentional.

For non-pooled `Promise` and `Promise<T>` objects, unobserved error reporting happens from the finalizer via `ReportUnobservedIfNeeded()`, which follows the same logic without clearing state.

---

## Pool configuration

Three settings control pool sizing. In Unity builds these read from a `ValkarnTaskSettings` ScriptableObject asset (with fallback defaults), and can be overridden at runtime via static properties:

```csharp
// Maximum objects per pool type (per TStateMachine or per promise type)
ValkarnTask.DefaultMaxPoolSize = 256;   // default: 256

// How many frames between trim checks (Unity PlayerLoop frames)
ValkarnTask.TrimCheckInterval = 300;    // default: 300 (~5 seconds at 60fps)

// Minimum objects to keep after a trim pass
ValkarnTask.MinPoolSize = 8;            // default: 8
```

`DefaultMaxPoolSize` is the ceiling applied at pool construction time. It is enforced per pool instance, not globally — a pool for `AsyncValkarnTaskRunner<LoadSceneStateMachine>` and a pool for `AsyncValkarnTaskRunner<FetchDataStateMachine>` each have their own ceiling.

### Pool trimming

The `PlayerLoopHelper` invokes `PoolRegistry.TrimAll(minPoolSize)` every `TrimCheckInterval` frames on the main thread. Each pool uses a hysteresis strategy:

```
Each trim check:
  currentSize = fastItem.count + stackSize
  if currentSize <= MinPoolSize: reset consecutive count, skip

  ratio = currentSize / totalCreated
  if ratio > 0.5 (pool holds > 50% of all ever-created objects):
    trimConsecutiveCount++
    if trimConsecutiveCount >= hysteresisThreshold:
      release some fraction (releaseRatio) of excess objects from the stack
      (fastItem is preserved — it is the most cache-friendly slot)
  else:
    reset trimConsecutiveCount
```

The hysteresis prevents a brief traffic spike from immediately causing all objects to be allocated then immediately trimmed. The fast slot is always preserved during trimming because it represents the most recently used item and is therefore most likely to be needed again.

---

## `PoolRegistry` and monitoring

Every `ValkarnTaskPool<T>` registers itself with the global `PoolRegistry` at construction time. The registry maintains a list of `IPoolInfo` references, which expose:

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

You can enumerate all active pools at runtime using the public API:

```csharp
foreach (var (type, size, maxSize) in ValkarnTask.GetPoolInfo())
{
    Debug.Log($"{type.Name}: {size}/{maxSize}");
}
```

This is the same data surfaced by the **Task Tracker** window in the Unity Editor. The window polls `GetPoolInfo()` and displays a live table of pool occupancy, letting you see whether pools are warmed up, whether any type is consistently hitting its ceiling, and whether trimming is working as expected.

Dead pool entries (where `IsAlive` returns false) are lazily pruned from the registry list during `GetAll()` and `TrimAll()` calls, preventing the registry from growing indefinitely if pool instances are somehow GC'd.

---

## `PooledPromise` and `PooledPromise<T>`

These are the pooled completion sources intended for use in custom async patterns — for example, wrapping a callback-based API or a repeating producer/consumer channel.

```csharp
// Acquire a pending promise from the pool
var promise = ValkarnTask.PooledPromise<string>.Create(out uint token);
ValkarnTask<string> task = promise.Task;

// Hand task to a consumer
// ... later, from any thread ...
promise.TrySetResult("hello");

// When the consumer awaits task and GetResult is called,
// the promise resets and returns to pool automatically.
```

Key characteristics:

- `Create(out uint token)` rents from the pool or allocates a new instance tracked by the pool.
- `CreateCompleted(T result, out uint token)` does the same but immediately signals the result, so the task is already complete when returned.
- After `GetResult` is called on the backing task, `TryReturn()` fires: the promise calls `core.Reset()` and returns itself to the pool.
- A double-return guard (`Interlocked.Exchange(ref returned, 1)`) prevents pool corruption if `GetResult` is called twice.

**Non-pooled alternative: `Promise` and `Promise<T>`.** These are heap-allocated classes not returned to a pool. Use them for long-lived operations where the lifetime is unpredictable or where the same source must outlive multiple await cycles. They rely on a finalizer to report unobserved exceptions.

---

## Combinator pools

`WhenAll` and `WhenAny` combinators also use pools. Each arity and type combination has its own pool:

| Combinator | Pool type |
|-----------|-----------|
| `WhenAll(task1, task2)` (typed) | `ValkarnTaskPool<WhenAllPromise<T1, T2>>` |
| `WhenAll(IEnumerable<ValkarnTask<T>>)` | `ValkarnTaskPool<WhenAllArrayPromise<T>>` |
| `WhenAll(task1, task2)` (void) | `ValkarnTaskPool<WhenAllVoidPromise2>` |
| `WhenAny(task1, task2)` (typed) | `ValkarnTaskPool<WhenAnyPromise2<T>>` |
| `WhenAny(IEnumerable<ValkarnTask<T>>)` | `ValkarnTaskPool<WhenAnyArrayPromise<T>>` |

Array-based combinators (`WhenAll<T>(IEnumerable<...>)` and `WhenAny<T>(IEnumerable<...>)`) use `System.Buffers.ArrayPool<T>.Shared` for their internal source/token arrays, so those arrays are also recycled rather than allocated fresh per call.

All combinators apply the same zero-alloc short-circuit: if all inputs are synchronously completed at the point `WhenAll` or `WhenAny` is called, a new pooled object is never created.

```csharp
// Zero allocation — both tasks are sync-completed
var t1 = ValkarnTask.FromResult(1);
var t2 = ValkarnTask.FromResult(2);
ValkarnTask<(int, int)> combined = ValkarnTask.WhenAll(t1, t2);
// combined.source == null; result is (1, 2) stored inline
```
