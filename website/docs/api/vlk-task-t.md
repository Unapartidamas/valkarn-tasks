---
sidebar_position: 2
title: ValkarnTask<T>
---

# `ValkarnTask<T>`

`ValkarnTask<T>` is the value-returning async task type in Valkarn Tasks. It is a `readonly struct`, carrying either an inline result (when synchronously completed) or a reference to a pooled source object (when asynchronously completed).

**Namespace:** `UnaPartidaMas.Valkarn.Tasks`

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncValkarnTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
```

There are no generic constraints on `T`. Any type — value type, reference type, struct, or class — is valid.

---

## Creating instances

### Synchronously completed tasks

These factory methods return a `ValkarnTask<T>` with no backing source object. Zero allocation.

#### `ValkarnTask.FromResult<T>(T value)`

Returns a completed `ValkarnTask<T>` carrying `value` inline. Declared as a static method on the non-generic `ValkarnTask` type.

```csharp
public static ValkarnTask<T> FromResult<T>(T value)
```

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(42);
ValkarnTask<string> name = ValkarnTask.FromResult("Valkarn");
ValkarnTask<Vector3> pos = ValkarnTask.FromResult(transform.position);
```

The returned struct has `source == null`. Awaiting it incurs no continuation allocation — the compiler sees `IsCompleted == true` immediately.

#### `ValkarnTask.FromException<T>(Exception exception)`

Returns a faulted `ValkarnTask<T>`. Awaiting it re-throws the exception with its original stack trace preserved via `ExceptionDispatchInfo`.

```csharp
public static ValkarnTask<T> FromException<T>(Exception exception)
```

```csharp
ValkarnTask<Texture2D> LoadTexture(string path)
{
    if (string.IsNullOrEmpty(path))
        return ValkarnTask.FromException<Texture2D>(
            new ArgumentException("Path must not be empty.", nameof(path)));

    return LoadTextureAsync(path);
}
```

#### `ValkarnTask.FromCanceled<T>(CancellationToken ct = default)`

Returns a canceled `ValkarnTask<T>`. Awaiting it throws `OperationCanceledException`.

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

### Via `async` methods

Any `async` method declared to return `ValkarnTask<T>` uses `AsyncValkarnTaskMethodBuilder<TResult>` automatically:

```csharp
async ValkarnTask<int> ComputeAsync()
{
    await ValkarnTask.Yield();
    return 42;
}
```

The compiler generates a state machine. If the method completes synchronously (never suspends), `AsyncValkarnTaskMethodBuilder<T>.Task` returns `new ValkarnTask<T>(result)` with `source == null` — zero allocation.

---

## Running work on the thread pool

These methods run a delegate on the .NET thread pool and return the result on the main thread (at the specified `PlayerLoopTiming`). They are convenience wrappers over the longer-named `RunOnThreadPool` variants.

#### `ValkarnTask.Run<T>(Func<T> func, PlayerLoopTiming timing, CancellationToken ct)`

Runs a synchronous `Func<T>` on the thread pool.

```csharp
public static ValkarnTask<T> Run<T>(
    Func<T> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
// Compute on thread pool, result arrives back on main thread at next Update
int hash = await ValkarnTask.Run(() => ComputeExpensiveHash(data));
```

#### `ValkarnTask.Run<T>(Func<ValkarnTask<T>> func, PlayerLoopTiming timing, CancellationToken ct)`

Runs an async `Func<ValkarnTask<T>>` on the thread pool. Use this when the work itself is async (e.g., file I/O).

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

Both `Run` overloads cancel early if the token is already cancelled, and switch back to the main thread at `timing` after the work completes.

---

## Instance members

### `IsCompleted`

```csharp
public bool IsCompleted { get; }
```

Returns `true` if the task has completed in any terminal state (Succeeded, Faulted, or Canceled). For synchronously completed tasks (`source == null`), always returns `true` with no interface dispatch.

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

Returns the current `ValkarnTask.Status`. Possible values: `Pending`, `Succeeded`, `Faulted`, `Canceled`. For `source == null`, always returns `Succeeded`.

### `GetAwaiter()`

```csharp
public Awaiter GetAwaiter()
```

Returns an `Awaiter` struct. Used by the compiler to implement `await`. You can also call it directly to obtain the result synchronously (only safe when `IsCompleted` is true).

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(10);
int value = task.GetAwaiter().GetResult(); // safe — sync-completed
```

Calling `GetResult()` on a pending task throws an `InvalidOperationException`.

### `AsNonGeneric()`

```csharp
public ValkarnTask AsNonGeneric()
```

Converts this `ValkarnTask<T>` to a non-generic `ValkarnTask`, discarding the result type. The resulting task shares the same underlying source and token, so it completes at the same time.

```csharp
ValkarnTask<int> typedTask = ComputeAsync();
ValkarnTask voidTask = typedTask.AsNonGeneric();
await voidTask;   // waits for completion, ignores result
```

This is useful when passing mixed-type tasks to combinators or when you only care about completion timing, not the value.

---

## Combinators returning `ValkarnTask<T>`

### `WhenAll` — typed two-task overload

```csharp
public static ValkarnTask<(T1, T2)> WhenAll<T1, T2>(
    ValkarnTask<T1> task1, ValkarnTask<T2> task2)
```

Runs both tasks concurrently and returns a tuple of results. If any task faults or is canceled, the first exception wins and the other's error is reported through `ValkarnTask.UnobservedException`.

**Tuple destructuring** works naturally with C# deconstruction:

```csharp
var (profile, inventory) = await ValkarnTask.WhenAll(
    FetchProfileAsync(userId),
    FetchInventoryAsync(userId)
);
```

**Zero-alloc fast path:** if both tasks are synchronously completed at the call site, no pooled object is created and the result tuple is returned inline.

### `WhenAll` — typed collection overload

```csharp
public static ValkarnTask<T[]> WhenAll<T>(IEnumerable<ValkarnTask<T>> tasks)
```

Awaits all tasks in the collection concurrently and returns a `T[]` in index order.

```csharp
var urls = new[] { "https://a.com", "https://b.com", "https://c.com" };
ValkarnTask<string>[] downloads = urls.Select(u => DownloadAsync(u)).ToArray();
string[] results = await ValkarnTask.WhenAll(downloads);
```

If the collection is empty, returns `ValkarnTask.FromResult(Array.Empty<T>())` — zero allocation. If all tasks are already synchronously completed, a result array is built inline without creating a combinator promise.

### `WhenAny` — typed two-task overload

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    ValkarnTask<T> task1, ValkarnTask<T> task2)
```

Returns as soon as either task completes. The result tuple contains the winner's 0-based index and its value. Losing tasks continue running; their errors (if any) are reported through `ValkarnTask.UnobservedException`. Losing cancellations are intentionally not reported.

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(
    FetchFromCacheAsync(key),
    FetchFromNetworkAsync(key)
);

if (winnerIndex == 0)
    Debug.Log("Cache won");
```

### `WhenAny` — typed collection overload

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    IEnumerable<ValkarnTask<T>> tasks)
```

Same semantics as the two-task overload, extended to any number of tasks. Requires at least one task; throws `ArgumentException` for empty collections.

```csharp
var tasks = servers.Select(s => s.FetchAsync(query)).ToArray();
var (winnerIndex, data) = await ValkarnTask.WhenAny(tasks);
Debug.Log($"Server {winnerIndex} responded first");
```

---

## Factory convenience methods

### `ValkarnTask.Create<T>(Func<ValkarnTask<T>> factory)`

Invokes the factory delegate and awaits the resulting task. Useful when you want to defer construction of an async operation.

```csharp
public static async ValkarnTask<T> Create<T>(Func<ValkarnTask<T>> factory)
```

```csharp
var result = await ValkarnTask.Create(() => LoadLevelDataAsync(levelId));
```

---

## `Awaiter` struct (nested)

`ValkarnTask<T>.Awaiter` is the compiler-facing awaiter. It is a `readonly struct` implementing `ICriticalNotifyCompletion`. You normally do not interact with it directly.

```csharp
public readonly struct Awaiter : ICriticalNotifyCompletion
{
    public bool IsCompleted { get; }
    public T GetResult();
    public void OnCompleted(Action continuation);
    public void UnsafeOnCompleted(Action continuation);
}
```

`UnsafeOnCompleted` is the path used by `AsyncValkarnTaskMethodBuilder<T>`. The "unsafe" label means `ExecutionContext` is not captured — this is intentional for Unity where no `SynchronizationContext` is in effect.

When `IsCompleted` is true, calling `GetResult()` reads the inline `result` field (for sync tasks) or calls through the source interface (for async tasks). Both paths are `[MethodImpl(MethodImplOptions.AggressiveInlining)]`.

---

## Extension methods on `ValkarnTask<T>`

### `AsResult<T>()`

```csharp
public static ValkarnTask<Result<T>> AsResult<T>(this ValkarnTask<T> task)
```

Wraps a `ValkarnTask<T>` into a `ValkarnTask<Result<T>>`, catching any exception or cancellation and encoding it into the `Result<T>` value. This avoids try/catch at the call site.

```csharp
Result<string> result = await FetchDataAsync(url).AsResult();

if (result.IsSuccess)
    Process(result.Value);
else if (result.IsCanceled)
    Debug.Log("Canceled");
else
    Debug.LogError(result.Exception);
```

**Sync fast path:** if the source task is already synchronously completed, `AsResult` returns synchronously with no async machinery involved.

---

## `Promise<T>` — manual completion source

`ValkarnTask.Promise<T>` is a heap-allocated manual completion source for cases where you need to control when a `ValkarnTask<T>` completes, and the lifetime is not bounded by a single async operation.

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
// Wrapping a callback-based API
var promise = new ValkarnTask.Promise<string>();

SomeCallbackApi.OnComplete += value => promise.TrySetResult(value);
SomeCallbackApi.OnError   += ex    => promise.TrySetException(ex);

string result = await promise.Task;
```

Unlike `PooledPromise<T>`, `Promise<T>` is not pooled. It uses a finalizer to detect and report unobserved exceptions if the task faults and the caller never awaits it.

For high-frequency patterns (producer/consumer loops, per-frame operations), prefer `ValkarnTask.PooledPromise<T>`, which automatically returns to the pool after `GetResult` is called.

---

## `PooledPromise<T>` — pooled manual completion source

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

After `GetResult` is called on the backing task, the promise resets its `ValkarnTaskCompletionCore<T>` and returns itself to the pool. A double-return guard ensures this happens at most once even if `GetResult` is called concurrently.

```csharp
// Pattern: produce a ValkarnTask<T> that completes when ready
var promise = ValkarnTask.PooledPromise<int>.Create(out uint token);
ValkarnTask<int> task = promise.Task;

// Dispatch work asynchronously
ThreadPool.QueueUserWorkItem(_ =>
{
    int result = DoWork();
    promise.TrySetResult(result);
});

// Consumer awaits; on completion promise returns to pool automatically
int value = await task;
```

---

## Summary of ways to obtain a `ValkarnTask<T>`

| Method | When to use |
|--------|------------|
| `return value` inside `async ValkarnTask<T>` | Normal async methods |
| `ValkarnTask.FromResult(value)` | Synchronous fast returns |
| `ValkarnTask.FromException<T>(ex)` | Pre-faulted tasks |
| `ValkarnTask.FromCanceled<T>(ct)` | Pre-canceled tasks |
| `ValkarnTask.Run<T>(Func<T>, ...)` | Thread-pool offloading |
| `ValkarnTask.Run<T>(Func<ValkarnTask<T>>, ...)` | Async thread-pool work |
| `ValkarnTask.WhenAll<T1,T2>(t1, t2)` | Wait for two typed tasks, get tuple |
| `ValkarnTask.WhenAll<T>(IEnumerable<...>)` | Wait for N typed tasks, get array |
| `ValkarnTask.WhenAny<T>(t1, t2)` | First of two typed tasks |
| `ValkarnTask.WhenAny<T>(IEnumerable<...>)` | First of N typed tasks |
| `task.AsResult<T>()` | Exception-safe wrapping |
| `new ValkarnTask.Promise<T>()` → `.Task` | Long-lived manual completion |
| `ValkarnTask.PooledPromise<T>.Create(...)` → `.Task` | High-frequency manual completion |
