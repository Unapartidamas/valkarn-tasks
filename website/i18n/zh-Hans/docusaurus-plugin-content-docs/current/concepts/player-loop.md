---
sidebar_position: 1
title: Unity PlayerLoop 集成
---

# Unity PlayerLoop 集成

ValkarnTasks 不使用线程或后台调度器来恢复你的 `async` 方法。一切都在 Unity 主线程上运行，由 Unity 自身的 PlayerLoop 系统驱动。理解这一工作原理将帮助你为自己的用例选择正确的时机，并准确推断 `await` 后代码何时恢复。

## Unity PlayerLoop 是什么

Unity 的 PlayerLoop 是驱动每一帧的内部引擎循环。它不是一个单一的 `Update()` 调用——而是一个按照固定顺序在每帧运行的层级阶段序列：

```
Initialization
EarlyUpdate
FixedUpdate      （如果物理步进，则重复执行）
PreUpdate
Update
PreLateUpdate
PostLateUpdate
TimeUpdate
```

这些顶层阶段中的每一个都包含 Unity（以及第三方包）插入的子系统，用于在特定时间点运行各自的逻辑。例如，`MonoBehaviour.Update()` 在 `Update` 阶段内运行，`MonoBehaviour.LateUpdate()` 在 `PreLateUpdate` 中运行。

由于 ValkarnTasks 直接钩入这个循环，`await VlkTask.Yield()` 不会暂停线程——它注册一个回调，Unity 在所选阶段的下一个 tick 时调用它，然后立即返回。

## 16 个 PlayerLoop 时机点

ValkarnTasks 在 16 个点注入：8 个 Unity PlayerLoop 阶段中每个阶段各注入**开始**和**结束**两个点。`Last` 变体附加在其父阶段子系统列表的末尾；普通变体则前插在头部。

| 值 | 枚举整数 | 父阶段 | 在父阶段中的位置 |
|---|---|---|---|
| `Initialization` | 0 | `UnityEngine.PlayerLoop.Initialization` | 第一个 |
| `LastInitialization` | 1 | `UnityEngine.PlayerLoop.Initialization` | 最后一个 |
| `EarlyUpdate` | 2 | `UnityEngine.PlayerLoop.EarlyUpdate` | 第一个 |
| `LastEarlyUpdate` | 3 | `UnityEngine.PlayerLoop.EarlyUpdate` | 最后一个 |
| `FixedUpdate` | 4 | `UnityEngine.PlayerLoop.FixedUpdate` | 第一个 |
| `LastFixedUpdate` | 5 | `UnityEngine.PlayerLoop.FixedUpdate` | 最后一个 |
| `PreUpdate` | 6 | `UnityEngine.PlayerLoop.PreUpdate` | 第一个 |
| `LastPreUpdate` | 7 | `UnityEngine.PlayerLoop.PreUpdate` | 最后一个 |
| `Update` | 8 | `UnityEngine.PlayerLoop.Update` | 第一个 |
| `LastUpdate` | 9 | `UnityEngine.PlayerLoop.Update` | 最后一个 |
| `PreLateUpdate` | 10 | `UnityEngine.PlayerLoop.PreLateUpdate` | 第一个 |
| `LastPreLateUpdate` | 11 | `UnityEngine.PlayerLoop.PreLateUpdate` | 最后一个 |
| `PostLateUpdate` | 12 | `UnityEngine.PlayerLoop.PostLateUpdate` | 第一个 |
| `LastPostLateUpdate` | 13 | `UnityEngine.PlayerLoop.PostLateUpdate` | 最后一个 |
| `TimeUpdate` | 14 | `UnityEngine.PlayerLoop.TimeUpdate` | 第一个 |
| `LastTimeUpdate` | 15 | `UnityEngine.PlayerLoop.TimeUpdate` | 最后一个 |

`Update`（值 8）是所有 ValkarnTasks 操作的**默认值**——`Yield()`、`Delay()`、`WaitUntil()`、`WaitWhile()`、`NextFrame()` 和 `DelayFrame()`。

## 标记结构体与幂等注入

为了注入 Unity 的 PlayerLoop，ValkarnTasks 为每个时机点创建一个 `PlayerLoopSystem`。每个系统由 `PlayerLoopHelper` 内部定义的唯一标记结构体标识：

```csharp
struct VlkTaskInitialization     { }
struct VlkTaskLastInitialization { }
struct VlkTaskEarlyUpdate        { }
struct VlkTaskLastEarlyUpdate    { }
struct VlkTaskFixedUpdate        { }
struct VlkTaskLastFixedUpdate    { }
struct VlkTaskPreUpdate          { }
struct VlkTaskLastPreUpdate      { }
struct VlkTaskUpdate             { }
struct VlkTaskLastUpdate         { }
struct VlkTaskPreLateUpdate      { }
struct VlkTaskLastPreLateUpdate  { }
struct VlkTaskPostLateUpdate     { }
struct VlkTaskLastPostLateUpdate { }
struct VlkTaskTimeUpdate         { }
struct VlkTaskLastTimeUpdate     { }
```

注入之前，`PlayerLoopHelper` 检查当前 PlayerLoop 树中是否已存在 `VlkTaskUpdate`。如果存在，则跳过注入。这使注入具有**幂等性**——多次调用 `Init()`（在编辑器中可能发生）不会导致重复注册系统。

注入始终通过 `PlayerLoop.GetCurrentPlayerLoop()` 读取**当前** PlayerLoop，而非 `GetDefaultPlayerLoop()`。这意味着其他包之前安装的系统都会被保留。

## ContinuationQueue——一次性回调

当你 `await VlkTask.Yield()`（或任何恰好挂起一个 tick 的操作）时，编译器生成的状态机在等待器上调用 `OnCompleted`。等待器调用 `PlayerLoopHelper.AddContinuation(timing, action, state)`，将回调加入该时机的 `ContinuationQueue`。

`ContinuationQueue` 使用**双缓冲**设计：

1. **活动缓冲**（`actionList`）：保存当前 drain 之前和期间入队的续体。
2. **等待缓冲**（`waitingList`）：捕获在活动缓冲 drain 期间入队的续体（即续体本身内部的重入入队）。
3. **跨线程 Treiber 栈**（`crossThreadHead`）：后台线程发布的续体通过无锁 compare-and-swap 落到这里。每次 drain 开始时，整个栈被原子性地取走并移入活动缓冲。

每个 PlayerLoop tick，`ContinuationQueue.Run()` 执行以下序列：

```
1. 将 crossThreadHead 引流到 actionList     （无锁原子取走，然后在 SpinLock 下复制）
2. 快照计数，设置 isDraining = true         （在 SpinLock 下）
3. 执行所有回调                             （锁外，清除引用以便 GC）
4. 交换 actionList ↔ waitingList            （在 SpinLock 下，设置 isDraining = false）
```

经过大约一两帧的预热后，内部数组稳定在高水位线，队列以**零分配**运行。

跨线程节点在有界无锁对象池（最多 1024 个节点）中被池化，以避免在每次后台线程入队时分配。

## PlayerLoopRunner——周期性项目

某些操作必须在每个 tick 上检查直到完成：`Delay()`、`DelayFrame()`、`WaitUntil()`、`WaitWhile()` 和 `NextFrame()`。这些操作实现了 `IPlayerLoopItem` 接口：

```csharp
internal interface IPlayerLoopItem
{
    // 返回 true 继续运行；返回 false 从 runner 中移除。
    bool MoveNext();
}
```

项目通过 `PlayerLoopHelper.AddAction(timing, item)` 添加到 `PlayerLoopRunner`。每个 tick，`PlayerLoopRunner.Run()` 迭代所有已注册项目并对每个项目调用 `MoveNext()`。返回 `false` 的项目通过**原地压缩**移除——数组在一次遍历中被压缩，保留插入顺序。在 `Run()` 调用期间添加的项目会被安全追加，并在下一个 tick 被处理。

```
Tick N：
  ContinuationQueue.Run()   → 恢复所有一次性 await
  PlayerLoopRunner.Run()    → tick 所有周期性项目（Delay、WaitUntil 等）
```

两者在 16 个时机点的每一个上都运行，按引擎调用它们的顺序。

`Update` 时机还跟踪全局帧计数，并定期触发对象池裁剪（每 `VlkTask.TrimCheckInterval` 帧）。

## 初始化——RuntimeInitializeOnLoadMethod

ValkarnTasks 在 `RuntimeInitializeLoadType.SubsystemRegistration` 初始化，这是 Unity 提供的最早钩子，在任何场景对象的 `Awake()` 之前触发：

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void Init()
{
    // 1. 捕获主线程 ID 用于线程安全检查
    // 2. 为所有 16 个时机点创建 ContinuationQueue 和 PlayerLoopRunner
    // 3. 注入 PlayerLoop（幂等）
    // 4. 注册播放模式状态变更回调（仅编辑器）
}
```

主线程 ID 在此时被捕获，并与所有对象池和队列子系统共享，使它们能够区分主线程入队（SpinLock 路径）和跨线程入队（Treiber 栈路径）。

## Domain Reload 处理（编辑器）

在 Unity 编辑器中，进入或退出播放模式会触发 domain reload。上一个播放会话的静态状态否则会持久存在并导致悬空引用。

ValkarnTasks 通过在 `Init()` 期间注册的 `EditorApplication.playModeStateChanged` 回调来处理这个问题。当状态转换到 `ExitingPlayMode` 或 `EnteredEditMode` 时，执行以下清理：

```csharp
// 重置所有队列和 runner
for (int i = 0; i < 16; i++)
{
    s_continuationQueues[i] = null;
    s_playerLoopRunners[i] = null;
}

// 重置其他子系统
VlkTask.ResetStatics();
TimeProvider.ResetToDefault();   // 还原为 UnityTimeProvider
PoolRegistry.Clear();
ContinuationQueue.ContinuationNode.ResetPool();
```

当随后再次进入播放模式时，`Init()` 通过 `RuntimeInitializeOnLoadMethod` 触发，分配新的队列和 runner。PlayerLoop 注入检查（`HasVlkTaskSystems`）防止如果 domain 未完全卸载时标记系统被第二次插入。

## 选择正确的时机

大多数代码应使用默认的 `Update` 时机。只有在有特定原因时才使用其他时机。

| 时机 | 使用场景 |
|---|---|
| `Initialization` / `LastInitialization` | 非常早期的帧设置；游戏代码中很少需要 |
| `EarlyUpdate` / `LastEarlyUpdate` | 输入采样；在物理和 `Update` 之前运行 |
| `FixedUpdate` / `LastFixedUpdate` | 物理同步逻辑；匹配 `MonoBehaviour.FixedUpdate` 节奏 |
| `PreUpdate` / `LastPreUpdate` | 在 Unity 主更新分发之前；适用于自定义调度器预处理 |
| **`Update`**（默认） | **标准游戏逻辑；匹配 `MonoBehaviour.Update`** |
| `LastUpdate` | 必须在所有 `MonoBehaviour.Update` 调用之后运行的逻辑 |
| `PreLateUpdate` / `LastPreLateUpdate` | 匹配 `MonoBehaviour.LateUpdate`；相机和 Transform 跟随 |
| `PostLateUpdate` / `LastPostLateUpdate` | 渲染提交后；UI 最终化、截图捕获 |
| `TimeUpdate` / `LastTimeUpdate` | 时间值同步；极少需要 |

```csharp
// 在下一个 Update tick 恢复（默认）
await VlkTask.Yield();

// 在下一个 FixedUpdate 开始时恢复（物理安全）
await VlkTask.Yield(PlayerLoopTiming.FixedUpdate);

// 等待 2 秒，使用非缩放时间，在 LateUpdate 中检查
await VlkTask.Delay(
    TimeSpan.FromSeconds(2),
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.PreLateUpdate);

// 等待条件为真，在所有 LateUpdate 调用之后检查
await VlkTask.WaitUntil(() => _ready, PlayerLoopTiming.LastPreLateUpdate);
```
