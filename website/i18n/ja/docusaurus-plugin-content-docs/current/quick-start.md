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
    async ValkarnTask Start()
    {
        await ValkarnTask.Delay(1000); // 1秒待機、ゼロアロケーション
        Debug.Log("完了！");
    }
}
```

## Destroy時の自動キャンセル

クラスを`partial`として宣言するだけ — ソースジェネレーターが残りを処理します：

```csharp
public partial class EnemyAI : MonoBehaviour
{
    async ValkarnTask ChasePlayer()
    {
        while (true)
        {
            MoveTowards(player.position);
            await ValkarnTask.Yield(); // このGameObjectが破棄されると自動的にキャンセルされる
        }
    }
}
```

`CancellationTokenSource`なし、`OnDestroy`オーバーライドなし、ボイラープレートなし。

## WhenAll

```csharp
// 複数のタスクを待機 — 分解代入をサポート
var (texture, audio, data) = await ValkarnTask.WhenAll(
    LoadTexture(),
    LoadAudio(),
    LoadData()
);
```

## WhenAny

```csharp
// 最初に完了したものが勝ち
var result = await ValkarnTask.WhenAny(FromCache(), FromNetwork());
```

## 値を返す

```csharp
async ValkarnTask<Texture2D> LoadTexture()
{
    await ValkarnTask.SwitchToThreadPool();
    var bytes = File.ReadAllBytes(path);
    await ValkarnTask.SwitchToMainThread();
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
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// LateUpdate後に再開
await ValkarnTask.Yield(PlayerLoopTiming.LastUpdate);
```

## 次のステップ

- [コアコンセプト — 構造体タスク](./concepts/struct-tasks)
- [APIリファレンス — ValkarnTask](./api/vlk-task)
- [上級 — Burst & ECS](./advanced/burst-ecs)
