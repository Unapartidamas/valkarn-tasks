---
sidebar_position: 2
title: IL2CPP 兼容性
---

# IL2CPP 兼容性

Valkarn Tasks 从设计之初就确保在 IL2CPP 下正确运行。本页介绍所采取的每项措施，以及你需要——或不需要——做什么，以便在 iOS、WebGL 和游戏主机等 IL2CPP 平台上安全发布。

---

## 为什么 IL2CPP 需要特殊处理

IL2CPP 将 C# IL 转换为 C++ 源代码，然后用原生编译器进行编译。该流水线有两个功能与异步库相关：

1. **代码剥离。** Unity 的托管代码剥离器（使用 IL2CPP 的链接器）会移除静态可分析调用图中未被引用的类型、方法和字段。仅通过接口分发、泛型共享或反射访问的类型——包括池化 promise 类和 `ISource` 实现——可能会被静默剥离。

2. **泛型共享。** IL2CPP 不会为每种泛型实例化生成单独的原生二进制文件。相反，它跨引用类型共享代码，并对值类型使用特定实例化。这可能隐藏在开发环境（Mono）中存在但只在 IL2CPP 构建中浮现的 bug。

---

## link.xml 文件

防止剥离的主要防线是 `link.xml` 文件，位于：

```
Runtime/link.xml
```

其内容：

```xml
<linker>
  <!-- 保留 Valkarn.Tasks 运行时程序集中的所有类型。
       IL2CPP 代码剥离可能移除仅通过接口分发、
       泛型共享或反射访问的内部类型（例如 promise
       类、池化运行器、ISource 实现）。 -->
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks.Burst" preserve="all"/>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks.ECS" preserve="all"/>
</linker>
```

`preserve="all"` 指示链接器保留这些程序集中的每个类型、方法、字段和构造函数，无论静态分析发现了什么。这是处理通过泛型参数访问内部类型（剥离器无法追踪）的库的最安全设置。

Unity 在将 `link.xml` 文件发现并应用时，要求它们放在导入到项目的包文件夹中。无需手动步骤。

如果你是**fork**或**嵌入**源代码而非使用包，请将 `link.xml` 复制到 `Resources` 相邻文件夹，或放在 Unity 能按照 [Unity 托管代码剥离文档](https://docs.unity3d.com/Manual/ManagedCodeStripping.html) 找到它的任何位置。

---

## 没有 link.xml 会被剥离的内部类型

以下几类内部类型是主要的剥离风险：

### 池化 Promise 类（ISource 实现）

每种组合器和延迟类型都创建一个实现 `VlkTask.ISource` 或 `VlkTask.ISource<T>` 的池化 promise 类：

- `AsyncVlkTaskRunner<TStateMachine>` — 池化状态机运行器，每个异步方法一个（在 `TStateMachine` 上特化）
- `WhenAllPromise<T1, T2>`、`WhenAllArrayPromise<T>`、`WhenAllVoidPromise2`、`WhenAllVoidPromise3`、`WhenAllVoidArrayPromise`
- `DeltaTimeDelayPromise`、`UnscaledDeltaTimeDelayPromise`、`RealtimeDelayPromise`
- `CanceledSource`、`CanceledSource<T>`、`ExceptionSource`、`ExceptionSource<T>`、`NeverSource`
- 通道内部实现：`BoundedChannel<T>`、`UnboundedChannel<T>` 及其读写器类型

这些类型通过泛型工厂方法（`VlkTaskPool<T>.GetOrCreate`）实例化。静态分析调用图从泛型方法调用开始，无法可靠追踪所有具体的 `T` 实例化，因此没有 `link.xml` 时，这些类型中的任何一个都可能被剥离。

### VlkTaskPool\<T\>

```csharp
internal sealed class VlkTaskPool<T> : IPoolInfo where T : class, IPoolNode<T>
```

对象池对其元素类型是泛型的。每个 promise 类都有自己的静态对象池字段。如果某个 promise 类型在场景中未使用，该对象池和 promise 类可能会一起被剥离。

### VlkTaskCompletionCore\<T\>

内部完成核心是一个在每个 promise 内部使用的值类型。它保存续体回调、令牌和完成状态。它从库外部从不按名称引用。

---

## 泛型类型约束与 IL2CPP

自定义异步方法构建器对状态机类型是泛型的：

```csharp
public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
    ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : ICriticalNotifyCompletion
    where TStateMachine : IAsyncStateMachine
```

运行器也对状态机是泛型的：

```csharp
internal sealed class AsyncVlkTaskRunner<TStateMachine>
    : IStateMachineRunnerPromise, IPoolNode<AsyncVlkTaskRunner<TStateMachine>>
    where TStateMachine : IAsyncStateMachine
```

在 IL2CPP 下，会为每个不同的 `TStateMachine` 生成一个新的具体类型（因为状态机是结构体，需要完整的泛型特化）。这意味着：

- 你项目中的每个 `async VlkTask` 方法都会产生一个独立的 `AsyncVlkTaskRunner<TYourStateMachine>` 原生类型。
- 如果剥离器在看到所有实例化之前移除了 `AsyncVlkTaskRunner<T>`，某些异步方法可能在运行时崩溃。
- `link.xml` 中的 `preserve="all"` 可防止这种情况。

---

## 托管代码剥离级别

Unity 的剥离级别在 **Player Settings → Other Settings → Managed Stripping Level** 中设置。

| 级别 | 与 Valkarn Tasks 的兼容状态 |
|------|--------------------------|
| 禁用 | 安全。不发生剥离。 |
| 低 | 安全。仅移除明显未使用的程序集。 |
| 中 | **有 `link.xml`** 时安全。附带的 `link.xml` 保留所有运行时类型。 |
| 高 | **有 `link.xml`** 时安全。同等覆盖范围；`link.xml` 是保护层。 |

包括**高**在内的所有剥离级别，只要 `link.xml` 文件存在并被应用，都是安全的。在不了解你正在暴露哪些内部类型给剥离器的情况下，不要移除或修改 `link.xml`。

---

## [Preserve] 属性的使用

搜索 Runtime 源代码，发现没有对单个成员应用 `[UnityEngine.Scripting.Preserve]` 属性。所选择的方法是通过 `link.xml` 进行程序集级别保留，而不是逐成员属性。这是有意为之的：

- 逐成员 `[Preserve]` 需要单独注解每个池化类，包括未来所有新增的类。
- `link.xml` 中的程序集级别 `preserve="all"` 更简单、更不易出错，并且保证覆盖未来版本中新增的类型。

如果你需要将 Valkarn Tasks 集成到更大的 `link.xml` 文件而不是使用包自带的，等效指令是：

```xml
<assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
```

---

## IL2CPP 优先的对象池设计

对象池（`VlkTaskPool<T>`）在其文档注释中包含一条明确的说明：

> IL2CPP 优先：主线程操作使用零原子操作。

对象池使用两条访问路径：

- **快速路径（主线程）：** 使用普通字段访问读写单个 `fastItem` 字段——没有 `Volatile` 或 `Interlocked` 操作。这避免了主线程上原子操作的开销，因为 IL2CPP 不总能将其优化掉。
- **溢出路径（任意线程）：** 使用 `Interlocked.CompareExchange` 的 Treiber 无锁栈，确保在来自后台线程（如 `VlkTask.RunOnThreadPool`）的并发访问下的正确性。

主线程标识存储在 `VlkTaskPoolShared.MainThreadId` 的 `volatile int` 字段中，在启动时通过 `RuntimeInitializeOnLoadMethod` 一次性发布。IL2CPP 正确处理 `volatile` 字段。

---

## 游戏主机平台注意事项

游戏主机平台（PlayStation、Xbox、Nintendo Switch）专门使用 IL2CPP。以下内容适用：

- `link.xml` 覆盖范围与其他 IL2CPP 目标相同。无需特定于游戏主机的额外保留配置。
- `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` 在 Unity 支持游戏主机认证的所有平台上都能正确触发。
- `Interlocked` 和 `Volatile` 操作在所有游戏主机 IL2CPP 目标上均受支持。对象池的 Treiber 栈是安全的。
- 结构体 `TStateMachine` 实例化的泛型共享适用于游戏主机。每个 `async VlkTask` 方法生成自己的原生类型——这是预期行为，已被正确处理。
- 如果游戏主机平台强制执行额外的 AOT 要求，请通过检查构建日志中的「Stripping assembly: UnaPartidaMas.Valkarn.Tasks」来确认你的 `link.xml` 被正确采用。如果程序集被剥离，说明 `link.xml` 未被发现。

---

## 验证没有内容被剥离

为 IL2CPP 目标构建后：

1. **检查构建日志。** Unity 会输出剥离决策。搜索 `UnaPartidaMas.Valkarn.Tasks`。如果看到内部 Valkarn 类型的 `Stripping class` 消息，说明 `link.xml` 未被应用。

2. **运行冒烟测试。** 最小测试——练习 `VlkTask.Delay`、`WhenAll` 和自定义 `VlkTaskCompletionSource`——将实例化最常被剥离的类型。如果这三个场景在构建中存活，核心功能是完整的。

3. **选择性启用「Strip Engine Code」。** 如果你必须使用高剥离级别且无法使用捆绑的 `link.xml`，逐步启用剥离并在每次提高剥离级别后运行测试。

4. **启用开发构建的 IL2CPP 构建。** 开发构建包含额外的诊断信息。如果某个类型缺失，运行时将在缺失类型的调用位置报告 `TypeInitializationException` 或空引用。将堆栈跟踪与上面「内部类型」部分列出的类型进行对照。

---

## 摘要检查清单

- `Runtime/link.xml` 存在于包中——验证它未被意外删除。
- 托管剥离级别可以设置为任何级别；支持高剥离。
- 无需向应用代码添加 `[Preserve]` 属性。
- 无需 AOT 提示属性或代码注册调用。
- 游戏主机构建使用相同的 `link.xml`；无需特定于平台的额外配置。
- 如果你 fork 了源代码，请随之携带 `link.xml`。
