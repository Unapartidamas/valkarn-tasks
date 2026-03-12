---
sidebar_position: 3
title: Testing
---

# Testing

Valkarn Tasks एक dedicated testing infrastructure ship करता है जो async code के लिए fast, deterministic unit tests लिखने देता है — कोई real timers नहीं, कोई frame waits नहीं, core test suite के लिए Unity Editor की आवश्यकता नहीं।

---

## Overview

Testing support `Testing/` assembly (`UnaPartidaMas.Valkarn.Tasks.Testing`) में है, जो `InternalsVisibleTo` के माध्यम से runtime को visible है। यह दो public types expose करता है:

| Type | Purpose |
|------|---------|
| `VlkTaskTestHelper` | Test session के लिए Valkarn Tasks runtime initialize और tear down करें |
| `TestClock` | Deterministic time और frame provider; tests के दौरान Unity `TimeProvider` को replace करता है |

---

## VlkTaskTestHelper

`VlkTaskTestHelper` `UnaPartidaMas.Valkarn.Tasks.Testing` namespace में एक static utility class है।

### यह क्या करता है

`Setup()` तीन काम करता है:
1. एक `TestClock` create करता है और उसे `TimeProvider.Current` के रूप में install करता है, किसी भी real Unity time provider को replace करता है।
2. `PlayerLoopHelper.InitializeForTest()` call करता है, जो सभी 16 PlayerLoop timings के लिए `ContinuationQueue` और `PlayerLoopRunner` arrays allocate करता है और runtime को initialised mark करता है।
3. `TestClock` return करता है ताकि आपका test time control कर सके।

`Teardown()` setup reverse करता है:
1. `TimeProvider.Current` के रूप में fresh, no-op `TestClock` install करता है ताकि stale time test fixtures के बीच leak न हो।
2. `PlayerLoopHelper.ShutdownForTest()` call करता है, जो queues और runners tear down करता है।

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
        clock = VlkTaskTestHelper.Setup();
    }

    [TearDown]
    public void TearDown()
    {
        VlkTaskTestHelper.Teardown();
    }

    // Tests यहाँ जाएँ
}
```

`[SetUp]` में प्रत्येक test के लिए `Setup` एक बार call करें और `[TearDown]` में `Teardown` एक बार। Pattern ensure करता है कि हर test clean runtime state के साथ शुरू हो और अगले test में leak न हो।

### Default delta time

`Setup` optional `defaultDeltaTime` parameter accept करता है (default: `1/60` seconds, यानी 60 fps):

```csharp
clock = VlkTaskTestHelper.Setup(defaultDeltaTime: 1f / 30f);  // 30 fps simulation
```

यह value `TestClock.AdvanceFrame()` और `TestClock.AdvanceFrames(n)` द्वारा use होती है।

---

## TestClock

`TestClock` `ITimeProvider` implement करता है और tests को इन पर full control देता है:

- Simulated time (`GetTimestamp()` के माध्यम से, `Stopwatch.Frequency` ticks द्वारा backed)
- Current frame के लिए `DeltaTime` और `UnscaledDeltaTime`
- `FrameCount`

### Time advance करना

#### `Advance(TimeSpan duration)`

एक single step में दिए गए duration से time advance करता है। उस frame के लिए entire duration `DeltaTime` के रूप में apply होती है, `FrameCount` 1 से increment होता है, और सभी PlayerLoop timings process होती हैं। Processing के बाद, `DeltaTime` उस value पर restore होती है जो call से पहले थी।

```csharp
var task = VlkTask.Delay(3000);  // 3-second delay

clock.Advance(TimeSpan.FromSeconds(2));
Assert.IsFalse(task.IsCompleted);   // अभी नहीं

clock.Advance(TimeSpan.FromSeconds(1));
Assert.IsTrue(task.IsCompleted);    // exactly 3 seconds पर
```

Individual frames simulate किए बिना time में specific point पर jump करना चाहते हों तो इसका उपयोग करें।

#### `AdvanceFrame()`

Current `DeltaTime` उपयोग करके एक frame advance करता है। `FrameCount` 1 से increment होता है, सभी PlayerLoop timings process होती हैं, और processing से पहले 1 ms `Thread.Sleep` insert होता है। Sleep exist करता है क्योंकि background worker threads (`RunOnThreadPool` और Unity Job System integration द्वारा used) को frame ticks के बीच complete होने के लिए small window चाहिए — इसके बिना, tests बिना किसी gap के back-to-back frames run करते हैं, जो race conditions cause करता है जो real Unity frames में नहीं होतीं।

```csharp
clock.AdvanceFrame();
```

#### `AdvanceFrames(int count)`

`AdvanceFrame()` को `count` बार call करता है।

```csharp
clock.AdvanceFrames(10);  // 10 frames simulate करें
```

#### `ProcessTick(PlayerLoopTiming timing)`

Time या `FrameCount` advance किए बिना single PlayerLoop timing phase process करता है। Useful है जब code test करना हो जो specific timing पर scheduled हो (जैसे `PlayerLoopTiming.FixedUpdate`) और आप सभी अन्य timings process नहीं करना चाहते।

```csharp
clock.ProcessTick(PlayerLoopTiming.FixedUpdate);
```

### Delta time control करना

#### `SetDeltaTime(float deltaTime)`

`DeltaTime` और `UnscaledDeltaTime` दोनों को सभी subsequent frames के लिए same value पर set करता है।

#### `SetDeltaTime(float deltaTime, float unscaledDeltaTime)`

Non-unit `Time.timeScale` simulate करने के लिए उन्हें independently set करता है। उदाहरण के लिए, `DelayType.UnscaledDeltaTime` उपयोग करने वाले code को test करने के लिए जब time paused हो:

```csharp
clock.SetDeltaTime(deltaTime: 0f, unscaledDeltaTime: 0.05f);

var scaledTask = VlkTask.Delay(500, DelayType.DeltaTime);
var unscaledTask = VlkTask.Delay(500, DelayType.UnscaledDeltaTime);

clock.AdvanceFrames(9);  // 450 ms unscaled, 0 ms scaled
Assert.IsFalse(scaledTask.IsCompleted);    // paused game time कभी 500ms नहीं पहुँचता
Assert.IsFalse(unscaledTask.IsCompleted); // 450ms < 500ms

clock.AdvanceFrame();  // 500 ms unscaled
Assert.IsTrue(unscaledTask.IsCompleted);  // unscaled delay done
Assert.IsFalse(scaledTask.IsCompleted);  // scaled अभी भी कभी move नहीं होता
```

---

## Async VlkTask Methods के लिए Unit Tests लिखना

### Tests जो synchronously complete होते हैं

कुछ `VlkTask` operations suspend हुए बिना complete होते हैं — उदाहरण के लिए, `VlkTask.CompletedTask` await करना, `VlkTask.FromResult(value)`, या `VlkTaskCompletionSource` जो await होने से पहले complete हो गया था। इन्हें clock की आवश्यकता नहीं है और ये सीधे internal `TestHelper` class use कर सकते हैं (test assembly के अंदर internal):

```csharp
[TestFixture]
public class SyncTests
{
    [SetUp]
    public void SetUp()
    {
        // केवल main thread ID set करता है — कोई clock या PlayerLoop आवश्यक नहीं
        TestHelper.EnsureInitialized();
    }

    [Test]
    public void FromResult_ReturnsValue()
    {
        var task = VlkTask.FromResult(42);
        Assert.IsTrue(task.IsCompleted);
        Assert.AreEqual(42, task.GetAwaiter().GetResult());
    }
}
```

`TestHelper` (`Tests/Editor/TestHelper.cs` में) test suite द्वारा खुद उपयोग किया जाने वाला internal helper है:

- `TestHelper.EnsureInitialized()` current thread को `VlkTaskPoolShared.MainThreadId` set करता है। यह आवश्यक है ताकि pool operations correct (non-atomic) fast path पर route हों।
- `TestHelper.RunSync(VlkTask task)` assert करता है कि task already completed है और `GetResult()` call करता है — उन code paths test करने के लिए useful जो synchronously complete होनी चाहिए।
- `TestHelper.RunSync<T>(VlkTask<T> task)` same करता है और result value return करता है।
- `TestHelper.UnobservedExceptionCollector` अपने scope की duration के लिए `VlkTask.UnobservedException` को subscribe करता है, assertion के लिए कोई भी unobserved exceptions collect करता है।

### Tests जिन्हें time चाहिए

`VlkTask.Delay`, `VlkTask.Yield`, या PlayerLoop timing पर scheduled code वाले किसी भी test को `VlkTaskTestHelper.Setup()` और returned `TestClock` की आवश्यकता है।

**Example: delay-based method test करना**

मान लें आपके पास है:

```csharp
public static async VlkTask WaitAndLog(int ms)
{
    await VlkTask.Delay(ms);
    Log("done");
}
```

Real waiting के बिना test करें:

```csharp
[TestFixture]
public class DelayTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = VlkTaskTestHelper.Setup();

    [TearDown]
    public void TearDown() => VlkTaskTestHelper.Teardown();

    [Test]
    public void WaitAndLog_CompletesAfterDelay()
    {
        var task = WaitAndLog(3000);
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(task.IsCompleted);

        // Result retrieve करें (faulted होने पर throws)
        task.GetAwaiter().GetResult();
    }

    [Test]
    public void WaitAndLog_ZeroDelay_CompletesImmediately()
    {
        var task = WaitAndLog(0);
        // VlkTask.Delay(0) immediately CompletedTask return करता है
        Assert.IsTrue(task.IsCompleted);
    }
}
```

### Cancellation test करना

```csharp
[Test]
public void Delay_CancelledMidway_ThrowsOCE()
{
    var cts = new CancellationTokenSource();
    var task = VlkTask.Delay(5000, cts.Token);
    Assert.IsFalse(task.IsCompleted);

    cts.Cancel();
    // Cancellation अगले tick पर propagate होती है
    clock.AdvanceFrame();

    Assert.IsTrue(task.IsCompleted);
    Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
}
```

### Unobserved exceptions collect करना

```csharp
[Test]
public void FaultedTask_PublishesUnobservedException()
{
    using var collector = new TestHelper.UnobservedExceptionCollector();

    var tcs = new VlkTaskCompletionSource();
    var task = tcs.Task;

    // Await न करें — fire and forget
    task.Forget();

    // Exception के साथ complete करें — unobserved path trigger होता है
    tcs.TrySetException(new InvalidOperationException("oops"));

    Assert.AreEqual(1, collector.Exceptions.Count);
    Assert.IsInstanceOf<InvalidOperationException>(collector.Exceptions[0]);
}
```

---

## Example: Channel Producer/Consumer Test करना

```csharp
[TestFixture]
public class ChannelPipelineTests
{
    [SetUp]
    public void SetUp() => TestHelper.EnsureInitialized();

    [Test]
    public void Producer_WritesItems_ConsumerReadsInOrder()
    {
        var channel = VlkTask.Channel.CreateUnbounded<int>();

        // Producer: synchronously write करें
        channel.Writer.TryWrite(1);
        channel.Writer.TryWrite(2);
        channel.Writer.TryWrite(3);
        channel.Writer.Complete();

        // Consumer: items वाले channel पर ReadAsync synchronously complete होता है
        Assert.AreEqual(1, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(2, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(3, TestHelper.RunSync(channel.Reader.ReadAsync()));
    }

    [Test]
    public void ReadAsync_BeforeWrite_PendsThenCompletesOnWrite()
    {
        var channel = VlkTask.Channel.CreateUnbounded<string>();

        // किसी write से पहले read issue
        var readTask = channel.Reader.ReadAsync();
        Assert.IsFalse(readTask.IsCompleted);

        // Write pending read synchronously complete करता है
        channel.Writer.TryWrite("hello");
        Assert.IsTrue(readTask.IsCompleted);
        Assert.AreEqual("hello", readTask.GetAwaiter().GetResult());
    }

    [Test]
    public void Complete_DrainedChannel_CompletionTaskCompletes()
    {
        var channel = VlkTask.Channel.CreateUnbounded<int>();
        channel.Writer.TryWrite(42);

        var completion = channel.Reader.Completion;
        Assert.IsFalse(completion.IsCompleted);

        channel.Writer.Complete();
        Assert.IsFalse(completion.IsCompleted);  // item अभी unread है

        channel.Reader.TryRead(out _);
        Assert.IsTrue(completion.IsCompleted);   // drained — completion fire होती है
    }

    [Test]
    public void ClosedChannel_Read_ThrowsChannelClosedException()
    {
        var channel = VlkTask.Channel.CreateUnbounded<int>();
        channel.Writer.Complete();

        var readTask = channel.Reader.ReadAsync();
        Assert.IsTrue(readTask.IsCompleted);
        Assert.Throws<ChannelClosedException>(() => readTask.GetAwaiter().GetResult());
    }
}
```

---

## Example: Frame Advancement के साथ VlkTask.Delay Test करना

यह example एक configurable delay wait करने वाले method को test करने को demonstrate करता है, verify करते हुए कि partial advancement task को prematurely complete नहीं करती।

```csharp
[TestFixture]
public class DelayFrameTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = VlkTaskTestHelper.Setup(defaultDeltaTime: 1f / 60f);

    [TearDown]
    public void TearDown() => VlkTaskTestHelper.Teardown();

    [Test]
    public void Delay_500ms_RequiresTenFramesAt50ms()
    {
        clock.SetDeltaTime(0.05f);  // प्रति frame 50 ms
        var task = VlkTask.Delay(500);

        clock.AdvanceFrames(9);
        Assert.IsFalse(task.IsCompleted, "450 ms elapsed — done नहीं होना चाहिए");

        clock.AdvanceFrame();
        Assert.IsTrue(task.IsCompleted, "500 ms elapsed — done होना चाहिए");
    }

    [Test]
    public void Delay_Realtime_UsesTimestampNotDeltaTime()
    {
        // Realtime delay DeltaTime ignore करता है; यह Stopwatch timestamp उपयोग करता है
        clock.SetDeltaTime(0f);  // DeltaTime frozen
        var task = VlkTask.Delay(200, DelayType.Realtime);

        // Advance केवल timestamp को TimeSpan के माध्यम से move करता है
        clock.Advance(TimeSpan.FromMilliseconds(199));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(task.IsCompleted);
    }
}
```

---

## NUnit Integration

Test suite **NUnit 3.x** उपयोग करता है (`TestRunner.csproj` में 3.x पर pinned)। NUnit 4 ने classic `Assert.IsTrue` / `Assert.AreEqual` API हटा दिया जो tests उपयोग करते हैं; NUnit 4 पर तब तक upgrade न करें जब तक assertions constraint-based API पर update न हों।

### Unity Test Framework

Unity Editor में, `Tests/Editor/` में tests **Unity Test Framework** (UTF) द्वारा discover और run होते हैं, जो NUnit-based है। Test runner integration इस प्रकार काम करता है:

- UTF हर `[Test]` main thread पर run करता है।
- `VlkTaskTestHelper.Setup()` हर test से पहले `TestClock` install करता है; UTF का `[SetUp]` attribute उसे call करता है।
- `VlkTaskTestHelper.Teardown()` clock को `[TearDown]` में remove करता है।
- कोई coroutine या `[UnityTest]` आवश्यक नहीं: सभी Valkarn Tasks tests synchronous NUnit `[Test]` methods उपयोग करते हैं। `TestClock` time manually drive करता है, इसलिए real frame advancement की आवश्यकता नहीं है।

---

## _TestRunner~ Project

`_TestRunner~/` directory में एक standalone .NET 8 project है जो **Unity के बाहर**, .NET SDK और `dotnet test` उपयोग करके entire test suite compile और run करता है। यह CI और contributor local development के लिए उपयोग होता है।

### Structure

```
_TestRunner~/
  TestRunner.csproj   — .NET 8 project file
  bin~/               — build output (gitignored)
  obj~/               — intermediate output (gitignored)
```

### यह कैसे काम करता है

`TestRunner.csproj` चार sets के sources को single assembly में compile करता है:

| Source set | Path | Notes |
|------------|------|-------|
| Runtime | `../Runtime/**/*.cs` | सभी runtime files |
| Testing | `../Testing/**/*.cs` | `VlkTaskTestHelper`, `TestClock` |
| Tests | `../Tests/Editor/**/*.cs` | सभी `.cs` test files |
| Exclusions | `UnityTimeProvider.cs`, `Bridge/`, `Burst/`, `ECS/` | Real Unity APIs की आवश्यकता — excluded |

Project `UNITY_5_3_OR_NEWER`, `VTASKS_HAS_BURST`, `VTASKS_HAS_COLLECTIONS`, या `VTASKS_HAS_ENTITIES` define **नहीं** करता, इसलिए सभी Unity-specific `#if` branches compile out होते हैं। इसका मतलब है tests pure-C# code paths exercise करते हैं।

Analyzer DLLs `<Analyzer>` items के रूप में load होते हैं, इसलिए IDE में fire होने वाले same rules test project के `dotnet build` के दौरान भी fire होते हैं।

### Tests run करना

```bash
cd _TestRunner~
dotnet test
```

या repository root से:

```bash
dotnet test _TestRunner~/TestRunner.csproj
```

### Excluded test files

तीन test subdirectories `_TestRunner~` project से excluded हैं क्योंकि उन्हें real Unity runtime APIs की आवश्यकता है:

| Directory | Reason |
|-----------|--------|
| `Tests/Editor/Bridge/` | Job System bridge के tests; `Unity.Jobs` की आवश्यकता |
| `Tests/Editor/Burst/` | Burst scheduler के tests; `Unity.Burst` की आवश्यकता |
| `Tests/Editor/ECS/` | ECS utilities के tests; `Unity.Entities` की आवश्यकता |

ये tests केवल Unity Editor के अंदर Unity Test Framework के माध्यम से run होते हैं।

### New test add करना

1. `Tests/Editor/` के नीचे एक new `.cs` file add करें (Unity-API subdirectory में नहीं)।
2. Class को `[TestFixture]` और methods को `[Test]` से decorate करें।
3. यदि test को time control चाहिए, `[SetUp]` / `[TearDown]` में `VlkTaskTestHelper.Setup()` / `Teardown()` उपयोग करें।
4. यदि test को केवल synchronous task assertions (कोई delay नहीं) चाहिए, `[SetUp]` में `TestHelper.EnsureInitialized()` उपयोग करें — कोई teardown आवश्यक नहीं।
5. Unity के बाहर verify करने के लिए `dotnet test _TestRunner~/TestRunner.csproj` run करें।
6. Unity के अंदर verify करने के लिए Unity Test Framework run करें।
