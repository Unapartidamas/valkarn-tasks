---
sidebar_position: 7
title: ValkarnSemaphore
---

# ValkarnSemaphore

`ValkarnSemaphore` 是一个基于 `ValkarnTask` 构建的零分配异步信号量。它的工作方式类似于 `SemaphoreSlim`，但与 Valkarn Tasks 管道原生集成——在稳定状态下无 `Task` 分配，无 GC 压力。

```csharp
namespace UnaPartidaMas.Valkarn.Tasks
```

**快速路径（无竞争）：** 立即返回 `ValkarnTask.CompletedTask`——零分配。
**慢速路径（有竞争）：** 等待节点从有界池中租借，完成后归还——稳定状态下 GC 为零。

---

## 构造函数

```csharp
public ValkarnSemaphore(int initialCount, int maxCount)
```

| 参数 | 描述 |
|-----------|-------------|
| `initialCount` | 立即可用的槽位数量。必须在 `[0, maxCount]` 范围内。 |
| `maxCount` | 槽位数量的上限。必须 > 0。 |

---

## 属性

```csharp
public int CurrentCount { get; } // 当前可用槽位数（线程安全）
```

---

## 方法

### WaitAsync

```csharp
public ValkarnTask WaitAsync(CancellationToken ct = default)
```

异步获取一个槽位。如果有可用槽位，立即返回已完成的 `ValkarnTask`（零分配）。若没有可用槽位，则挂起调用方直到 `Release()` 被调用。等待者按 FIFO 顺序服务。

如果 `ct` 在等待期间触发，则抛出 `OperationCanceledException`。

### Wait

```csharp
public void Wait(CancellationToken ct = default)
```

同步变体。如果没有可用槽位，则阻塞调用线程。仅在无法使用 `await` 时使用（例如非异步入口点）。

### Release

```csharp
public void Release(int releaseCount = 1)
```

归还 `releaseCount` 个槽位。待处理的等待者按 FIFO 顺序完成，在内部锁之外执行——续延永远不会在持有锁的情况下运行。

如果释放后超过 `maxCount`，则抛出 `InvalidOperationException`。

### Dispose

```csharp
public void Dispose()
```

取消所有待处理的等待者并释放资源。幂等操作。

---

## 用法

### 互斥（1 个槽位）

```csharp
var sem = new ValkarnSemaphore(initialCount: 1, maxCount: 1);

async ValkarnTask WriteFileAsync(string path, string content, CancellationToken ct)
{
    await sem.WaitAsync(ct);
    try
    {
        await File.WriteAllTextAsync(path, content, ct);
    }
    finally
    {
        sem.Release();
    }
}
```

### 有界并发（N 个槽位）

```csharp
// 允许最多 4 个并发网络请求。
var sem = new ValkarnSemaphore(initialCount: 4, maxCount: 4);

async ValkarnTask FetchAsync(string url, CancellationToken ct)
{
    await sem.WaitAsync(ct);
    try
    {
        await DownloadAsync(url, ct);
    }
    finally
    {
        sem.Release();
    }
}
```

### 生产者 / 消费者门控

```csharp
// 从 0 个槽位开始——消费者等待直到生产者发出信号。
var gate = new ValkarnSemaphore(initialCount: 0, maxCount: 1);

async ValkarnTask ProducerAsync(CancellationToken ct)
{
    await ProduceDataAsync(ct);
    gate.Release(); // 通知消费者
}

async ValkarnTask ConsumerAsync(CancellationToken ct)
{
    await gate.WaitAsync(ct); // 等待信号
    await ConsumeDataAsync(ct);
}
```

---

## 线程安全

所有操作均为线程安全。`Release()` 可从任意线程调用，包括后台线程。完成操作在内部锁之外分发——重新进入信号量的续延不会发生死锁。

---

## 与 SemaphoreSlim 的比较

| | `ValkarnSemaphore` | `SemaphoreSlim` |
|---|---|---|
| 快速路径分配 | 零 | 零 |
| 慢速路径分配 | 池化节点，GC 为零 | `Task` 分配 |
| 与 ValkarnTask 集成 | 是 | 需要 `.AsValkarnTask()` |
| `IDisposable` | 是 | 是 |
| FIFO 排序 | 是 | 是 |
| 取消支持 | 是 | 是 |
