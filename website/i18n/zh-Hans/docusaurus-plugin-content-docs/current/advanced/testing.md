---
sidebar_position: 3
title: 测试
---

# 测试

Valkarn Tasks 附带专用的测试基础设施，让你可以为异步代码编写快速、确定性的单元测试——无需真实计时器、无需等待帧、核心测试套件无需 Unity 编辑器。

---

## 概述

测试支持位于 `Testing/` 程序集（`UnaPartidaMas.Valkarn.Tasks.Testing`）中，该程序集通过 `InternalsVisibleTo` 对运行时可见。它暴露两个公共类型：

| 类型 | 用途 |
|------|------|
| `VlkTaskTestHelper` | 为测试会话初始化和销毁 Valkarn Tasks 运行时 |
| `TestClock` | 确定性时间和帧提供器；在测试期间替换 Unity 的 `TimeProvider` |

---

## VlkTaskTestHelper

`VlkTaskTestHelper` 是 `UnaPartidaMas.Valkarn.Tasks.Testing` 命名空间中的一个静态工具类。

### 功能说明

`Setup()` 执行三件事：
1. 创建一个 `TestClock` 并将其安装为 `TimeProvider.Current`，替换任何真实的 Unity 时间提供器。
2. 调用 `PlayerLoopHelper.InitializeForTest()`，为所有 16 个 PlayerLoop 时机分配 `ContinuationQueue` 和 `PlayerLoopRunner` 数组，并将运行时标记为已初始化。
3. 返回 `TestClock`，让你的测试可以控制时间。

`Teardown()` 反转设置：
1. 将一个全新的、无操作的 `TestClock` 安装为 `TimeProvider.Current`，防止过期时间在测试夹具之间泄漏。
2. 调用 `PlayerLoopHelper.ShutdownForTest()`，销毁队列和运行器。

### 使用模式

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

    // 测试方法放这里
}
```

在 `[SetUp]` 中每个测试调用一次 `Setup`，在 `[TearDown]` 中调用一次 `Teardown`。该模式确保每个测试以干净的运行时状态开始，不会泄漏到下一个测试。

### 默认 delta time

`Setup` 接受一个可选的 `defaultDeltaTime` 参数（默认：`1/60` 秒，即 60 fps）：

```csharp
clock = VlkTaskTestHelper.Setup(defaultDeltaTime: 1f / 30f);  // 30 fps 模拟
```

该值由 `TestClock.AdvanceFrame()` 和 `TestClock.AdvanceFrames(n)` 使用。

---

## TestClock

`TestClock` 实现了 `ITimeProvider`，让测试完全控制：

- 模拟时间（通过 `GetTimestamp()`，基于 `Stopwatch.Frequency` 刻度）
- 当前帧的 `DeltaTime` 和 `UnscaledDeltaTime`
- `FrameCount`

### 推进时间

#### `Advance(TimeSpan duration)`

以单步方式将时间推进给定的持续时间。整个持续时间作为该帧的 `DeltaTime` 应用，`FrameCount` 递增 1，并处理所有 PlayerLoop 时机。处理后，`DeltaTime` 恢复为调用前的值。

```csharp
var task = VlkTask.Delay(3000);  // 3 秒延迟

clock.Advance(TimeSpan.FromSeconds(2));
Assert.IsFalse(task.IsCompleted);   // 尚未完成

clock.Advance(TimeSpan.FromSeconds(1));
Assert.IsTrue(task.IsCompleted);    // 恰好在 3 秒时完成
```

当你想跳到时间中的某个特定点而不模拟单独的帧时，使用此方法。

#### `AdvanceFrame()`

使用当前 `DeltaTime` 推进一帧。`FrameCount` 递增 1，处理所有 PlayerLoop 时机，并在处理前插入 1 ms 的 `Thread.Sleep`。睡眠的存在是因为后台工作线程（由 `RunOnThreadPool` 和 Unity Job System 集成使用）需要在帧刻之间有一个小窗口来完成工作——没有它，测试会背靠背地运行帧，中间没有间隔，这会导致在真实 Unity 帧中不会出现的竞争条件。

```csharp
clock.AdvanceFrame();
```

#### `AdvanceFrames(int count)`

调用 `AdvanceFrame()` `count` 次。

```csharp
clock.AdvanceFrames(10);  // 模拟 10 帧
```

#### `ProcessTick(PlayerLoopTiming timing)`

处理单个 PlayerLoop 时机阶段，而不推进时间或 `FrameCount`。当测试在特定时机（例如 `PlayerLoopTiming.FixedUpdate`）调度的代码且不想处理所有其他时机时很有用。

```csharp
clock.ProcessTick(PlayerLoopTiming.FixedUpdate);
```

### 控制 delta time

#### `SetDeltaTime(float deltaTime)`

将 `DeltaTime` 和 `UnscaledDeltaTime` 设为相同值，用于所有后续帧。

#### `SetDeltaTime(float deltaTime, float unscaledDeltaTime)`

独立设置它们以模拟非单位 `Time.timeScale`。例如，测试游戏暂停时使用 `DelayType.UnscaledDeltaTime` 的代码：

```csharp
clock.SetDeltaTime(deltaTime: 0f, unscaledDeltaTime: 0.05f);

var scaledTask = VlkTask.Delay(500, DelayType.DeltaTime);
var unscaledTask = VlkTask.Delay(500, DelayType.UnscaledDeltaTime);

clock.AdvanceFrames(9);  // 非缩放 450 ms，缩放 0 ms
Assert.IsFalse(scaledTask.IsCompleted);    // 暂停的游戏时间永远达不到 500ms
Assert.IsFalse(unscaledTask.IsCompleted); // 450ms < 500ms

clock.AdvanceFrame();  // 非缩放 500 ms
Assert.IsTrue(unscaledTask.IsCompleted);  // 非缩放延迟完成
Assert.IsFalse(scaledTask.IsCompleted);  // 缩放时间仍然不动
```

---

## 为异步 VlkTask 方法编写单元测试

### 同步完成的测试

某些 `VlkTask` 操作无需挂起即可完成——例如，等待 `VlkTask.CompletedTask`、`VlkTask.FromResult(value)`，或等待在被等待前已完成的 `VlkTaskCompletionSource`。这些不需要时钟，可以直接使用内部的 `TestHelper` 类（对测试程序集内部可见）：

```csharp
[TestFixture]
public class SyncTests
{
    [SetUp]
    public void SetUp()
    {
        // 仅设置主线程 ID — 不需要时钟或 PlayerLoop
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

`TestHelper`（位于 `Tests/Editor/TestHelper.cs`）是测试套件本身使用的内部辅助工具：

- `TestHelper.EnsureInitialized()` 将 `VlkTaskPoolShared.MainThreadId` 设为当前线程。这是必需的，以便对象池操作路由到正确的（非原子）快速路径。
- `TestHelper.RunSync(VlkTask task)` 断言任务已完成并调用 `GetResult()`——用于测试应该同步完成的代码路径。
- `TestHelper.RunSync<T>(VlkTask<T> task)` 做同样的事并返回结果值。
- `TestHelper.UnobservedExceptionCollector` 在其作用域期间订阅 `VlkTask.UnobservedException`，收集任何未观察到的异常以供断言。

### 需要时间的测试

任何涉及 `VlkTask.Delay`、`VlkTask.Yield` 或在 PlayerLoop 时机调度代码的测试都需要 `VlkTaskTestHelper.Setup()` 和返回的 `TestClock`。

**示例：测试基于延迟的方法**

假设你有：

```csharp
public static async VlkTask WaitAndLog(int ms)
{
    await VlkTask.Delay(ms);
    Log("done");
}
```

在不实际等待的情况下测试它：

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

        // 获取结果（如果出错则抛出）
        task.GetAwaiter().GetResult();
    }

    [Test]
    public void WaitAndLog_ZeroDelay_CompletesImmediately()
    {
        var task = WaitAndLog(0);
        // VlkTask.Delay(0) 立即返回 CompletedTask
        Assert.IsTrue(task.IsCompleted);
    }
}
```

### 测试取消

```csharp
[Test]
public void Delay_CancelledMidway_ThrowsOCE()
{
    var cts = new CancellationTokenSource();
    var task = VlkTask.Delay(5000, cts.Token);
    Assert.IsFalse(task.IsCompleted);

    cts.Cancel();
    // 取消在下一个 tick 传播
    clock.AdvanceFrame();

    Assert.IsTrue(task.IsCompleted);
    Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
}
```

### 收集未观察到的异常

```csharp
[Test]
public void FaultedTask_PublishesUnobservedException()
{
    using var collector = new TestHelper.UnobservedExceptionCollector();

    var tcs = new VlkTaskCompletionSource();
    var task = tcs.Task;

    // 不 await — 即发即弃
    task.Forget();

    // 以异常完成 — 触发未观察到路径
    tcs.TrySetException(new InvalidOperationException("oops"));

    Assert.AreEqual(1, collector.Exceptions.Count);
    Assert.IsInstanceOf<InvalidOperationException>(collector.Exceptions[0]);
}
```

---

## 示例：测试通道生产者/消费者

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

        // 生产者：同步写入
        channel.Writer.TryWrite(1);
        channel.Writer.TryWrite(2);
        channel.Writer.TryWrite(3);
        channel.Writer.Complete();

        // 消费者：对有条目的通道调用 ReadAsync 同步完成
        Assert.AreEqual(1, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(2, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(3, TestHelper.RunSync(channel.Reader.ReadAsync()));
    }

    [Test]
    public void ReadAsync_BeforeWrite_PendsThenCompletesOnWrite()
    {
        var channel = VlkTask.Channel.CreateUnbounded<string>();

        // 在任何写入之前发起读取
        var readTask = channel.Reader.ReadAsync();
        Assert.IsFalse(readTask.IsCompleted);

        // 写入同步完成待处理的读取
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
        Assert.IsFalse(completion.IsCompleted);  // 条目仍未读取

        channel.Reader.TryRead(out _);
        Assert.IsTrue(completion.IsCompleted);   // 已耗尽 — 完成触发
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

## 示例：使用帧推进测试 VlkTask.Delay

此示例演示测试一个等待可配置延迟的方法，验证部分推进不会过早完成任务。

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
        clock.SetDeltaTime(0.05f);  // 每帧 50 ms
        var task = VlkTask.Delay(500);

        clock.AdvanceFrames(9);
        Assert.IsFalse(task.IsCompleted, "已过 450 ms — 不应完成");

        clock.AdvanceFrame();
        Assert.IsTrue(task.IsCompleted, "已过 500 ms — 应该完成");
    }

    [Test]
    public void Delay_Realtime_UsesTimestampNotDeltaTime()
    {
        // Realtime 延迟忽略 DeltaTime；它使用 Stopwatch 时间戳
        clock.SetDeltaTime(0f);  // DeltaTime 冻结
        var task = VlkTask.Delay(200, DelayType.Realtime);

        // Advance 仅通过 TimeSpan 移动时间戳
        clock.Advance(TimeSpan.FromMilliseconds(199));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(task.IsCompleted);
    }
}
```

---

## NUnit 集成

测试套件使用 **NUnit 3.x**（在 `TestRunner.csproj` 中固定为 3.x）。NUnit 4 移除了测试使用的经典 `Assert.IsTrue` / `Assert.AreEqual` API；除非将断言更新为基于约束的 API，否则不要升级到 NUnit 4。

### Unity Test Framework

在 Unity 编辑器中，`Tests/Editor/` 中的测试由基于 NUnit 的 **Unity Test Framework**（UTF）发现和运行。测试运行器集成工作方式如下：

- UTF 在主线程上运行每个 `[Test]`。
- `VlkTaskTestHelper.Setup()` 在每个测试前安装 `TestClock`；UTF 的 `[SetUp]` 属性调用它。
- `VlkTaskTestHelper.Teardown()` 在 `[TearDown]` 中移除时钟。
- 不需要协程或 `[UnityTest]`：所有 Valkarn Tasks 测试都使用同步 NUnit `[Test]` 方法。`TestClock` 手动驱动时间，因此不需要真实的帧推进。

---

## _TestRunner~ 项目

`_TestRunner~/` 目录包含一个独立的 .NET 8 项目，可以使用 .NET SDK 和 `dotnet test` **在 Unity 之外**编译并运行整个测试套件。它用于 CI 和贡献者本地开发。

### 结构

```
_TestRunner~/
  TestRunner.csproj   — .NET 8 项目文件
  bin~/               — 构建输出（已 gitignore）
  obj~/               — 中间输出（已 gitignore）
```

### 工作原理

`TestRunner.csproj` 将四组源码编译成单个程序集：

| 源码组 | 路径 | 说明 |
|--------|------|------|
| 运行时 | `../Runtime/**/*.cs` | 所有运行时文件 |
| 测试支持 | `../Testing/**/*.cs` | `VlkTaskTestHelper`、`TestClock` |
| 测试 | `../Tests/Editor/**/*.cs` | 所有 `.cs` 测试文件 |
| 排除项 | `UnityTimeProvider.cs`、`Bridge/`、`Burst/`、`ECS/` | 需要真实 Unity API — 已排除 |

该项目**不**定义 `UNITY_5_3_OR_NEWER`、`VTASKS_HAS_BURST`、`VTASKS_HAS_COLLECTIONS` 或 `VTASKS_HAS_ENTITIES`，因此所有特定于 Unity 的 `#if` 分支都被编译排除。这意味着测试练习纯 C# 代码路径。

分析器 DLL 作为 `<Analyzer>` 项加载，因此在 IDE 中触发的相同规则也会在测试项目的 `dotnet build` 期间触发。

### 运行测试

```bash
cd _TestRunner~
dotnet test
```

或从仓库根目录：

```bash
dotnet test _TestRunner~/TestRunner.csproj
```

### 排除的测试文件

`_TestRunner~` 项目排除了三个测试子目录，因为它们需要真实的 Unity 运行时 API：

| 目录 | 原因 |
|------|------|
| `Tests/Editor/Bridge/` | Job System 桥接测试；需要 `Unity.Jobs` |
| `Tests/Editor/Burst/` | Burst 调度器测试；需要 `Unity.Burst` |
| `Tests/Editor/ECS/` | ECS 工具测试；需要 `Unity.Entities` |

这些测试仅在 Unity 编辑器中通过 Unity Test Framework 运行。

### 添加新测试

1. 在 `Tests/Editor/` 下新建 `.cs` 文件（不在 Unity API 子目录中）。
2. 用 `[TestFixture]` 装饰类，用 `[Test]` 装饰方法。
3. 如果测试需要时间控制，在 `[SetUp]` / `[TearDown]` 中使用 `VlkTaskTestHelper.Setup()` / `Teardown()`。
4. 如果测试只需要同步任务断言（无延迟），在 `[SetUp]` 中使用 `TestHelper.EnsureInitialized()`——无需销毁。
5. 运行 `dotnet test _TestRunner~/TestRunner.csproj` 在 Unity 之外验证。
6. 运行 Unity Test Framework 在 Unity 内验证。
