---
sidebar_position: 1
title: ValkarnTask
---

# ValkarnTask

`ValkarnTask` is the core awaitable type. It is a `readonly struct` — no heap allocation on the happy path (when the task completes synchronously or via pool).

```csharp
namespace UnaPartidaMas.Valkarn.Tasks;

public readonly struct ValkarnTask : IEquatable<ValkarnTask>
```

---

## Static factory methods

### Delay

```csharp
// Milliseconds (uses PlayerLoopTiming.Update by default)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, CancellationToken cancellationToken)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, PlayerLoopTiming timing, CancellationToken cancellationToken)

// TimeSpan overloads
ValkarnTask ValkarnTask.Delay(TimeSpan delay)
ValkarnTask ValkarnTask.Delay(TimeSpan delay, PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Delay(TimeSpan delay, CancellationToken cancellationToken)
```

### Yield

```csharp
// Yield to the next frame (Update timing by default)
ValkarnTask ValkarnTask.Yield()
ValkarnTask ValkarnTask.Yield(PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Yield(CancellationToken cancellationToken)
```

### WhenAll

```csharp
ValkarnTask ValkarnTask.WhenAll(ValkarnTask task1, ValkarnTask task2)
ValkarnTask ValkarnTask.WhenAll(ValkarnTask task1, ValkarnTask task2, ValkarnTask task3)
// ... up to 8 parameters

// With return values — tuple destructuring supported
ValkarnTask<(T1, T2)>     ValkarnTask.WhenAll<T1, T2>(ValkarnTask<T1>, ValkarnTask<T2>)
ValkarnTask<(T1, T2, T3)> ValkarnTask.WhenAll<T1, T2, T3>(...)
// ... up to 8

// Collection overloads
ValkarnTask ValkarnTask.WhenAll(IEnumerable<ValkarnTask> tasks)
ValkarnTask<T[]> ValkarnTask.WhenAll<T>(IEnumerable<ValkarnTask<T>> tasks)
```

### WhenAny

```csharp
ValkarnTask<int> ValkarnTask.WhenAny(ValkarnTask task1, ValkarnTask task2)   // returns index of first completed
ValkarnTask<T>   ValkarnTask.WhenAny<T>(ValkarnTask<T> task1, ValkarnTask<T> task2) // returns value of first
```

### Thread switching

```csharp
ValkarnTask ValkarnTask.SwitchToMainThread()
ValkarnTask ValkarnTask.SwitchToThreadPool()
ValkarnTask ValkarnTask.SwitchToSynchronizationContext(SynchronizationContext context)
```

### Completed / Never

```csharp
ValkarnTask ValkarnTask.CompletedTask      // pre-completed, zero alloc
ValkarnTask<T> ValkarnTask.FromResult<T>(T value)
ValkarnTask ValkarnTask.FromCanceled(CancellationToken ct)
ValkarnTask<T> ValkarnTask.FromCanceled<T>(CancellationToken ct)
ValkarnTask ValkarnTask.FromException(Exception ex)
ValkarnTask<T> ValkarnTask.FromException<T>(Exception ex)
ValkarnTask ValkarnTask.Never                // never completes
```

### Run

```csharp
// Runs delegate on thread pool
ValkarnTask ValkarnTask.Run(Action action)
ValkarnTask ValkarnTask.Run(Func<ValkarnTask> factory)
ValkarnTask<T> ValkarnTask.Run<T>(Func<T> func)
ValkarnTask<T> ValkarnTask.Run<T>(Func<ValkarnTask<T>> factory)
```

### Forget

```csharp
// Fire-and-forget: suppress CS4014 warning safely
void ValkarnTask.Forget(ValkarnTask task)
void ValkarnTask.Forget(ValkarnTask task, Action<Exception> exceptionHandler)
```

---

## Instance members

```csharp
ValkarnTaskStatus Status { get; }
bool IsCompleted { get; }
bool IsCompletedSuccessfully { get; }
bool IsFaulted { get; }
bool IsCanceled { get; }

// Get result (throws if faulted/canceled)
void GetResult()

// Awaiter
ValkarnTaskAwaiter GetAwaiter()

// Convert to ValueTask
ValueTask AsValueTask()
```

---

## Wait conditions

```csharp
// Wait until a condition is true
ValkarnTask ValkarnTask.WaitUntil(Func<bool> condition)
ValkarnTask ValkarnTask.WaitUntil(Func<bool> condition, PlayerLoopTiming timing)

// Wait while a condition is true
ValkarnTask ValkarnTask.WaitWhile(Func<bool> condition)

// Wait for a fixed number of frames
ValkarnTask ValkarnTask.WaitForFrames(int frameCount)
ValkarnTask ValkarnTask.NextFrame()
```

---

## Pool diagnostics

```csharp
// Returns (Type type, int currentSize, int maxSize) per pool
IEnumerable<(Type, int, int)> ValkarnTask.GetPoolInfo()
```

Available in the **Window → Valkarn Tasks → Task Tracker** window.
