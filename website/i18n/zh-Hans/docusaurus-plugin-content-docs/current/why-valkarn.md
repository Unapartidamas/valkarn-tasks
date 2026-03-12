---
sidebar_position: 6
title: 为什么选择 Valkarn Tasks
---

# 为什么选择 Valkarn Tasks

> 适用于 Unity 的零分配 async/await。比 UniTask 更快。比 Awaitable 更智能。

---

## 问题：Unity 中的 async 存在缺陷

Unity 开发者在各处都需要异步操作：加载场景、下载资源、等待动画、延迟生成、与服务器通信。目前的选择有：

| 选项 | 问题 |
|--------|---------|
| **协程** | 无返回值、无错误处理、无取消、无组合 |
| **System.Threading.Tasks** | 每次 `async` 调用分配内存（约144–232字节），触发GC，不感知Unity生命周期 |
| **UniTask** | 不错——但：18分钟后发生令牌冲突，无编译时诊断，依赖终结器的错误报告 |
| **Unity Awaitable**（2023+） | 基于类（会分配），无组合器，无通道，无测试支持 |

Valkarn Tasks 通过单个源生成的零分配包解决了上述所有问题。

---

## 零分配——为什么每个字节都重要

### 分配对游戏意味着什么

每次 `new object()`、`new List<T>()` 或 `async Task` 调用都会在**托管堆**上分配内存，由垃圾收集器追踪。Unity 使用 Boehm GC，存在两个关键问题：

1. **全局停顿** — GC 运行时，游戏会冻结。在60fps下，2ms的暂停会消耗12%的帧预算；在120fps（VR）下，会消耗24%。
2. **不可预测的时机** — GC 可能在Boss战、过场动画或竞技对战中触发。

### Valkarn Tasks 的不同之处

| 场景 | `System.Task` | UniTask | **Valkarn Tasks** |
|----------|--------------|---------|------------------|
| `async Method()` — 同步完成 | 144字节 | **0字节** | **0字节** |
| `async Method()` — 挂起一次 | 232+字节 | 0字节（池化） | **0字节（池化）** |
| `WhenAll(a, b)` | 232字节 | 0字节（池化） | **0字节（源生成池化）** |
| `WhenAny(a, b)` | 144字节 | 72字节 | **0字节** |
| Promise（手动完成） | 144字节 | 104字节 | **88字节** |
| 池化 Promise（可复用） | 144字节 | 0字节 | **0字节** |

在每帧包含50–100个异步操作的典型游戏中，`System.Task` 会产生7–23KB的垃圾。Valkarn Tasks 产生**零**。

### 这对您的游戏意味着什么

- **无GC卡顿** — 异步操作不会导致锁帧卡顿
- **VR友好** — 90/120fps目标无GC峰值
- **移动端友好** — 减少内存有限设备上的内存压力
- **主机认证友好** — 可预测的内存行为有助于通过认证要求

---

## 性能——与最优秀的库对比基准测试

基准测试环境：BenchmarkDotNet v0.14.0，.NET 9.0，Intel Core i7-10875H。

### 核心 async/await — 比 ValueTask 快2倍

| 基准测试 | ValueTask | UniTask | **Valkarn Tasks** | vs ValueTask |
|-----------|-----------|---------|------------------|--------------|
| 批量100个任务 | 956 ns | 508 ns | **489 ns** | **1.95×** |
| 批量1,000个任务 | 9,697 ns | 5,016 ns | **4,728 ns** | **2.05×** |
| 带 CancellationToken | 38.8 ns | 36.2 ns | **29.6 ns** | **1.31×** |
| 异常处理 | 10,399 ns | 8,247 ns | **9,248 ns** | **1.12×** |

所有路径：**0字节分配**。

### 组合器——比 Task 快最多9.6倍

| 基准测试 | Task | UniTask | **Valkarn Tasks** | vs Task |
|-----------|------|---------|------------------|---------|
| WhenAll（2个任务） | 117 ns / 232B | 13.6 ns / 0B | **12.1 ns / 0B** | **9.6×** |
| WhenAll（5个任务） | 156 ns / 272B | 25.1 ns / 0B | **25.3 ns / 0B** | **6.2×** |
| WhenAny（2个任务） | 39.0 ns / 144B | 60.0 ns / 72B | **11.6 ns / 0B** | **3.4×** |
| 池化 Promise | 59.5 ns / 144B | 53.6 ns / 0B | **38.3 ns / 0B** | **1.55×** |

WhenAny **比 UniTask 快5.2倍**，且零字节分配。

### 对象池——4.3纳秒

| 操作 | 时间 | 分配 |
|-----------|------|------------|
| 主线程快速槽位租用＋归还 | **4.3 ns** | 0字节 |
| 跨线程 Treiber 栈 | ~15 ns | 0字节 |

主线程零原子操作——在IL2CPP中 `Volatile.Read` 慢9.2倍的情况下，这点至关重要。

### 实际影响（60fps每帧50个异步操作）

| 库 | 时间/帧 | 帧预算 | GC/秒 |
|---------|-----------|-------------|-----------|
| System.Task | ~48 µs | 0.29% | ~430 KB/s |
| UniTask | ~25 µs | 0.15% | ~3.6 KB/s |
| **Valkarn Tasks** | **~24 µs** | **0.14%** | **0 KB/s** |

10分钟内，`System.Task` 产生约**258MB**的异步垃圾。Valkarn Tasks 产生**零**。

---

## 其他库没有的功能

### 自动生命周期取消

```csharp
public partial class EnemySpawner : MonoBehaviour
{
    async ValkarnTask Start()
    {
        while (true)
        {
            await ValkarnTask.Delay(3000);
            SpawnWave();
        }
        // 当此 GameObject 被销毁时自动取消。
        // 无需 CancellationToken。无内存泄漏。无僵尸任务。
    }
}
```

不再有因异步方法在销毁后运行导致的 `MissingReferenceException`。无需手动 `OnDestroy` 清理，无需记得处置 `CancellationTokenSource`。

### 关键区间

```csharp
async ValkarnTask SaveProgress()
{
    var data = await LoadPlayerData();       // 可取消

    await using (ValkarnTask.Critical())
    {
        await db.Insert(data);              // 即使 GO 被销毁也会完成
        await db.Commit();
    }                                       // 挂起的取消在此处生效

    await SendNotification();               // 再次可取消
}
```

即使玩家退出或场景卸载，数据库写入、网络请求和文件保存也会完成。不会有损坏的存档、不完整的分析数据或丢失的收据。

### 编译时诊断

| 诊断代码 | 捕获内容 |
|------------|----------------|
| TT001 | 对 ValkarnTask 的双重 await（释放后使用漏洞） |
| TT002 | 忘记 await 任务（静默失败） |
| TT012 | 没有取消检查的异步循环（僵尸循环） |
| TT013 | 返回但未被 await 的任务（即发即忘漏洞） |
| TT016 | 没有 await 的异步方法（不必要的开销） |
| TT017 | `ValkarnTask<T>` 上的 `[FireAndForget]`（丢弃返回值） |

漏洞在IDE中显示为红色波浪线，而不是测试20分钟后的运行时崩溃。

### Result\<T\> — 无需 try/catch 的错误处理

```csharp
var result = await loadTask.AsResult();

if (result.Succeeded)       Use(result.Value);
else if (result.IsCanceled) ShowRetryDialog();
else                         Debug.LogError(result.Error);
```

每个错误路径都是显式的。无被吞掉的异常，无遗漏的处理器。

### 带背压的通道

```csharp
var channel = ValkarnTask.Channel.CreateBounded<EnemySpawnRequest>(capacity: 10);

// 生产者（游戏逻辑）
await channel.Writer.WriteAsync(new EnemySpawnRequest { type = EnemyType.Dragon });

// 消费者（生成系统）
await foreach (var request in channel.Reader.ReadAllAsync())
    await SpawnEnemy(request);
```

干净地解耦系统，限制生成速率，排队网络消息，缓冲输入事件。当消费者跟不上时，生产者会减速，防止内存峰值。

### 使用 TestClock 进行确定性测试

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

即时测试时间依赖逻辑。无需在测试中使用 `yield return new WaitForSeconds(3)`，无不稳定的CI计时。

### 世代令牌安全性

UniTask 使用 `short` 令牌（16位）。经过65,536次池循环（约18分钟的活跃异步工作）后，旧引用会静默读取另一个任务的结果——这是几乎不可能复现的释放后使用漏洞。

Valkarn Tasks 对每个池槽使用 `uint` 世代计数器：每个槽位**4,294,967,296次循环**后才会发生冲突。在任何现实场景中都不可能发生。

---

## 分钟级迁移，而非数周

### 从 UniTask 迁移

```
第1步：安装 Valkarn Tasks
第2步：IDE 中 UniTask 用法处出现黄色灯泡
第3步：右键单击 → "修复解决方案中的所有匹配项"（Ctrl+.）
第4步：删除 UniTask 包引用
```

**15条迁移诊断**（MIG001–MIG015）自动覆盖每个 UniTask API。拥有500–2,000个异步方法的典型项目可在5分钟内完成迁移。95%以上完全自动化。

### 从 Unity Awaitable 迁移

同样的一键迁移：
- `async Awaitable` → `async ValkarnTask`
- `Awaitable.NextFrameAsync()` → `ValkarnTask.NextFrame()`
- `Awaitable.BackgroundThreadAsync()` → `ValkarnTask.SwitchToThreadPool()`
- `Awaitable.MainThreadAsync()` → 已移除（Valkarn 默认在主线程上运行）

---

## 功能对比矩阵

| 功能 | System.Task | UniTask | Awaitable | **Valkarn Tasks** |
|---------|------------|---------|-----------|------------------|
| 零分配同步路径 | 否 | 是 | 否 | **是** |
| 零分配组合器 | 否 | 否 | 否 | **是（源生成）** |
| 基于结构体 | 否 | 是 | 否 | **是** |
| 生命周期自动取消 | 否 | 手动 | 部分 | **自动** |
| 无兄弟任务取消 | 否 | 否 | 否 | **是** |
| 关键区间 | 否 | 否 | 否 | **是** |
| Result\<T\>（不抛异常） | 否 | 部分 | 否 | **是** |
| TestClock | 否 | 否 | 否 | **是** |
| Job系统桥接 | 否 | 否 | 否 | **是** |
| 编译时诊断 | 否 | 否 | 否 | **是（17条规则）** |
| 有界池＋修剪 | 否 | 否 | 不适用 | **是** |
| 确定性错误报告 | 否 | 否（终结器） | 部分 | **是（池归还时）** |
| 完整通道 | 是（.NET） | 最小 | 否 | **是** |
| Awaitable桥接 | 不适用 | 最小 | 原生 | **透明** |
| IL2CPP优化池化 | 否 | 否（每次操作Volatile） | 不适用 | **是（零原子操作）** |
| 令牌冲突安全性 | 不适用 | 18分钟（short） | 不适用 | **永不（uint世代）** |
| UniTask自动迁移 | 不适用 | 不适用 | 不适用 | **是（15个修复）** |
| Awaitable自动迁移 | 不适用 | 不适用 | 不适用 | **是（8个修复）** |

---

**您的游戏值得拥有不会卡顿的 async。**
