---
sidebar_position: 1
title: Struct Tasks
---

# Struct Tasks

`ValkarnTask` and `ValkarnTask<T>` are the core async return types in Valkarn Tasks. Unlike `System.Threading.Tasks.Task`, which is a reference type that always allocates on the heap, both Valkarn task types are `readonly struct` values. This page explains what that means in practice, how the zero-allocation fast path works, and how the compiler integrates with the async/await machinery.

---

## Why a `readonly struct`?

A class-based task like `Task<T>` must be allocated on the heap every time an async method is called, even for methods that complete synchronously. In a Unity game loop running at 60 fps, hundreds of small async operations each frame can add up to measurable GC pressure.

`ValkarnTask` and `ValkarnTask<T>` are declared as `readonly partial struct`:

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

Being a struct means the task value itself lives on the stack (or inline in its parent object) rather than on the heap. The `readonly` modifier ensures the compiler can reason about immutability and prevents accidental copying bugs. `StructLayout.Auto` lets the runtime optimize field ordering for the target platform.

### The key invariant: `source == null`

The design is built around a single invariant:

> When `source` is `null`, the task is synchronously completed with no error. No heap object is involved.

`ValkarnTask.CompletedTask` is `default(ValkarnTask)` — its `source` field is null, so it costs nothing. `ValkarnTask<T>` carries its result inline in the `result` field, also making `ValkarnTask.FromResult(value)` a zero-allocation call:

```csharp
// Zero allocation — source is null, result stored inline
ValkarnTask<int> task = ValkarnTask.FromResult(42);

// Also zero allocation — source is null
ValkarnTask done = ValkarnTask.CompletedTask;
```

---

## The zero-allocation happy path

When an `async` method completes without ever suspending (no `await` yields to an incomplete operation), the entire method runs synchronously on the calling thread. The builder detects this and returns a task with `source == null`.

The awaiter checks this immediately:

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

When `IsCompleted` is true before `OnCompleted` is ever called, the state machine does not register a continuation. `GetResult` is called immediately, and for `ValkarnTask<T>` with `source == null`, the result is read from the struct's inline `result` field:

```csharp
public T GetResult()
{
    var s = task.source;
    if (s == null)
        return task.result;   // inline, no ISource call
    return s.GetResult(task.token);
}
```

No object is created, no interface dispatch occurs, and no continuation delegate is allocated. The entire await resolves as a direct value read.

### When a source is needed

If an async method suspends (awaits something that is not yet complete), the builder allocates a pooled `AsyncValkarnTaskRunner<TStateMachine>` object (or `AsyncValkarnTaskRunner<TStateMachine, TResult>` for the generic variant). This object serves double duty: it holds the compiler-generated state machine by value and implements `ValkarnTask.ISource`, so it can be used directly as the task's backing source. The task returned to callers wraps this runner along with a generational `uint` token.

On completion, when the caller calls `GetResult` on the awaiter, the runner resets itself and returns to its pool — so the allocation is amortized across many method invocations.

---

## The `ISource` interface

The contract between a `ValkarnTask` struct and its asynchronous backing object is `ValkarnTask.ISource`:

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

Any object that implements `ISource` can back a `ValkarnTask`. The library ships several implementations:

| Type | Purpose |
|------|---------|
| `AsyncValkarnTaskRunner<TStateMachine>` | Backs every `async ValkarnTask` method (internal) |
| `AsyncValkarnTaskRunner<TStateMachine, TResult>` | Backs every `async ValkarnTask<T>` method (internal) |
| `ValkarnTask.PooledPromise` | Manual completion source with automatic pool return |
| `ValkarnTask.PooledPromise<T>` | Generic variant of the above |
| `ValkarnTask.Promise` | Manual completion source without pooling (long-lived operations) |
| `ValkarnTask.Promise<T>` | Generic variant of the above |

The `uint token` parameter is a generational guard. When a pooled source is reset for reuse, its generation counter increments. Any `ValkarnTask` struct holding the old token will immediately receive an `InvalidOperationException` rather than silently reading recycled state.

---

## `ValkarnTask` vs `ValkarnTask<T>`

| Feature | `ValkarnTask` | `ValkarnTask<T>` |
|---------|-----------|-------------|
| Return value | None (void equivalent) | `T` |
| Inline result storage | No `result` field | `result` field (type `T`) |
| Awaiter `GetResult` | `void` | Returns `T` |
| Builder type | `AsyncValkarnTaskMethodBuilder` | `AsyncValkarnTaskMethodBuilder<TResult>` |
| Sync-completed value | `ValkarnTask.CompletedTask` | `ValkarnTask.FromResult(value)` |
| Convert to non-generic | Not applicable | `.AsNonGeneric()` |

Use `ValkarnTask` when an async method has no meaningful return value, and `ValkarnTask<T>` when it produces a result. You can always downcast a `ValkarnTask<T>` to a `ValkarnTask` via `AsNonGeneric()` when you need to mix typed and untyped tasks in combinators like `WhenAll`.

---

## How the async method builder works

The C# compiler looks for the type named in `[AsyncMethodBuilder(...)]` on the return type. For `ValkarnTask`, that is `AsyncValkarnTaskMethodBuilder`. For `ValkarnTask<T>`, it is `AsyncValkarnTaskMethodBuilder<TResult>`.

The builder is itself a struct to avoid a heap allocation just for the builder object. It has two fields (three for the generic variant):

```csharp
public struct AsyncValkarnTaskMethodBuilder
{
    IStateMachineRunnerPromise runner;   // null until first suspension
    Exception syncException;            // only set on sync-faulted path
}

public struct AsyncValkarnTaskMethodBuilder<TResult>
{
    IStateMachineRunnerPromise<TResult> runner;
    Exception syncException;
    TResult result;                     // only set on sync-success path
}
```

### Builder lifecycle

The compiler calls these methods in order:

**1. `Create()`** — returns a default builder (all fields null/default). No allocation.

**2. `Start(ref stateMachine)`** — calls `stateMachine.MoveNext()` synchronously. If the method completes without hitting an incomplete `await`, `SetResult`/`SetException` is called and `runner` remains null.

**3. `AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine)`** — called when the method encounters an incomplete `await`. If `runner` is null (first suspension), it rents or creates an `AsyncValkarnTaskRunner` and copies the state machine into it. Then it calls `awaiter.UnsafeOnCompleted(runner.MoveNextAction)` to register the state machine continuation.

**4. `SetResult()` / `SetException(exception)`** — signals completion into the runner's `ValkarnTaskCompletionCore`, which wakes any registered awaiter.

**5. `Task` property** — checked by the caller to get the `ValkarnTask` value. On the sync-success path (`runner == null && syncException == null`), returns `default` (or `new ValkarnTask<T>(result)` for the generic variant) — zero allocation. On the async path, wraps the runner as the source.

The critical optimization is that `runner` is allocated lazily. If a method completes synchronously (the common case for cache hits, guards, early returns), no pooled object is ever rented.

---

## `ValkarnTaskStatus` states

Status is represented by a `byte`-sized enum nested inside `ValkarnTask`:

```csharp
public enum Status : byte
{
    Pending   = 0,   // not yet complete
    Succeeded = 1,   // completed normally
    Faulted   = 2,   // completed with an unhandled exception
    Canceled  = 3    // completed via OperationCanceledException
}
```

You can check status directly:

```csharp
ValkarnTask task = SomeOperation();
ValkarnTask.Status status = task.GetStatus();

switch (status)
{
    case ValkarnTask.Status.Pending:
        // Still running — cannot call GetResult
        break;
    case ValkarnTask.Status.Succeeded:
        // Completed normally
        break;
    case ValkarnTask.Status.Faulted:
        // Completed with exception — GetResult will rethrow
        break;
    case ValkarnTask.Status.Canceled:
        // Completed with OperationCanceledException
        break;
}
```

For the sync-completed fast path (where `source == null`), `GetStatus()` returns `Succeeded` without any interface call:

```csharp
public Status GetStatus()
{
    if (source == null) return Status.Succeeded;
    return source.GetStatus(token);
}
```

The `IsCompleted` property follows the same pattern and returns `true` for any non-`Pending` state.

---

## IL2CPP implications

IL2CPP compiles C# to C++ before building to native code. Generic value types — including structs — are fully specialized in the generated code, which has important consequences for this library.

**State machine specialization.** The compiler generates a unique state machine struct per async method. `AsyncValkarnTaskRunner<TStateMachine>` is therefore also unique per async method, and `ValkarnTaskPool<AsyncValkarnTaskRunner<TStateMachine>>` is a separate pool per method. This is actually beneficial: the pool is never shared across incompatible types, eliminating any risk of type confusion.

**No boxing of the state machine.** The state machine is stored by value inside the runner object, not boxed. IL2CPP handles this correctly because the runner is a `sealed class` with a concrete `TStateMachine` field.

**Stripping protection.** The `[AsyncMethodBuilder]` attribute keeps the builder types alive. However, if you use `ValkarnTask.ISource` through an interface reference in IL2CPP with aggressive stripping, add a `link.xml` entry preserving the `UnaPartidaMas.Valkarn.Tasks` assembly:

```xml
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```

**`ICriticalNotifyCompletion`.** The awaiter structs implement `ICriticalNotifyCompletion`, which tells the compiler to call `UnsafeOnCompleted` instead of `OnCompleted`. The "unsafe" variant intentionally skips `ExecutionContext` capture. This is correct for Unity — there is no `SynchronizationContext` in Unity's default configuration, and capturing one would add overhead for no benefit. Under IL2CPP, this also avoids the overhead of the `ExecutionContext.Run` path that standard `Task` always pays.

---

## Practical examples

### Returning early without allocation

```csharp
// async ValkarnTask<int> that completes synchronously on the hot path
async ValkarnTask<int> GetCachedValue(string key)
{
    if (_cache.TryGetValue(key, out var value))
        return value;            // compiler calls SetResult(value); source stays null

    var result = await FetchFromDatabaseAsync(key);
    _cache[key] = result;
    return result;
}
```

When the value is cached, the method never suspends. The returned `ValkarnTask<int>` has `source == null` and carries the result inline. No heap allocation occurs on this path.

### Checking IsCompleted before awaiting

```csharp
ValkarnTask<Texture2D> loadTask = LoadTextureAsync("sprites/hero.png");

if (loadTask.IsCompleted)
{
    // Already done — GetAwaiter().GetResult() reads inline result with no ISource call
    Texture2D tex = loadTask.GetAwaiter().GetResult();
    ApplyTexture(tex);
}
else
{
    // Genuinely async — register continuation
    ApplyTextureAsync(loadTask).Forget();
}
```

### Observing unhandled exceptions

Faulted tasks that are never awaited (fire-and-forget patterns) report their exceptions through the `ValkarnTask.UnobservedException` event. This is raised deterministically at pool-return time for pooled sources, or from the finalizer for `Promise`-backed tasks.

```csharp
ValkarnTask.UnobservedException += ex =>
{
    Debug.LogError($"[ValkarnTask] Unobserved: {ex}");
};
```

The event is thread-safe; handlers may be added or removed from any thread using a lock-free compare-exchange loop.
