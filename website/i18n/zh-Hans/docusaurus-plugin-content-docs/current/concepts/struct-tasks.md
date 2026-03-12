---
sidebar_position: 1
title: 结构体任务
---

# 结构体任务

`ValkarnTask` 和 `ValkarnTask<T>` 是 Valkarn Tasks 的核心异步返回类型。与 `System.Threading.Tasks.Task`（一种始终在堆上分配内存的引用类型）不同，两种 Valkarn 任务类型均为 `readonly struct` 值类型。本页解释这在实践中意味着什么、零分配快速路径如何工作，以及编译器如何与 async/await 机制集成。

---

## 为何使用 `readonly struct`？

像 `Task<T>` 这样基于类的任务，每次调用异步方法时都必须在堆上分配内存，即使该方法是同步完成的也不例外。在以 60fps 运行的 Unity 游戏循环中，每帧数百个小型异步操作累积起来会产生可观的 GC 压力。

`ValkarnTask` 和 `ValkarnTask<T>` 被声明为 `readonly partial struct`：

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncValkarnTaskMethodBuilder))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct ValkarnTask
{
    internal readonly ISource source;
    internal readonly uint token;
}
```

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncValkarnTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
{
    internal readonly ValkarnTask.ISource<T> source;
    internal readonly T result;
    internal readonly uint token;
}
```

作为结构体，任务值本身存在于栈上（或内联在其父对象中），而不是堆上。`readonly` 修饰符确保编译器能推断不变性并防止意外的复制 bug。`StructLayout.Auto` 让运行时可以针对目标平台优化字段排列。

### 核心不变量：`source == null`

设计围绕一个核心不变量：

> 当 `source` 为 `null` 时，任务已同步完成且无错误。不涉及任何堆对象。

`ValkarnTask.CompletedTask` 就是 `default(ValkarnTask)`——其 `source` 字段为 null，因此零开销。`ValkarnTask<T>` 将结果内联存储在 `result` 字段中，使得 `ValkarnTask.FromResult(value)` 也是零分配调用：

```csharp
// 零分配——source 为 null，结果内联存储
ValkarnTask<int> task = ValkarnTask.FromResult(42);

// 同样零分配——source 为 null
ValkarnTask done = ValkarnTask.CompletedTask;
```

---

## 零分配快速路径

当一个 `async` 方法在未挂起的情况下完成（所有 `await` 都等待到了已完成的操作），整个方法在调用线程上同步运行。构建器检测到这种情况并返回一个 `source == null` 的任务。

等待器会立即检查这一点：

```csharp
public bool IsCompleted
{
    get
    {
        var s = task.source;
        return s == null || s.GetStatus(task.token).IsCompleted();
    }
}
```

当 `IsCompleted` 在 `OnCompleted` 被调用之前就为 true 时，状态机不会注册续体。`GetResult` 立即被调用，对于 `source == null` 的 `ValkarnTask<T>`，结果直接从结构体的内联 `result` 字段读取：

```csharp
public T GetResult()
{
    var s = task.source;
    if (s == null)
        return task.result;   // 内联读取，无 ISource 调用
    return s.GetResult(task.token);
}
```

不创建任何对象，不发生接口派发，不分配续体委托。整个 await 解析为一次直接的值读取。

### 需要 source 时

如果一个异步方法挂起（等待了一个尚未完成的操作），构建器会分配一个来自对象池的 `AsyncValkarnTaskRunner<TStateMachine>` 对象（泛型变体为 `AsyncValkarnTaskRunner<TStateMachine, TResult>`）。该对象身兼双职：既按值保存编译器生成的状态机，又实现 `ValkarnTask.ISource`，因此可以直接作为任务的后备 source 使用。返回给调用者的任务包装了这个运行器以及一个世代 `uint` token。

完成后，当调用者在等待器上调用 `GetResult` 时，运行器会重置自身并返回到对象池——因此内存分配在许多方法调用中被均摊。

---

## `ISource` 接口

`ValkarnTask` 结构体与其异步后备对象之间的契约是 `ValkarnTask.ISource`：

```csharp
public interface ISource
{
    ValkarnTask.Status GetStatus(uint token);
    void GetResult(uint token);
    void OnCompleted(Action<object> continuation, object state, uint token);
    ValkarnTask.Status UnsafeGetStatus();
}

public interface ISource<out T> : ISource
{
    new T GetResult(uint token);
}
```

任何实现了 `ISource` 的对象都可以作为 `ValkarnTask` 的后备。本库附带几个实现：

| 类型 | 用途 |
|------|---------|
| `AsyncValkarnTaskRunner<TStateMachine>` | 支撑每个 `async ValkarnTask` 方法（内部） |
| `AsyncValkarnTaskRunner<TStateMachine, TResult>` | 支撑每个 `async ValkarnTask<T>` 方法（内部） |
| `ValkarnTask.PooledPromise` | 带自动回池的手动完成源 |
| `ValkarnTask.PooledPromise<T>` | 上述的泛型变体 |
| `ValkarnTask.Promise` | 不带对象池的手动完成源（长生命周期操作） |
| `ValkarnTask.Promise<T>` | 上述的泛型变体 |

`uint token` 参数是世代保护。当一个池化 source 被重置以供复用时，其世代计数器递增。任何持有旧 token 的 `ValkarnTask` 结构体将立即收到 `InvalidOperationException`，而不会静默读取已回收的状态。

---

## `ValkarnTask` 与 `ValkarnTask<T>` 对比

| 特性 | `ValkarnTask` | `ValkarnTask<T>` |
|---------|-----------|-------------|
| 返回值 | 无（等效于 void） | `T` |
| 内联结果存储 | 无 `result` 字段 | `result` 字段（类型为 `T`） |
| 等待器 `GetResult` | `void` | 返回 `T` |
| 构建器类型 | `AsyncValkarnTaskMethodBuilder` | `AsyncValkarnTaskMethodBuilder<TResult>` |
| 同步完成值 | `ValkarnTask.CompletedTask` | `ValkarnTask.FromResult(value)` |
| 转换为非泛型 | 不适用 | `.AsNonGeneric()` |

当异步方法没有有意义的返回值时使用 `ValkarnTask`，有结果时使用 `ValkarnTask<T>`。你可以通过 `AsNonGeneric()` 将 `ValkarnTask<T>` 向下转型为 `ValkarnTask`，以便在 `WhenAll` 等组合器中混合使用有类型和无类型的任务。

---

## 异步方法构建器的工作原理

C# 编译器在返回类型的 `[AsyncMethodBuilder(...)]` 中查找指定的类型。对于 `ValkarnTask`，这是 `AsyncValkarnTaskMethodBuilder`；对于 `ValkarnTask<T>`，这是 `AsyncValkarnTaskMethodBuilder<TResult>`。

构建器本身也是结构体，以避免仅为构建器对象就产生一次堆分配。它有两个字段（泛型变体有三个）：

```csharp
public struct AsyncValkarnTaskMethodBuilder
{
    IStateMachineRunnerPromise runner;   // 首次挂起前为 null
    Exception syncException;            // 仅在同步错误路径上设置
}

public struct AsyncValkarnTaskMethodBuilder<TResult>
{
    IStateMachineRunnerPromise<TResult> runner;
    Exception syncException;
    TResult result;                     // 仅在同步成功路径上设置
}
```

### 构建器生命周期

编译器按顺序调用以下方法：

**1. `Create()`** — 返回一个默认构建器（所有字段均为 null/default）。无分配。

**2. `Start(ref stateMachine)`** — 同步调用 `stateMachine.MoveNext()`。如果方法在未遇到未完成的 `await` 的情况下完成，则调用 `SetResult`/`SetException`，`runner` 保持为 null。

**3. `AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine)`** — 当方法遇到未完成的 `await` 时调用。如果 `runner` 为 null（首次挂起），则租用或创建一个 `AsyncValkarnTaskRunner` 并将状态机复制其中。然后调用 `awaiter.UnsafeOnCompleted(runner.MoveNextAction)` 注册状态机续体。

**4. `SetResult()` / `SetException(exception)`** — 向运行器的 `ValkarnTaskCompletionCore` 发出完成信号，唤醒任何已注册的等待器。

**5. `Task` 属性** — 由调用者检查以获取 `ValkarnTask` 值。在同步成功路径（`runner == null && syncException == null`）上，返回 `default`（泛型变体返回 `new ValkarnTask<T>(result)`）——零分配。在异步路径上，将运行器包装为 source。

关键优化在于 `runner` 是惰性分配的。如果方法同步完成（对于缓存命中、守卫条件、提前返回而言是常见情况），则永远不会租用任何池化对象。

---

## `ValkarnTaskStatus` 状态

状态由嵌套在 `ValkarnTask` 内部的 `byte` 大小枚举表示：

```csharp
public enum Status : byte
{
    Pending   = 0,   // 尚未完成
    Succeeded = 1,   // 正常完成
    Faulted   = 2,   // 以未处理异常完成
    Canceled  = 3    // 通过 OperationCanceledException 完成
}
```

你可以直接检查状态：

```csharp
ValkarnTask task = SomeOperation();
ValkarnTask.Status status = task.GetStatus();

switch (status)
{
    case ValkarnTask.Status.Pending:
        // 仍在运行——无法调用 GetResult
        break;
    case ValkarnTask.Status.Succeeded:
        // 正常完成
        break;
    case ValkarnTask.Status.Faulted:
        // 以异常完成——GetResult 将重新抛出
        break;
    case ValkarnTask.Status.Canceled:
        // 以 OperationCanceledException 完成
        break;
}
```

对于同步完成快速路径（`source == null`），`GetStatus()` 无需任何接口调用即可返回 `Succeeded`：

```csharp
public Status GetStatus()
{
    if (source == null) return Status.Succeeded;
    return source.GetStatus(token);
}
```

`IsCompleted` 属性遵循相同的模式，对任何非 `Pending` 状态均返回 `true`。

---

## IL2CPP 的影响

IL2CPP 在构建为原生代码之前将 C# 编译为 C++。泛型值类型（包括结构体）在生成的代码中会被完全特化，这对本库有重要影响。

**状态机特化。** 编译器为每个异步方法生成一个独特的状态机结构体。因此 `AsyncValkarnTaskRunner<TStateMachine>` 对每个异步方法也是独特的，`ValkarnTaskPool<AsyncValkarnTaskRunner<TStateMachine>>` 是每个方法独立的对象池。这实际上是有益的：对象池永远不会在不兼容的类型之间共享，消除了类型混淆的任何风险。

**状态机不装箱。** 状态机按值存储在运行器对象内部，不装箱。IL2CPP 能正确处理这种情况，因为运行器是一个带有具体 `TStateMachine` 字段的 `sealed class`。

**剥离保护。** `[AsyncMethodBuilder]` 特性保持构建器类型存活。但是，如果你在 IL2CPP 积极剥离的情况下通过接口引用使用 `ValkarnTask.ISource`，请添加一个 `link.xml` 条目来保护 `UnaPartidaMas.Valkarn.Tasks` 程序集：

```xml
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```

**`ICriticalNotifyCompletion`。** 等待器结构体实现了 `ICriticalNotifyCompletion`，这告诉编译器调用 `UnsafeOnCompleted` 而不是 `OnCompleted`。"unsafe"变体有意跳过 `ExecutionContext` 捕获。这对 Unity 是正确的——Unity 的默认配置中没有 `SynchronizationContext`，捕获它只会增加开销而无任何收益。在 IL2CPP 下，这也避免了标准 `Task` 始终支付的 `ExecutionContext.Run` 路径的开销。

---

## 实践示例

### 无分配地提前返回

```csharp
// 在热路径上同步完成的 async ValkarnTask<int>
async ValkarnTask<int> GetCachedValue(string key)
{
    if (_cache.TryGetValue(key, out var value))
        return value;            // 编译器调用 SetResult(value)；source 保持为 null

    var result = await FetchFromDatabaseAsync(key);
    _cache[key] = result;
    return result;
}
```

当值已被缓存时，方法从不挂起。返回的 `ValkarnTask<int>` 的 `source == null`，结果内联存储。此路径不发生堆分配。

### 在等待前检查 IsCompleted

```csharp
ValkarnTask<Texture2D> loadTask = LoadTextureAsync("sprites/hero.png");

if (loadTask.IsCompleted)
{
    // 已完成——GetAwaiter().GetResult() 无需 ISource 调用即可读取内联结果
    Texture2D tex = loadTask.GetAwaiter().GetResult();
    ApplyTexture(tex);
}
else
{
    // 真正的异步——注册续体
    ApplyTextureAsync(loadTask).Forget();
}
```

### 观察未处理的异常

未被等待的已错误任务（即发即弃模式）通过 `ValkarnTask.UnobservedException` 事件报告其异常。对于池化 source，此事件在回池时确定性地触发；对于 `Promise` 后备的任务，则从终结器触发。

```csharp
ValkarnTask.UnobservedException += ex =>
{
    Debug.LogError($"[ValkarnTask] 未观察到的异常: {ex}");
};
```

该事件是线程安全的；处理器可以通过无锁 compare-exchange 循环在任意线程上添加或移除。
