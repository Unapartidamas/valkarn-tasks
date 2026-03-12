---
sidebar_position: 3
title: Migration von UniTask
---

# Migration von UniTask

Valkarn Tasks ist in den meisten gängigen Fällen API-kompatibel mit UniTask. Dieser Leitfaden beschreibt die Unterschiede.

## Typumbenennung

| UniTask | Valkarn Tasks |
|---------|--------------|
| `UniTask` | `VlkTask` |
| `UniTask<T>` | `VlkTask<T>` |
| `UniTaskCompletionSource` | `VlkTaskCompletionSource` |
| `UniTaskCompletionSource<T>` | `VlkTaskCompletionSource<T>` |

## Namespace

```csharp
// Vorher
using Cysharp.Threading.Tasks;

// Nachher
using UnaPartidaMas.Valkarn.Tasks;
```

## Auto-Abbruch

UniTask erfordert manuelles `CancellationTokenSource`-Management. Valkarn Tasks generiert es:

```csharp
// UniTask (manuell)
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

// Valkarn Tasks (quellgeneriert)
public partial class EnemyAI : MonoBehaviour
{
    async VlkTask Chase()
    {
        while (true)
        {
            Move();
            await VlkTask.Yield(); // wird bei Destroy automatisch abgebrochen
        }
    }
}
```

## PlayerLoop-Zeitpunkte

Valkarn Tasks erweitert von 10 auf 16 Zeitpunkte. Alle UniTask-Zeitpunkte werden direkt abgebildet:

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

Neu in Valkarn Tasks: `PreUpdate`, `LastPreUpdate`, `LastPreLateUpdate`, `PostLateUpdate`, `TimeUpdate`, `LastTimeUpdate`.

## Fire-and-Forget

```csharp
// UniTask
UniTask.Void(async () => { ... });

// Valkarn Tasks
VlkTask.Forget(MyMethod());
// oder die Methode dekorieren:
[FireAndForget]
async VlkTask MyMethod() { ... }
```

## Was in Valkarn Tasks besser ist

- **Auto-Abbruch** per Quellgenerator — kein Boilerplate
- **17 Analyzer-Regeln** — Fehler zur Kompilierzeit abfangen
- **Burst / ECS** — nativer Timer-Heap, asynchrone ECS-Systeme
- **6 zusätzliche PlayerLoop-Zeitpunkte**
- **Thread-bewusster Pool** — keine Atomics auf dem Haupt-Thread
