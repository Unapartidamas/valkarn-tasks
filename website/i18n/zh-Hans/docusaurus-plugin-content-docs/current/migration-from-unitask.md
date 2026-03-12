---
sidebar_position: 3
title: 从 UniTask 迁移
---

# 从 UniTask 迁移

Valkarn Tasks 在大多数常见场景下与 UniTask 保持 API 兼容。本指南涵盖两者的差异。

## 类型重命名

| UniTask | Valkarn Tasks |
|---------|--------------|
| `UniTask` | `ValkarnTask` |
| `UniTask<T>` | `ValkarnTask<T>` |
| `UniTaskCompletionSource` | `ValkarnTaskCompletionSource` |
| `UniTaskCompletionSource<T>` | `ValkarnTaskCompletionSource<T>` |

## 命名空间

```csharp
// 迁移前
using Cysharp.Threading.Tasks;

// 迁移后
using UnaPartidaMas.Valkarn.Tasks;
```

## 自动取消

UniTask 需要手动管理 `CancellationTokenSource`，Valkarn Tasks 会自动生成：

```csharp
// UniTask（手动）
public class EnemyAI : MonoBehaviour
{
    CancellationTokenSource _cts;

    void OnEnable() => _cts = new CancellationTokenSource();
    void OnDestroy() => _cts.Cancel();

    async UniTaskVoid Chase(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Move();
            await UniTask.Yield(ct);
        }
    }
}

// Valkarn Tasks（源码生成）
public partial class EnemyAI : MonoBehaviour
{
    async ValkarnTask Chase()
    {
        while (true)
        {
            Move();
            await ValkarnTask.Yield(); // 销毁时自动取消
        }
    }
}
```

## PlayerLoop 时机点

Valkarn Tasks 将时机点从 10 个扩展到 16 个。所有 UniTask 时机点均可直接映射：

| UniTask | Valkarn Tasks |
|---------|--------------|
| `PlayerLoopTiming.Initialization` | `PlayerLoopTiming.Initialization` |
| `PlayerLoopTiming.LastInitialization` | `PlayerLoopTiming.LastInitialization` |
| `PlayerLoopTiming.EarlyUpdate` | `PlayerLoopTiming.EarlyUpdate` |
| `PlayerLoopTiming.FixedUpdate` | `PlayerLoopTiming.FixedUpdate` |
| `PlayerLoopTiming.Update` | `PlayerLoopTiming.Update` |
| `PlayerLoopTiming.LastUpdate` | `PlayerLoopTiming.LastUpdate` |
| `PlayerLoopTiming.PreLateUpdate` | `PlayerLoopTiming.PreLateUpdate` |
| `PlayerLoopTiming.LastPostLateUpdate` | `PlayerLoopTiming.LastPostLateUpdate` |

Valkarn Tasks 新增：`PreUpdate`、`LastPreUpdate`、`LastPreLateUpdate`、`PostLateUpdate`、`TimeUpdate`、`LastTimeUpdate`。

## 即发即弃

```csharp
// UniTask
UniTask.Void(async () => { ... });

// Valkarn Tasks
ValkarnTask.Forget(MyMethod());
// 或修饰方法：
[FireAndForget]
async ValkarnTask MyMethod() { ... }
```

## Valkarn Tasks 的优势

- **自动取消**——通过源码生成器，无样板代码
- **17 条分析器规则**——在编译时捕获 bug
- **Burst / ECS**——原生计时器堆、异步 ECS 系统
- **额外 6 个 PlayerLoop 时机点**
- **线程感知对象池**——主线程无原子操作
