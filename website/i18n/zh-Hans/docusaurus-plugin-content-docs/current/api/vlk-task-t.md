---
sidebar_position: 2
title: ValkarnTask<T>
---

# `ValkarnTask<T>`

`ValkarnTask<T>` 是 Valkarn Tasks 中的有返回值异步任务类型。它是一个 `readonly struct`，当同步完成时内联存储结果，当异步完成时持有对池化 source 对象的引用。

**命名空间：** `UnaPartidaMas.Valkarn.Tasks`

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncValkarnTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
```

对 `T` 没有泛型约束。任何类型——值类型、引用类型、结构体或类——都有效。

---

## 创建实例

### 同步完成的任务

这些工厂方法返回没有后备 source 对象的 `ValkarnTask<T>`。零分配。

#### `ValkarnTask.FromResult<T>(T value)`

返回一个内联携带 `value` 的已完成 `ValkarnTask<T>`。声明为非泛型 `ValkarnTask` 类型上的静态方法。

```csharp
public static ValkarnTask<T> FromResult<T>(T value)
```

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(42);
ValkarnTask<string> name = ValkarnTask.FromResult("Valkarn");
ValkarnTask<Vector3> pos = ValkarnTask.FromResult(transform.position);
```

返回的结构体的 `source == null`。等待它不会产生续体分配——编译器立即看到 `IsCompleted == true`。

#### `ValkarnTask.FromException<T>(Exception exception)`

返回一个已错误的 `ValkarnTask<T>`。等待它会通过 `ExceptionDispatchInfo` 保留原始堆栈跟踪地重新抛出异常。

```csharp
public static ValkarnTask<T> FromException<T>(Exception exception)
```

```csharp
ValkarnTask<Texture2D> LoadTexture(string path)
{
    if (string.IsNullOrEmpty(path))
        return ValkarnTask.FromException<Texture2D>(
            new ArgumentException("路径不能为空。", nameof(path)));

    return LoadTextureAsync(path);
}
```

#### `ValkarnTask.FromCanceled<T>(CancellationToken ct = default)`

返回一个已取消的 `ValkarnTask<T>`。等待它会抛出 `OperationCanceledException`。

```csharp
public static ValkarnTask<T> FromCanceled<T>(CancellationToken ct = default)
```

```csharp
ValkarnTask<byte[]> Download(string url, CancellationToken ct)
{
    if (ct.IsCancellationRequested)
        return ValkarnTask.FromCanceled<byte[]>(ct);

    return DownloadAsync(url, ct);
}
```

### 通过 `async` 方法

任何声明返回 `ValkarnTask<T>` 的 `async` 方法都会自动使用 `AsyncValkarnTaskMethodBuilder<TResult>`：

```csharp
async ValkarnTask<int> ComputeAsync()
{
    await ValkarnTask.Yield();
    return 42;
}
```

编译器生成一个状态机。如果方法同步完成（从不挂起），`AsyncValkarnTaskMethodBuilder<T>.Task` 返回 `source == null` 的 `new ValkarnTask<T>(result)`——零分配。

---

## 在线程池上运行工作

这些方法在 .NET 线程池上运行一个委托，并在主线程（指定的 `PlayerLoopTiming`）上返回结果。它们是较长名称的 `RunOnThreadPool` 变体的便捷包装。

#### `ValkarnTask.Run<T>(Func<T> func, PlayerLoopTiming timing, CancellationToken ct)`

在线程池上运行一个同步 `Func<T>`。

```csharp
public static ValkarnTask<T> Run<T>(
    Func<T> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
// 在线程池上计算，结果在下一个 Update 时返回主线程
int hash = await ValkarnTask.Run(() => ComputeExpensiveHash(data));
```

#### `ValkarnTask.Run<T>(Func<ValkarnTask<T>> func, PlayerLoopTiming timing, CancellationToken ct)`

在线程池上运行一个异步 `Func<ValkarnTask<T>>`。当工作本身是异步的（如文件 I/O）时使用此方法。

```csharp
public static ValkarnTask<T> Run<T>(
    Func<ValkarnTask<T>> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
string json = await ValkarnTask.Run(async () =>
{
    using var reader = File.OpenText("data.json");
    return await reader.ReadToEndAsync();
});
```

两种 `Run` 重载在 token 已被取消时提前取消，并在工作完成后切换回 `timing` 时机的主线程。

---

## 实例成员

### `IsCompleted`

```csharp
public bool IsCompleted { get; }
```

如果任务已在任何终结状态（Succeeded、Faulted 或 Canceled）中完成，则返回 `true`。对于同步完成的任务（`source == null`），始终返回 `true` 且无接口派发。

```csharp
var task = SomeLongOperation();
if (task.IsCompleted)
{
    int result = task.GetAwaiter().GetResult();
    Use(result);
}
```

### `GetStatus()`

```csharp
public ValkarnTask.Status GetStatus()
```

返回当前的 `ValkarnTask.Status`。可能的值：`Pending`、`Succeeded`、`Faulted`、`Canceled`。对于 `source == null`，始终返回 `Succeeded`。

### `GetAwaiter()`

```csharp
public Awaiter GetAwaiter()
```

返回一个 `Awaiter` 结构体。由编译器用于实现 `await`。你也可以直接调用它以同步获取结果（仅在 `IsCompleted` 为 true 时安全）。

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(10);
int value = task.GetAwaiter().GetResult(); // 安全——同步完成
```

对待处理任务调用 `GetResult()` 会抛出 `InvalidOperationException`。

### `AsNonGeneric()`

```csharp
public ValkarnTask AsNonGeneric()
```

将此 `ValkarnTask<T>` 转换为非泛型 `ValkarnTask`，丢弃结果类型。生成的任务共享相同的底层 source 和 token，因此在同一时间完成。

```csharp
ValkarnTask<int> typedTask = ComputeAsync();
ValkarnTask voidTask = typedTask.AsNonGeneric();
await voidTask;   // 等待完成，忽略结果
```

当向组合器传递混合类型任务，或者只关心完成时机而不关心值时，这很有用。

---

## 返回 `ValkarnTask<T>` 的组合器

### `WhenAll`——类型化两任务重载

```csharp
public static ValkarnTask<(T1, T2)> WhenAll<T1, T2>(
    ValkarnTask<T1> task1, ValkarnTask<T2> task2)
```

并发运行两个任务并返回结果元组。如果任何任务错误或被取消，第一个异常胜出，另一个的错误通过 `ValkarnTask.UnobservedException` 报告。

**元组解构**可与 C# 解构自然配合：

```csharp
var (profile, inventory) = await ValkarnTask.WhenAll(
    FetchProfileAsync(userId),
    FetchInventoryAsync(userId)
);
```

**零分配快速路径：** 如果两个任务在调用点都已同步完成，则不创建池化对象，结果元组内联返回。

### `WhenAll`——类型化集合重载

```csharp
public static ValkarnTask<T[]> WhenAll<T>(IEnumerable<ValkarnTask<T>> tasks)
```

并发等待集合中的所有任务，并按索引顺序返回 `T[]`。

```csharp
var urls = new[] { "https://a.com", "https://b.com", "https://c.com" };
ValkarnTask<string>[] downloads = urls.Select(u => DownloadAsync(u)).ToArray();
string[] results = await ValkarnTask.WhenAll(downloads);
```

如果集合为空，返回 `ValkarnTask.FromResult(Array.Empty<T>())`——零分配。如果所有任务都已同步完成，则直接构建结果数组而不创建组合器 promise。

### `WhenAny`——类型化两任务重载

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    ValkarnTask<T> task1, ValkarnTask<T> task2)
```

任一任务完成后立即返回。结果元组包含胜者的从 0 开始的索引及其值。失败的任务继续运行；它们的错误（如有）通过 `ValkarnTask.UnobservedException` 报告。失败的取消不会被报告。

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(
    FetchFromCacheAsync(key),
    FetchFromNetworkAsync(key)
);

if (winnerIndex == 0)
    Debug.Log("缓存胜出");
```

### `WhenAny`——类型化集合重载

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    IEnumerable<ValkarnTask<T>> tasks)
```

与两任务重载语义相同，扩展到任意数量的任务。至少需要一个任务；对于空集合抛出 `ArgumentException`。

```csharp
var tasks = servers.Select(s => s.FetchAsync(query)).ToArray();
var (winnerIndex, data) = await ValkarnTask.WhenAny(tasks);
Debug.Log($"服务器 {winnerIndex} 最先响应");
```

---

## 工厂便捷方法

### `ValkarnTask.Create<T>(Func<ValkarnTask<T>> factory)`

调用工厂委托并等待生成的任务。当你希望延迟构建异步操作时很有用。

```csharp
public static async ValkarnTask<T> Create<T>(Func<ValkarnTask<T>> factory)
```

```csharp
var result = await ValkarnTask.Create(() => LoadLevelDataAsync(levelId));
```

---

## `Awaiter` 结构体（嵌套）

`ValkarnTask<T>.Awaiter` 是面向编译器的等待器。它是一个实现了 `ICriticalNotifyCompletion` 的 `readonly struct`。通常你不需要直接与它交互。

```csharp
public readonly struct Awaiter : ICriticalNotifyCompletion
{
    public bool IsCompleted { get; }
    public T GetResult();
    public void OnCompleted(Action continuation);
    public void UnsafeOnCompleted(Action continuation);
}
```

`UnsafeOnCompleted` 是 `AsyncValkarnTaskMethodBuilder<T>` 使用的路径。"unsafe"标签意味着不捕获 `ExecutionContext`——这对 Unity 是有意为之的，因为 Unity 中没有 `SynchronizationContext` 生效。

当 `IsCompleted` 为 true 时，调用 `GetResult()` 读取内联 `result` 字段（对于同步任务）或通过 source 接口调用（对于异步任务）。两条路径都使用 `[MethodImpl(MethodImplOptions.AggressiveInlining)]`。

---

## `ValkarnTask<T>` 上的扩展方法

### `AsResult<T>()`

```csharp
public static ValkarnTask<Result<T>> AsResult<T>(this ValkarnTask<T> task)
```

将 `ValkarnTask<T>` 包装成 `ValkarnTask<Result<T>>`，捕获任何异常或取消并将其编码到 `Result<T>` 值中。这避免了在调用点使用 try/catch。

```csharp
Result<string> result = await FetchDataAsync(url).AsResult();

if (result.IsSuccess)
    Process(result.Value);
else if (result.IsCanceled)
    Debug.Log("已取消");
else
    Debug.LogError(result.Exception);
```

**同步快速路径：** 如果源任务已同步完成，`AsResult` 同步返回，不涉及任何异步机制。

---

## `Promise<T>`——手动完成源

`ValkarnTask.Promise<T>` 是一个堆分配的手动完成源，适用于需要控制 `ValkarnTask<T>` 何时完成且生命周期不受单个异步操作限制的场景。

```csharp
public class Promise<T>
{
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

```csharp
// 包装基于回调的 API
var promise = new ValkarnTask.Promise<string>();

SomeCallbackApi.OnComplete += value => promise.TrySetResult(value);
SomeCallbackApi.OnError   += ex    => promise.TrySetException(ex);

string result = await promise.Task;
```

与 `PooledPromise<T>` 不同，`Promise<T>` 不使用对象池。它使用终结器来检测和报告未观察到的异常（如果任务错误而调用者从未等待它）。

对于高频模式（生产者/消费者循环、每帧操作），优先使用 `ValkarnTask.PooledPromise<T>`，它在 `GetResult` 被调用后自动返回对象池。

---

## `PooledPromise<T>`——池化手动完成源

```csharp
public sealed class PooledPromise<T> : ValkarnTask.ISource<T>, IPoolNode<PooledPromise<T>>
{
    public static PooledPromise<T> Create(out uint token);
    public static PooledPromise<T> CreateCompleted(T result, out uint token);
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

在后备任务上调用 `GetResult` 后，promise 重置其 `ValkarnTaskCompletionCore<T>` 并将自身返回对象池。即使并发调用两次 `GetResult`，双重归还防护也确保这只发生一次。

```csharp
// 模式：生成一个在就绪时完成的 ValkarnTask<T>
var promise = ValkarnTask.PooledPromise<int>.Create(out uint token);
ValkarnTask<int> task = promise.Task;

// 异步分发工作
ThreadPool.QueueUserWorkItem(_ =>
{
    int result = DoWork();
    promise.TrySetResult(result);
});

// 消费者等待；完成后 promise 自动返回对象池
int value = await task;
```

---

## 获取 `ValkarnTask<T>` 的方式总结

| 方法 | 使用场景 |
|--------|------------|
| 在 `async ValkarnTask<T>` 内 `return value` | 普通异步方法 |
| `ValkarnTask.FromResult(value)` | 同步快速返回 |
| `ValkarnTask.FromException<T>(ex)` | 预先错误的任务 |
| `ValkarnTask.FromCanceled<T>(ct)` | 预先取消的任务 |
| `ValkarnTask.Run<T>(Func<T>, ...)` | 线程池卸载 |
| `ValkarnTask.Run<T>(Func<ValkarnTask<T>>, ...)` | 异步线程池工作 |
| `ValkarnTask.WhenAll<T1,T2>(t1, t2)` | 等待两个类型化任务，获取元组 |
| `ValkarnTask.WhenAll<T>(IEnumerable<...>)` | 等待 N 个类型化任务，获取数组 |
| `ValkarnTask.WhenAny<T>(t1, t2)` | 两个类型化任务中的第一个 |
| `ValkarnTask.WhenAny<T>(IEnumerable<...>)` | N 个类型化任务中的第一个 |
| `task.AsResult<T>()` | 异常安全包装 |
| `new ValkarnTask.Promise<T>()` → `.Task` | 长生命周期手动完成 |
| `ValkarnTask.PooledPromise<T>.Create(...)` → `.Task` | 高频手动完成 |
