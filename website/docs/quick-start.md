---
sidebar_position: 2
title: Quick Start
---

# Quick Start

## Basic async method

```csharp
using UnaPartidaMas.Valkarn.Tasks;

public class MyBehaviour : MonoBehaviour
{
    async ValkarnTask Start()
    {
        await ValkarnTask.Delay(1000); // 1 second, zero allocation
        Debug.Log("Done!");
    }
}
```

## Auto-cancel on destroy

Mark the class `partial` — the source generator does the rest:

```csharp
public partial class EnemyAI : MonoBehaviour
{
    async ValkarnTask ChasePlayer()
    {
        while (true)
        {
            MoveTowards(player.position);
            await ValkarnTask.Yield(); // automatically cancels when this GameObject is destroyed
        }
    }
}
```

No `CancellationTokenSource`, no `OnDestroy` override, no boilerplate.

## WhenAll

```csharp
// Wait for multiple tasks — destructuring supported
var (texture, audio, data) = await ValkarnTask.WhenAll(
    LoadTexture(),
    LoadAudio(),
    LoadData()
);
```

## WhenAny

```csharp
// First one wins
var result = await ValkarnTask.WhenAny(FromCache(), FromNetwork());
```

## Returning a value

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
// Continue at the start of the next FixedUpdate
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// Continue after LateUpdate
await ValkarnTask.Yield(PlayerLoopTiming.LastUpdate);
```

## Next steps

- [Core Concepts — Struct Tasks](./concepts/struct-tasks)
- [API Reference — ValkarnTask](./api/vlk-task)
- [Advanced — Burst & ECS](./advanced/burst-ecs)
