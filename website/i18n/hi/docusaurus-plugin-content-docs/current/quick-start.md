---
sidebar_position: 2
title: त्वरित प्रारंभ
---

# त्वरित प्रारंभ

## बुनियादी async method

```csharp
using UnaPartidaMas.Valkarn.Tasks;

public class MyBehaviour : MonoBehaviour
{
    async ValkarnTask Start()
    {
        await ValkarnTask.Delay(1000); // 1 सेकंड, शून्य आवंटन
        Debug.Log("Done!");
    }
}
```

## नष्ट होने पर auto-cancel

क्लास को `partial` mark करें — source generator बाकी काम करेगा:

```csharp
public partial class EnemyAI : MonoBehaviour
{
    async ValkarnTask ChasePlayer()
    {
        while (true)
        {
            MoveTowards(player.position);
            await ValkarnTask.Yield(); // जब यह GameObject नष्ट होगा, स्वचालित रूप से cancel हो जाएगा
        }
    }
}
```

कोई `CancellationTokenSource` नहीं, कोई `OnDestroy` override नहीं, कोई boilerplate नहीं।

## WhenAll

```csharp
// कई tasks की प्रतीक्षा करें — destructuring supported
var (texture, audio, data) = await ValkarnTask.WhenAll(
    LoadTexture(),
    LoadAudio(),
    LoadData()
);
```

## WhenAny

```csharp
// पहला जीतता है
var result = await ValkarnTask.WhenAny(FromCache(), FromNetwork());
```

## एक value लौटाना

```csharp
async ValkarnTask<Texture2D> LoadTexture()
{
    await ValkarnTask.SwitchToThreadPool();
    var bytes = File.ReadAllBytes(path);
    await ValkarnTask.SwitchToMainThread();
    return CreateTexture(bytes);
}
```

## Channels

```csharp
var channel = Channel.CreateBounded<int>(capacity: 16);

// Producer
await channel.Writer.WriteAsync(42);

// Consumer
var value = await channel.Reader.ReadAsync();
```

## PlayerLoop timing

```csharp
// अगले FixedUpdate की शुरुआत में continue करें
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// LateUpdate के बाद continue करें
await ValkarnTask.Yield(PlayerLoopTiming.LastUpdate);
```

## अगले कदम

- [मूल अवधारणाएँ — Struct Tasks](./concepts/struct-tasks)
- [API संदर्भ — ValkarnTask](./api/vlk-task)
- [उन्नत — Burst & ECS](./advanced/burst-ecs)
