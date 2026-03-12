---
sidebar_position: 2
title: 对象池
---

# 对象池

Valkarn Tasks 通过对支撑每个 `ValkarnTask` 的对象进行池化，消除了常见异步路径上的 GC 分配。本页解释对象池架构——对象如何存储、如何获取和归还，以及系统提供哪些生命周期保证。

---

## 概述

当一个 `async` 方法挂起时，库需要一个地方来存储编译器生成的状态机，以及一个等待器可以订阅的完成机制。在 `System.Threading.Tasks` 中，这就是 `Task` 对象本身——每次调用一次堆分配。在 Valkarn Tasks 中，这一角色由实现了 `ValkarnTask.ISource` 的池化对象承担。

对象池设计有三个目标：

1. **主线程热路径零原子操作。** Unity 的游戏循环按惯例是单线程的。从主线程租用和归还应该是普通的读写操作。
2. **安全的跨线程访问。** 使用 `ValkarnTask.Run` 的后台任务在线程池线程上运行。对象池必须正确处理并发的租用/归还。
3. **有界增长与自适应裁剪。** 对象池不应在流量峰值后无限增长，但也不应过于激进地收缩而导致频繁重新分配。

---

## `ValkarnTaskPool<T>`

`ValkarnTaskPool<T>` 是核心对象池类。它是 `internal sealed` 的——你不会直接与之交互，但理解它能帮你了解内存分配去了哪里。

```
ValkarnTaskPool<T>
  |
  +-- fastItem: T            （单槽缓存，仅主线程，普通读写）
  |
  +-- stackHead: T           （Treiber 栈头，基于 CAS 保证跨线程安全）
  +-- stackSize: int
  |
  +-- maxSize: int           （受 ValkarnTask.DefaultMaxPoolSize 限制）
  +-- totalCreated: int      （跟踪生命周期分配数量用于裁剪比例计算）
```

### 快速槽（主线程）

`fastItem` 字段是一个单独的预留槽，用于最近归还的对象。在主线程上，租用和归还是普通的读写——无原子操作，无自旋。这覆盖了 Unity 游戏循环操作的绝大多数情况。

```
租用（主线程）：
  fastItem != null  →  取出（fastItem = null），返回它   [零原子操作]
  fastItem == null  →  降级到 Treiber 栈

归还（主线程）：
  fastItem == null  →  fastItem = item                   [零原子操作]
  fastItem != null  →  降级到 Treiber 栈
```

### Treiber 栈（溢出 / 后台线程）

当快速槽已被占用（或调用线程不是主线程）时，对象池使用无锁 Treiber 栈——一种使用 compare-and-swap（CAS）的经典侵入式链表：

```
租用（任意线程）：
  while (true):
    head = Volatile.Read(stackHead)
    if head == null: return null（池为空）
    next = head.NextNode
    if CAS(stackHead, next, head) == head: return head  // 赢得竞争
    spinner.SpinOnce()                                   // 输了，重试

归还（任意线程）：
  if stackSize >= maxSize: return false（池已满，丢弃对象）
  while (true):
    head = Volatile.Read(stackHead)
    item.NextNode = head
    if CAS(stackHead, item, head) == head: stackSize++; return true
    spinner.SpinOnce()
```

该栈是侵入式的：每个池化对象存储自己的 `NextNode` 指针，无需外部包装节点。这由 `IPoolNode<T>` 接口强制执行。

### 线程路由

```csharp
internal static class ValkarnTaskPoolShared
{
    internal static volatile int MainThreadId;
}
```

所有对象池实例共享一个 `MainThreadId`。租用/归还操作通过检查 `Thread.CurrentThread.ManagedThreadId == MainThreadId` 来路由到正确的路径。`volatile` 字段确保在启动时发布 ID 后跨线程可见。

---

## `IPoolNode<T>`

任何参与对象池的类型都必须实现此接口：

```csharp
internal interface IPoolNode<T> where T : class
{
    ref T NextNode { get; }
}
```

`ref T NextNode` 返回对对象内部存储下一指针的字段的引用。对象池通过 ref 直接写入该字段，无需单独的包装节点。库中所有池化类型——运行器、promise、组合器——都通过声明一个私有字段并暴露它来实现此接口：

```csharp
// 来自 PooledPromise<T> 的示例
PooledPromise<T> nextNode;
public ref PooledPromise<T> NextNode => ref nextNode;
```

---

## 对象池生命周期：获取、使用、归还

池化对象的完整生命周期是：

```
调用者调用异步方法
  |
  +--> AsyncValkarnTaskMethodBuilder.AwaitUnsafeOnCompleted()
        |
        +--> runner == null? YES --> Pool.TryRent() ?? Pool.TrackNew(new Runner())
              |
              +--> runner 存储在构建器中；状态机被复制到 runner 中
              |
              +--> awaiter.UnsafeOnCompleted(runner.MoveNextAction)
              |
              [... 异步工作继续 ...]
              |
              +--> runner.SetResult() / runner.SetException()
                    |
                    +--> ValkarnTaskCompletionCore.TrySetResult() --> 续体被调用
                          |
                          +--> 调用者的等待器调用 GetResult(token)
                                |
                                +--> core.GetResult(token)——读取结果或重新抛出
                                |
                                +--> TryReturn():
                                      stateMachine = default
                                      core.Reset()           // 递增世代
                                      Pool.TryReturn(this)
```

`TryReturn` 方法始终在调用 `core.Reset()` 之前清除状态机。这个顺序很重要：`Reset()` 递增世代计数器，使槽位对并发租用者可见为可用状态。如果状态机在 `Reset()` 之后才被清除，另一个线程上的租用者可能获得该槽位并使其状态机被覆盖。

---

## `ValkarnTaskCompletionCore<TResult>`

`ValkarnTaskCompletionCore<TResult>` 是嵌入在每个池化对象内部的 `internal struct`。它是 promise 的实际状态机——跟踪完成状态、存储结果和错误，并解决 `OnCompleted`（注册续体）与 `TrySetResult`（发出完成信号）之间的竞争。

```
字段：
  result:          TResult     -- 成功值
  error:           object      -- ExceptionDispatchInfo 或 OperationCanceledException
  errorKind:       byte        -- 0=无, 1=错误, 2=已取消(OCE), 3=已取消(EDI)
  generation:      int         -- 单调递增；转型为 uint 用于 token 比较
  completedCount:  int         -- 0=待处理, 1=已声明, 2=已完成（两阶段发布）
  continuation:    Action<object>
  continuationState: object
  hasUnhandledError: bool
```

### 两阶段完成协议

完成使用两阶段 CAS，以在 ARM64 上保证安全（需要 store-release / load-acquire 对）：

```
TrySetResult(value):
  阶段 1：CAS(completedCount, 0 -> 1)   -- 声明独占所有权
  阶段 2：写入 result
  阶段 3：Volatile.Write(completedCount, 2)  -- 以 release 语义发布
  阶段 4：InvokeContinuation()
```

读取者使用 `Volatile.Read(completedCount)`（acquire 语义）在读取结果之前，确保能看到阶段 2 中写入的值。

### `OnCompleted` 与 `TrySetResult` 之间的竞争解决

可能发生三种模式：

```
模式 A——OnCompleted 先到：
  OnCompleted 通过 CAS(continuation, null -> cont) 存储续体
  TrySetResult 读到非 null 续体 -> 调用它

模式 B——TrySetResult 先到（同步快速路径）：
  TrySetResult 通过 CAS(continuation, null -> sentinel) 放置 ContinuationSentinel
  OnCompleted 读到 sentinel -> 立即内联调用续体

模式 C（并发竞争）：
  C.1：OnCompleted 赢得 CAS -> TrySetResult 读到它 -> 调用
  C.2：TrySetResult 赢得 CAS（放置 sentinel）-> OnCompleted 检测到 sentinel -> 内联调用
```

sentinel 是一个静态 `Action<object>` 对象，纯粹用作标记值——它永远不会作为委托被实际调用。

### Token 验证与 ABA 安全

每次调用 `GetStatus`、`GetResult` 和 `OnCompleted` 都会将 `uint token` 与当前 `generation` 进行验证。当 `Reset()` 调用 `Interlocked.Increment(ref generation)` 时，任何持有旧 token 的 `ValkarnTask` 结构体将收到 `InvalidOperationException`，而不是静默操作已回收的状态。32 位世代计数器回绕（需要单个槽位约 40 亿次复用）在实践中被认为实际上不可能发生。

### 重置与未观察到的错误报告

`Reset()` 在回池时被调用。在递增世代之前，它检查是否存储了一个从未被观察到的错误（即在错误之后从未调用 `GetResult`）。如果是，它通过 `ValkarnTask.UnobservedException` 发布异常。取消错误仅在 `ValkarnTaskSettings` 中启用 `LogUnobservedCancellations` 时才报告，因为取消通常是有意为之的。

对于非池化的 `Promise` 和 `Promise<T>` 对象，未观察到的错误报告通过终结器中的 `ReportUnobservedIfNeeded()` 发生，遵循相同的逻辑但不清除状态。

---

## 对象池配置

三个设置控制对象池大小。在 Unity 构建中，这些设置从 `ValkarnTaskSettings` ScriptableObject 资产读取（有内置默认值作为回退），也可以在运行时通过静态属性覆盖：

```csharp
// 每种对象池类型的最大对象数（按 TStateMachine 或 promise 类型分别计算）
ValkarnTask.DefaultMaxPoolSize = 256;   // 默认值：256

// 两次裁剪检查之间的帧数（Unity PlayerLoop 帧）
ValkarnTask.TrimCheckInterval = 300;    // 默认值：300（60fps 下约 5 秒）

// 裁剪后保留的最少对象数
ValkarnTask.MinPoolSize = 8;            // 默认值：8
```

`DefaultMaxPoolSize` 是在对象池构建时应用的上限。它按对象池实例而非全局执行——`AsyncValkarnTaskRunner<LoadSceneStateMachine>` 的对象池和 `AsyncValkarnTaskRunner<FetchDataStateMachine>` 的对象池各自有自己的上限。

### 对象池裁剪

`PlayerLoopHelper` 每隔 `TrimCheckInterval` 帧在主线程上调用 `PoolRegistry.TrimAll(minPoolSize)`。每个对象池使用滞后策略：

```
每次裁剪检查：
  currentSize = fastItem.count + stackSize
  if currentSize <= MinPoolSize: 重置连续计数，跳过

  ratio = currentSize / totalCreated
  if ratio > 0.5（对象池持有超过 50% 曾创建的所有对象）：
    trimConsecutiveCount++
    if trimConsecutiveCount >= hysteresisThreshold:
      从栈中释放一定比例（releaseRatio）的多余对象
      （fastItem 被保留——它是对缓存最友好的槽位）
  else:
    重置 trimConsecutiveCount
```

滞后机制防止短暂的流量峰值立即导致所有对象被分配后随即被裁剪。裁剪期间 fastItem 始终被保留，因为它代表最近使用的对象，因此最有可能再次被需要。

---

## `PoolRegistry` 与监控

每个 `ValkarnTaskPool<T>` 在构建时向全局 `PoolRegistry` 注册自身。注册表维护 `IPoolInfo` 引用列表，公开：

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

你可以在运行时使用公共 API 枚举所有活动的对象池：

```csharp
foreach (var (type, size, maxSize) in ValkarnTask.GetPoolInfo())
{
    Debug.Log($"{type.Name}: {size}/{maxSize}");
}
```

这与 Unity 编辑器中 **Task Tracker** 窗口展示的数据相同。该窗口轮询 `GetPoolInfo()` 并显示一个实时的对象池占用率表格，让你可以查看对象池是否已预热、是否有任何类型持续达到上限，以及裁剪是否按预期工作。

死亡的对象池条目（`IsAlive` 返回 false 的）在 `GetAll()` 和 `TrimAll()` 调用期间从注册表列表中惰性清除，防止注册表在对象池实例被 GC 后无限增长。

---

## `PooledPromise` 与 `PooledPromise<T>`

这些是用于自定义异步模式的池化完成源——例如，包装基于回调的 API 或重复的生产者/消费者通道。

```csharp
// 从对象池获取一个待处理的 promise
var promise = ValkarnTask.PooledPromise<string>.Create(out uint token);
ValkarnTask<string> task = promise.Task;

// 将 task 交给消费者
// ... 稍后，从任意线程 ...
promise.TrySetResult("hello");

// 当消费者 await task 并调用 GetResult 时，
// promise 会自动重置并返回对象池。
```

关键特性：

- `Create(out uint token)` 从对象池租用，或分配一个由对象池跟踪的新实例。
- `CreateCompleted(T result, out uint token)` 执行相同操作，但立即发出结果信号，使任务在返回时已完成。
- 在后备任务上调用 `GetResult` 后，`TryReturn()` 触发：promise 调用 `core.Reset()` 并将自身返回对象池。
- 双重归还防护（`Interlocked.Exchange(ref returned, 1)`）防止在并发调用 `GetResult` 两次时破坏对象池。

**非池化替代方案：`Promise` 和 `Promise<T>`。** 这些是不归还到对象池的堆分配类。在生命周期不可预测或同一 source 必须跨多个 await 周期存活的长生命周期操作中使用它们。它们依赖终结器报告未观察到的异常。

---

## 组合器对象池

`WhenAll` 和 `WhenAny` 组合器也使用对象池。每种参数数量和类型组合都有自己的对象池：

| 组合器 | 对象池类型 |
|-----------|-----------|
| `WhenAll(task1, task2)`（类型化） | `ValkarnTaskPool<WhenAllPromise<T1, T2>>` |
| `WhenAll(IEnumerable<ValkarnTask<T>>)` | `ValkarnTaskPool<WhenAllArrayPromise<T>>` |
| `WhenAll(task1, task2)`（void） | `ValkarnTaskPool<WhenAllVoidPromise2>` |
| `WhenAny(task1, task2)`（类型化） | `ValkarnTaskPool<WhenAnyPromise2<T>>` |
| `WhenAny(IEnumerable<ValkarnTask<T>>)` | `ValkarnTaskPool<WhenAnyArrayPromise<T>>` |

基于数组的组合器（`WhenAll<T>(IEnumerable<...>)` 和 `WhenAny<T>(IEnumerable<...>)`）使用 `System.Buffers.ArrayPool<T>.Shared` 来管理其内部的 source/token 数组，因此这些数组也被复用而不是每次调用时重新分配。

所有组合器都应用相同的零分配短路逻辑：如果在调用 `WhenAll` 或 `WhenAny` 时所有输入已同步完成，则永远不会创建新的池化对象。

```csharp
// 零分配——两个任务均已同步完成
var t1 = ValkarnTask.FromResult(1);
var t2 = ValkarnTask.FromResult(2);
ValkarnTask<(int, int)> combined = ValkarnTask.WhenAll(t1, t2);
// combined.source == null；结果 (1, 2) 内联存储
```
