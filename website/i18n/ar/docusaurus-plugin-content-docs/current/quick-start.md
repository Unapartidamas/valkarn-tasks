---
sidebar_position: 2
title: البدء السريع
---

# البدء السريع

## طريقة غير متزامنة أساسية

```csharp
using UnaPartidaMas.Valkarn.Tasks;

public class MyBehaviour : MonoBehaviour
{
    async VlkTask Start()
    {
        await VlkTask.Delay(1000); // ثانية واحدة، صفر تخصيص
        Debug.Log("Done!");
    }
}
```

## إلغاء تلقائي عند التدمير

اجعل الفئة `partial` — مولّد الكود يتولى الباقي:

```csharp
public partial class EnemyAI : MonoBehaviour
{
    async VlkTask ChasePlayer()
    {
        while (true)
        {
            MoveTowards(player.position);
            await VlkTask.Yield(); // يُلغى تلقائيًا عند تدمير هذا الكائن
        }
    }
}
```

لا `CancellationTokenSource`، لا تجاوز `OnDestroy`، لا بويلربلايت.

## WhenAll

```csharp
// انتظار مهام متعددة — تفكيك الصفوف مدعوم
var (texture, audio, data) = await VlkTask.WhenAll(
    LoadTexture(),
    LoadAudio(),
    LoadData()
);
```

## WhenAny

```csharp
// الأول يفوز
var result = await VlkTask.WhenAny(FromCache(), FromNetwork());
```

## إرجاع قيمة

```csharp
async VlkTask<Texture2D> LoadTexture()
{
    await VlkTask.SwitchToThreadPool();
    var bytes = File.ReadAllBytes(path);
    await VlkTask.SwitchToMainThread();
    return CreateTexture(bytes);
}
```

## القنوات

```csharp
var channel = Channel.CreateBounded<int>(capacity: 16);

// المنتج
await channel.Writer.WriteAsync(42);

// المستهلك
var value = await channel.Reader.ReadAsync();
```

## توقيت PlayerLoop

```csharp
// الاستمرار في بداية FixedUpdate التالي
await VlkTask.Yield(PlayerLoopTiming.FixedUpdate);

// الاستمرار بعد LateUpdate
await VlkTask.Yield(PlayerLoopTiming.LastUpdate);
```

## الخطوات التالية

- [المفاهيم الأساسية — المهام المبنية على البنى](./concepts/struct-tasks)
- [مرجع API — VlkTask](./api/vlk-task)
- [متقدم — Burst وECS](./advanced/burst-ecs)
