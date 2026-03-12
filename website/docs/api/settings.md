---
sidebar_position: 4
title: Settings & Result
---

# ValkarnTaskSettings

`ValkarnTaskSettings` is a Unity `ScriptableObject` that controls the runtime behavior of Valkarn Tasks — primarily the object pool that recycles async state machines and promise objects to avoid garbage during play.

---

## Creating the Settings Asset

1. In the Project window, right-click on a `Resources` folder (create one if you do not have one).
2. Select **Assets > Create > Valkarn Tasks > Task Settings**.
3. Name the file `ValkarnTaskSettings` and place it inside the `Resources` folder.

The file must be named exactly `ValkarnTaskSettings` and must reside in a folder named `Resources` anywhere in your project. The asset is loaded at runtime with `Resources.Load<ValkarnTaskSettings>("ValkarnTaskSettings")`.

If no asset is found, all settings fall back to their built-in defaults. The library works correctly without the asset — creating it is only necessary when you want to change the defaults.

---

## Accessing Settings at Runtime

In Unity builds, settings are read from the asset through a cached singleton:

```csharp
ValkarnTaskSettings settings = ValkarnTaskSettings.Instance;
```

The commonly needed pool parameters are also surfaced directly on `ValkarnTask` for convenience:

```csharp
int max      = ValkarnTask.DefaultMaxPoolSize;   // reads from ValkarnTaskSettings.Instance
int min      = ValkarnTask.MinPoolSize;
int interval = ValkarnTask.TrimCheckInterval;
```

In non-Unity builds (tests, standalone .NET), `ValkarnTaskSettings` is a static class with mutable properties instead of a `ScriptableObject`. The same property names apply and can be written to directly:

```csharp
// Non-Unity / test builds only
ValkarnTask.DefaultMaxPoolSize = 512;
ValkarnTask.TrimCheckInterval  = 600;
ValkarnTask.MinPoolSize        = 16;
```

---

## Configurable Properties

### Pool Configuration

#### DefaultMaxPoolSize

| | |
|-|-|
| Type | `int` |
| Default | `256` |
| Valid range | `8` – `1024` |
| Inspector tooltip | "Maximum items per pool type. Excess items are trimmed." |

The maximum number of objects retained in each pool per type. Each distinct generic instantiation (e.g., `PooledPromise<int>`, `PooledPromise<string>`) has its own pool capped at this value.

When a task completes and its internal object is returned to the pool, if the pool already holds `DefaultMaxPoolSize` items the returned object is discarded (eligible for GC). This prevents unbounded memory growth after a burst of async activity.

Increase this value if profiling shows frequent GC allocations during sustained high-throughput async workloads. Decrease it if memory pressure is a concern and tasks are not reused frequently.

#### MinPoolSize

| | |
|-|-|
| Type | `int` |
| Default | `8` |
| Valid range | `1` – `64` |
| Inspector tooltip | "Minimum pool size — never shrink below this." |

The pool trim pass will never reduce any pool below this count. This guarantees a warm pool baseline is always available, avoiding allocation spikes after a quiet period where the trim pass might have released everything.

#### TrimCheckInterval

| | |
|-|-|
| Type | `int` |
| Default | `300` |
| Valid range | `30` – `1000` |
| Inspector tooltip | "Frames between trim checks. At 60fps, 300 ≈ 5 seconds." |

How many frames elapse between pool trim passes. The trim pass walks all registered pools and releases excess objects (those above `MinPoolSize`) if the pool has been consistently oversized.

At 60 fps the default value of 300 equals approximately 5 seconds between checks. Lower this value if you want more aggressive, frequent trimming (at the cost of more frequent trim work). Raise it if trim passes are appearing as spikes in profiling.

#### TrimHysteresisCount

| | |
|-|-|
| Type | `int` |
| Default | `2` |
| Valid range | `1` – `10` |
| Inspector tooltip | "Number of consecutive above-threshold checks before trimming." |

A pool is only trimmed after it has been observed as oversized for this many consecutive trim cycles. This prevents thrashing — if the game has a brief spike followed by a quiet period, a hysteresis count of 2 means the pool survives one quiet cycle before starting to release objects.

#### TrimReleaseRatio

| | |
|-|-|
| Type | `float` |
| Default | `0.25` |
| Valid range | `0.1` – `1.0` |
| Inspector tooltip | "Fraction of excess to release per trim cycle (0.25 = 25%)." |

When a pool is trimmed, this fraction of the excess capacity (items above `MinPoolSize`) is released per cycle rather than all at once. A value of `0.25` means each trim pass removes 25% of the overage. This gradual release avoids a sudden drop in pool size that could cause an allocation burst if load picks back up.

Set this to `1.0` if you want all excess released immediately on each cycle.

---

### Lifecycle

#### EnableAutoCancel

| | |
|-|-|
| Type | `bool` |
| Default | `true` |
| Inspector tooltip | "Auto-bind MonoBehaviour tasks to destroyCancellationToken." |

When enabled, tasks started from a `MonoBehaviour` are automatically linked to that `MonoBehaviour`'s `destroyCancellationToken`. If the `MonoBehaviour` is destroyed while a task is running, the task is canceled rather than continuing to execute against a destroyed object.

Disable this only if you are managing cancellation manually and do not want automatic binding.

---

### Error Handling

#### LogUnobservedCancellations

| | |
|-|-|
| Type | `bool` |
| Default | `false` |
| Inspector tooltip | "Log unobserved cancellations as warnings." |

By default, a task that is canceled but never awaited (unobserved cancellation) is silently ignored. Enable this to log a warning when that happens. Useful during development to find fire-and-forget tasks that silently cancel.

Unobserved faults are always reported via the `ValkarnTask.UnobservedException` event regardless of this setting.

#### MaxExceptionLogsPerFrame

| | |
|-|-|
| Type | `int` |
| Default | `10` |
| Valid range | `1` – `100` |
| Inspector tooltip | "Maximum exception logs per frame to prevent spam." |

Caps the number of unobserved exception log entries emitted in a single frame. If many tasks fault in the same frame (for example, after a network failure), this prevents the console from being flooded with hundreds of identical stack traces.

---

## How Pool Trimming Works at Runtime

On each Unity player loop update, `PlayerLoopHelper` increments a frame counter. When the counter reaches `TrimCheckInterval`, it calls `PoolRegistry.TrimAll(MinPoolSize)`. Every pool that has been registered checks whether it has been over capacity for at least `TrimHysteresisCount` consecutive checks. If so, it releases `TrimReleaseRatio` of its excess.

Pools register themselves automatically on first use. The `ValkarnTask.GetPoolInfo()` method returns a snapshot of all currently registered pools with their type, current size, and max size — this is what the Task Tracker window displays.

```
Frame 0 ──────────────────────────────────────────────────────────
  Player loop runs
  Pool A: size 200, max 256 — normal, no trim needed

Frame 300 ────────────────────────────────────────────────────────
  TrimAll fires
  Pool A: size 18, max 256 — above MinPoolSize(8), but first excess check
  hysteresisCount for A = 1 (not yet at threshold of 2)

Frame 600 ────────────────────────────────────────────────────────
  TrimAll fires
  Pool A: size 18, max 256 — above MinPoolSize(8), second excess check
  hysteresisCount for A = 2 — threshold reached!
  Excess = 18 - 8 = 10; release 10 * 0.25 = 2 objects
  Pool A: size 16
```

---

## Task Tracker Window

Open via **Window > Valkarn Tasks > Task Tracker**.

The Task Tracker is an Editor-only window that shows live pool state while you are in Play Mode. It refreshes at a configurable interval (default 0.5 seconds, adjustable from 0.1 to 5 seconds via the slider in the toolbar).

### Pools tab

Lists every pool type that has been active since the last domain reload, sorted by current size (largest first). Each row shows:

| Column | Description |
|--------|-------------|
| Type | The pooled object's type name, with generic arguments expanded |
| Size | Current number of objects in the pool |
| Max | The `DefaultMaxPoolSize` ceiling for this pool |
| Usage | A progress bar showing `Size / Max` as a percentage |

If no pools have been used yet, a message reads "No pools active. Pools are created on first use."

### Config tab

Shows the three live pool parameters as read from `ValkarnTask.DefaultMaxPoolSize`, `ValkarnTask.TrimCheckInterval`, and `ValkarnTask.MinPoolSize`. Values are only shown while in Play Mode — in Edit Mode a note instructs you to enter Play Mode to see live values.

The Config tab also displays a reference to the `ValkarnTaskSettings` asset (if one exists in `Resources`), so you can click through to inspect or modify it. If no asset is found, a warning directs you to create one.

---

## Result&lt;T&gt;

`Result<T>` is a discriminated union struct for representing the outcome of an operation without throwing. It is the return type used by `WhenAll` combinators to report per-task outcomes, and is also available as a general-purpose result pattern.

```csharp
public readonly struct Result<T>
{
    public bool IsSuccess  { get; }
    public bool IsFailure  { get; }   // true if faulted or canceled
    public bool IsFaulted  { get; }
    public bool IsCanceled { get; }

    public ValkarnTask.Status Status { get; }
    public T Value    { get; }        // throws if not succeeded
    public Exception Error { get; }   // null if not faulted

    public static Result<T> Success(T value);
    public static Result<T> Failure(string error);    // wraps in InvalidOperationException
    public static Result<T> Faulted(Exception error);
    public static Result<T> Canceled(OperationCanceledException oce = null);

    public static implicit operator bool(Result<T> r);  // true if succeeded
}
```

A non-generic `Result` exists for void tasks with the same shape but without `Value`.

### AsResult extension methods

Convert any `ValkarnTask` or `ValkarnTask<T>` into a `Result` without a `try/catch` in your own code:

```csharp
public static ValkarnTask<Result<T>> AsResult<T>(this ValkarnTask<T> task);
public static ValkarnTask<Result>    AsResult(this ValkarnTask task);
```

If the underlying task is already synchronously completed (the zero-allocation fast path), `AsResult` returns immediately without creating an async state machine. Otherwise it wraps into an async method that catches `OperationCanceledException` and all other exceptions, translating them into the appropriate `Result` variant.

### When to use Result&lt;T&gt;

Use `Result<T>` instead of catching exceptions when:

- You are calling multiple tasks in parallel and want per-task outcomes without short-circuiting the whole batch.
- You want to express fallible operations in a type-safe way without exception control flow.
- You are returning from a method that the caller may not want to wrap in `try/catch`.

```csharp
// Fire multiple tasks, get all outcomes regardless of individual failures
Result<int>[] results = await ValkarnTask.WhenAll(
    FetchScoreAsync(playerA).AsResult(),
    FetchScoreAsync(playerB).AsResult(),
    FetchScoreAsync(playerC).AsResult()
);

foreach (var r in results)
{
    if (r.IsSuccess)
        Debug.Log($"Score: {r.Value}");
    else if (r.IsFaulted)
        Debug.LogError($"Failed: {r.Error.Message}");
    else
        Debug.Log("Canceled");
}
```

Checking `IsSuccess` or using the implicit `bool` operator are the preferred ways to branch on a result:

```csharp
var result = await SomeOperationAsync().AsResult();

if (result)
{
    Use(result.Value);
}
```

Accessing `result.Value` when `IsSuccess` is `false` throws an `InvalidOperationException`.

### Factory methods

| Method | Status set | Error set |
|--------|-----------|-----------|
| `Result<T>.Success(value)` | `Succeeded` | none |
| `Result<T>.Failure(message)` | `Faulted` | `new InvalidOperationException(message)` |
| `Result<T>.Faulted(exception)` | `Faulted` | the provided exception |
| `Result<T>.Canceled(oce?)` | `Canceled` | the provided `OperationCanceledException`, or `null` |

The `Succeeded` property exists on both `Result` and `Result<T>` but is marked `[Obsolete]` — use `IsSuccess` instead for consistency with `IsFailure`, `IsFaulted`, and `IsCanceled`.
