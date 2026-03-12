---
sidebar_position: 1
title: VlkTask
---

# VlkTask

`VlkTask` core awaitable type है। यह एक `readonly struct` है — happy path पर (जब task synchronously या pool के माध्यम से complete होता है) कोई heap allocation नहीं।

```csharp
namespace UnaPartidaMas.Valkarn.Tasks;

public readonly struct VlkTask : IEquatable<VlkTask>
```

---

## Static factory methods

### Delay

```csharp
// Milliseconds (default रूप से PlayerLoopTiming.Update उपयोग करता है)
VlkTask VlkTask.Delay(int millisecondsDelay)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(int millisecondsDelay, CancellationToken cancellationToken)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing, CancellationToken cancellationToken)

// TimeSpan overloads
VlkTask VlkTask.Delay(TimeSpan delay)
VlkTask VlkTask.Delay(TimeSpan delay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(TimeSpan delay, CancellationToken cancellationToken)
```

### Yield

```csharp
// अगले frame पर Yield करें (default रूप से Update timing)
VlkTask VlkTask.Yield()
VlkTask VlkTask.Yield(PlayerLoopTiming timing)
VlkTask VlkTask.Yield(CancellationToken cancellationToken)
```

### WhenAll

```csharp
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2)
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2, VlkTask task3)
// ... 8 parameters तक

// Return values के साथ — tuple destructuring supported
VlkTask<(T1, T2)>     VlkTask.WhenAll<T1, T2>(VlkTask<T1>, VlkTask<T2>)
VlkTask<(T1, T2, T3)> VlkTask.WhenAll<T1, T2, T3>(...)
// ... 8 तक

// Collection overloads
VlkTask VlkTask.WhenAll(IEnumerable<VlkTask> tasks)
VlkTask<T[]> VlkTask.WhenAll<T>(IEnumerable<VlkTask<T>> tasks)
```

### WhenAny

```csharp
VlkTask<int> VlkTask.WhenAny(VlkTask task1, VlkTask task2)   // पहले complete का index return करता है
VlkTask<T>   VlkTask.WhenAny<T>(VlkTask<T> task1, VlkTask<T> task2) // पहले का value return करता है
```

### Thread switching

```csharp
VlkTask VlkTask.SwitchToMainThread()
VlkTask VlkTask.SwitchToThreadPool()
VlkTask VlkTask.SwitchToSynchronizationContext(SynchronizationContext context)
```

### Completed / Never

```csharp
VlkTask VlkTask.CompletedTask      // pre-completed, zero alloc
VlkTask<T> VlkTask.FromResult<T>(T value)
VlkTask VlkTask.FromCanceled(CancellationToken ct)
VlkTask<T> VlkTask.FromCanceled<T>(CancellationToken ct)
VlkTask VlkTask.FromException(Exception ex)
VlkTask<T> VlkTask.FromException<T>(Exception ex)
VlkTask VlkTask.Never                // कभी complete नहीं होता
```

### Run

```csharp
// thread pool पर delegate run करता है
VlkTask VlkTask.Run(Action action)
VlkTask VlkTask.Run(Func<VlkTask> factory)
VlkTask<T> VlkTask.Run<T>(Func<T> func)
VlkTask<T> VlkTask.Run<T>(Func<VlkTask<T>> factory)
```

### Forget

```csharp
// Fire-and-forget: CS4014 warning safely suppress करें
void VlkTask.Forget(VlkTask task)
void VlkTask.Forget(VlkTask task, Action<Exception> exceptionHandler)
```

---

## Instance members

```csharp
VlkTaskStatus Status { get; }
bool IsCompleted { get; }
bool IsCompletedSuccessfully { get; }
bool IsFaulted { get; }
bool IsCanceled { get; }

// Result प्राप्त करें (faulted/canceled होने पर throws)
void GetResult()

// Awaiter
VlkTaskAwaiter GetAwaiter()

// ValueTask में convert करें
ValueTask AsValueTask()
```

---

## Wait conditions

```csharp
// एक condition true होने तक wait करें
VlkTask VlkTask.WaitUntil(Func<bool> condition)
VlkTask VlkTask.WaitUntil(Func<bool> condition, PlayerLoopTiming timing)

// एक condition true होते तक wait करें
VlkTask VlkTask.WaitWhile(Func<bool> condition)

// एक fixed number of frames के लिए wait करें
VlkTask VlkTask.WaitForFrames(int frameCount)
VlkTask VlkTask.NextFrame()
```

---

## Pool diagnostics

```csharp
// प्रति pool (Type type, int currentSize, int maxSize) return करता है
IEnumerable<(Type, int, int)> VlkTask.GetPoolInfo()
```

**Window → Valkarn Tasks → Task Tracker** window में available।
