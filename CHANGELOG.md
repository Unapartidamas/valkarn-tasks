# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-03-12

### Added

**Core async primitive**
- `ValkarnTask` / `ValkarnTask<T>` — zero-allocation, struct-based async return types with zero-alloc synchronous fast path
- Thread-aware pool — zero atomics on main thread (IL2CPP-optimized), Treiber stack for background threads; bounded with shrinkable capacity
- Generational token validation — `ulong` token encodes `(slotIndex, generation)`; stale references can never match a recycled pool slot (4 billion generations per slot)
- `ValkarnTask.Promise` / `ValkarnTask.Promise<T>` — manual completion sources for custom async operations; finalizer-based unobserved exception reporting
- `ValkarnTask.PooledPromise` / `ValkarnTask.PooledPromise<T>` — auto-reset pooled completion sources for high-frequency patterns
- `Result<T>` / `Result` — error handling without try/catch; implicit `bool` conversion; used by `.AsResult()`
- Factory methods: `CompletedTask`, `FromResult<T>`, `FromException`, `FromCanceled`, `Never`

**Lifecycle cancellation**
- Auto-cancel source generation — `CancellationToken` bound to `MonoBehaviour.destroyCancellationToken`, zero boilerplate
- `[NoAutoCancel]` attribute — opt-out of automatic cancellation per method
- Manual token override — passing explicit `CancellationToken` replaces the auto-injected lifecycle token
- No sibling cancellation — `WhenAll` / `WhenAny` never auto-cancel healthy tasks when a sibling fails

**Critical sections**
- `ValkarnTask.Critical()` — `await using` scope that defers lifecycle cancellation until the section exits
- `CriticalSectionScope.IsInCriticalSection` — static property to query nesting depth at runtime
- Nested critical sections supported; cross-thread misuse detected and reported via `UnobservedException`

**Combinators**
- `ValkarnTask.WhenAll` — typed (2–8 arities + `IEnumerable`) and void variants; pooled, zero-alloc in steady state
- `ValkarnTask.WhenAny` — returns winner index and result; losing tasks run to completion; loser faults reported via `UnobservedException`
- `.AsResult()` / `.AsResult<T>()` — non-throwing error handling; sync fast path for already-completed tasks
- `.AsNonGeneric()` — convert `ValkarnTask<T>` to `ValkarnTask` for mixed-type combinators
- `.Forget()` — explicit fire-and-forget; routes faults to `UnobservedException`; zero-alloc on sync-completed tasks
- `[FireAndForget]` attribute — suppresses unawaited-task warnings at all call sites

**Time, delay, and thread switching**
- `ValkarnTask.Delay` — milliseconds or `TimeSpan`; `DelayType.UnscaledDeltaTime` and `DelayType.Realtime`
- `ValkarnTask.Yield` / `NextFrame` / `DelayFrame` — frame-based suspension with optional `PlayerLoopTiming`
- `ValkarnTask.WaitUntil` / `WaitWhile` — predicate-based suspension
- `ValkarnTask.SwitchToThreadPool` / `SwitchToMainThread` — explicit thread switching with optional `PlayerLoopTiming`
- `ValkarnTask.Run` / `RunOnThreadPool` — execute delegates on specific contexts

**PlayerLoop integration**
- 16 `PlayerLoopTiming` injection points — `Initialization` / `LastInitialization` through `PostLateUpdate` / `LastPostLateUpdate`
- `ValkarnTaskSettings.InjectTimings` — configurable injection set to reduce PlayerLoop entries

**Channels**
- `Channel<T>` — bounded and unbounded async producer/consumer channels
- `ChannelReader<T>` — `ReadAsync`, `TryRead`, `ReadAllAsync` (`IAsyncEnumerable<T>`), `Completion`
- `ChannelWriter<T>` — `TryWrite`, `WriteAsync` (backpressure on bounded), `Complete`
- Multi-consumer support via `multiConsumer` flag on factory methods

**Awaitable bridge**
- Transparent interop with Unity's `Awaitable` — source generator detects and wraps automatically, no manual `.AsValkarnTask()` calls

**Job System bridge**
- `IJob.ScheduleAsync()` / `IJobParallelFor.ScheduleParallelAsync()` — source-generated async wrappers; pooled, zero-alloc in steady state
- Cancellation support — `Complete()` called on cancel to prevent job leaks
- `JobHandle.ToValkarnTask()` / `JobHandle.WhenAll()` — manual bridge for custom scheduling

**Burst and ECS**
- `NativeTimerHeap` / `BurstSchedulerRunner` / `NativeScheduler` — Burst-compiled timer scheduler
- `AsyncSystemUtilities` — async work inside `ISystem` for Unity ECS
- `versionDefines` in asmdef — Burst and ECS integration opt-in; zero impact when packages absent

**Pool management**
- Bounded pools with incremental frame-based trimming (default: every 300 frames)
- `ValkarnTask.GetPoolInfo()` — runtime diagnostics for all registered pools
- `ValkarnTaskSettings` ScriptableObject (`Resources/ValkarnTaskSettings.asset`) — configure pool sizes, trim ratios, auto-cancel, and logging

**Error handling**
- `ValkarnTask.UnobservedException` — global handler; fires at pool-return time (not GC/finalizer)
- Thread-safe handler registration via lock-free CAS

**Compile-time diagnostics**
- 10 analyzer rules (TT001–TT017) — double-await, unawaited tasks, zombie loops, missing cancellation tokens, and more
- 15 migration diagnostics (MIG001–MIG015) — automated UniTask → ValkarnTask and `Awaitable` → ValkarnTask migration with Roslyn code fixes

**Automatic migration**
- One-click UniTask migration — covers 30+ API mappings; 95%+ of cases require zero manual edits
- One-click `Awaitable` migration — covers 8 API mappings
- Structural transforms for `SuppressCancellationThrow`, `WhenAll` with catch blocks, `RunOnThreadPool`, `.ToCoroutine()`, and async LINQ

**Testing**
- `TestClock` — deterministic fake time; `Advance(TimeSpan)` and `AdvanceFrame()` for unit tests without real async delays
- `TimeProvider.Current` — static injection point; production uses `UnityTimeProvider`, tests use `TestClock`

**Utilities**
- `AsyncThrottle` — concurrency-limited fire-and-forget
- IL2CPP stripping protection via `link.xml`
- Domain reload safety — pools and PlayerLoop entries cleared on `[RuntimeInitializeOnLoadMethod]`
