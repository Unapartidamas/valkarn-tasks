---
sidebar_position: 4
title: 设置与结果
---

# ValkarnTaskSettings

`ValkarnTaskSettings` 是一个 Unity `ScriptableObject`，用于控制 Valkarn Tasks 的运行时行为——主要是回收异步状态机和 promise 对象以在游戏中避免垃圾的对象池。

---

## 创建设置资产

1. 在 Project 窗口中，右键单击 `Resources` 文件夹（如果没有则创建一个）。
2. 选择 **Assets > Create > Valkarn Tasks > Task Settings**。
3. 将文件命名为 `ValkarnTaskSettings` 并放在 `Resources` 文件夹内。

文件必须恰好命名为 `ValkarnTaskSettings`，且必须位于项目中任何位置的名为 `Resources` 的文件夹内。资产在运行时通过 `Resources.Load<ValkarnTaskSettings>("ValkarnTaskSettings")` 加载。

如果未找到资产，所有设置都回退到内置默认值。库在没有资产的情况下也能正确工作——只有在你想更改默认值时才需要创建它。

---

## 在运行时访问设置

在 Unity 构建中，设置通过缓存的单例从资产读取：

```csharp
ValkarnTaskSettings settings = ValkarnTaskSettings.Instance;
```

常用的对象池参数也直接在 `ValkarnTask` 上公开以方便使用：

```csharp
int max      = ValkarnTask.DefaultMaxPoolSize;   // 从 ValkarnTaskSettings.Instance 读取
int min      = ValkarnTask.MinPoolSize;
int interval = ValkarnTask.TrimCheckInterval;
```

在非 Unity 构建（测试、独立 .NET）中，`ValkarnTaskSettings` 是一个带有可变属性的静态类而不是 `ScriptableObject`。相同的属性名适用，可以直接写入：

```csharp
// 仅适用于非 Unity / 测试构建
ValkarnTask.DefaultMaxPoolSize = 512;
ValkarnTask.TrimCheckInterval  = 600;
ValkarnTask.MinPoolSize        = 16;
```

---

## 可配置属性

### 对象池配置

#### DefaultMaxPoolSize

| | |
|-|-|
| 类型 | `int` |
| 默认值 | `256` |
| 有效范围 | `8` – `1024` |
| Inspector 提示 | "每种对象池类型的最大条目数。多余的条目将被裁剪。" |

每种类型的对象池中保留的最大对象数。每个不同的泛型实例化（例如 `PooledPromise<int>`、`PooledPromise<string>`）都有自己的对象池，以此值为上限。

当一个任务完成且其内部对象归还到对象池时，如果对象池已持有 `DefaultMaxPoolSize` 个条目，则归还的对象被丢弃（符合 GC 条件）。这防止了在一次异步活动峰值后内存无限增长。

如果分析显示在持续高吞吐量异步工作负载中频繁 GC 分配，则增大此值。如果内存压力是一个问题且任务不经常被重用，则减小它。

#### MinPoolSize

| | |
|-|-|
| 类型 | `int` |
| 默认值 | `8` |
| 有效范围 | `1` – `64` |
| Inspector 提示 | "最小对象池大小——永远不会收缩到此值以下。" |

对象池裁剪过程永远不会将任何对象池减少到低于此计数。这保证了始终有一个预热的对象池基线可用，避免在安静期后（裁剪过程可能已释放一切）出现分配峰值。

#### TrimCheckInterval

| | |
|-|-|
| 类型 | `int` |
| 默认值 | `300` |
| 有效范围 | `30` – `1000` |
| Inspector 提示 | "两次裁剪检查之间的帧数。在 60fps 下，300 ≈ 5 秒。" |

两次对象池裁剪过程之间经过的帧数。裁剪过程遍历所有已注册的对象池，如果对象池持续过大，则释放多余的对象（超过 `MinPoolSize` 的对象）。

在 60fps 下，默认值 300 相当于检查间隔约 5 秒。如果你希望更积极、频繁地裁剪（以更频繁的裁剪工作为代价），请降低此值。如果裁剪过程在分析中出现为峰值，请提高它。

#### TrimHysteresisCount

| | |
|-|-|
| 类型 | `int` |
| 默认值 | `2` |
| 有效范围 | `1` – `10` |
| Inspector 提示 | "裁剪前连续超过阈值检查的次数。" |

只有在连续这么多次裁剪周期中都观察到对象池过大时，才会对其进行裁剪。这防止了抖动——如果游戏有一个短暂的峰值后跟着一个安静期，滞后计数为 2 意味着对象池在开始释放对象前能存活一个安静周期。

#### TrimReleaseRatio

| | |
|-|-|
| 类型 | `float` |
| 默认值 | `0.25` |
| 有效范围 | `0.1` – `1.0` |
| Inspector 提示 | "每次裁剪周期释放的多余部分（0.25 = 25%）。" |

当对象池被裁剪时，每个周期释放多余容量（`MinPoolSize` 以上的条目）的这一比例，而不是一次全部释放。值为 `0.25` 意味着每次裁剪过程移除 25% 的超额部分。这种渐进式释放避免了如果负载重新上升时可能导致分配爆发的对象池大小突然下降。

如果你希望每个周期立即释放所有多余部分，将此值设为 `1.0`。

---

### 生命周期

#### EnableAutoCancel

| | |
|-|-|
| 类型 | `bool` |
| 默认值 | `true` |
| Inspector 提示 | "自动将 MonoBehaviour 任务绑定到 destroyCancellationToken。" |

启用时，从 `MonoBehaviour` 启动的任务会自动链接到该 `MonoBehaviour` 的 `destroyCancellationToken`。如果 `MonoBehaviour` 在任务运行时被销毁，任务将被取消而不是继续对已销毁的对象执行。

仅在你手动管理取消且不需要自动绑定时禁用此选项。

---

### 错误处理

#### LogUnobservedCancellations

| | |
|-|-|
| 类型 | `bool` |
| 默认值 | `false` |
| Inspector 提示 | "将未观察到的取消记录为警告。" |

默认情况下，被取消但从未被等待的任务（未观察到的取消）会被静默忽略。启用此选项可在发生这种情况时记录警告。在开发期间用于查找静默取消的即发即弃任务很有用。

无论此设置如何，未观察到的错误始终通过 `ValkarnTask.UnobservedException` 事件报告。

#### MaxExceptionLogsPerFrame

| | |
|-|-|
| 类型 | `int` |
| 默认值 | `10` |
| 有效范围 | `1` – `100` |
| Inspector 提示 | "每帧最大异常日志条数，防止日志泛滥。" |

限制单帧中发出的未观察到异常日志条目的数量。如果许多任务在同一帧中错误（例如，网络故障后），这可以防止控制台被数百条相同的堆栈跟踪淹没。

---

## 运行时对象池裁剪的工作原理

在每次 Unity 玩家循环更新时，`PlayerLoopHelper` 递增帧计数器。当计数器达到 `TrimCheckInterval` 时，它调用 `PoolRegistry.TrimAll(MinPoolSize)`。每个已注册的对象池检查是否连续 `TrimHysteresisCount` 次检查都超过容量。如果是，它释放其多余部分的 `TrimReleaseRatio`。

对象池在首次使用时自动注册。`ValkarnTask.GetPoolInfo()` 方法返回所有当前已注册对象池的快照，包含它们的类型、当前大小和最大大小——这是 Task Tracker 窗口显示的内容。

```
帧 0 ──────────────────────────────────────────────────────────
  玩家循环运行
  对象池 A：大小 200，最大 256——正常，不需要裁剪

帧 300 ────────────────────────────────────────────────────────
  TrimAll 触发
  对象池 A：大小 18，最大 256——超过 MinPoolSize(8)，但是首次多余检查
  A 的 hysteresisCount = 1（尚未达到阈值 2）

帧 600 ────────────────────────────────────────────────────────
  TrimAll 触发
  对象池 A：大小 18，最大 256——超过 MinPoolSize(8)，第二次多余检查
  A 的 hysteresisCount = 2——达到阈值！
  多余 = 18 - 8 = 10；释放 10 * 0.25 = 2 个对象
  对象池 A：大小 16
```

---

## Task Tracker 窗口

通过 **Window > Valkarn Tasks > Task Tracker** 打开。

Task Tracker 是一个仅限编辑器的窗口，在播放模式下显示实时对象池状态。它以可配置的间隔刷新（默认 0.5 秒，可通过工具栏上的滑块在 0.1 到 5 秒之间调整）。

### 对象池标签页

列出自上次 domain reload 以来所有活动的对象池类型，按当前大小排序（最大的在前）。每行显示：

| 列 | 描述 |
|--------|-------------|
| 类型 | 池化对象的类型名称，泛型参数已展开 |
| 大小 | 对象池中当前的对象数 |
| 最大值 | 此对象池的 `DefaultMaxPoolSize` 上限 |
| 使用率 | 以百分比显示 `Size / Max` 的进度条 |

如果还没有对象池被使用，显示消息"暂无活动对象池。对象池在首次使用时创建。"

### 配置标签页

显示从 `ValkarnTask.DefaultMaxPoolSize`、`ValkarnTask.TrimCheckInterval` 和 `ValkarnTask.MinPoolSize` 读取的三个实时对象池参数。值仅在播放模式下显示——在编辑模式下，一条说明指引你进入播放模式以查看实时值。

配置标签页还显示对 `ValkarnTaskSettings` 资产的引用（如果 `Resources` 中存在一个），你可以点击查看或修改它。如果未找到资产，会显示警告提示你创建一个。

---

## Result&lt;T&gt;

`Result<T>` 是一个判别联合结构体，用于在不抛出异常的情况下表示操作的结果。它是 `WhenAll` 组合器用于报告每个任务结果的返回类型，也可作为通用结果模式使用。

```csharp
public readonly struct Result<T>
{
    public bool IsSuccess  { get; }
    public bool IsFailure  { get; }   // 如果已错误或已取消则为 true
    public bool IsFaulted  { get; }
    public bool IsCanceled { get; }

    public ValkarnTask.Status Status { get; }
    public T Value    { get; }        // 如果不是成功则抛出
    public Exception Error { get; }   // 如果不是错误则为 null

    public static Result<T> Success(T value);
    public static Result<T> Failure(string error);    // 包装到 InvalidOperationException
    public static Result<T> Faulted(Exception error);
    public static Result<T> Canceled(OperationCanceledException oce = null);

    public static implicit operator bool(Result<T> r);  // 如果成功则为 true
}
```

非泛型的 `Result` 存在于 void 任务，形状相同但没有 `Value`。

### AsResult 扩展方法

将任何 `ValkarnTask` 或 `ValkarnTask<T>` 转换为 `Result`，而无需在你的代码中使用 `try/catch`：

```csharp
public static ValkarnTask<Result<T>> AsResult<T>(this ValkarnTask<T> task);
public static ValkarnTask<Result>    AsResult(this ValkarnTask task);
```

如果底层任务已同步完成（零分配快速路径），`AsResult` 立即返回，无需创建异步状态机。否则，它包装进一个异步方法，捕获 `OperationCanceledException` 和所有其他异常，将它们转换为适当的 `Result` 变体。

### 何时使用 Result&lt;T&gt;

在以下情况使用 `Result<T>` 而不是捕获异常：

- 你并行调用多个任务，希望获得每个任务的结果而不短路整个批次。
- 你希望以类型安全的方式表达可失败操作，而无需异常控制流。
- 你从一个调用者可能不想包装在 `try/catch` 中的方法返回。

```csharp
// 触发多个任务，无论单个失败都获取所有结果
Result<int>[] results = await ValkarnTask.WhenAll(
    FetchScoreAsync(playerA).AsResult(),
    FetchScoreAsync(playerB).AsResult(),
    FetchScoreAsync(playerC).AsResult()
);

foreach (var r in results)
{
    if (r.IsSuccess)
        Debug.Log($"分数: {r.Value}");
    else if (r.IsFaulted)
        Debug.LogError($"失败: {r.Error.Message}");
    else
        Debug.Log("已取消");
}
```

检查 `IsSuccess` 或使用隐式 `bool` 操作符是分支结果的首选方式：

```csharp
var result = await SomeOperationAsync().AsResult();

if (result)
{
    Use(result.Value);
}
```

当 `IsSuccess` 为 `false` 时访问 `result.Value` 会抛出 `InvalidOperationException`。

### 工厂方法

| 方法 | 设置的状态 | 设置的错误 |
|--------|-----------|-----------|
| `Result<T>.Success(value)` | `Succeeded` | 无 |
| `Result<T>.Failure(message)` | `Faulted` | `new InvalidOperationException(message)` |
| `Result<T>.Faulted(exception)` | `Faulted` | 提供的异常 |
| `Result<T>.Canceled(oce?)` | `Canceled` | 提供的 `OperationCanceledException`，或 `null` |

`Succeeded` 属性在 `Result` 和 `Result<T>` 上都存在，但被标记为 `[Obsolete]`——为了与 `IsFailure`、`IsFaulted` 和 `IsCanceled` 保持一致性，请改用 `IsSuccess`。
