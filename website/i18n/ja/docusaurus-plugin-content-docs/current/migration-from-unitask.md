---
sidebar_position: 3
title: UniTaskからの移行
---

# UniTaskからの移行

Valkarn TasksはほとんどのよくあるケースでUniTaskとAPI互換性があります。このガイドでは相違点について説明します。

## 型の名前変更

| UniTask | Valkarn Tasks |
|---------|--------------|
| `UniTask` | `ValkarnTask` |
| `UniTask<T>` | `ValkarnTask<T>` |
| `UniTaskCompletionSource` | `ValkarnTaskCompletionSource` |
| `UniTaskCompletionSource<T>` | `ValkarnTaskCompletionSource<T>` |

## 名前空間

```csharp
// 変更前
using Cysharp.Threading.Tasks;

// 変更後
using UnaPartidaMas.Valkarn.Tasks;
```

## 自動キャンセル

UniTaskは手動で`CancellationTokenSource`を管理する必要があります。Valkarn Tasksはそれを自動生成します：

```csharp
// UniTask（手動）
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

// Valkarn Tasks（ソース生成）
public partial class EnemyAI : MonoBehaviour
{
    async ValkarnTask Chase()
    {
        while (true)
        {
            Move();
            await ValkarnTask.Yield(); // Destroy時に自動的にキャンセル
        }
    }
}
```

## PlayerLoopタイミング

Valkarn Tasksは10から16のタイミングに拡張されています。すべてのUniTaskタイミングは直接マッピングされます：

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

Valkarn Tasksの新規追加：`PreUpdate`、`LastPreUpdate`、`LastPreLateUpdate`、`PostLateUpdate`、`TimeUpdate`、`LastTimeUpdate`。

## Fire-and-forget

```csharp
// UniTask
UniTask.Void(async () => { ... });

// Valkarn Tasks
ValkarnTask.Forget(MyMethod());
// またはメソッドを装飾する：
[FireAndForget]
async ValkarnTask MyMethod() { ... }
```

## Valkarn Tasksで優れている点

- **自動キャンセル** — ソースジェネレーター経由、ボイラープレートなし
- **17のアナライザールール** — コンパイル時にバグを検出
- **Burst / ECS** — ネイティブタイマーヒープ、非同期ECSシステム
- **6つの追加PlayerLoopタイミング**
- **スレッド対応プール** — メインスレッドでアトミック操作なし
