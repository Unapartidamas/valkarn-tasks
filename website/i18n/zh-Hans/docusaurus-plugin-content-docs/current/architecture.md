---
sidebar_position: 5
title: 架构
---

# 架构

Valkarn Tasks 内部实现的技术概述。

---

## 高层结构

```
┌─────────────────────────────────────────────────────────────────┐
│                    编译时                                         │
│                                                                   │
│  ┌──────────────┐  ┌──────────────────┐  ┌───────────────────┐  │
│  │  生命周期     │  │  Awaitable       │  │  诊断             │  │
│  │  分析器       │  │  桥接生成        │  │  (TT001–TT017)    │  │
│  └──────────────┘  └──────────────────┘  └───────────────────┘  │
│                                                                   │
│  ┌──────────────┐  ┌──────────────────┐                          │
│  │  Job 桥接    │  │  组合器          │                          │
│  │  生成        │  │  生成            │                          │
│  └──────────────┘  └──────────────────┘                          │
├─────────────────────────────────────────────────────────────────┤
│                    运行时                                         │
│                                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐  │
│  │ ValkarnTask │  │ Result<T>  │  │ ValkarnPool│  │ Completion│  │
│  │  struct    │  │  struct    │  │  bounded   │  │  Core<T>  │  │
│  └────────────┘  └────────────┘  └────────────┘  └───────────┘  │
│                                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐  │
│  │ PlayerLoop │  │Continuation│  │  Channels  │  │ TestClock │  │
│  │  Helper    │  │   Queue    │  │            │  │           │  │
│  └────────────┘  └────────────┘  └────────────┘  └───────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### 程序集布局

```
ValkarnTask.Runtime      — 随游戏一起发布
ValkarnTask.SourceGen    — 仅编译时（源生成器）
ValkarnTask.Analyzer     — 仅编译时（诊断＋代码修复）
ValkarnTask.Testing      — TestClock＋测试工具
```

---

## ValkarnTask 结构体

```csharp
[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask
{
    internal readonly ValkarnTask.ISource source;
    internal readonly ulong token; // packed: high 32 bits = generation, low 32 = slot index
}

[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
{
    internal readonly ValkarnTask.ISource<T> source;
    internal readonly T result;  // inline on sync fast path
    internal readonly ulong token;
}
```

**关键不变量：** `source == null` 表示任务同步完成且无错误——不涉及堆对象。`ValkarnTask.CompletedTask` 是 `default(ValkarnTask)`；`ValkarnTask.FromResult(value)` 内联存储结果。

### 世代令牌

```csharp
// 打包
ulong token = ((ulong)generation << 32) | slotIndex;

// 解包
uint slotIndex  = (uint)(token & 0xFFFFFFFF);
uint generation = (uint)(token >> 32);
```

在每次 ISource 调用时验证：`slots[slotIndex].generation == expectedGeneration`。对已回收池槽的旧引用立即抛出 `InvalidOperationException`。每个槽位40亿个世代——冲突在实际中不可能发生。（UniTask 使用 `short`——约18分钟后发生冲突。）

---

## ISource 契约

```csharp
public interface ISource
{
    Status GetStatus(ulong token);
    void   GetResult(ulong token);
    void   OnCompleted(Action<object> continuation, object state, ulong token);
    Status UnsafeGetStatus();
}

public interface ISource<out T> : ISource
{
    new T GetResult(ulong token);
}
```

任何实现 `ISource` 的对象都可以支撑 `ValkarnTask`。内置实现：

| 类型 | 用途 |
|------|---------|
| `AsyncValkarnRunner<TStateMachine>` | 支撑每个 `async ValkarnTask` 方法 |
| `AsyncValkarnRunner<TStateMachine, T>` | 支撑每个 `async ValkarnTask<T>` 方法 |
| `ValkarnTask.PooledPromise[<T>]` | 手动完成，自动归还池 |
| `ValkarnTask.Promise[<T>]` | 手动完成，不池化（长期存活） |
| `ExceptionSource` | 支撑 `FromException` |
| `CanceledSource` | 支撑 `FromCanceled` |
| `NeverSource` | 单例——永远不从 Pending 转换 |

---

## 异步方法构建器

C# 编译器驱动自定义异步返回类型的构建器协议：

```
Create()
  └─ 返回结构体构建器（零分配）

Start(ref stateMachine)
  └─ 同步运行状态机
     ├─ 无挂起完成 → SetResult()，运行器保持 null
     │  └─ Task 返回 default(ValkarnTask)  ← 零分配
     └─ 遇到未完成的 await → AwaitUnsafeOnCompleted()
           └─ 从池中租用 AsyncValkarnRunner
              将状态机复制到运行器（按值，无装箱）
              在 awaitable 上注册续体
              └─ Task 将运行器包装为 ISource  ← 异步路径
```

构建器本身是 `struct`——仅构建器本身不产生任何分配。`runner` 被**懒惰地**分配：如果方法同步完成，则永远不会发生池租用。

---

## 状态机运行器与池

`AsyncValkarnRunner<TStateMachine>` **按值**（无装箱）持有编译器生成的状态机，并充当 `ISource`。它在第一次挂起时从 `ValkarnPool<T>` 租用，在 `GetResult` 时归还。

由于 `TStateMachine` 是每个异步方法的唯一类型（每个封闭泛型实例化），通过 C# 泛型特化，每个异步方法自动获得自己的池。

### ValkarnPool\<T\>

| 上下文 | 结构 | 原因 |
|---------|-----------|--------|
| Unity 主线程 | 单线程栈 | 无同步——最快速 |
| 后台线程 | Treiber 无锁栈 | CAS 操作，无锁 |

线程上下文通过 `Thread.CurrentThread.IsBackground` 检测。池形状（容量、修剪率）通过 `ValkarnTaskSettings` 配置。

---

## ValkarnCompletionCore\<T\>

每个 `ISource` 实现内的共享状态：

- 当前 `Status`（Pending / Succeeded / Faulted / Canceled）
- 结果值（泛型源）
- 异常或 `OperationCanceledException`（错误路径）
- 已注册的续体委托＋状态

状态转换使用 `Interlocked.CompareExchange`——无锁，线程安全。**双重完成守卫**确保只有第一次 `TrySet*` 调用成功；后续调用是静默的无操作。

---

## PlayerLoop 集成

`PlayerLoopHelper` 在启动时（`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]`）将轻量级运行器回调插入 Unity 的 PlayerLoop。

每个 `PlayerLoopTiming` 值对应一个阶段。当调用 `await ValkarnTask.Yield(timing)` 时，续体被排入该阶段的运行器队列，并在 Unity 下次到达该阶段时分派。

```
Initialization  →  EarlyUpdate  →  FixedUpdate  →  PreUpdate
→  Update  →  PreLateUpdate  →  PostLateUpdate  →  TimeUpdate
  （每个阶段的 Last* 变体）
```

---

## 源生成器

Roslyn 源生成器在编译时运行。对于每个扩展 `MonoBehaviour` 且包含 `async ValkarnTask` 方法的 `partial` 类，它生成一个 partial 类文件，该文件：

1. 声明 `_valkarnCancelToken` 字段
2. 在 `Awake` 中从 `destroyCancellationToken` 赋值
3. 包装每个异步方法以自动穿透令牌

生成的文件永远不会在调试器中显示，也不会修改用户源码。

生成器还生成：

- **Awaitable 桥接适配器** — 当 `Awaitable` 在 `async ValkarnTask` 中被 await 时
- **Job 异步包装器** — 当检测到 `IJob` / `IJobParallelFor` 类型时
- **组合器池** — 2–8 元数元组的类型化 `WhenAll`/`WhenAny` 源

---

## Roslyn 分析器

17个 `DiagnosticAnalyzer` 规则包含在 `Analyzers/netstandard2.0/` 中。它们在 Unity 编辑器和 CI 的 C# 编译器阶段运行：

- 全部使用 `SemanticModel` 进行类型解析（而非字符串匹配）
- `ValkarnTypeHelper` 共享工具检测任何 `ValkarnTask` 变体
- 僵尸循环分析器正确跳过嵌套的本地函数和 lambda
- 迁移分析器（MIG001–MIG015）仅在引用了 UniTask / Awaitable 时自动激活——否则处于非活动状态

---

## Burst 与 ECS 层

三个可选模块，每个都通过 `#if` 定义检查保护：

| 模块 | 要求 | 用途 |
|--------|----------|---------|
| `JobBridge` | `Unity.Jobs` | 将 `JobHandle` 包装为可等待对象；每个 PlayerLoop 帧轮询 `handle.IsCompleted` |
| `AsyncSystemBase` | `Unity.Entities` | 支持异步的 ECS 系统基类 |
| `BurstScheduler` | `Unity.Burst` + `Unity.Collections` | 从异步上下文调度 Burst 作业；管理 `NativeTimerHeap` |

`NativeTimerHeap` 是一个 Burst 兼容的最小堆，用于高精度计时器，完全避免托管堆分配。

---

## 编辑器集成

**Valkarn Hub**（`Tools → Valkarn → Hub`）使用 `TypeCache.GetTypesDerivedFrom<IValkarnEditorPanel>()` 自动发现所有已安装的 Valkarn 包。无需手动注册。

`TasksTrackerPanel` 订阅 `EditorApplication.update` 以每0.5秒（可配置）刷新池诊断信息，并提供 `ValkarnTaskSettings` 资源引用以便快速访问。

---

## IL2CPP 注意事项

- 状态机**按值**存储在运行器内部——无装箱，IL2CPP 正确处理
- 每个异步方法的运行器是独立的泛型特化——类型安全，无交叉污染
- Awaiter 结构体实现 `ICriticalNotifyCompletion`——编译器调用 `UnsafeOnCompleted`，跳过 `ExecutionContext` 捕获（在 Unity 默认配置中无开销）
- 如果启用了激进的代码剥离，请保留运行时程序集：

```xml
<!-- link.xml -->
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```
