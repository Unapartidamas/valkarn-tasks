---
sidebar_position: 3
title: 完成源
---

# VlkTaskCompletionSource

`VlkTaskCompletionSource<T>` 和非泛型的 `VlkTaskCompletionSource` 让你对 `VlkTask` 拥有手动控制权。它们是自己编写异步结果的等价物——你持有一个 source 对象，将其 `.Task` 分发给调用者，然后从另一个调用点解决、错误或取消它。

这是 Valkarn Tasks 对 BCL 中 `TaskCompletionSource<T>` 的等价实现，但由 `VlkTask.Promise<T>` 后备，以保持与库其余部分一致的分配模型。

---

## VlkTaskCompletionSource&lt;T&gt;

```csharp
public class VlkTaskCompletionSource<T>
{
    public VlkTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled();
}
```

### Task

```csharp
public VlkTask<T> Task { get; }
```

等待代码将观察到的任务。将其分发给所有需要等待结果的调用者。多个调用者可以并发 `await` 同一个任务。

### TrySetResult

```csharp
public bool TrySetResult(T result);
```

以给定值成功完成任务。所有等待中的续体恢复。如果完成被接受，返回 `true`；如果任务已经完成（通过任何先前的 `TrySet*` 调用），返回 `false`。永不抛出。

### TrySetException

```csharp
public bool TrySetException(Exception ex);
```

以提供的异常使任务错误。等待代码在调用 `await` 时收到异常。如果 `ex` 为 `null`，抛出 `ArgumentNullException`。如果被接受，返回 `true`；如果已完成，返回 `false`。

### TrySetCanceled

```csharp
public bool TrySetCanceled();
```

取消任务。等待代码收到 `OperationCanceledException`。如果被接受，返回 `true`；如果已完成，返回 `false`。

---

## 非泛型 VlkTaskCompletionSource

公共 API 中没有独立的非泛型 `VlkTaskCompletionSource` 类。对于 void 返回的手动任务，直接使用 `VlkTask.Promise`：

```csharp
var promise = new VlkTask.Promise();
VlkTask task = promise.Task;

promise.TrySetResult();    // 完成
promise.TrySetException(ex);
promise.TrySetCanceled();
```

`VlkTask.Promise` 公开了相同的 `TrySet*` 接口和相同的双重完成保护，但产生非泛型 `VlkTask` 而不是 `VlkTask<T>`。

---

## 双重完成保护

所有 `TrySet*` 方法都可以从任意线程在任意时间安全调用，包括并发调用。第一个在内部状态机上赢得 compare-and-swap 的调用成功；每个后续调用返回 `false` 且无效果。这意味着：

- 两次调用 `TrySetResult` 时，第二次无效。
- 在 `TrySetException` 之后调用 `TrySetResult` 无效。
- 两个线程同时竞争完成同一个 source 是安全的——一个赢，一个被静默忽略。

如果你需要知道哪个调用者"赢了"，检查返回值。如果你不在乎（即发即弃信号），可以安全地忽略它。

```csharp
// 安全竞争——只有其中一个会真正完成任务
_ = source.TrySetResult(value);
_ = source.TrySetCanceled();
```

---

## 常见模式

### 桥接回调 API

许多 Unity 和平台 API 通过回调而不是 async/await 传递结果。`VlkTaskCompletionSource<T>` 让你可以干净地包装它们。

```csharp
public VlkTask<Texture2D> LoadTextureAsync(string url)
{
    var tcs = new VlkTaskCompletionSource<Texture2D>();

    StartCoroutine(LoadCoroutine(url, tcs));

    return tcs.Task;
}

IEnumerator LoadCoroutine(string url, VlkTaskCompletionSource<Texture2D> tcs)
{
    var request = UnityWebRequestTexture.GetTexture(url);
    yield return request.SendWebRequest();

    if (request.result == UnityWebRequest.Result.Success)
        tcs.TrySetResult(DownloadHandlerTexture.GetContent(request));
    else
        tcs.TrySetException(new Exception(request.error));
}
```

调用者现在可以简单地 `await` 返回的任务：

```csharp
Texture2D tex = await LoadTextureAsync("https://example.com/image.png");
```

### 一次性信号（异步门）

当你需要一个触发一次并解除任意数量等待者的信号时，使用 `VlkTask.Promise`。这类似于 `ManualResetEventSlim`，但是原生异步的。

```csharp
public class AsyncGate
{
    readonly VlkTask.Promise _promise = new();

    // 任意数量的调用者都可以等待这个
    public VlkTask WaitAsync() => _promise.Task;

    // 调用一次以解除所有人的阻塞
    public void Open() => _promise.TrySetResult();
}

// 使用
var gate = new AsyncGate();

// 几个系统独立等待门
async VlkTask SystemAAsync()
{
    await gate.WaitAsync();
    // 门打开后继续
}

async VlkTask SystemBAsync()
{
    await gate.WaitAsync();
    // 与 SystemA 同时继续
}

// 在其他地方——打开门
gate.Open();
```

一旦调用了 `TrySetResult()`，所有当前和未来对任务的 `await` 调用都立即完成（如果任务在它们运行时已完成，则同步完成）。

### 包装带取消支持的第三方异步操作

```csharp
public VlkTask<Result> RunWithTimeoutAsync(
    Func<VlkTask<Result>> operation,
    float timeoutSeconds,
    CancellationToken ct)
{
    var tcs = new VlkTaskCompletionSource<Result>();

    RunCoreAsync(tcs, operation, timeoutSeconds, ct).Forget();

    return tcs.Task;
}

async VlkTask RunCoreAsync(
    VlkTaskCompletionSource<Result> tcs,
    Func<VlkTask<Result>> operation,
    float timeout,
    CancellationToken ct)
{
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
    linked.CancelAfter(TimeSpan.FromSeconds(timeout));

    try
    {
        var result = await operation();
        tcs.TrySetResult(result);
    }
    catch (OperationCanceledException)
    {
        tcs.TrySetCanceled();
    }
    catch (Exception ex)
    {
        tcs.TrySetException(ex);
    }
}
```

### 延迟初始化门

一个常见的 Unity 模式是公开一个"就绪"任务，无论初始化是否已开始，组件都可以等待它。

```csharp
public class ServiceBus : MonoBehaviour
{
    readonly VlkTask.Promise _readyPromise = new();

    // 任何人都可以在任何时间等待这个——在 Initialize() 之前或之后
    public VlkTask Ready => _readyPromise.Task;

    async void Start()
    {
        await LoadConfigAsync();
        await ConnectAsync();
        _readyPromise.TrySetResult();  // 解除所有等待者
    }
}

// 在任何其他组件中
async VlkTask OnEnableAsync()
{
    await ServiceBus.Instance.Ready;  // 如果未就绪则等待，如果已就绪则立即返回
    DoWork();
}
```

---

## 与 VlkTask.Promise 的关系

`VlkTaskCompletionSource<T>` 是 `VlkTask.Promise<T>` 的薄公共包装。两者提供相同的功能。区别在于命名约定：

| 类型 | 返回 | 常见用途 |
|------|---------|------------|
| `VlkTaskCompletionSource<T>` | `VlkTask<T>` | 公共 API，镜像 BCL `TaskCompletionSource<T>` 风格 |
| `VlkTask.Promise<T>` | `VlkTask<T>` | 内部使用，更直接 |
| `VlkTask.Promise` | `VlkTask` | Void 信号（门、事件） |

所有三者都用 `VlkTaskCompletionCore<T>` 作为其任务的后备，这是内部的基于结构体的状态机，使用基于 CAS 的两阶段协议处理线程安全和续体/结果竞争。
