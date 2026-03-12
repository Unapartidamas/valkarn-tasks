---
sidebar_position: 4
title: 功能特性
---

# 功能特性

Valkarn Tasks 完整功能参考。

---

## 核心异步原语

### ValkarnTask 结构体

零分配、基于结构体的异步返回类型，同时替代 `UniTask` 和 Unity 的 `Awaitable`。

```csharp
async ValkarnTask LoadLevel() { ... }
async ValkarnTask<int> CountEnemies() { ... }
```

- **零分配同步快速路径** — 如果方法在从不挂起的情况下完成，则零堆分配发生。
- **池化异步路径** — 如果方法挂起，状态机运行器从有界、可收缩的池中取用。零装箱，带自动修剪的有界池。
- **IL2CPP优先池化** — 主线程上的池操作使用零原子操作。IL2CPP 相比 Mono 将 `Volatile` 惩罚9.2倍、`Interlocked` 惩罚2.9倍；Valkarn 在热路径上避免了两者。
- **世代令牌安全性** — 每个池槽的 `uint` 世代计数器。每个槽位碰撞前有4,294,967,296次循环——实际上不可能发生。（UniTask 使用 `short` 令牌：约18分钟活跃异步工作后碰撞。）

### Result\<T\> — 无异常的错误处理

```csharp
var result = await loadTask.AsResult();

if (result.Succeeded)    Use(result.Value);
else if (result.IsCanceled) ShowRetry();
else                         Debug.LogError(result.Error);
```

`Result<T>` 和 `Result` 是 `readonly struct` 值，无需抛出异常即可表示任务结果。两者都支持到 `bool` 的隐式转换（成功时为 true）。

### 手动完成源

```csharp
var promise = new ValkarnTask.Promise<string>();
promise.TrySetResult("done");
string value = await promise.Task;
```

支持 `TrySetResult`、`TrySetException` 和 `TrySetCanceled`。基于终结器的未观察异常报告确保错误永远不会被静默丢失。

### 池化完成源

用于重复模式的自动重置池化变体（通道和组合器在内部使用）：

```csharp
var source = ValkarnTask.PooledPromise<int>.Create(out uint token);
source.TrySetResult(42);
int value = await source.Task; // source 自动归还到池中
```

---

## Awaitable 桥接

与 Unity `Awaitable` 的透明互操作——无需手动转换：

```csharp
async ValkarnTask LoadGame()
{
    await SceneManager.LoadSceneAsync("Level2"); // Unity Awaitable — 直接工作
    await Resources.LoadAsync<Texture2D>("hero"); // Unity Awaitable — 直接工作
    await ValkarnTask.Delay(1000);                // Valkarn 原生
}
```

源生成器自动检测 `Awaitable` 的 await 并生成适配器。

---

## 生命周期取消

### 自动（源生成）

将类标记为 `partial`——源生成器处理其余部分：

```csharp
public partial class EnemySpawner : MonoBehaviour
{
    async ValkarnTask Start()
    {
        while (true)
        {
            await ValkarnTask.Delay(3000);
            SpawnWave();
            // 当此 GameObject 被销毁时自动取消。
        }
    }
}
```

- **MonoBehaviour** — 绑定到 `destroyCancellationToken`
- **ScriptableObject** — 绑定到应用程序生命周期
- **普通类** — 无自动绑定（需要手动令牌）

### 退出自动取消

```csharp
[NoAutoCancel]
async ValkarnTask BackgroundSync()
{
    await SyncToServer(); // 销毁时不自动取消
}
```

### 手动 CancellationToken 覆盖

传递显式 `CancellationToken` 会覆盖自动注入的生命周期令牌：

```csharp
async ValkarnTask DoWork(CancellationToken ct)
{
    await ValkarnTask.Delay(1000, ct);
}
```

### 无兄弟任务取消

任务永远不会取消兄弟任务。`WhenAll` 等待所有任务；`WhenAny` 返回第一个结果，但失败的任务继续运行。这可以防止任务有副作用时的数据损坏。

---

## 关键区间

对于不得被生命周期取消中断的操作：

```csharp
async ValkarnTask SaveProgress()
{
    var data = await LoadPlayerData();        // 可取消

    await using (ValkarnTask.Critical())
    {
        await db.Insert(data);               // 即使 GO 被销毁也不会取消
        await db.Commit();
    }                                        // 挂起的取消在此处生效

    await SendNotification();                // 再次可取消
}
```

在关键区间内，取消被**延迟**——而非忽略。当区间结束时，挂起的取消被应用。

---

## 组合器

### WhenAll（类型化）

```csharp
// 直接 — 任何任务失败时抛出
var (enemies, map) = await ValkarnTask.WhenAll(LoadEnemies(), LoadMap());

// 安全 — 使用 AsResult() 包装以获得不抛出的行为
var (a, b) = await ValkarnTask.WhenAll(
    LoadEnemies().AsResult(), LoadMap().AsResult());
```

所有任务已完成时的零分配同步快速路径。`IEnumerable<ValkarnTask<T>>` 重载对内部数组使用 `ArrayPool<T>`。

### WhenAll（void）

```csharp
await ValkarnTask.WhenAll(SaveA(), SaveB(), SaveC());
await ValkarnTask.WhenAll(taskList); // IEnumerable<ValkarnTask>
```

### WhenAny

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(DownloadFromA(), DownloadFromB());
```

返回第一个完成的结果。失败的任务自然地继续运行。

### 即发即忘

```csharp
SendAnalytics("event").Forget();

[FireAndForget]
async ValkarnTask SendAnalytics(string eventName) { ... }
// 调用者无需 .Forget() — 不会产生警告
```

`.Forget()` 将错误路由到 `ValkarnTask.UnobservedException`。永远不会被静默吞掉。

### AsNonGeneric

```csharp
ValkarnTask voidTask = typedTask.AsNonGeneric();
```

---

## 时间与延迟

```csharp
await ValkarnTask.Delay(1000);                                    // 毫秒
await ValkarnTask.Delay(TimeSpan.FromSeconds(2));                  // TimeSpan
await ValkarnTask.Delay(1000, DelayType.UnscaledDeltaTime);       // 忽略时间缩放
await ValkarnTask.Delay(1000, DelayType.Realtime);                 // 基于秒表

await ValkarnTask.Yield();                                         // 下一个 PlayerLoop 帧
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);             // 特定时机
await ValkarnTask.NextFrame();                                      // 保证下一帧
await ValkarnTask.DelayFrame(5);                                    // N 帧

await ValkarnTask.WaitUntil(() => player.IsReady);
await ValkarnTask.WaitWhile(() => isLoading);
```

---

## 线程切换

```csharp
async ValkarnTask ProcessData()
{
    var raw = await DownloadData();

    await ValkarnTask.SwitchToThreadPool();
    var processed = HeavyComputation(raw);  // 后台线程

    await ValkarnTask.SwitchToMainThread();
    ApplyToGameObject(processed);           // 主线程
}
```

---

## 16个 PlayerLoop 时机

| 分组 | 时机 |
|-------|---------|
| Initialization | `Initialization`、`LastInitialization` |
| EarlyUpdate | `EarlyUpdate`、`LastEarlyUpdate` |
| FixedUpdate | `FixedUpdate`、`LastFixedUpdate` |
| PreUpdate | `PreUpdate`、`LastPreUpdate` |
| Update | `Update`、`LastUpdate` |
| PreLateUpdate | `PreLateUpdate`、`LastPreLateUpdate` |
| PostLateUpdate | `PostLateUpdate`、`LastPostLateUpdate` |
| TimeUpdate | `TimeUpdate`、`LastTimeUpdate` |

除非另有指定，所有操作默认使用 `PlayerLoopTiming.Update`。

---

## 通道

```csharp
// 无界
var channel = ValkarnTask.Channel.CreateUnbounded<EnemySpawnRequest>();

// 有界 — 满时产生背压
var channel = ValkarnTask.Channel.CreateBounded<LogEntry>(capacity: 100);

// 多消费者
var channel = ValkarnTask.Channel.CreateUnbounded<WorkItem>(multiConsumer: true);

// 生产者
await channel.Writer.WriteAsync(entry);
bool accepted = channel.Writer.TryWrite(entry);

// 消费者
await foreach (var item in channel.Reader.ReadAllAsync())
    Process(item);

// 完成
channel.Writer.Complete();
await channel.Reader.Completion;
```

---

## 确定性测试（TestClock）

```csharp
[Test]
public void Respawn_WaitsThreeSeconds()
{
    var clock = new TestClock();
    var task = spawner.RespawnEnemy();

    clock.Advance(TimeSpan.FromSeconds(2));
    Assert.IsFalse(task.IsCompleted);

    clock.Advance(TimeSpan.FromSeconds(1));
    Assert.IsTrue(task.IsCompleted);
}
```

所有时间依赖操作从 `TimeProvider.Current` 读取。在测试中，将其替换为 `TestClock`。`AdvanceFrame()` 模拟单个 PlayerLoop 帧。

---

## Job 系统桥接

```csharp
var job = new PathfindingJob { start = a, end = b, results = results };
await job.ScheduleAsync();
// results NativeArray 在 await 后立即可读

await job.ScheduleParallelAsync(dataCount, batchSize); // IJobParallelFor

// 取消 — 在报告取消前完成 job handle（无 job 泄漏）
await job.ScheduleAsync(cancellationToken);
```

---

## 编译时诊断

| 代码 | 严重性 | 描述 |
|------|----------|-------------|
| TT001 | 警告 | 对 `ValkarnTask` 的双重 await / 释放后使用 |
| TT002 | 错误 | `async ValkarnTask` 结果用作表达式语句——必须 await 或 `.Forget()` |
| TT010 | 信息 | MonoBehaviour 中的异步方法在 Destroy 时自动取消 |
| TT011 | 警告 | `WhenAll` 包含不同生命周期的任务 |
| TT012 | 警告 | 没有取消检查的异步循环（潜在僵尸循环） |
| TT013 | 警告 | `ValkarnTask` 已返回但从未 await 且未明确丢弃 |
| TT014 | 警告 | `[NoAutoCancel]` 没有手动 `CancellationToken` 参数 |
| TT015 | 信息 | 在 `async ValkarnTask` 中 await `Awaitable` — 生成桥接适配器 |
| TT016 | 警告 | 没有 await 表达式的异步方法 |
| TT017 | 警告 | `ValkarnTask<T>` 上的 `[FireAndForget]` — 丢弃返回值 |

---

## 池管理

每个异步方法运行器通过 `ValkarnPool<T>` 进行池化：

- **主线程** — 无锁栈，零原子操作
- **后台线程** — 带CAS操作的 Treiber 无锁栈
- **基于帧的修剪** — 每300帧（60fps约5秒），多余对象被逐步释放
- **永不收缩**至可配置最小值以下（默认：8）

运行时监控：

```csharp
foreach (var (type, size, maxSize) in ValkarnTask.GetPoolInfo())
    Debug.Log($"{type}: {size}/{maxSize}");
```

---

## ValkarnTaskSettings

通过 `ScriptableObject` 配置（`Assets > Create > Valkarn > Tasks > Task Settings`，放置在 `Resources/`）：

| 设置 | 默认值 | 描述 |
|---------|---------|-------------|
| `DefaultMaxPoolSize` | 256 | 每个池类型的最大项目数 |
| `MinPoolSize` | 8 | 永不修剪至此值以下 |
| `TrimCheckInterval` | 300 | 修剪检查之间的帧数 |
| `TrimHysteresisCount` | 2 | 修剪前的连续检查次数 |
| `TrimReleaseRatio` | 0.25 | 每次循环释放的多余部分比例 |
| `EnableAutoCancel` | true | 自动将 MonoBehaviour 任务绑定到 destroyCancellationToken |
| `LogUnobservedCancellations` | false | 将未观察到的取消记录为警告 |
| `MaxExceptionLogsPerFrame` | 10 | 每帧异常日志上限 |

---

## 错误处理

```csharp
// 未观察到的异常 — 在池归还时确定性触发
ValkarnTask.UnobservedException += ex =>
    Debug.LogError($"Unobserved: {ex}");
```

- `WhenAll` — 抛出第一个异常，将额外异常路由到 `UnobservedException`
- `WhenAny` — 抛出胜者的异常；失败者的错误发送到 `UnobservedException`
- 生命周期取消 — `OperationCanceledException` 默认被抑制（可配置）

### 工厂方法

```csharp
ValkarnTask.CompletedTask          // void，零分配
ValkarnTask.FromResult<T>(value)   // 类型化，零分配
ValkarnTask.FromException(ex)      // 已出错
ValkarnTask.FromException<T>(ex)
ValkarnTask.FromCanceled()         // 已取消
ValkarnTask.FromCanceled<T>()
ValkarnTask.Never                  // 永不完成（WhenAny 的哨兵值）
```
