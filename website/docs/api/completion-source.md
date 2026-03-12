---
sidebar_position: 3
title: Completion Sources
---

# ValkarnTaskCompletionSource

`ValkarnTaskCompletionSource<T>` and the non-generic `ValkarnTaskCompletionSource` give you manual control over a `ValkarnTask`. They are the async equivalent of writing the result yourself — you hold a source object, hand out its `.Task` to callers, and then resolve, fault, or cancel it from a separate call site.

This is the Valkarn Tasks equivalent of `TaskCompletionSource<T>` from the BCL, but backed by `ValkarnTask.Promise<T>` to keep the allocation model consistent with the rest of the library.

---

## ValkarnTaskCompletionSource&lt;T&gt;

```csharp
public class ValkarnTaskCompletionSource<T>
{
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled();
}
```

### Task

```csharp
public ValkarnTask<T> Task { get; }
```

The task that awaiting code will observe. Distribute this to all callers that need to wait for the result. Multiple callers may `await` the same task concurrently.

### TrySetResult

```csharp
public bool TrySetResult(T result);
```

Completes the task successfully with the given value. All awaiting continuations resume. Returns `true` if the completion was accepted; returns `false` if the task was already completed (by any prior `TrySet*` call). Never throws.

### TrySetException

```csharp
public bool TrySetException(Exception ex);
```

Faults the task with the provided exception. Awaiting code receives the exception when it calls `await`. Throws `ArgumentNullException` if `ex` is `null`. Returns `true` if accepted, `false` if already completed.

### TrySetCanceled

```csharp
public bool TrySetCanceled();
```

Cancels the task. Awaiting code receives an `OperationCanceledException`. Returns `true` if accepted, `false` if already completed.

---

## Non-generic ValkarnTaskCompletionSource

There is no separate non-generic `ValkarnTaskCompletionSource` class in the public API. For void-returning manual tasks, use `ValkarnTask.Promise` directly:

```csharp
var promise = new ValkarnTask.Promise();
ValkarnTask task = promise.Task;

promise.TrySetResult();    // complete
promise.TrySetException(ex);
promise.TrySetCanceled();
```

`ValkarnTask.Promise` exposes the same `TrySet*` surface and the same double-completion protection, but produces a non-generic `ValkarnTask` rather than a `ValkarnTask<T>`.

---

## Double-Completion Protection

All `TrySet*` methods are safe to call from any thread at any time, including concurrently. The first call that wins a compare-and-swap on the internal state machine succeeds; every subsequent call returns `false` and has no effect. This means:

- Calling `TrySetResult` twice does nothing on the second call.
- Calling `TrySetResult` after `TrySetException` does nothing.
- Two threads racing to complete the same source simultaneously is safe — one wins, one is silently ignored.

If you need to know which caller "won", check the return value. If you do not care (fire-and-forget signal), you can safely ignore it.

```csharp
// Safe race — only one of these will actually complete the task
_ = source.TrySetResult(value);
_ = source.TrySetCanceled();
```

---

## Common Patterns

### Bridging a callback API

Many Unity and platform APIs deliver results through callbacks rather than async/await. `ValkarnTaskCompletionSource<T>` lets you wrap them cleanly.

```csharp
public ValkarnTask<Texture2D> LoadTextureAsync(string url)
{
    var tcs = new ValkarnTaskCompletionSource<Texture2D>();

    StartCoroutine(LoadCoroutine(url, tcs));

    return tcs.Task;
}

IEnumerator LoadCoroutine(string url, ValkarnTaskCompletionSource<Texture2D> tcs)
{
    var request = UnityWebRequestTexture.GetTexture(url);
    yield return request.SendWebRequest();

    if (request.result == UnityWebRequest.Result.Success)
        tcs.TrySetResult(DownloadHandlerTexture.GetContent(request));
    else
        tcs.TrySetException(new Exception(request.error));
}
```

Callers can now simply `await` the returned task:

```csharp
Texture2D tex = await LoadTextureAsync("https://example.com/image.png");
```

### One-shot signal (async gate)

Use `ValkarnTask.Promise` when you need a signal that fires once and unblocks any number of waiters. This is similar to a `ManualResetEventSlim` but async-native.

```csharp
public class AsyncGate
{
    readonly ValkarnTask.Promise _promise = new();

    // Any number of callers can await this
    public ValkarnTask WaitAsync() => _promise.Task;

    // Call once to unblock everyone
    public void Open() => _promise.TrySetResult();
}

// Usage
var gate = new AsyncGate();

// Several systems await the gate independently
async ValkarnTask SystemAAsync()
{
    await gate.WaitAsync();
    // proceed after gate opens
}

async ValkarnTask SystemBAsync()
{
    await gate.WaitAsync();
    // proceeds at the same time as SystemA
}

// Somewhere else — open the gate
gate.Open();
```

Once `TrySetResult()` is called, all current and future `await` calls on the task complete immediately (synchronously if the task is already done by the time they run).

### Wrapping a third-party async operation with cancellation support

```csharp
public ValkarnTask<Result> RunWithTimeoutAsync(
    Func<ValkarnTask<Result>> operation,
    float timeoutSeconds,
    CancellationToken ct)
{
    var tcs = new ValkarnTaskCompletionSource<Result>();

    RunCoreAsync(tcs, operation, timeoutSeconds, ct).Forget();

    return tcs.Task;
}

async ValkarnTask RunCoreAsync(
    ValkarnTaskCompletionSource<Result> tcs,
    Func<ValkarnTask<Result>> operation,
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

### Deferred initialization gate

A common Unity pattern is to expose a "ready" task that components can await regardless of whether initialization has started yet.

```csharp
public class ServiceBus : MonoBehaviour
{
    readonly ValkarnTask.Promise _readyPromise = new();

    // Anyone can await this at any point — before or after Initialize()
    public ValkarnTask Ready => _readyPromise.Task;

    async void Start()
    {
        await LoadConfigAsync();
        await ConnectAsync();
        _readyPromise.TrySetResult();  // unblocks all waiters
    }
}

// In any other component
async ValkarnTask OnEnableAsync()
{
    await ServiceBus.Instance.Ready;  // waits if not ready, returns instantly if already ready
    DoWork();
}
```

---

## Relationship to ValkarnTask.Promise

`ValkarnTaskCompletionSource<T>` is a thin public wrapper around `ValkarnTask.Promise<T>`. Both provide the same functionality. The difference is naming convention:

| Type | Returns | Common use |
|------|---------|------------|
| `ValkarnTaskCompletionSource<T>` | `ValkarnTask<T>` | Public API, mirrors BCL `TaskCompletionSource<T>` style |
| `ValkarnTask.Promise<T>` | `ValkarnTask<T>` | Internal use, slightly more direct |
| `ValkarnTask.Promise` | `ValkarnTask` | Void signals (gates, events) |

All three back their task with a `ValkarnTaskCompletionCore<T>`, the internal struct-based state machine that handles thread safety and the continuation/result race using a CAS-based two-phase protocol.
