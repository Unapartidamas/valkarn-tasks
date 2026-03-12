---
sidebar_position: 3
title: Миграция с UniTask
---

# Миграция с UniTask

В большинстве распространённых случаев Valkarn Tasks совместим с UniTask на уровне API. В этом руководстве описаны отличия.

## Переименование типов

| UniTask | Valkarn Tasks |
|---------|--------------|
| `UniTask` | `VlkTask` |
| `UniTask<T>` | `VlkTask<T>` |
| `UniTaskCompletionSource` | `VlkTaskCompletionSource` |
| `UniTaskCompletionSource<T>` | `VlkTaskCompletionSource<T>` |

## Пространство имён

```csharp
// До
using Cysharp.Threading.Tasks;

// После
using UnaPartidaMas.Valkarn.Tasks;
```

## Авто-отмена

UniTask требует ручного управления `CancellationTokenSource`. Valkarn Tasks генерирует его автоматически:

```csharp
// UniTask (вручную)
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

// Valkarn Tasks (генерация исходного кода)
public partial class EnemyAI : MonoBehaviour
{
    async VlkTask Chase()
    {
        while (true)
        {
            Move();
            await VlkTask.Yield(); // автоматически отменяется при Destroy
        }
    }
}
```

## Фазы PlayerLoop

Valkarn Tasks расширяет количество фаз с 10 до 16. Все фазы UniTask отображаются напрямую:

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

Новые фазы в Valkarn Tasks: `PreUpdate`, `LastPreUpdate`, `LastPreLateUpdate`, `PostLateUpdate`, `TimeUpdate`, `LastTimeUpdate`.

## Fire-and-forget

```csharp
// UniTask
UniTask.Void(async () => { ... });

// Valkarn Tasks
VlkTask.Forget(MyMethod());
// или декорируйте метод:
[FireAndForget]
async VlkTask MyMethod() { ... }
```

## Что лучше в Valkarn Tasks

- **Авто-отмена** через генератор исходного кода — никакого шаблонного кода
- **17 правил анализатора** — обнаружение ошибок во время компиляции
- **Burst / ECS** — нативный таймерный хип, асинхронные ECS-системы
- **6 дополнительных фаз PlayerLoop**
- **Пул с учётом потоков** — нет атомарных операций на главном потоке
