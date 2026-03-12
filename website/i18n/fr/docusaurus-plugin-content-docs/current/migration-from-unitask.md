---
sidebar_position: 3
title: Migration depuis UniTask
---

# Migration depuis UniTask

Valkarn Tasks est compatible au niveau de l'API avec UniTask dans la plupart des cas courants. Ce guide couvre les différences.

## Renommage des types

| UniTask | Valkarn Tasks |
|---------|--------------|
| `UniTask` | `VlkTask` |
| `UniTask<T>` | `VlkTask<T>` |
| `UniTaskCompletionSource` | `VlkTaskCompletionSource` |
| `UniTaskCompletionSource<T>` | `VlkTaskCompletionSource<T>` |

## Espace de noms

```csharp
// Avant
using Cysharp.Threading.Tasks;

// Après
using UnaPartidaMas.Valkarn.Tasks;
```

## Auto-annulation

UniTask nécessite une gestion manuelle de `CancellationTokenSource`. Valkarn Tasks la génère :

```csharp
// UniTask (manuel)
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

// Valkarn Tasks (généré par la source)
public partial class EnemyAI : MonoBehaviour
{
    async VlkTask Chase()
    {
        while (true)
        {
            Move();
            await VlkTask.Yield(); // annulé automatiquement à la destruction
        }
    }
}
```

## Timings PlayerLoop

Valkarn Tasks passe de 10 à 16 timings. Tous les timings UniTask sont directement mappés :

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

Nouveaux dans Valkarn Tasks : `PreUpdate`, `LastPreUpdate`, `LastPreLateUpdate`, `PostLateUpdate`, `TimeUpdate`, `LastTimeUpdate`.

## Fire-and-forget

```csharp
// UniTask
UniTask.Void(async () => { ... });

// Valkarn Tasks
VlkTask.Forget(MyMethod());
// ou décorer la méthode :
[FireAndForget]
async VlkTask MyMethod() { ... }
```

## Ce qui est meilleur dans Valkarn Tasks

- **Auto-annulation** via générateur de source — pas de code passe-partout
- **17 règles d'analyseur** — détectez les bugs à la compilation
- **Burst / ECS** — file d'attente de timers native, systèmes ECS asynchrones
- **6 timings PlayerLoop supplémentaires**
- **Pool conscient des threads** — pas d'atomiques sur le thread principal
