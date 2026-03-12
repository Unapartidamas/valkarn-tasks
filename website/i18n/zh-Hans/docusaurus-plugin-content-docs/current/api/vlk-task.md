---
sidebar_position: 1
title: ValkarnTask
---

# ValkarnTask

`ValkarnTask` 是核心可等待类型。它是一个 `readonly struct`——在快速路径上（当任务同步完成或通过对象池完成时）无堆分配。

```csharp
namespace UnaPartidaMas.Valkarn.Tasks;

public readonly struct ValkarnTask : IEquatable<ValkarnTask>
```

---

## 静态工厂方法

### Delay

```csharp
// 毫秒（默认使用 PlayerLoopTiming.Update）
ValkarnTask ValkarnTask.Delay(int millisecondsDelay)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, CancellationToken cancellationToken)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, PlayerLoopTiming timing, CancellationToken cancellationToken)

// TimeSpan 重载
ValkarnTask ValkarnTask.Delay(TimeSpan delay)
ValkarnTask ValkarnTask.Delay(TimeSpan delay, PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Delay(TimeSpan delay, CancellationToken cancellationToken)
```

### Yield

```csharp
// 让出到下一帧（默认 Update 时机）
ValkarnTask ValkarnTask.Yield()
ValkarnTask ValkarnTask.Yield(PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Yield(CancellationToken cancellationToken)
```

### WhenAll

```csharp
ValkarnTask ValkarnTask.WhenAll(ValkarnTask task1, ValkarnTask task2)
ValkarnTask ValkarnTask.WhenAll(ValkarnTask task1, ValkarnTask task2, ValkarnTask task3)
// ... 最多 8 个参数

// 带返回值——支持元组解构
ValkarnTask<(T1, T2)>     ValkarnTask.WhenAll<T1, T2>(ValkarnTask<T1>, ValkarnTask<T2>)
ValkarnTask<(T1, T2, T3)> ValkarnTask.WhenAll<T1, T2, T3>(...)
// ... 最多 8 个

// 集合重载
ValkarnTask ValkarnTask.WhenAll(IEnumerable<ValkarnTask> tasks)
ValkarnTask<T[]> ValkarnTask.WhenAll<T>(IEnumerable<ValkarnTask<T>> tasks)
```

### WhenAny

```csharp
ValkarnTask<int> ValkarnTask.WhenAny(ValkarnTask task1, ValkarnTask task2)   // 返回第一个完成的索引
ValkarnTask<T>   ValkarnTask.WhenAny<T>(ValkarnTask<T> task1, ValkarnTask<T> task2) // 返回第一个的值
```

### 线程切换

```csharp
ValkarnTask ValkarnTask.SwitchToMainThread()
ValkarnTask ValkarnTask.SwitchToThreadPool()
ValkarnTask ValkarnTask.SwitchToSynchronizationContext(SynchronizationContext context)
```

### 已完成 / 永不完成

```csharp
ValkarnTask ValkarnTask.CompletedTask      // 预先完成，零分配
ValkarnTask<T> ValkarnTask.FromResult<T>(T value)
ValkarnTask ValkarnTask.FromCanceled(CancellationToken ct)
ValkarnTask<T> ValkarnTask.FromCanceled<T>(CancellationToken ct)
ValkarnTask ValkarnTask.FromException(Exception ex)
ValkarnTask<T> ValkarnTask.FromException<T>(Exception ex)
ValkarnTask ValkarnTask.Never                // 永不完成
```

### Run

```csharp
// 在线程池上运行委托
ValkarnTask ValkarnTask.Run(Action action)
ValkarnTask ValkarnTask.Run(Func<ValkarnTask> factory)
ValkarnTask<T> ValkarnTask.Run<T>(Func<T> func)
ValkarnTask<T> ValkarnTask.Run<T>(Func<ValkarnTask<T>> factory)
```

### Forget

```csharp
// 即发即弃：安全地抑制 CS4014 警告
void ValkarnTask.Forget(ValkarnTask task)
void ValkarnTask.Forget(ValkarnTask task, Action<Exception> exceptionHandler)
```

---

## 实例成员

```csharp
ValkarnTaskStatus Status { get; }
bool IsCompleted { get; }
bool IsCompletedSuccessfully { get; }
bool IsFaulted { get; }
bool IsCanceled { get; }

// 获取结果（如果已错误/已取消则抛出异常）
void GetResult()

// 等待器
ValkarnTaskAwaiter GetAwaiter()

// 转换为 ValueTask
ValueTask AsValueTask()
```

---

## 等待条件

```csharp
// 等待直到条件为真
ValkarnTask ValkarnTask.WaitUntil(Func<bool> condition)
ValkarnTask ValkarnTask.WaitUntil(Func<bool> condition, PlayerLoopTiming timing)

// 等待直到条件为假
ValkarnTask ValkarnTask.WaitWhile(Func<bool> condition)

// 等待固定帧数
ValkarnTask ValkarnTask.WaitForFrames(int frameCount)
ValkarnTask ValkarnTask.NextFrame()
```

---

## 对象池诊断

```csharp
// 返回每个对象池的 (Type type, int currentSize, int maxSize)
IEnumerable<(Type, int, int)> ValkarnTask.GetPoolInfo()
```

可在 **Window → Valkarn Tasks → Task Tracker** 窗口中查看。
