---
sidebar_position: 1
title: Burst 与 ECS 集成
---

# Burst 与 ECS 集成

Valkarn Tasks 包含与 Unity Burst 编译器、Unity Collections 和 Entities（ECS）包的可选集成。所有这些功能都是条件编译的——只有在所需包存在且设置了相应的脚本定义符号时才会激活。

## 系统要求

| 功能 | 所需包 | 脚本定义 |
|---|---|---|
| `NativeTimerHeap`、`NativeScheduler`、`BurstSchedulerRunner` | Unity Burst 1.8+、Unity Collections 2.0+ | `VTASKS_HAS_BURST` 和 `VTASKS_HAS_COLLECTIONS` |
| `AsyncSystemUtilities` | Unity Entities 1.0+ | `VTASKS_HAS_ENTITIES` |

所有 Burst/ECS 源文件都用与这些定义匹配的 `#if` 守卫包裹。除非这些定义存在，否则这些文件中的任何内容都不会编译或链接。

### 设置

1. 通过 Unity Package Manager 安装所需包：
   - `com.unity.burst` 1.8 或更高版本
   - `com.unity.collections` 2.0 或更高版本
   - `com.unity.entities` 1.0 或更高版本（仅用于 ECS 工具）

2. 在 **Project Settings > Player > Scripting Define Symbols** 中添加脚本定义符号：
   ```
   VTASKS_HAS_BURST;VTASKS_HAS_COLLECTIONS;VTASKS_HAS_ENTITIES
   ```
   你只需要为已安装的包添加定义。

---

## NativeTimerHeap

**命名空间：** `UnaPartidaMas.Valkarn.Tasks.Burst`
**守卫：** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeTimerHeap` 是一个 Burst 兼容的二进制最小堆，用于调度计时器。它按截止时间顺序存储 `TimerEntry` 值，每次插入和每次过期计时器移除的时间复杂度为 O(log n)。

### 关键类型

```csharp
// 存储在堆中的条目，按 Deadline 排序。
public struct TimerEntry : IComparable<TimerEntry>
{
    public long Deadline;
    public int  Id;
}

public struct NativeTimerHeap : IDisposable
{
    public bool IsCreated { get; }
    public int  Count     { get; }

    // 创建堆。长生命周期堆使用 Allocator.Persistent。
    public NativeTimerHeap(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // 插入新计时器。返回计时器 ID（用于识别回调）。
    // 截止时间和传给 DrainExpired 的值必须使用相同单位
    // （BurstSchedulerRunner 通过 Time.realtimeSinceStartupAsDouble 使用 DateTime 刻度）。
    [BurstCompile]
    public int Schedule(long deadline);

    // 移除并追加所有 Deadline <= currentTimestamp 的计时器 ID。
    // 返回排干的计时器数量。
    [BurstCompile]
    public int DrainExpired(long currentTimestamp, NativeList<int> expiredIds);

    public void Dispose();
}
```

`NativeTimerHeap` 是一个非托管结构体。它不能存储托管委托——ID 在主线程上的 `BurstSchedulerRunner` 字典中与托管回调匹配。

---

## NativeScheduler

**命名空间：** `UnaPartidaMas.Valkarn.Tasks.Burst`
**守卫：** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeScheduler` 是一个由 `NativeQueue<ScheduledWork>` 支撑的 Burst 兼容工作队列。Burst 编译的作业将工作项入队；主线程每帧将其排干。

### 关键类型

```csharp
public enum WorkType : byte
{
    TimerExpired  = 0,
    JobCompleted  = 1,
    Custom        = 2
}

public struct ScheduledWork
{
    public int      Id;
    public WorkType Type;
    public long     Payload;
}

public struct NativeScheduler : IDisposable
{
    public bool IsCreated { get; }

    public NativeScheduler(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // 从 Burst 编译的作业中将工作项入队。
    [BurstCompile]
    public void Enqueue(ScheduledWork work);

    // 将所有待处理工作排入 `results`。仅从主线程调用。
    // 返回排干的条目数。
    public int Drain(NativeList<ScheduledWork> results);

    // 返回适用于 IJobParallelFor 作业的并行写入器。
    public NativeQueue<ScheduledWork>.ParallelWriter AsParallelWriter();

    public void Dispose();
}
```

队列是 Burst 世界与托管世界之间的交叉点。`Enqueue` 是 Burst 可调用的；`Drain` 仅限主线程。

---

## BurstSchedulerRunner

**命名空间：** `UnaPartidaMas.Valkarn.Tasks.Burst`
**守卫：** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`BurstSchedulerRunner` 是原生调度器/计时器堆与游戏其余部分之间的托管桥接。它实现了 `IPlayerLoopItem`，所以 Unity 在注册的时机每帧调用一次 `MoveNext()`。每帧它：

1. 排干 `NativeScheduler` 并触发任何按工作 ID 匹配的已注册托管回调。
2. 排干 `NativeTimerHeap` 中已过期的计时器并触发其托管回调。

回调抛出的异常被转发到 `ValkarnTask.PublishUnobservedException`，而不是通过 PlayerLoop 向上传播。

### API

```csharp
public sealed class BurstSchedulerRunner : IPlayerLoopItem, IDisposable
{
    // 创建一个 runner 并在 PlayerLoop 上注册它。返回实例。
    // 不再需要时释放返回的 runner。
    public static BurstSchedulerRunner Create(
        PlayerLoopTiming timing       = PlayerLoopTiming.Update,
        int initialCapacity           = 64);

    // 直接访问 NativeScheduler 以从 Burst 作业入队。
    public NativeScheduler Scheduler { get; }

    // 调度托管计时器回调。必须从主线程调用。
    // 返回计时器 ID（仅用于识别；没有取消 API）。
    public int ScheduleTimer(TimeSpan delay, Action callback);

    // 将托管回调与 Burst 作业入队的工作 ID 关联。
    // 必须在作业完成的帧之前或期间从主线程调用。
    public void RegisterCallback(int workId, Action callback);

    // 释放所有原生容器并从 PlayerLoop 取消注册。
    public void Dispose();
}
```

### BurstSchedulerRunner 与默认调度器的区别

默认 Valkarn Tasks 调度器通过 `PlayerLoopHelper` 直接与 `async/await` 状态机集成并管理续体分发。`BurstSchedulerRunner` 添加了一条专门用于从 Burst 编译作业发出信号的独立通道：

| | 默认调度器 | BurstSchedulerRunner |
|---|---|---|
| 续体类型 | 通过 `ISource` 的托管 `Action` | 按 ID 注册的托管 `Action` |
| 信号来源 | C# 等待器模式 | 非托管 `NativeScheduler.Enqueue` |
| 计时器来源 | `ValkarnTask.Delay`（托管） | `NativeTimerHeap`（非托管） |
| 线程安全 | 主线程续体 | 入队是 Burst 安全的；排干仅限主线程 |

### 使用模式

```csharp
// 1. 一次性创建 runner（例如，在引导 MonoBehaviour 或 ISystem.OnCreate 中）。
var runner = BurstSchedulerRunner.Create(PlayerLoopTiming.Update, initialCapacity: 128);

// 2. 调度计时器回调（主线程）。
runner.ScheduleTimer(TimeSpan.FromSeconds(2.0), () =>
{
    Debug.Log("两秒已过（非托管计时器）。");
});

// 3. 从 Burst 作业入队工作项。
//    NativeScheduler.ParallelWriter 可安全用于 IJobParallelFor。
var writer = runner.Scheduler.AsParallelWriter();
// 在 Execute(int index) 内部：
writer.Enqueue(new ScheduledWork { Id = myWorkId, Type = WorkType.Custom });

// 4. 在作业完成之前在主线程上注册托管回调。
runner.RegisterCallback(myWorkId, () =>
{
    Debug.Log($"收到工作 {myWorkId} 的作业完成信号。");
});

// 5. 完成后释放 runner（例如，在 OnDestroy 或 domain reload 时）。
runner.Dispose();
```

---

## AsyncSystemUtilities

**命名空间：** `UnaPartidaMas.Valkarn.Tasks.ECS`
**守卫：** `#if VTASKS_HAS_ENTITIES`

`AsyncSystemUtilities` 为编写异步 ECS 系统提供了两个扩展助手。

### GetWorldCancellationToken

```csharp
public static CancellationToken GetWorldCancellationToken(
    this World world,
    PlayerLoopTiming timing = PlayerLoopTiming.Update)
```

返回一个在给定 `World` 被销毁时自动取消的 `CancellationToken`。内部启动一个即发即弃的 `ValkarnTask`，在 `world.IsCreated` 为 true 时每帧调用 `ValkarnTask.Yield(timing)`，然后在循环退出时取消一个 `CancellationTokenSource`。

如果你调用此方法时世界已被销毁，它返回一个已处于取消状态的 token。

将此 token 传递给从系统启动的每个异步方法，以便在世界消失时自动停止进行中的工作。

### SafeEntityExists

```csharp
public static bool SafeEntityExists(this EntityManager entityManager, Entity entity)
```

调用 `entityManager.Exists(entity)` 并在抛出 `ObjectDisposedException` 时返回 `false`。当世界被销毁后访问 `EntityManager` 时可能发生这种情况，这是跨帧边界存活的异步代码中的真实竞态条件。

在每个 `await` 点之后，在向实体写回之前使用此方法。

---

## 工作示例：异步 ECS 系统

以下示例来自 `Samples~/ECS/AsyncLoadSystem.cs`，演示了从 `ISystem` 进行一次性异步初始化的规范模式。

```csharp
#if VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Entities;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.ECS;

public partial struct AsyncLoadSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // 获取绑定到此 World 生命周期的取消 token。
        // 如果 World 被销毁，所有以此 token 启动的异步工作
        // 都将自动取消。
        var worldCt = state.World.GetWorldCancellationToken();

        // 启动异步初始化并忘记任务。
        // Forget() 将任何未处理的异常路由到 ValkarnTask.PublishUnobservedException。
        InitializeAsync(state.WorldUnmanaged, worldCt).Forget();
    }

    public void OnUpdate(ref SystemState state) { }
    public void OnDestroy(ref SystemState state) { }

    static async ValkarnTask InitializeAsync(WorldUnmanaged world, CancellationToken ct)
    {
        // 阶段 1：在后台线程上加载数据。
        // RunOnThreadPool 切换到工作线程，运行委托，
        // 并自动返回主线程。
        var configData = await ValkarnTask.RunOnThreadPool(
            () => LoadFromDisk(),
            cancellationToken: ct);

        // 阶段 2：在主线程上应用结果。
        // 检查取消，以防 World 在加载期间被销毁。
        ct.ThrowIfCancellationRequested();
        ApplyConfiguration(world, configData);
    }

    static ConfigData LoadFromDisk()
    {
        // 纯 C#——这里不允许 Unity 或 ECS API 调用。
        return new ConfigData { MaxEnemies = 100, SpawnRadius = 50f };
    }

    static void ApplyConfiguration(WorldUnmanaged world, ConfigData data)
    {
        // 安全：我们在主线程上。
        UnityEngine.Debug.Log($"已加载配置：MaxEnemies={data.MaxEnemies}");
    }

    struct ConfigData
    {
        public int   MaxEnemies;
        public float SpawnRadius;
    }
}
#endif
```

AI 节流示例（`Samples~/ECS/AISystemExample.cs`）基于此模式构建，并添加了 `AsyncThrottle` 来限制同时进行中的异步任务数量。有关该模式的详细信息，请参阅节流文档。

---

## 限制

以下约束适用于所有 Burst/ECS 异步代码。违反它们将产生编辑器错误、作业安全违规或静默数据损坏。

### 在 Burst 编译的代码中

- **禁止托管类型。** Burst 无法编译分配、访问或引用托管对象（类、委托、数组、字符串、`List<T>` 等）的代码。只允许可平化结构体和原生容器。
- **禁止异常。** Burst 不支持 `try`/`catch`/`throw`。使用返回码或标志来传递错误。
- **禁止 `async`/`await`。** C# 异步状态机是托管的，无法被 Burst 编译。`NativeScheduler` 和 `NativeTimerHeap` 提供了一个向托管续体发出信号的旁路通道，但续体本身在主线程上运行。
- **禁止静态可变托管状态。** Burst 作业可以读取静态只读字段，但不能写入托管静态变量。

### 在 ECS 系统的 await 点之间

- **实体生命周期。** 实体可能在异步方法挂起时被销毁。在每个 `await` 点之后，在写回之前始终调用 `entityManager.SafeEntityExists(entity)`。
- **ComponentLookup 失效。** `ComponentLookup`、`RefRW` 和其他块指针类型在结构性变化后失效，这可能在任何帧发生。不要在 `await` 点之间缓存这些。在恢复后从 `SystemState` 重新获取，或直接使用 `EntityManager`。
- **`ref` 参数。** 异步方法不能有 `ref`、`in` 或 `out` 参数（C# 错误 CS1988）。在同步的 `OnUpdate` 方法中同步提取所有 ECS 数据，并按值传递给异步方法。
- **异步方法中的 `SystemAPI`。** `SystemAPI` 是源码生成的，仅在 partial `ISystem` 方法中有效。它在 `async` 方法中不可用。在第一个 `await` 之前执行所有 `SystemAPI` 查询。
- **线程安全。** `EntityManager`、`ComponentLookup` 和结构性变化仅限主线程。仅对没有 Unity 或 ECS API 调用的纯 C# 计算使用 `ValkarnTask.RunOnThreadPool`。
