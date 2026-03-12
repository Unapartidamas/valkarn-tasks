---
sidebar_position: 2
title: クイックスタート
---

# クイックスタート

## 基本的な非同期メソッド

```csharp
using UnaPartidaMas.Valkarn.Tasks;

public class MyBehaviour : MonoBehaviour
{
    async VlkTask Start()
    {
        await VlkTask.Delay(1000); // 1秒待機、ゼロアロケーション
        Debug.Log("完了！");
    }
}
```

## Destroy時の自動キャンセル

クラスを`partial`として宣言するだけ — ソースジェネレーターが残りを処理します：

```csharp
public partial class EnemyAI : MonoBehaviour
{
    async VlkTask ChasePlayer()
    {
        while (true)
        {
            MoveTowards(player.position);
            await VlkTask.Yield(); // このGameObjectが破棄されると自動的にキャンセルされる
        }
    }
}
```

`CancellationTokenSource`なし、`OnDestroy`オーバーライドなし、ボイラープレートなし。

## WhenAll

```csharp
// 複数のタスクを待機 — 分解代入をサポート
var (texture, audio, data) = await VlkTask.WhenAll(
    LoadTexture(),
    LoadAudio(),
    LoadData()
);
```

## WhenAny

```csharp
// 最初に完了したものが勝ち
var result = await VlkTask.WhenAny(FromCache(), FromNetwork());
```

## 値を返す

```csharp
async VlkTask<Texture2D> LoadTexture()
{
    await VlkTask.SwitchToThreadPool();
    var bytes = File.ReadAllBytes(path);
    await VlkTask.SwitchToMainThread();
    return CreateTexture(bytes);
}
```

## チャネル

```csharp
var channel = Channel.CreateBounded<int>(capacity: 16);

// プロデューサー
await channel.Writer.WriteAsync(42);

// コンシューマー
var value = await channel.Reader.ReadAsync();
```

## PlayerLoopタイミング

```csharp
// 次のFixedUpdateの開始時に再開
await VlkTask.Yield(PlayerLoopTiming.FixedUpdate);

// LateUpdate後に再開
await VlkTask.Yield(PlayerLoopTiming.LastUpdate);
```

## 次のステップ

- [コアコンセプト — 構造体タスク](./concepts/struct-tasks)
- [APIリファレンス — VlkTask](./api/vlk-task)
- [上級 — Burst & ECS](./advanced/burst-ecs)
