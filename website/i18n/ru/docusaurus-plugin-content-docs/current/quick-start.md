---
sidebar_position: 2
title: Быстрый старт
---

# Быстрый старт

## Базовый асинхронный метод

```csharp
using UnaPartidaMas.Valkarn.Tasks;

public class MyBehaviour : MonoBehaviour
{
    async VlkTask Start()
    {
        await VlkTask.Delay(1000); // 1 секунда, без аллокаций
        Debug.Log("Done!");
    }
}
```

## Авто-отмена при уничтожении

Объявите класс `partial` — генератор исходного кода сделает всё остальное:

```csharp
public partial class EnemyAI : MonoBehaviour
{
    async VlkTask ChasePlayer()
    {
        while (true)
        {
            MoveTowards(player.position);
            await VlkTask.Yield(); // автоматически отменяется при уничтожении этого GameObject
        }
    }
}
```

Никакого `CancellationTokenSource`, никакого переопределения `OnDestroy`, никакого шаблонного кода.

## WhenAll

```csharp
// Ожидание нескольких задач — поддерживается деструктуризация
var (texture, audio, data) = await VlkTask.WhenAll(
    LoadTexture(),
    LoadAudio(),
    LoadData()
);
```

## WhenAny

```csharp
// Побеждает первый завершившийся
var result = await VlkTask.WhenAny(FromCache(), FromNetwork());
```

## Возврат значения

```csharp
async VlkTask<Texture2D> LoadTexture()
{
    await VlkTask.SwitchToThreadPool();
    var bytes = File.ReadAllBytes(path);
    await VlkTask.SwitchToMainThread();
    return CreateTexture(bytes);
}
```

## Каналы

```csharp
var channel = Channel.CreateBounded<int>(capacity: 16);

// Производитель
await channel.Writer.WriteAsync(42);

// Потребитель
var value = await channel.Reader.ReadAsync();
```

## Фаза PlayerLoop

```csharp
// Продолжить в начале следующего FixedUpdate
await VlkTask.Yield(PlayerLoopTiming.FixedUpdate);

// Продолжить после LateUpdate
await VlkTask.Yield(PlayerLoopTiming.LastUpdate);
```

## Следующие шаги

- [Основные концепции — Задачи на структурах](./concepts/struct-tasks)
- [Справочник API — VlkTask](./api/vlk-task)
- [Продвинутые темы — Burst и ECS](./advanced/burst-ecs)
