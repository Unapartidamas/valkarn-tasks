---
sidebar_position: 3
title: Testing
---

# Testing

Valkarn Tasks ships a dedicated testing infrastructure that lets you write fast, deterministic unit tests for async code — no real timers, no frame waits, no Unity Editor required for the core test suite.

---

## Overview

The testing support lives in the `Testing/` assembly (`UnaPartidaMas.Valkarn.Tasks.Testing`), which is visible to the runtime via `InternalsVisibleTo`. It exposes two public types:

| Type | Purpose |
|------|---------|
| `ValkarnTaskTestHelper` | Initialise and tear down the Valkarn Tasks runtime for a test session |
| `TestClock` | Deterministic time and frame provider; replaces the Unity `TimeProvider` during tests |

---

## ValkarnTaskTestHelper

`ValkarnTaskTestHelper` is a static utility class in the `UnaPartidaMas.Valkarn.Tasks.Testing` namespace.

### What it does

`Setup()` performs three things:
1. Creates a `TestClock` and installs it as `TimeProvider.Current`, replacing any real Unity time provider.
2. Calls `PlayerLoopHelper.InitializeForTest()`, which allocates the `ContinuationQueue` and `PlayerLoopRunner` arrays for all 16 PlayerLoop timings and marks the runtime as initialised.
3. Returns the `TestClock` so your test can control time.

`Teardown()` reverses the setup:
1. Installs a fresh, no-op `TestClock` as `TimeProvider.Current` to prevent stale time from leaking between test fixtures.
2. Calls `PlayerLoopHelper.ShutdownForTest()`, which tears down the queues and runners.

### Usage pattern

```csharp
using NUnit.Framework;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Testing;

[TestFixture]
public class MyAsyncTests
{
    TestClock clock;

    [SetUp]
    public void SetUp()
    {
        clock = ValkarnTaskTestHelper.Setup();
    }

    [TearDown]
    public void TearDown()
    {
        ValkarnTaskTestHelper.Teardown();
    }

    // Tests go here
}
```

Call `Setup` once per test in `[SetUp]` and `Teardown` once per test in `[TearDown]`. The pattern ensures each test starts with a clean runtime state and does not leak into the next test.

### Default delta time

`Setup` accepts an optional `defaultDeltaTime` parameter (default: `1/60` seconds, i.e. 60 fps):

```csharp
clock = ValkarnTaskTestHelper.Setup(defaultDeltaTime: 1f / 30f);  // 30 fps simulation
```

This value is used by `TestClock.AdvanceFrame()` and `TestClock.AdvanceFrames(n)`.

---

## TestClock

`TestClock` implements `ITimeProvider` and gives tests full control over:

- Simulated time (via `GetTimestamp()`, backed by `Stopwatch.Frequency` ticks)
- `DeltaTime` and `UnscaledDeltaTime` for the current frame
- `FrameCount`

### Advancing time

#### `Advance(TimeSpan duration)`

Advances time by the given duration in a single step. The entire duration is applied as `DeltaTime` for that frame, `FrameCount` increments by 1, and all PlayerLoop timings are processed. After processing, `DeltaTime` is restored to the value it had before the call.

```csharp
var task = ValkarnTask.Delay(3000);  // 3-second delay

clock.Advance(TimeSpan.FromSeconds(2));
Assert.IsFalse(task.IsCompleted);   // not yet

clock.Advance(TimeSpan.FromSeconds(1));
Assert.IsTrue(task.IsCompleted);    // exactly at 3 seconds
```

Use this when you want to jump to a specific point in time without simulating individual frames.

#### `AdvanceFrame()`

Advances one frame using the current `DeltaTime`. `FrameCount` increments by 1, all PlayerLoop timings are processed, and a 1 ms `Thread.Sleep` is inserted before processing. The sleep exists because background worker threads (used by `RunOnThreadPool` and Unity Job System integration) need a small window to complete between frame ticks — without it, tests run frames back-to-back with no gap, which causes race conditions that do not occur in real Unity frames.

```csharp
clock.AdvanceFrame();
```

#### `AdvanceFrames(int count)`

Calls `AdvanceFrame()` `count` times.

```csharp
clock.AdvanceFrames(10);  // simulate 10 frames
```

#### `ProcessTick(PlayerLoopTiming timing)`

Processes a single PlayerLoop timing phase without advancing time or `FrameCount`. Useful when testing code that is scheduled at a specific timing (e.g., `PlayerLoopTiming.FixedUpdate`) and you do not want to process all other timings.

```csharp
clock.ProcessTick(PlayerLoopTiming.FixedUpdate);
```

### Controlling delta time

#### `SetDeltaTime(float deltaTime)`

Sets both `DeltaTime` and `UnscaledDeltaTime` to the same value for all subsequent frames.

#### `SetDeltaTime(float deltaTime, float unscaledDeltaTime)`

Sets them independently to simulate non-unit `Time.timeScale`. For example, to test code that uses `DelayType.UnscaledDeltaTime` while time is paused:

```csharp
clock.SetDeltaTime(deltaTime: 0f, unscaledDeltaTime: 0.05f);

var scaledTask = ValkarnTask.Delay(500, DelayType.DeltaTime);
var unscaledTask = ValkarnTask.Delay(500, DelayType.UnscaledDeltaTime);

clock.AdvanceFrames(9);  // 450 ms unscaled, 0 ms scaled
Assert.IsFalse(scaledTask.IsCompleted);    // paused game time never reaches 500ms
Assert.IsFalse(unscaledTask.IsCompleted); // 450ms < 500ms

clock.AdvanceFrame();  // 500 ms unscaled
Assert.IsTrue(unscaledTask.IsCompleted);  // unscaled delay done
Assert.IsFalse(scaledTask.IsCompleted);  // scaled still never moves
```

---

## Writing Unit Tests for Async ValkarnTask Methods

### Tests that complete synchronously

Some `ValkarnTask` operations complete without suspending — for example, awaiting `ValkarnTask.CompletedTask`, `ValkarnTask.FromResult(value)`, or a `ValkarnTaskCompletionSource` that was completed before being awaited. These do not require a clock at all and can use the internal `TestHelper` class directly (internal to the test assembly):

```csharp
[TestFixture]
public class SyncTests
{
    [SetUp]
    public void SetUp()
    {
        // Only sets main thread ID — no clock or PlayerLoop needed
        TestHelper.EnsureInitialized();
    }

    [Test]
    public void FromResult_ReturnsValue()
    {
        var task = ValkarnTask.FromResult(42);
        Assert.IsTrue(task.IsCompleted);
        Assert.AreEqual(42, task.GetAwaiter().GetResult());
    }
}
```

`TestHelper` (in `Tests/Editor/TestHelper.cs`) is an internal helper used by the test suite itself:

- `TestHelper.EnsureInitialized()` sets `ValkarnTaskPoolShared.MainThreadId` to the current thread. This is required so that pool operations route to the correct (non-atomic) fast path.
- `TestHelper.RunSync(ValkarnTask task)` asserts that the task is already completed and calls `GetResult()` — useful for testing code paths that should complete synchronously.
- `TestHelper.RunSync<T>(ValkarnTask<T> task)` does the same and returns the result value.
- `TestHelper.UnobservedExceptionCollector` subscribes to `ValkarnTask.UnobservedException` for the duration of its scope, collecting any unobserved exceptions for assertion.

### Tests that require time

Any test involving `ValkarnTask.Delay`, `ValkarnTask.Yield`, or code scheduled at a PlayerLoop timing requires `ValkarnTaskTestHelper.Setup()` and the returned `TestClock`.

**Example: testing a delay-based method**

Suppose you have:

```csharp
public static async ValkarnTask WaitAndLog(int ms)
{
    await ValkarnTask.Delay(ms);
    Log("done");
}
```

Test it without real waiting:

```csharp
[TestFixture]
public class DelayTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = ValkarnTaskTestHelper.Setup();

    [TearDown]
    public void TearDown() => ValkarnTaskTestHelper.Teardown();

    [Test]
    public void WaitAndLog_CompletesAfterDelay()
    {
        var task = WaitAndLog(3000);
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(task.IsCompleted);

        // Retrieve result (throws if faulted)
        task.GetAwaiter().GetResult();
    }

    [Test]
    public void WaitAndLog_ZeroDelay_CompletesImmediately()
    {
        var task = WaitAndLog(0);
        // ValkarnTask.Delay(0) returns CompletedTask immediately
        Assert.IsTrue(task.IsCompleted);
    }
}
```

### Testing cancellation

```csharp
[Test]
public void Delay_CancelledMidway_ThrowsOCE()
{
    var cts = new CancellationTokenSource();
    var task = ValkarnTask.Delay(5000, cts.Token);
    Assert.IsFalse(task.IsCompleted);

    cts.Cancel();
    // Cancellation propagates on the next tick
    clock.AdvanceFrame();

    Assert.IsTrue(task.IsCompleted);
    Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
}
```

### Collecting unobserved exceptions

```csharp
[Test]
public void FaultedTask_PublishesUnobservedException()
{
    using var collector = new TestHelper.UnobservedExceptionCollector();

    var tcs = new ValkarnTaskCompletionSource();
    var task = tcs.Task;

    // Don't await — fire and forget
    task.Forget();

    // Complete with exception — triggers unobserved path
    tcs.TrySetException(new InvalidOperationException("oops"));

    Assert.AreEqual(1, collector.Exceptions.Count);
    Assert.IsInstanceOf<InvalidOperationException>(collector.Exceptions[0]);
}
```

---

## Example: Testing a Channel Producer/Consumer

```csharp
[TestFixture]
public class ChannelPipelineTests
{
    [SetUp]
    public void SetUp() => TestHelper.EnsureInitialized();

    [Test]
    public void Producer_WritesItems_ConsumerReadsInOrder()
    {
        var channel = ValkarnTask.Channel.CreateUnbounded<int>();

        // Producer: write synchronously
        channel.Writer.TryWrite(1);
        channel.Writer.TryWrite(2);
        channel.Writer.TryWrite(3);
        channel.Writer.Complete();

        // Consumer: ReadAsync on a channel with items completes synchronously
        Assert.AreEqual(1, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(2, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(3, TestHelper.RunSync(channel.Reader.ReadAsync()));
    }

    [Test]
    public void ReadAsync_BeforeWrite_PendsThenCompletesOnWrite()
    {
        var channel = ValkarnTask.Channel.CreateUnbounded<string>();

        // Read issued before any write
        var readTask = channel.Reader.ReadAsync();
        Assert.IsFalse(readTask.IsCompleted);

        // Write completes the pending read synchronously
        channel.Writer.TryWrite("hello");
        Assert.IsTrue(readTask.IsCompleted);
        Assert.AreEqual("hello", readTask.GetAwaiter().GetResult());
    }

    [Test]
    public void Complete_DrainedChannel_CompletionTaskCompletes()
    {
        var channel = ValkarnTask.Channel.CreateUnbounded<int>();
        channel.Writer.TryWrite(42);

        var completion = channel.Reader.Completion;
        Assert.IsFalse(completion.IsCompleted);

        channel.Writer.Complete();
        Assert.IsFalse(completion.IsCompleted);  // item still unread

        channel.Reader.TryRead(out _);
        Assert.IsTrue(completion.IsCompleted);   // drained — completion fires
    }

    [Test]
    public void ClosedChannel_Read_ThrowsChannelClosedException()
    {
        var channel = ValkarnTask.Channel.CreateUnbounded<int>();
        channel.Writer.Complete();

        var readTask = channel.Reader.ReadAsync();
        Assert.IsTrue(readTask.IsCompleted);
        Assert.Throws<ChannelClosedException>(() => readTask.GetAwaiter().GetResult());
    }
}
```

---

## Example: Testing ValkarnTask.Delay With Frame Advancement

This example demonstrates testing a method that waits a configurable delay, verifying that partial advancement does not complete the task prematurely.

```csharp
[TestFixture]
public class DelayFrameTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = ValkarnTaskTestHelper.Setup(defaultDeltaTime: 1f / 60f);

    [TearDown]
    public void TearDown() => ValkarnTaskTestHelper.Teardown();

    [Test]
    public void Delay_500ms_RequiresTenFramesAt50ms()
    {
        clock.SetDeltaTime(0.05f);  // 50 ms per frame
        var task = ValkarnTask.Delay(500);

        clock.AdvanceFrames(9);
        Assert.IsFalse(task.IsCompleted, "450 ms elapsed — should not be done");

        clock.AdvanceFrame();
        Assert.IsTrue(task.IsCompleted, "500 ms elapsed — should be done");
    }

    [Test]
    public void Delay_Realtime_UsesTimestampNotDeltaTime()
    {
        // Realtime delay ignores DeltaTime; it uses the Stopwatch timestamp
        clock.SetDeltaTime(0f);  // DeltaTime frozen
        var task = ValkarnTask.Delay(200, DelayType.Realtime);

        // Advance only moves the timestamp via TimeSpan
        clock.Advance(TimeSpan.FromMilliseconds(199));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(task.IsCompleted);
    }
}
```

---

## NUnit Integration

The test suite uses **NUnit 3.x** (pinned at 3.x in `TestRunner.csproj`). NUnit 4 removed the classic `Assert.IsTrue` / `Assert.AreEqual` API that the tests use; do not upgrade to NUnit 4 unless the assertions are updated to the constraint-based API.

### Unity Test Framework

In the Unity Editor, tests in `Tests/Editor/` are discovered and run by the **Unity Test Framework** (UTF), which is NUnit-based. The test runner integration works as follows:

- UTF runs each `[Test]` on the main thread.
- `ValkarnTaskTestHelper.Setup()` installs the `TestClock` before each test; UTF's `[SetUp]` attribute calls it.
- `ValkarnTaskTestHelper.Teardown()` removes the clock in `[TearDown]`.
- No coroutine or `[UnityTest]` is needed: all Valkarn Tasks tests use synchronous NUnit `[Test]` methods. The `TestClock` drives time manually, so there is no need for real frame advancement.

---

## The _TestRunner~ Project

The `_TestRunner~/` directory contains a standalone .NET 8 project that compiles and runs the entire test suite **outside Unity**, using the .NET SDK and `dotnet test`. This is used for CI and for contributor local development.

### Structure

```
_TestRunner~/
  TestRunner.csproj   — .NET 8 project file
  bin~/               — build output (gitignored)
  obj~/               — intermediate output (gitignored)
```

### How it works

`TestRunner.csproj` compiles four sets of sources into a single assembly:

| Source set | Path | Notes |
|------------|------|-------|
| Runtime | `../Runtime/**/*.cs` | All runtime files |
| Testing | `../Testing/**/*.cs` | `ValkarnTaskTestHelper`, `TestClock` |
| Tests | `../Tests/Editor/**/*.cs` | All `.cs` test files |
| Exclusions | `UnityTimeProvider.cs`, `Bridge/`, `Burst/`, `ECS/` | Requires real Unity APIs — excluded |

The project does **not** define `UNITY_5_3_OR_NEWER`, `VTASKS_HAS_BURST`, `VTASKS_HAS_COLLECTIONS`, or `VTASKS_HAS_ENTITIES`, so all Unity-specific `#if` branches are compiled out. This means the tests exercise the pure-C# code paths.

The analyzer DLLs are loaded as `<Analyzer>` items, so the same rules that fire in the IDE also fire during `dotnet build` of the test project.

### Running the tests

```bash
cd _TestRunner~
dotnet test
```

Or from the repository root:

```bash
dotnet test _TestRunner~/TestRunner.csproj
```

### Excluded test files

Three test subdirectories are excluded from the `_TestRunner~` project because they require real Unity runtime APIs:

| Directory | Reason |
|-----------|--------|
| `Tests/Editor/Bridge/` | Tests for Job System bridge; requires `Unity.Jobs` |
| `Tests/Editor/Burst/` | Tests for Burst scheduler; requires `Unity.Burst` |
| `Tests/Editor/ECS/` | Tests for ECS utilities; requires `Unity.Entities` |

These tests run only inside the Unity Editor via the Unity Test Framework.

### Adding a new test

1. Add a new `.cs` file under `Tests/Editor/` (not in a Unity-API subdirectory).
2. Decorate the class with `[TestFixture]` and the methods with `[Test]`.
3. If the test needs time control, use `ValkarnTaskTestHelper.Setup()` / `Teardown()` in `[SetUp]` / `[TearDown]`.
4. If the test needs only synchronous task assertions (no delay), use `TestHelper.EnsureInitialized()` in `[SetUp]` — no teardown needed.
5. Run `dotnet test _TestRunner~/TestRunner.csproj` to verify outside Unity.
6. Run the Unity Test Framework to verify inside Unity.
