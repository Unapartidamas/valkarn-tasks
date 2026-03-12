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
    async VlkTask Start()
    {
        await VlkTask.Delay(1000); // 1 सेकंड, शून्य आवंटन
        Debug.Log("Done!");
    }
}
```

## नष्ट होने पर auto-cancel

क्लास को `partial` mark करें — source generator बाकी काम करेगा:

```csharp
public partial class EnemyAI : MonoBehaviour
{
    async VlkTask ChasePlayer()
    {
        while (true)
        {
            MoveTowards(player.position);
            await VlkTask.Yield(); // जब यह GameObject नष्ट होगा, स्वचालित रूप से cancel हो जाएगा
        }
    }
}
```

कोई `CancellationTokenSource` नहीं, कोई `OnDestroy` override नहीं, कोई boilerplate नहीं।

## WhenAll

```csharp
// कई tasks की प्रतीक्षा करें — destructuring supported
var (texture, audio, data) = await VlkTask.WhenAll(
    LoadTexture(),
    LoadAudio(),
    LoadData()
);
```

## WhenAny

```csharp
// पहला जीतता है
var result = await VlkTask.WhenAny(FromCache(), FromNetwork());
```

## एक value लौटाना

```csharp
async VlkTask<Texture2D> LoadTexture()
{
    await VlkTask.SwitchToThreadPool();
    var bytes = File.ReadAllBytes(path);
    await VlkTask.SwitchToMainThread();
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
await VlkTask.Yield(PlayerLoopTiming.FixedUpdate);

// LateUpdate के बाद continue करें
await VlkTask.Yield(PlayerLoopTiming.LastUpdate);
```

## अगले कदम

- [मूल अवधारणाएँ — Struct Tasks](./concepts/struct-tasks)
- [API संदर्भ — VlkTask](./api/vlk-task)
- [उन्नत — Burst & ECS](./advanced/burst-ecs)
