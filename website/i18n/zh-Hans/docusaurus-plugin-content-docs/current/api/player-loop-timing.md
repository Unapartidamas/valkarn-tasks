---
sidebar_position: 2
title: PlayerLoopTiming
---

# PlayerLoopTiming

```csharp
public enum PlayerLoopTiming : byte
```

命名空间：`UnaPartidaMas.Valkarn.Tasks`

指定 ValkarnTasks 操作在挂起后使用哪个 Unity PlayerLoop 阶段来恢复。传入 `PlayerLoopTiming` 值控制你的 `await` 续体**在帧中何时**运行，以及延迟和等待条件等周期性项目**何时**被检查。

枚举值与 UniTask 的 `PlayerLoopTiming` 枚举完全匹配，这简化了从 UniTask 的迁移。

---

## 默认值

`PlayerLoopTiming.Update`（值 `8`）是所有操作的默认值。如果你不传递 timing 参数，将使用 `Update`。

---

## 值

### Initialization

```
值：0
父阶段：UnityEngine.PlayerLoop.Initialization
位置：该阶段中的第一个子系统
```

在 Initialization 阶段最开始运行，在 Unity 自身的初始化子系统之前。每帧触发一次，在 `EarlyUpdate` 之前。对游戏代码很少有用，但对于必须在帧中其他任何东西访问之前读取或设置状态的系统可能有用。

---

### LastInitialization

```
值：1
父阶段：UnityEngine.PlayerLoop.Initialization
位置：该阶段中的最后一个子系统
```

在 Initialization 阶段结束时运行，在 Unity 内置的初始化子系统之后。如果你需要初始化阶段的时机但希望 Unity 自身的系统先运行，优先选择此选项而非 `Initialization`。

---

### EarlyUpdate

```
值：2
父阶段：UnityEngine.PlayerLoop.EarlyUpdate
位置：该阶段中的第一个子系统
```

在 EarlyUpdate 开始时运行，在 Unity 处理输入事件和物理模拟步骤之前。这是帧中上一帧输入数据可用的最早时机。适用于必须在任何游戏代码运行之前采样输入的系统。

---

### LastEarlyUpdate

```
值：3
父阶段：UnityEngine.PlayerLoop.EarlyUpdate
位置：该阶段中的最后一个子系统
```

在 EarlyUpdate 结束时运行，在 Unity 自身的早期更新子系统完成之后。

---

### FixedUpdate

```
值：4
父阶段：UnityEngine.PlayerLoop.FixedUpdate
位置：该阶段中的第一个子系统
```

在每个 FixedUpdate 步骤开始时运行。这对应于 `MonoBehaviour.FixedUpdate()` 的时机。Unity 根据物理时间步与帧增量时间的对齐方式，每渲染帧运行零个或多个 FixedUpdate 步骤。

对必须与物理模拟保持同步的任何操作使用此时机：读取 `Rigidbody` 速度、施加力，或等待物理确定的步骤数。

```csharp
// 等待 10 个物理步骤
await VlkTask.DelayFrame(10, PlayerLoopTiming.FixedUpdate, ct);

// 等待物理条件为真，每个物理步骤检查一次
await VlkTask.WaitUntil(() => rb.velocity.magnitude < 0.1f,
    PlayerLoopTiming.FixedUpdate, ct);
```

---

### LastFixedUpdate

```
值：5
父阶段：UnityEngine.PlayerLoop.FixedUpdate
位置：该阶段中的最后一个子系统
```

在 FixedUpdate 阶段结束时运行，在 Unity 的物理子系统（包括 `Physics.Simulate`）完成之后。用于在模拟推进后读取物理结果。

---

### PreUpdate

```
值：6
父阶段：UnityEngine.PlayerLoop.PreUpdate
位置：该阶段中的第一个子系统
```

在 PreUpdate 阶段开始时运行，该阶段在 FixedUpdate 之后、Update 之前运行。Unity 将 PreUpdate 用于风区更新和网络事件处理等任务。

---

### LastPreUpdate

```
值：7
父阶段：UnityEngine.PlayerLoop.PreUpdate
位置：该阶段中的最后一个子系统
```

在 PreUpdate 结束时运行。

---

### Update

```
值：8（默认）
父阶段：UnityEngine.PlayerLoop.Update
位置：该阶段中的第一个子系统
```

所有 ValkarnTasks 操作的**默认时机**。在 Update 阶段开始时运行，`MonoBehaviour.Update()` 就在此处触发。这是绝大多数游戏代码的正确选择。

```csharp
// 以下所有操作默认使用 Update
await VlkTask.Yield();
await VlkTask.Delay(1000, ct);
await VlkTask.WaitUntil(() => _ready, ct: ct);
await VlkTask.NextFrame(ct: ct);
await VlkTask.DelayFrame(3, ct: ct);

// 显式指定——与上面相同
await VlkTask.Yield(PlayerLoopTiming.Update);
```

---

### LastUpdate

```
值：9
父阶段：UnityEngine.PlayerLoop.Update
位置：该阶段中的最后一个子系统
```

在 Update 阶段结束时运行，在所有 `MonoBehaviour.Update()` 调用和任何其他 Update 子系统完成之后。当你的续体必须观察其他脚本的 `Update()` 方法的结果时使用此时机——例如，轮询另一个脚本在 `Update` 期间写入的值。

```csharp
// 在本帧所有 MonoBehaviour.Update() 调用之后恢复
await VlkTask.Yield(PlayerLoopTiming.LastUpdate);
```

---

### PreLateUpdate

```
值：10
父阶段：UnityEngine.PlayerLoop.PreLateUpdate
位置：该阶段中的第一个子系统
```

在 PreLateUpdate 阶段开始时运行，`MonoBehaviour.LateUpdate()` 就在此处触发。用于相机跟随、变换调整，以及任何应该响应 `Update` 期间设置的最终位置的操作。

```csharp
// 在与 MonoBehaviour.LateUpdate() 相同的时机恢复
await VlkTask.Yield(PlayerLoopTiming.PreLateUpdate);
```

---

### LastPreLateUpdate

```
值：11
父阶段：UnityEngine.PlayerLoop.PreLateUpdate
位置：该阶段中的最后一个子系统
```

在 PreLateUpdate 阶段结束时运行，在所有 `MonoBehaviour.LateUpdate()` 调用之后。当你需要读取脚本在其 `LateUpdate` 期间设置的值时使用此时机。

---

### PostLateUpdate

```
值：12
父阶段：UnityEngine.PlayerLoop.PostLateUpdate
位置：该阶段中的第一个子系统
```

在 PostLateUpdate 开始时运行。此阶段在帧的渲染已提交后触发。Unity 将其用于呈现帧缓冲和清理渲染状态等任务。`Camera.onPostRender` 回调也在此处触发。

对帧末操作使用此时机：截图捕获、流式卸载关卡，或必须在场景完全渲染后、下一帧开始前发生的任何工作。

---

### LastPostLateUpdate

```
值：13
父阶段：UnityEngine.PlayerLoop.PostLateUpdate
位置：该阶段中的最后一个子系统
```

在标准帧循环中 Unity 重置帧局部状态并开始下一帧之前的最后时机。就时机而言，这是一帧的绝对末尾。

---

### TimeUpdate

```
值：14
父阶段：UnityEngine.PlayerLoop.TimeUpdate
位置：该阶段中的第一个子系统
```

在 TimeUpdate 阶段开始时运行。Unity 使用此阶段推进与时间相关的状态（如 `Time.time`）。在较新的 Unity 版本中，此时机在 `EarlyUpdate` 之前触发。

游戏代码中很少需要此时机。它主要供必须在任何其他系统读取新时间值之前拦截或观察时间推进的系统使用。

---

### LastTimeUpdate

```
值：15
父阶段：UnityEngine.PlayerLoop.TimeUpdate
位置：该阶段中的最后一个子系统
```

在 TimeUpdate 阶段结束时运行，在 Unity 的时间相关子系统完成之后。

---

## 摘要表

| 值 | 名称 | Unity 阶段 | 位置 | 可对比的 MonoBehaviour 回调 |
|---|---|---|---|---|
| 0 | `Initialization` | `Initialization` | 第一个 | — |
| 1 | `LastInitialization` | `Initialization` | 最后一个 | — |
| 2 | `EarlyUpdate` | `EarlyUpdate` | 第一个 | — |
| 3 | `LastEarlyUpdate` | `EarlyUpdate` | 最后一个 | — |
| 4 | `FixedUpdate` | `FixedUpdate` | 第一个 | `FixedUpdate()` |
| 5 | `LastFixedUpdate` | `FixedUpdate` | 最后一个 | `FixedUpdate()` 之后 |
| 6 | `PreUpdate` | `PreUpdate` | 第一个 | — |
| 7 | `LastPreUpdate` | `PreUpdate` | 最后一个 | — |
| **8** | **`Update`**（默认） | **`Update`** | **第一个** | **`Update()`** |
| 9 | `LastUpdate` | `Update` | 最后一个 | 所有 `Update()` 之后 |
| 10 | `PreLateUpdate` | `PreLateUpdate` | 第一个 | `LateUpdate()` |
| 11 | `LastPreLateUpdate` | `PreLateUpdate` | 最后一个 | 所有 `LateUpdate()` 之后 |
| 12 | `PostLateUpdate` | `PostLateUpdate` | 第一个 | 渲染之后 |
| 13 | `LastPostLateUpdate` | `PostLateUpdate` | 最后一个 | 帧末 |
| 14 | `TimeUpdate` | `TimeUpdate` | 第一个 | — |
| 15 | `LastTimeUpdate` | `TimeUpdate` | 最后一个 | — |

---

## 接受 PlayerLoopTiming 的 API

所有基于时间和基于条件的 ValkarnTasks API 都接受一个可选的 `PlayerLoopTiming` 参数。默认值始终为 `Update`。

| 方法 | 签名 |
|---|---|
| `VlkTask.Yield` | `Yield(PlayerLoopTiming timing = Update)` |
| `VlkTask.NextFrame` | `NextFrame(PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `VlkTask.DelayFrame` | `DelayFrame(int frameCount, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `VlkTask.Delay(int)` | `Delay(int ms, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `VlkTask.Delay(TimeSpan)` | `Delay(TimeSpan delay, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `VlkTask.WaitUntil` | `WaitUntil(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `VlkTask.WaitWhile` | `WaitWhile(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |

---

## 代码示例

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using System.Threading;

// 让出到下一个 Update tick（默认）
await VlkTask.Yield();

// 让出到下一个 FixedUpdate tick——在物理驱动的代码中使用
await VlkTask.Yield(PlayerLoopTiming.FixedUpdate);

// 使用缩放 deltaTime 等待 500 ms，每个 Update 检查一次
await VlkTask.Delay(500, ct: destroyCancellationToken);

// 使用非缩放 deltaTime 等待 500 ms——不受 Time.timeScale 影响
await VlkTask.Delay(
    500,
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// 使用真实墙钟时间等待 500 ms（基于 Stopwatch）
await VlkTask.Delay(
    500,
    DelayType.Realtime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// 等待标志被设置，在 LateUpdate 中检查
bool _isReady;
await VlkTask.WaitUntil(() => _isReady, PlayerLoopTiming.PreLateUpdate, destroyCancellationToken);

// 在加载时等待，在帧的最末尾检查
await VlkTask.WaitWhile(() => _loading, PlayerLoopTiming.LastPostLateUpdate, destroyCancellationToken);

// 精确跳过 5 个物理步骤
await VlkTask.DelayFrame(5, PlayerLoopTiming.FixedUpdate, destroyCancellationToken);

// 推进整整一个渲染帧
await VlkTask.NextFrame(PlayerLoopTiming.Update, destroyCancellationToken);
```
