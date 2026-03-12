---
sidebar_position: 1
title: Analyzer Rules
---

# Analyzer Rules

Valkarn Tasks ships two Roslyn analyzer packages that activate automatically when the package is imported:

- **`UnaPartidaMas.Valkarn.Tasks.SourceGen.dll`** — Rules specific to ValkarnTask correctness and Unity lifecycle. Rule IDs begin with `TT`.
- **`UnaPartidaMas.Valkarn.Tasks.Analyzer.dll`** — Migration rules for codebases moving from UniTask. Rule IDs begin with `MIG`. These rules fire **only when `Cysharp.Threading.Tasks.UniTask` is present** in the compilation, so they are silent in new projects.

Both packages are pre-compiled DLLs located in the `Analyzers/` folder of the package. Unity loads them as Roslyn analyzers via the `.asmdef` reference; the `_TestRunner~` project loads them via `<Analyzer>` items in `TestRunner.csproj`.

---

## Correctness Rules (TT)

These rules catch bugs related to the single-consumption nature of `ValkarnTask` and fire-and-forget misuse.

---

### TT001 — ValkarnTask already awaited

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTask            |
| Code fix   | No                     |

`ValkarnTask` is single-consumption: once awaited, the internal token becomes stale. A second `await` on the same variable will encounter that stale token and throw. The analyzer detects when the same local variable, parameter, or field of type `ValkarnTask`/`ValkarnTask<T>` is awaited more than once inside the same method.

**Triggers on:**
```csharp
async ValkarnTask Bad()
{
    ValkarnTask work = DoWorkAsync();
    await work;   // first await — fine
    await work;   // TT001: already awaited
}
```

**Fix:** If you need to branch on the result, capture it with `.AsResult()` before the first await, or restructure so the task is awaited exactly once.
```csharp
async ValkarnTask Good()
{
    var result = await DoWorkAsync().AsResult();
    // use result in both branches
}
```

---

### TT002 — ValkarnTask not awaited or discarded

| Property   | Value                  |
|------------|------------------------|
| Severity   | **Error**              |
| Category   | ValkarnTask            |
| Code fix   | No                     |

A `ValkarnTask`-returning method call used as an expression statement — not awaited, not assigned, and not followed by `.Forget()` — is a silent bug. Exceptions thrown inside the task are never observed, and the task's pooled state machine is never returned to the pool.

The analyzer checks expression statements where the expression type resolves to `ValkarnTask` or `ValkarnTask<T>`. It skips:
- Assignment expressions (`tasks[i] = DoWork()`)
- Chains ending in `.Forget()` (intentional fire-and-forget)

**Triggers on:**
```csharp
void Bad()
{
    LoadDataAsync();      // TT002: not awaited or discarded
    ProcessItemAsync();   // TT002
}
```

**Fix — await it:**
```csharp
async ValkarnTask Good()
{
    await LoadDataAsync();
    await ProcessItemAsync();
}
```

**Fix — explicit fire-and-forget:**
```csharp
void GoodFireAndForget()
{
    LoadDataAsync().Forget();
}
```

---

### TT013 — ValkarnTask returned but never consumed

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTask            |
| Code fix   | No                     |

This rule ID is **reserved for future data-flow analysis**. It is intended to catch the assigned-but-never-awaited pattern — storing a `ValkarnTask` into a variable and then never awaiting or discarding it — which TT002 does not cover because TT002 only examines bare expression statements.

The current implementation registers no syntax actions. When data-flow analysis is implemented, TT013 will complement TT002 by covering:
```csharp
async ValkarnTask Bad()
{
    ValkarnTask task = DoWorkAsync();  // assigned but never awaited
    DoOtherThing();
    // task is abandoned — TT013 (future)
}
```

Methods decorated with `[FireAndForget]` will be exempt.

---

## Lifecycle Rules (TT)

These rules relate to MonoBehaviour lifetime management and cancellation.

---

### TT010 — Auto-cancel active for MonoBehaviour async method

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTask            |
| Code fix   | No                     |

An informational hint. Any `async ValkarnTask` (or `async ValkarnTask<T>`) method inside a class that inherits from `UnityEngine.MonoBehaviour` will be automatically cancelled when the object is destroyed, **unless** the method is decorated with `[NoAutoCancel]`. This diagnostic makes that behavior visible in the IDE without requiring the developer to remember it.

**Triggers on:**
```csharp
public class MyBehaviour : MonoBehaviour
{
    async ValkarnTask LoadLevel()  // TT010: auto-cancel active
    {
        await ValkarnTask.Delay(2000);
    }
}
```

**To opt out** (and suppress TT010 for that method), add `[NoAutoCancel]`:
```csharp
[NoAutoCancel]
async ValkarnTask LoadLevel(CancellationToken ct)
{
    await ValkarnTask.Delay(2000, ct);
}
```

---

### TT011 — WhenAll mixes different lifetime scopes

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTask            |
| Code fix   | No                     |

`ValkarnTask.WhenAll` called with tasks from different lifetime scopes can produce surprising partial-cancellation behavior. If one task is bound to a `MonoBehaviour` (returned by an instance method of a class that inherits `MonoBehaviour`) and another is unbound (a static task, or from a non-MonoBehaviour class), destroying the object will cancel one task but not the other, leaving the combinator in an indeterminate state.

**Triggers on:**
```csharp
// Assuming EnemyAI : MonoBehaviour
await ValkarnTask.WhenAll(
    enemyAI.PatrolAsync(),   // bound: auto-cancelled on Destroy
    GlobalMusic.FadeAsync()  // unbound: lives indefinitely
);
// TT011: WhenAll mixes lifetimes — PatrolAsync() vs GlobalMusic.FadeAsync()
```

**Fix — give both tasks a shared cancellation token:**
```csharp
using var cts = new CancellationTokenSource();
await ValkarnTask.WhenAll(
    enemyAI.PatrolAsync(cts.Token),
    GlobalMusic.FadeAsync(cts.Token)
);
```

---

### TT012 — Async loop without cancellation check

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTask            |
| Code fix   | No                     |

An `async ValkarnTask` method containing a `for`, `while`, `foreach`, or `do`-`while` loop whose body has no cancellation check is a "zombie loop": if lifecycle cancellation triggers (e.g., a MonoBehaviour is destroyed), the auto-cancel signal cannot break the loop. The loop keeps running even though the owning object is gone.

The analyzer considers a loop body to have a cancellation check if it contains any of:
- An `await` expression (the awaited operation can observe the token and throw `OperationCanceledException`)
- `ThrowIfCancellationRequested` (as identifier or member access)
- `IsCancellationRequested` (as identifier or member access)

The check does **not** descend into nested lambdas or local functions.

**Triggers on:**
```csharp
async ValkarnTask PollForever(CancellationToken ct)
{
    while (true)                     // TT012: no cancellation check in body
    {
        ProcessNextItem();
        Thread.Sleep(16);            // synchronous — not an await
    }
}
```

**Fix — add an await or an explicit check:**
```csharp
async ValkarnTask PollForever(CancellationToken ct)
{
    while (true)
    {
        ct.ThrowIfCancellationRequested();
        ProcessNextItem();
        await ValkarnTask.Yield();       // also satisfies the check
    }
}
```

---

### TT014 — [NoAutoCancel] without CancellationToken parameter

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTask            |
| Code fix   | No                     |

`[NoAutoCancel]` on an `async ValkarnTask` method in a `MonoBehaviour` opts the method out of automatic lifecycle cancellation. But if the method has no `CancellationToken` parameter, it has no mechanism to observe cancellation at all, making the attribute pointless and almost certainly indicating a forgotten parameter.

**Triggers on:**
```csharp
public class Enemy : MonoBehaviour
{
    [NoAutoCancel]
    async ValkarnTask Chase()      // TT014: [NoAutoCancel] but no CancellationToken parameter
    {
        await ValkarnTask.Delay(1000);
    }
}
```

**Fix — add a `CancellationToken` parameter:**
```csharp
[NoAutoCancel]
async ValkarnTask Chase(CancellationToken ct)
{
    await ValkarnTask.Delay(1000, ct);
}
```

---

## Informational Rules (TT)

---

### TT015 — Awaitable bridge adapter generated

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTask            |
| Code fix   | No                     |

When you `await` a Unity `Awaitable` (from `UnityEngine`) inside an `async ValkarnTask` method, Valkarn Tasks generates a bridge adapter automatically via `AwaitableBridge`. This diagnostic is informational: it confirms the bridge is in effect. No action is required.

**Triggers on:**
```csharp
async ValkarnTask LoadScene()
{
    await SceneManager.LoadSceneAsync("Main");  // TT015: bridge adapter generated
}
```

No change is needed. The bridge handles the conversion transparently.

---

## Code Quality Rules (TT)

---

### TT016 — Async method without await

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTask            |
| Code fix   | No                     |

An `async ValkarnTask` (or `async ValkarnTask<T>`) method that contains no `await` expressions — including `await foreach`, `await using`, and `await` inside `using` declarations — incurs the full overhead of state machine allocation for no benefit. The compiler still generates a state machine, but since there is no suspension point, the method always completes synchronously.

The analyzer checks for: `AwaitExpressionSyntax`, `foreach` with `await` keyword, `using` declarations with `await` keyword, and `using` statements with `await` keyword.

**Triggers on:**
```csharp
async ValkarnTask<int> ComputeTotal()  // TT016: no await in body
{
    return items.Sum(x => x.Value);
}
```

**Fix — remove `async` and return a completed task:**
```csharp
ValkarnTask<int> ComputeTotal()
{
    return ValkarnTask.FromResult(items.Sum(x => x.Value));
}
```

Or for a void return:
```csharp
ValkarnTask DoSetup()
{
    Initialize();
    return ValkarnTask.CompletedTask;
}
```

---

### TT017 — [FireAndForget] on ValkarnTask\<T\>

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTask            |
| Code fix   | No                     |

`[FireAndForget]` signals that a method is intentionally run without awaiting its result. Applying it to a method that returns `ValkarnTask<T>` (typed result) always discards `T`, making the typed return meaningless. The method should return `ValkarnTask` (void) instead.

**Triggers on:**
```csharp
[FireAndForget]
async ValkarnTask<int> SendReport()   // TT017: return value is discarded
{
    await UploadAsync();
    return 42;                     // this 42 is never seen
}
```

**Fix — change the return type to `ValkarnTask`:**
```csharp
[FireAndForget]
async ValkarnTask SendReport()
{
    await UploadAsync();
}
```

---

## Migration Rules (MIG)

The migration analyzer activates **only when the `Cysharp.Threading.Tasks.UniTask` type is present** in the compilation. Rules are informational or warnings to guide the transition from UniTask to Valkarn Tasks. None of these rules have auto-fixes; the descriptions below explain the manual change.

---

### MIG001 — UniTask type detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTaskMigration   |

A use of a `UniTask` type (the struct itself or `UniTask<T>`) was detected. Replace it with the `ValkarnTask` or `ValkarnTask<T>` equivalent.

```csharp
// Before
UniTask<Sprite> LoadSprite(string path) { ... }

// After
ValkarnTask<Sprite> LoadSprite(string path) { ... }
```

---

### MIG002 — cancelImmediately parameter is unnecessary

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTaskMigration   |

UniTask's `Delay` and other time-based methods accept a `cancelImmediately` parameter to opt into fast cancellation. Valkarn Tasks cancels immediately by default — there is no `cancelImmediately` parameter. Remove the argument.

```csharp
// Before
await UniTask.Delay(1000, cancelImmediately: true, cancellationToken: ct);

// After
await ValkarnTask.Delay(1000, ct);
```

---

### MIG003 — SuppressCancellationThrow() detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTaskMigration   |

UniTask's `.SuppressCancellationThrow()` converts a cancelled task into a `(bool isCancelled, T result)` tuple without throwing. Valkarn Tasks uses `.AsResult()` for the same purpose, which returns a `Result<T>` struct.

```csharp
// Before
var (isCancelled, value) = await myUniTask.SuppressCancellationThrow();

// After
var result = await myValkarnTask.AsResult();
if (result.IsCanceled) { ... }
T value = result.Value;
```

---

### MIG004 — Awaitable return type detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTaskMigration   |

A method returns Unity's `Awaitable` type. Consider replacing it with `ValkarnTask`, which integrates natively with the Valkarn Tasks runtime and supports auto-cancellation, pooling, and the full combinator set.

---

### MIG005 — SingleConsumerUnbounded channel detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTaskMigration   |

`Channel.CreateSingleConsumerUnbounded<T>()` from UniTask creates a channel with no backpressure. Consider replacing it with `ValkarnTask.Channel.CreateBounded<T>(capacity)` to add backpressure and prevent unbounded memory growth under load.

```csharp
// Before
var ch = Channel.CreateSingleConsumerUnbounded<Event>();

// After (with backpressure)
var ch = ValkarnTask.Channel.CreateBounded<Event>(capacity: 256);

// Or if unbounded is intentional
var ch = ValkarnTask.Channel.CreateUnbounded<Event>();
```

---

### MIG006 — UniTask PlayerLoopTiming detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTaskMigration   |

A `PlayerLoopTiming` enum value from the `Cysharp.Threading.Tasks` namespace was detected. Change the `using` directive to `UnaPartidaMas.Valkarn.Tasks`; the enum value names are identical.

```csharp
// Before
using Cysharp.Threading.Tasks;
timing = PlayerLoopTiming.Update;

// After
using UnaPartidaMas.Valkarn.Tasks;
timing = PlayerLoopTiming.Update;  // same name, different namespace
```

---

### MIG007 — async UniTaskVoid detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTaskMigration   |

`async UniTaskVoid` was UniTask's fire-and-forget method pattern. Valkarn Tasks replaces it with two options:

```csharp
// Before
async UniTaskVoid StartLoadAsync() { ... }

// After — option 1: [FireAndForget] attribute
[FireAndForget]
async ValkarnTask StartLoadAsync() { ... }

// After — option 2: standard ValkarnTask + .Forget() at the call site
async ValkarnTask StartLoadAsync() { ... }
// Called as:
StartLoadAsync().Forget();
```

---

### MIG008 — Awaitable MainThreadAsync() detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTaskMigration   |

`Awaitable.MainThreadAsync()` was used in Unity's `Awaitable` API to switch back to the main thread. Valkarn Tasks runs continuations on the main thread by default via PlayerLoop integration, so explicit `MainThreadAsync()` calls are usually unnecessary and can be removed.

---

### MIG009 — UniTask.RunOnThreadPool detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTaskMigration   |

Replace with `ValkarnTask.RunOnThreadPool`. The API is the same.

```csharp
// Before
await UniTask.RunOnThreadPool(() => HeavyWork());

// After
await ValkarnTask.RunOnThreadPool(() => HeavyWork());
```

---

### MIG010 — .ToCoroutine() detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTaskMigration   |

`.ToCoroutine()` was a UniTask bridge for legacy coroutine callers. Rewrite the consuming code as an `async ValkarnTask` method instead.

```csharp
// Before
IEnumerator LegacyCaller() { yield return MyUniTask().ToCoroutine(); }

// After
async ValkarnTask ModernCaller() { await MyValkarnTask(); }
```

---

### MIG011 — UniTask.Create() detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTaskMigration   |

`UniTask.Create(Func<UniTask>)` wraps a factory delegate. Replace with the `ValkarnTask.Promise<T>` pattern for manually controlled completion.

```csharp
// Before
var task = UniTask.Create(async () => { await DoWork(); return 42; });

// After
var promise = new ValkarnTaskCompletionSource<int>();
DoWorkThenComplete(promise);
var task = promise.Task;
```

---

### MIG012 — UniTask.Lazy/Defer detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTaskMigration   |

`UniTask.Lazy<T>` and `UniTask.Defer` existed to avoid allocation when a task might complete synchronously. Valkarn Tasks has a zero-allocation synchronous fast path built in: returning `ValkarnTask.CompletedTask` or `ValkarnTask.FromResult(value)` never allocates. Remove the `Lazy`/`Defer` wrappers.

---

### MIG013 — .ToUniTask()/.AsUniTask() detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTaskMigration   |

Conversion calls from Unity `Awaitable` to UniTask. Remove them; Valkarn Tasks bridges `Awaitable` natively (see TT015).

---

### MIG014 — UniTaskAsyncEnumerable detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTaskMigration   |

UniTask shipped its own `IUniTaskAsyncEnumerable<T>` and `UniTaskAsyncEnumerable` utilities. Use `IAsyncEnumerable<T>` from the BCL with `System.Linq.Async` instead. `IAsyncEnumerable<T>` is supported natively by C# `await foreach`.

```csharp
// Before
IUniTaskAsyncEnumerable<int> GetItems() { ... }

// After
IAsyncEnumerable<int> GetItems() { ... }
```

---

### MIG015 — TimeoutController detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTaskMigration   |

UniTask's `TimeoutController` was a helper for reusable timeouts. Replace it with a standard `CancellationTokenSource` constructed with a `TimeSpan`, which the BCL supports directly.

```csharp
// Before
var controller = new TimeoutController();
var ct = controller.Timeout(TimeSpan.FromSeconds(5));

// After
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var ct = cts.Token;
```

---

## Suppressing Rules

Standard Roslyn suppression mechanisms work for all rules:

```csharp
// Suppress a single occurrence inline
#pragma warning disable TT012
while (true) { DoSomething(); }
#pragma warning restore TT012
```

Or via `.editorconfig` to suppress project-wide:
```ini
[*.cs]
dotnet_diagnostic.TT012.severity = none
```

Migration rules (`MIG*`) can be suppressed the same way, or disabled globally once migration is complete by setting severity to `none` in `.editorconfig`.
