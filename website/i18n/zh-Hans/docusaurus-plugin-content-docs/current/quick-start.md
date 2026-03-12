---
sidebar_position: 2
title: 快速开始
---

# 快速开始

## 基本异步方法

```csharp
using UnaPartidaMas.Valkarn.Tasks;

public class MyBehaviour : MonoBehaviour
{
    async ValkarnTask Start()
    {
        await ValkarnTask.Delay(1000); // 1 秒，零内存分配
        Debug.Log("Done!");
    }
}
```

## 销毁时自动取消

将类声明为 `partial`——源码生成器会完成其余工作：

```csharp
public partial class EnemyAI : MonoBehaviour
{
    async ValkarnTask ChasePlayer()
    {
        while (true)
        {
            MoveTowards(player.position);
            await ValkarnTask.Yield(); // 当此 GameObject 被销毁时自动取消
        }
    }
}
```

无需 `CancellationTokenSource`，无需覆写 `OnDestroy`，无样板代码。

## WhenAll

```csharp
// 等待多个任务——支持解构
var (texture, audio, data) = await ValkarnTask.WhenAll(
    LoadTexture(),
    LoadAudio(),
    LoadData()
);
```

## WhenAny

```csharp
// 第一个完成的胜出
var result = await ValkarnTask.WhenAny(FromCache(), FromNetwork());
```

## 返回值

```csharp
async ValkarnTask<Texture2D> LoadTexture()
{
    await ValkarnTask.SwitchToThreadPool();
    var bytes = File.ReadAllBytes(path);
    await ValkarnTask.SwitchToMainThread();
    return CreateTexture(bytes);
}
```

## 通道

```csharp
var channel = Channel.CreateBounded<int>(capacity: 16);

// 生产者
await channel.Writer.WriteAsync(42);

// 消费者
var value = await channel.Reader.ReadAsync();
```

## PlayerLoop 时机

```csharp
// 在下一次 FixedUpdate 开始时继续
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// 在 LateUpdate 之后继续
await ValkarnTask.Yield(PlayerLoopTiming.LastUpdate);
```

## 后续步骤

- [核心概念——结构体任务](./concepts/struct-tasks)
- [API 参考——ValkarnTask](./api/vlk-task)
- [进阶——Burst 与 ECS](./advanced/burst-ecs)
