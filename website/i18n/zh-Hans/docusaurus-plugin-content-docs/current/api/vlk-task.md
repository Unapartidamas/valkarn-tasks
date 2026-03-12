---
sidebar_position: 1
title: VlkTask
---

# VlkTask

`VlkTask` 是核心可等待类型。它是一个 `readonly struct`——在快速路径上（当任务同步完成或通过对象池完成时）无堆分配。

```csharp
namespace UnaPartidaMas.Valkarn.Tasks;

public readonly struct VlkTask : IEquatable<VlkTask>
```

---

## 静态工厂方法

### Delay

```csharp
// 毫秒（默认使用 PlayerLoopTiming.Update）
VlkTask VlkTask.Delay(int millisecondsDelay)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(int millisecondsDelay, CancellationToken cancellationToken)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing, CancellationToken cancellationToken)

// TimeSpan 重载
VlkTask VlkTask.Delay(TimeSpan delay)
VlkTask VlkTask.Delay(TimeSpan delay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(TimeSpan delay, CancellationToken cancellationToken)
```

### Yield

```csharp
// 让出到下一帧（默认 Update 时机）
VlkTask VlkTask.Yield()
VlkTask VlkTask.Yield(PlayerLoopTiming timing)
VlkTask VlkTask.Yield(CancellationToken cancellationToken)
```

### WhenAll

```csharp
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2)
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2, VlkTask task3)
// ... 最多 8 个参数

// 带返回值——支持元组解构
VlkTask<(T1, T2)>     VlkTask.WhenAll<T1, T2>(VlkTask<T1>, VlkTask<T2>)
VlkTask<(T1, T2, T3)> VlkTask.WhenAll<T1, T2, T3>(...)
// ... 最多 8 个

// 集合重载
VlkTask VlkTask.WhenAll(IEnumerable<VlkTask> tasks)
VlkTask<T[]> VlkTask.WhenAll<T>(IEnumerable<VlkTask<T>> tasks)
```

### WhenAny

```csharp
VlkTask<int> VlkTask.WhenAny(VlkTask task1, VlkTask task2)   // 返回第一个完成的索引
VlkTask<T>   VlkTask.WhenAny<T>(VlkTask<T> task1, VlkTask<T> task2) // 返回第一个的值
```

### 线程切换

```csharp
VlkTask VlkTask.SwitchToMainThread()
VlkTask VlkTask.SwitchToThreadPool()
VlkTask VlkTask.SwitchToSynchronizationContext(SynchronizationContext context)
```

### 已完成 / 永不完成

```csharp
VlkTask VlkTask.CompletedTask      // 预先完成，零分配
VlkTask<T> VlkTask.FromResult<T>(T value)
VlkTask VlkTask.FromCanceled(CancellationToken ct)
VlkTask<T> VlkTask.FromCanceled<T>(CancellationToken ct)
VlkTask VlkTask.FromException(Exception ex)
VlkTask<T> VlkTask.FromException<T>(Exception ex)
VlkTask VlkTask.Never                // 永不完成
```

### Run

```csharp
// 在线程池上运行委托
VlkTask VlkTask.Run(Action action)
VlkTask VlkTask.Run(Func<VlkTask> factory)
VlkTask<T> VlkTask.Run<T>(Func<T> func)
VlkTask<T> VlkTask.Run<T>(Func<VlkTask<T>> factory)
```

### Forget

```csharp
// 即发即弃：安全地抑制 CS4014 警告
void VlkTask.Forget(VlkTask task)
void VlkTask.Forget(VlkTask task, Action<Exception> exceptionHandler)
```

---

## 实例成员

```csharp
VlkTaskStatus Status { get; }
bool IsCompleted { get; }
bool IsCompletedSuccessfully { get; }
bool IsFaulted { get; }
bool IsCanceled { get; }

// 获取结果（如果已错误/已取消则抛出异常）
void GetResult()

// 等待器
VlkTaskAwaiter GetAwaiter()

// 转换为 ValueTask
ValueTask AsValueTask()
```

---

## 等待条件

```csharp
// 等待直到条件为真
VlkTask VlkTask.WaitUntil(Func<bool> condition)
VlkTask VlkTask.WaitUntil(Func<bool> condition, PlayerLoopTiming timing)

// 等待直到条件为假
VlkTask VlkTask.WaitWhile(Func<bool> condition)

// 等待固定帧数
VlkTask VlkTask.WaitForFrames(int frameCount)
VlkTask VlkTask.NextFrame()
```

---

## 对象池诊断

```csharp
// 返回每个对象池的 (Type type, int currentSize, int maxSize)
IEnumerable<(Type, int, int)> VlkTask.GetPoolInfo()
```

可在 **Window → Valkarn Tasks → Task Tracker** 窗口中查看。
