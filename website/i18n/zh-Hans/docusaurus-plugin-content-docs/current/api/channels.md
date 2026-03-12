---
sidebar_position: 2
title: 通道
---

# 通道

通道提供了一个线程安全、支持异步的管道，用于在生产者和消费者之间传递数据。它们特别适合游戏系统：工作在一侧产生（后台线程、事件回调、作业完成），需要在另一侧消费（主线程、工作池）。

## 创建通道

通道通过 `ValkarnTask.Channel` 静态工厂类创建。没有公共构造函数。

### 无界通道

```csharp
Channel<T> channel = ValkarnTask.Channel.CreateUnbounded<T>(bool multiConsumer = false);
```

无界通道没有容量限制。只要通道未被完成，`WriteAsync` 和 `TryWrite` 总是立即成功。条目在内部 `Queue<T>` 中积累，直到消费者读取它们。

**参数**

| 参数 | 默认值 | 描述 |
|-----------|---------|-------------|
| `multiConsumer` | `false` | 当为 `true` 时，支持多个并发 `ReadAsync` 调用（竞争消费者）。当为 `false` 时，每次只能有一个读取者等待 `ReadAsync`。 |

### 有界通道

```csharp
Channel<T> channel = ValkarnTask.Channel.CreateBounded<T>(int capacity, bool multiConsumer = false);
```

有界通道在固定大小的环形缓冲区中最多持有 `capacity` 个条目。当缓冲区满时，`WriteAsync` 会异步挂起调用者，直到有空间可用（背压）。缓冲区满时 `TryWrite` 立即返回 `false` 而不等待。

**参数**

| 参数 | 默认值 | 描述 |
|-----------|---------|-------------|
| `capacity` | 必填 | 缓冲区可容纳的最大条目数。必须大于零。 |
| `multiConsumer` | `false` | 与无界通道相同——启用多个并发读取者。 |

**选择有界还是无界**

当你需要施加背压时——即希望生产者在消费者落后时自动减速——使用 `CreateBounded`。当生产者速率天然有界（如输入事件）且无界缓冲是可接受的，或者你已经考虑了内存增长时，使用 `CreateUnbounded`。

---

## Channel&lt;T&gt;

`Channel<T>` 是将管道两侧暴露为独立对象的容器。

```csharp
public sealed class Channel<T>
{
    public ChannelReader<T> Reader { get; }
    public ChannelWriter<T> Writer { get; }
}
```

在生产者侧保留 `Writer` 引用，在消费者侧保留 `Reader` 引用。它们不要求在同一个线程上。

---

## ChannelWriter&lt;T&gt;

`ChannelWriter<T>` 是通道的写入侧。从 `channel.Writer` 获取。

### TryWrite

```csharp
public abstract bool TryWrite(T item);
```

尝试在不挂起的情况下写入条目。如果条目被接受，返回 `true`；如果通道已满（有界）或已完成，返回 `false`。

在可以承受丢弃条目的热路径中，或在轮询循环中想避免异步状态机分配时，使用 `TryWrite`。

```csharp
if (!channel.Writer.TryWrite(item))
{
    // 通道已满或已关闭——相应处理
}
```

### WriteAsync

```csharp
public abstract ValkarnTask WriteAsync(T item);
```

将条目写入通道，必要时异步挂起调用者。

- **无界通道**：只要通道开放，总是同步完成（零分配快速路径）。
- **有界通道**：当缓冲区有空间时同步完成；当缓冲区满时挂起调用者并排队一个待处理写入记录。一旦消费者读取一个条目并释放一个槽位，调用者就恢复。

如果在写入之前或期间通过 `Complete()` 完成了通道，则抛出 `ChannelClosedException`。

```csharp
await channel.Writer.WriteAsync(item);
```

多个生产者可以在有界通道上并发调用 `WriteAsync`。每个挂起的写入者被排队，并按 FIFO 顺序在有空间时解除阻塞。

### Complete

```csharp
public abstract void Complete();
```

发出信号表示不会再写入更多条目。调用 `Complete()` 之后：

- 已写入的任何条目仍保留在缓冲区中，仍可被消费。
- 新的 `WriteAsync` 或 `TryWrite` 调用将以 `ChannelClosedException` 失败。
- 一旦缓冲区被完全耗尽，`ChannelReader<T>.Completion` 完成，任何待处理或未来的 `ReadAsync` 调用都会抛出 `ChannelClosedException`。

`Complete()` 在单次调用上是幂等的——多次调用它是安全的（后续调用被忽略）。

```csharp
// 发出工作结束信号
channel.Writer.Complete();
```

---

## ChannelReader&lt;T&gt;

`ChannelReader<T>` 是通道的读取侧。从 `channel.Reader` 获取。

### ReadAsync

```csharp
public abstract ValkarnTask<T> ReadAsync();
```

从通道读取下一个条目。如果当前没有可用条目，调用者异步挂起直到有条目到达。当通道既已完成又已完全耗尽时，抛出 `ChannelClosedException`。

```csharp
T item = await channel.Reader.ReadAsync();
```

**单消费者模式**（默认）：每次只能有一个 `ReadAsync` 在进行中。尝试启动第二个并发 `ReadAsync` 会立即抛出。此限制允许内部零分配优化——读取器核心直接嵌入到通道实现中，而不是每次调用从对象池分配。

**多消费者模式**（`multiConsumer: true`）：任意数量的 `ReadAsync` 调用可以同时待处理。每个待处理的调用者被排队，按 FIFO 顺序在条目可用时解决。

### TryRead

```csharp
public abstract bool TryRead(out T item);
```

尝试在不挂起的情况下读取条目。如果有条目可用，返回 `true` 并填充 `item`；如果通道为空，返回 `false`（将 `item` 设为 `default`）。

`TryRead` 不区分"空但开放"的通道和"空且关闭"的通道。在轮询循环中使用 `TryRead` 时，使用 `Completion` 检测关闭状态。

### ReadAllAsync

```csharp
public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default);
```

返回一个 `IAsyncEnumerable<T>`，迭代所有条目直到通道完成并耗尽。枚举结束时不传播 `ChannelClosedException`——它只是停止。

```csharp
await foreach (var item in channel.Reader.ReadAllAsync(ct))
{
    Process(item);
}
// 通道完成且为空时到达这里
```

传给 `ReadAllAsync` 的取消 token 作为后备。如果 `GetAsyncEnumerator` 也传递了 token（就像 `await foreach` 通过 `WithCancellation` 做的那样），`foreach` 侧的 token 优先。

### Completion

```csharp
public abstract ValkarnTask Completion { get; }
```

在调用 `Complete()` 且通道完全耗尽后完成的 `ValkarnTask`。具体来说：

- 如果在已空的通道上调用 `Complete()`，`Completion` 立即解决。
- 如果在缓冲区还有条目时调用 `Complete()`，`Completion` 仅在最后一个条目被消费后解决。

等待 `Completion` 是等待管道完成的规范方式。

```csharp
channel.Writer.Complete();
await channel.Reader.Completion;
// 所有条目已被消费
```

---

## ChannelClosedException

```csharp
public sealed class ChannelClosedException : InvalidOperationException
```

在两种情况下抛出：

1. **从已完成且耗尽的通道读取**——当通道被标记为完成且没有剩余条目时，`ReadAsync()` 抛出。
2. **向已完成的通道写入**——当在写入之前调用了 `Complete()` 时，`WriteAsync()` 抛出。

`ChannelClosedException` 继承自 `InvalidOperationException`。`TryRead` 或 `TryWrite` 不会抛出它，这两者返回 `false` 代替。

构造函数：

```csharp
new ChannelClosedException()
new ChannelClosedException(string message)
new ChannelClosedException(Exception innerException)
```

---

## 有界通道：背压详解

当有界通道的缓冲区满且调用了 `WriteAsync` 时，写入者被挂起，一个待处理写入记录被内部排队。写入者保留其条目。当消费者调用 `ReadAsync` 或 `TryRead` 并出队一个条目时：

1. 释放的槽位立即被最早的待处理写入者声明。
2. 该写入者的条目被放入缓冲区。
3. 写入者的等待代码恢复。

这意味着有界通道永远不会丢失条目，也永远不会浪费缓冲区容量——槽位变空与被阻塞的写入者恢复之间始终是一对一的对应关系。在读取者到达而有待处理写入者等待但缓冲区为空的情况下，条目被直接传递而不经过缓冲区。

---

## 模式

### 基本生产者/消费者

```csharp
var channel = ValkarnTask.Channel.CreateUnbounded<WorkItem>();

// 生产者（例如，在后台线程或回调中运行）
async ValkarnTask ProduceAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var work = await FetchNextWorkItemAsync(ct);
        await channel.Writer.WriteAsync(work);
    }
    channel.Writer.Complete();
}

// 消费者（在你选择调用它的地方运行）
async ValkarnTask ConsumeAsync()
{
    await foreach (var item in channel.Reader.ReadAllAsync())
    {
        await ProcessAsync(item);
    }
}
```

### 多生产者，单消费者

多个生产者各自持有 `channel.Writer` 引用并并发调用 `WriteAsync`。所有通道操作都受内部锁保护，因此这是安全的。

```csharp
var channel = ValkarnTask.Channel.CreateBounded<Event>(capacity: 64);

// 几个生产者并发写入
ValkarnTask ProducerA() => ProduceFrom(sourceA, channel.Writer);
ValkarnTask ProducerB() => ProduceFrom(sourceB, channel.Writer);
ValkarnTask ProducerC() => ProduceFrom(sourceC, channel.Writer);

// 单消费者（默认——不需要 multiConsumer 标志）
async ValkarnTask ConsumerAsync()
{
    await foreach (var ev in channel.Reader.ReadAllAsync())
        HandleEvent(ev);
}
```

使用多生产者和有界通道时，谨慎地协调 `Complete()` 调用——只有在所有生产者都完成写入后才调用它，否则某些写入者可能收到 `ChannelClosedException`。

### 多生产者，多消费者

```csharp
// multiConsumer: true 允许多个消费者并发 ReadAsync
var channel = ValkarnTask.Channel.CreateUnbounded<Job>(multiConsumer: true);

async ValkarnTask WorkerAsync(int id, CancellationToken ct)
{
    try
    {
        while (true)
        {
            var job = await channel.Reader.ReadAsync();
            await ExecuteJobAsync(job, ct);
        }
    }
    catch (ChannelClosedException)
    {
        // 通道完成——优雅退出
    }
}
```

每个条目只被传递给一个消费者。消费者按 FIFO 顺序竞争条目（等待时间最长的消费者获得下一个可用条目）。

### 优雅关闭

推荐的关闭序列是：

1. 发出信号让所有生产者停止（例如，取消其 `CancellationToken`）。
2. 在所有生产者停止写入后调用 `channel.Writer.Complete()`。
3. 等待 `channel.Reader.Completion` 确认所有条目已被消费。

```csharp
cts.Cancel();                        // 停止生产者
await allProducersTask;              // 等待它们退出
channel.Writer.Complete();           // 封闭通道
await channel.Reader.Completion;     // 耗尽剩余条目
```

如果你使用 `ReadAllAsync`，第 3 步自动发生——当通道完成且为空时，`await foreach` 循环退出。

---

## 与 System.Threading.Channels 的对比

| 功能 | Valkarn 通道 | System.Threading.Channels |
|---------|-----------------|---------------------------|
| 返回类型 | `ValkarnTask` / `ValkarnTask<T>` | `ValueTask` / `ValueTask<T>` |
| 分配（热路径） | 零（单消费者无界） | 接近零 |
| `WaitToReadAsync` | 不存在——使用 `ReadAsync` 或 `ReadAllAsync` | 存在 |
| `TryComplete(Exception)` | 不存在——使用 `Complete()` | 存在 |
| `Count` / `CanCount` | 不公开 | 某些通道类型上存在 |
| 满时丢弃策略 | 不支持——`WriteAsync` 阻塞 | `DropWrite`、`DropNewest`、`DropOldest`、`Wait` |
| 异步枚举 | `ReadAllAsync()` | `ReadAllAsync()` |
| 线程安全 | 完全（基于锁） | 完全（基于锁） |

主要区别是 Valkarn 通道与 `ValkarnTask` 原生集成，在 Unity 构建中实现零开销等待，并且单消费者路径通过将完成核心直接嵌入通道，在每次 `ReadAsync` 调用时避免了对象池的租用/归还。
