---
sidebar_position: 1
title: 分析器规则
---

# 分析器规则

Valkarn Tasks 附带两个在包导入后自动激活的 Roslyn 分析器包：

- **`UnaPartidaMas.Valkarn.Tasks.SourceGen.dll`** — 针对 VlkTask 正确性和 Unity 生命周期的专属规则。规则 ID 以 `TT` 开头。
- **`UnaPartidaMas.Valkarn.Tasks.Analyzer.dll`** — 用于从 UniTask 迁移代码库的迁移规则。规则 ID 以 `MIG` 开头。这些规则**仅在编译中存在 `Cysharp.Threading.Tasks.UniTask` 时**才触发，因此在新项目中保持静默。

两个包都是预编译的 DLL，位于包的 `Analyzers/` 文件夹中。Unity 通过 `.asmdef` 引用将它们作为 Roslyn 分析器加载；`_TestRunner~` 项目则通过 `TestRunner.csproj` 中的 `<Analyzer>` 项加载它们。

---

## 正确性规则（TT）

这些规则捕获与 `VlkTask` 单次消费特性以及即发即弃误用相关的 bug。

---

### TT001 — ValkarnTask 已被等待过

| 属性 | 值 |
|------|-----|
| 严重级别 | 警告 |
| 类别 | ValkarnTask |
| 代码修复 | 无 |

`VlkTask` 是单次消费的：一旦被等待，内部令牌就会过期。对同一变量再次执行 `await` 会遇到该过期令牌并抛出异常。分析器会检测同一方法内对类型为 `VlkTask`/`VlkTask<T>` 的同一本地变量、参数或字段多次执行 `await` 的情况。

**触发条件：**
```csharp
async VlkTask Bad()
{
    VlkTask work = DoWorkAsync();
    await work;   // 第一次 await — 正常
    await work;   // TT001：已被等待过
}
```

**修复方法：** 如果需要对结果进行分支处理，在第一次 await 前用 `.AsResult()` 捕获结果，或重构代码使任务只被等待一次。
```csharp
async VlkTask Good()
{
    var result = await DoWorkAsync().AsResult();
    // 在两个分支中使用 result
}
```

---

### TT002 — ValkarnTask 未被等待或丢弃

| 属性 | 值 |
|------|-----|
| 严重级别 | **错误** |
| 类别 | ValkarnTask |
| 代码修复 | 无 |

将返回 `VlkTask` 的方法调用作为表达式语句使用——既未 await、未赋值，也未跟随 `.Forget()`——是一个静默的 bug。任务内部抛出的异常永远不会被观察到，任务的池化状态机也永远不会归还到对象池。

分析器检查表达式语句中表达式类型解析为 `VlkTask` 或 `VlkTask<T>` 的情况。它跳过：
- 赋值表达式（`tasks[i] = DoWork()`）
- 以 `.Forget()` 结尾的调用链（有意为之的即发即弃）

**触发条件：**
```csharp
void Bad()
{
    LoadDataAsync();      // TT002：未被等待或丢弃
    ProcessItemAsync();   // TT002
}
```

**修复方法 — 等待它：**
```csharp
async VlkTask Good()
{
    await LoadDataAsync();
    await ProcessItemAsync();
}
```

**修复方法 — 显式即发即弃：**
```csharp
void GoodFireAndForget()
{
    LoadDataAsync().Forget();
}
```

---

### TT013 — ValkarnTask 已返回但从未被消费

| 属性 | 值 |
|------|-----|
| 严重级别 | 警告 |
| 类别 | ValkarnTask |
| 代码修复 | 无 |

此规则 ID **保留用于未来的数据流分析**。它旨在捕获「已赋值但从未被等待」的模式——将 `VlkTask` 存储到变量中，然后从不 await 也不丢弃它——这是 TT002 无法覆盖的情况，因为 TT002 只检查裸表达式语句。

当前实现不注册任何语法操作。数据流分析实现后，TT013 将通过覆盖以下情况来补充 TT002：
```csharp
async VlkTask Bad()
{
    VlkTask task = DoWorkAsync();  // 已赋值但从未被等待
    DoOtherThing();
    // task 被遗弃 — TT013（未来）
}
```

标记有 `[FireAndForget]` 的方法将获得豁免。

---

## 生命周期规则（TT）

这些规则与 MonoBehaviour 生命周期管理和取消相关。

---

### TT010 — MonoBehaviour 异步方法的自动取消已激活

| 属性 | 值 |
|------|-----|
| 严重级别 | 信息 |
| 类别 | ValkarnTask |
| 代码修复 | 无 |

这是一个信息提示。任何继承自 `UnityEngine.MonoBehaviour` 的类中的 `async VlkTask`（或 `async VlkTask<T>`）方法，**除非**方法标有 `[NoAutoCancel]`，否则在对象被销毁时会自动取消。此诊断使该行为在 IDE 中可见，无需开发者刻意记住它。

**触发条件：**
```csharp
public class MyBehaviour : MonoBehaviour
{
    async VlkTask LoadLevel()  // TT010：自动取消已激活
    {
        await VlkTask.Delay(2000);
    }
}
```

**要退出自动取消**（并为该方法抑制 TT010），添加 `[NoAutoCancel]`：
```csharp
[NoAutoCancel]
async VlkTask LoadLevel(CancellationToken ct)
{
    await VlkTask.Delay(2000, ct);
}
```

---

### TT011 — WhenAll 混用了不同的生命周期作用域

| 属性 | 值 |
|------|-----|
| 严重级别 | 警告 |
| 类别 | ValkarnTask |
| 代码修复 | 无 |

`VlkTask.WhenAll` 使用来自不同生命周期作用域的任务时，可能产生令人意外的部分取消行为。如果一个任务绑定到 `MonoBehaviour`（由继承 `MonoBehaviour` 的类的实例方法返回），另一个是无绑定的（静态任务或来自非 MonoBehaviour 类），那么销毁对象会取消一个任务但不取消另一个，使组合器处于不确定状态。

**触发条件：**
```csharp
// 假设 EnemyAI : MonoBehaviour
await VlkTask.WhenAll(
    enemyAI.PatrolAsync(),   // 绑定：在 Destroy 时自动取消
    GlobalMusic.FadeAsync()  // 无绑定：无限期存活
);
// TT011：WhenAll 混用了生命周期 — PatrolAsync() 与 GlobalMusic.FadeAsync()
```

**修复方法 — 给两个任务共享同一个取消 token：**
```csharp
using var cts = new CancellationTokenSource();
await VlkTask.WhenAll(
    enemyAI.PatrolAsync(cts.Token),
    GlobalMusic.FadeAsync(cts.Token)
);
```

---

### TT012 — 没有取消检查的异步循环

| 属性 | 值 |
|------|-----|
| 严重级别 | 警告 |
| 类别 | ValkarnTask |
| 代码修复 | 无 |

包含 `for`、`while`、`foreach` 或 `do`-`while` 循环，且循环体中没有取消检查的 `async VlkTask` 方法是一个「僵尸循环」：如果生命周期取消触发（例如 MonoBehaviour 被销毁），自动取消信号无法打断循环。即使拥有该对象已消失，循环也会继续运行。

分析器认为循环体存在取消检查的条件包括：
- `await` 表达式（被等待的操作可以观察到 token 并抛出 `OperationCanceledException`）
- `ThrowIfCancellationRequested`（作为标识符或成员访问）
- `IsCancellationRequested`（作为标识符或成员访问）

检查**不会**深入到嵌套 lambda 或局部函数中。

**触发条件：**
```csharp
async VlkTask PollForever(CancellationToken ct)
{
    while (true)                     // TT012：循环体中没有取消检查
    {
        ProcessNextItem();
        Thread.Sleep(16);            // 同步操作 — 不是 await
    }
}
```

**修复方法 — 添加 await 或显式检查：**
```csharp
async VlkTask PollForever(CancellationToken ct)
{
    while (true)
    {
        ct.ThrowIfCancellationRequested();
        ProcessNextItem();
        await VlkTask.Yield();       // 也满足检查条件
    }
}
```

---

### TT014 — [NoAutoCancel] 但没有 CancellationToken 参数

| 属性 | 值 |
|------|-----|
| 严重级别 | 警告 |
| 类别 | ValkarnTask |
| 代码修复 | 无 |

在 `MonoBehaviour` 中的 `async VlkTask` 方法上使用 `[NoAutoCancel]` 会使该方法退出自动生命周期取消。但如果该方法没有 `CancellationToken` 参数，它就没有任何观察取消的机制，使该属性毫无意义，几乎可以肯定是漏掉了参数。

**触发条件：**
```csharp
public class Enemy : MonoBehaviour
{
    [NoAutoCancel]
    async VlkTask Chase()      // TT014：[NoAutoCancel] 但没有 CancellationToken 参数
    {
        await VlkTask.Delay(1000);
    }
}
```

**修复方法 — 添加 `CancellationToken` 参数：**
```csharp
[NoAutoCancel]
async VlkTask Chase(CancellationToken ct)
{
    await VlkTask.Delay(1000, ct);
}
```

---

## 信息性规则（TT）

---

### TT015 — 已生成 Awaitable 桥接适配器

| 属性 | 值 |
|------|-----|
| 严重级别 | 信息 |
| 类别 | ValkarnTask |
| 代码修复 | 无 |

当你在 `async VlkTask` 方法中 `await` Unity 的 `Awaitable`（来自 `UnityEngine`）时，Valkarn Tasks 会通过 `AwaitableBridge` 自动生成桥接适配器。此诊断是信息性的：它确认桥接已生效。无需任何操作。

**触发条件：**
```csharp
async VlkTask LoadScene()
{
    await SceneManager.LoadSceneAsync("Main");  // TT015：已生成桥接适配器
}
```

无需更改。桥接会透明地处理转换。

---

## 代码质量规则（TT）

---

### TT016 — 没有 await 的异步方法

| 属性 | 值 |
|------|-----|
| 严重级别 | 警告 |
| 类别 | ValkarnTask |
| 代码修复 | 无 |

包含零个 `await` 表达式——包括 `await foreach`、`await using` 以及 `using` 声明中的 `await`——的 `async VlkTask`（或 `async VlkTask<T>`）方法会产生完整的状态机分配开销，却毫无收益。编译器仍然会生成状态机，但由于没有挂起点，该方法总是同步完成。

分析器检查：`AwaitExpressionSyntax`、带有 `await` 关键字的 `foreach`、带有 `await` 关键字的 `using` 声明，以及带有 `await` 关键字的 `using` 语句。

**触发条件：**
```csharp
async VlkTask<int> ComputeTotal()  // TT016：方法体中没有 await
{
    return items.Sum(x => x.Value);
}
```

**修复方法 — 移除 `async` 并返回已完成的任务：**
```csharp
VlkTask<int> ComputeTotal()
{
    return VlkTask.FromResult(items.Sum(x => x.Value));
}
```

或对于 void 返回：
```csharp
VlkTask DoSetup()
{
    Initialize();
    return VlkTask.CompletedTask;
}
```

---

### TT017 — [FireAndForget] 用于 ValkarnTask\<T\>

| 属性 | 值 |
|------|-----|
| 严重级别 | 警告 |
| 类别 | ValkarnTask |
| 代码修复 | 无 |

`[FireAndForget]` 表示某个方法有意在不等待结果的情况下运行。将其应用于返回 `VlkTask<T>`（带类型结果）的方法时，`T` 总是被丢弃，使带类型的返回值毫无意义。该方法应改为返回 `VlkTask`（void）。

**触发条件：**
```csharp
[FireAndForget]
async VlkTask<int> SendReport()   // TT017：返回值被丢弃
{
    await UploadAsync();
    return 42;                     // 这个 42 永远不会被看到
}
```

**修复方法 — 将返回类型改为 `VlkTask`：**
```csharp
[FireAndForget]
async VlkTask SendReport()
{
    await UploadAsync();
}
```

---

## 迁移规则（MIG）

迁移分析器**仅在编译中存在 `Cysharp.Threading.Tasks.UniTask` 类型时**才激活。规则为信息性或警告级别，用于指导从 UniTask 到 Valkarn Tasks 的过渡。这些规则都没有自动修复；以下描述说明了手动更改方式。

---

### MIG001 — 检测到 UniTask 类型

| 属性 | 值 |
|------|-----|
| 严重级别 | 信息 |
| 类别 | ValkarnTaskMigration |

检测到 `UniTask` 类型的使用（结构体本身或 `UniTask<T>`）。将其替换为等效的 `VlkTask` 或 `VlkTask<T>`。

```csharp
// 之前
UniTask<Sprite> LoadSprite(string path) { ... }

// 之后
VlkTask<Sprite> LoadSprite(string path) { ... }
```

---

### MIG002 — cancelImmediately 参数不再需要

| 属性 | 值 |
|------|-----|
| 严重级别 | 信息 |
| 类别 | ValkarnTaskMigration |

UniTask 的 `Delay` 和其他基于时间的方法接受 `cancelImmediately` 参数来启用快速取消。Valkarn Tasks 默认即时取消——没有 `cancelImmediately` 参数。移除该参数即可。

```csharp
// 之前
await UniTask.Delay(1000, cancelImmediately: true, cancellationToken: ct);

// 之后
await VlkTask.Delay(1000, ct);
```

---

### MIG003 — 检测到 SuppressCancellationThrow()

| 属性 | 值 |
|------|-----|
| 严重级别 | 警告 |
| 类别 | ValkarnTaskMigration |

UniTask 的 `.SuppressCancellationThrow()` 将已取消的任务转换为 `(bool isCancelled, T result)` 元组而不抛出异常。Valkarn Tasks 使用 `.AsResult()` 达到同样目的，它返回一个 `Result<T>` 结构体。

```csharp
// 之前
var (isCancelled, value) = await myUniTask.SuppressCancellationThrow();

// 之后
var result = await myVlkTask.AsResult();
if (result.IsCanceled) { ... }
T value = result.Value;
```

---

### MIG004 — 检测到 Awaitable 返回类型

| 属性 | 值 |
|------|-----|
| 严重级别 | 信息 |
| 类别 | ValkarnTaskMigration |

某个方法返回 Unity 的 `Awaitable` 类型。考虑将其替换为 `VlkTask`，后者与 Valkarn Tasks 运行时原生集成，支持自动取消、对象池和完整的组合器集。

---

### MIG005 — 检测到 SingleConsumerUnbounded 通道

| 属性 | 值 |
|------|-----|
| 严重级别 | 警告 |
| 类别 | ValkarnTaskMigration |

UniTask 的 `Channel.CreateSingleConsumerUnbounded<T>()` 创建一个没有背压的通道。考虑将其替换为 `VlkTask.Channel.CreateBounded<T>(capacity)`，以添加背压并防止在高负载下出现无界内存增长。

```csharp
// 之前
var ch = Channel.CreateSingleConsumerUnbounded<Event>();

// 之后（带背压）
var ch = VlkTask.Channel.CreateBounded<Event>(capacity: 256);

// 或者如果无界是有意为之的
var ch = VlkTask.Channel.CreateUnbounded<Event>();
```

---

### MIG006 — 检测到 UniTask PlayerLoopTiming

| 属性 | 值 |
|------|-----|
| 严重级别 | 信息 |
| 类别 | ValkarnTaskMigration |

检测到来自 `Cysharp.Threading.Tasks` 命名空间的 `PlayerLoopTiming` 枚举值。将 `using` 指令改为 `UnaPartidaMas.Valkarn.Tasks`；枚举值名称完全相同。

```csharp
// 之前
using Cysharp.Threading.Tasks;
timing = PlayerLoopTiming.Update;

// 之后
using UnaPartidaMas.Valkarn.Tasks;
timing = PlayerLoopTiming.Update;  // 名称相同，命名空间不同
```

---

### MIG007 — 检测到 async UniTaskVoid

| 属性 | 值 |
|------|-----|
| 严重级别 | 警告 |
| 类别 | ValkarnTaskMigration |

`async UniTaskVoid` 是 UniTask 的即发即弃方法模式。Valkarn Tasks 用两种选项替代它：

```csharp
// 之前
async UniTaskVoid StartLoadAsync() { ... }

// 之后 — 选项 1：[FireAndForget] 属性
[FireAndForget]
async VlkTask StartLoadAsync() { ... }

// 之后 — 选项 2：标准 VlkTask + 在调用处 .Forget()
async VlkTask StartLoadAsync() { ... }
// 调用方式：
StartLoadAsync().Forget();
```

---

### MIG008 — 检测到 Awaitable MainThreadAsync()

| 属性 | 值 |
|------|-----|
| 严重级别 | 信息 |
| 类别 | ValkarnTaskMigration |

`Awaitable.MainThreadAsync()` 在 Unity 的 `Awaitable` API 中用于切回主线程。Valkarn Tasks 通过 PlayerLoop 集成默认在主线程上运行续体，因此显式的 `MainThreadAsync()` 调用通常不必要，可以移除。

---

### MIG009 — 检测到 UniTask.RunOnThreadPool

| 属性 | 值 |
|------|-----|
| 严重级别 | 警告 |
| 类别 | ValkarnTaskMigration |

替换为 `VlkTask.RunOnThreadPool`。API 完全相同。

```csharp
// 之前
await UniTask.RunOnThreadPool(() => HeavyWork());

// 之后
await VlkTask.RunOnThreadPool(() => HeavyWork());
```

---

### MIG010 — 检测到 .ToCoroutine()

| 属性 | 值 |
|------|-----|
| 严重级别 | 警告 |
| 类别 | ValkarnTaskMigration |

`.ToCoroutine()` 是 UniTask 为遗留协程调用者提供的桥接。将消费代码改写为 `async VlkTask` 方法替代。

```csharp
// 之前
IEnumerator LegacyCaller() { yield return MyUniTask().ToCoroutine(); }

// 之后
async VlkTask ModernCaller() { await MyVlkTask(); }
```

---

### MIG011 — 检测到 UniTask.Create()

| 属性 | 值 |
|------|-----|
| 严重级别 | 警告 |
| 类别 | ValkarnTaskMigration |

`UniTask.Create(Func<UniTask>)` 包装一个工厂委托。将其替换为 `VlkTask.Promise<T>` 模式以实现手动控制完成。

```csharp
// 之前
var task = UniTask.Create(async () => { await DoWork(); return 42; });

// 之后
var promise = new VlkTaskCompletionSource<int>();
DoWorkThenComplete(promise);
var task = promise.Task;
```

---

### MIG012 — 检测到 UniTask.Lazy/Defer

| 属性 | 值 |
|------|-----|
| 严重级别 | 信息 |
| 类别 | ValkarnTaskMigration |

`UniTask.Lazy<T>` 和 `UniTask.Defer` 用于在任务可能同步完成时避免分配。Valkarn Tasks 内置零分配同步快速路径：返回 `VlkTask.CompletedTask` 或 `VlkTask.FromResult(value)` 永远不会分配内存。移除 `Lazy`/`Defer` 包装器即可。

---

### MIG013 — 检测到 .ToUniTask()/.AsUniTask()

| 属性 | 值 |
|------|-----|
| 严重级别 | 信息 |
| 类别 | ValkarnTaskMigration |

从 Unity `Awaitable` 到 UniTask 的转换调用。移除它们；Valkarn Tasks 原生桥接 `Awaitable`（参见 TT015）。

---

### MIG014 — 检测到 UniTaskAsyncEnumerable

| 属性 | 值 |
|------|-----|
| 严重级别 | 警告 |
| 类别 | ValkarnTaskMigration |

UniTask 附带了自己的 `IUniTaskAsyncEnumerable<T>` 和 `UniTaskAsyncEnumerable` 工具类。改为使用 BCL 中的 `IAsyncEnumerable<T>` 配合 `System.Linq.Async`。C# 的 `await foreach` 原生支持 `IAsyncEnumerable<T>`。

```csharp
// 之前
IUniTaskAsyncEnumerable<int> GetItems() { ... }

// 之后
IAsyncEnumerable<int> GetItems() { ... }
```

---

### MIG015 — 检测到 TimeoutController

| 属性 | 值 |
|------|-----|
| 严重级别 | 信息 |
| 类别 | ValkarnTaskMigration |

UniTask 的 `TimeoutController` 是一个用于可复用超时的辅助工具。将其替换为使用 `TimeSpan` 构造的标准 `CancellationTokenSource`，BCL 直接支持此方式。

```csharp
// 之前
var controller = new TimeoutController();
var ct = controller.Timeout(TimeSpan.FromSeconds(5));

// 之后
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var ct = cts.Token;
```

---

## 抑制规则

所有规则都可以使用标准 Roslyn 抑制机制：

```csharp
// 内联抑制单个出现
#pragma warning disable TT012
while (true) { DoSomething(); }
#pragma warning restore TT012
```

或通过 `.editorconfig` 在项目范围内抑制：
```ini
[*.cs]
dotnet_diagnostic.TT012.severity = none
```

迁移规则（`MIG*`）可以用相同方式抑制，或在迁移完成后通过在 `.editorconfig` 中将严重级别设为 `none` 来全局禁用。
