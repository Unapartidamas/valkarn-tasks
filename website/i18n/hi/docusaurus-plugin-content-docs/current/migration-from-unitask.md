---
sidebar_position: 3
title: UniTask से Migration
---

# UniTask से Migration

Valkarn Tasks अधिकांश सामान्य cases में UniTask के साथ API-compatible है। यह गाइड अंतरों को cover करती है।

## Type नाम बदलाव

| UniTask | Valkarn Tasks |
|---------|--------------|
| `UniTask` | `ValkarnTask` |
| `UniTask<T>` | `ValkarnTask<T>` |
| `UniTaskCompletionSource` | `ValkarnTaskCompletionSource` |
| `UniTaskCompletionSource<T>` | `ValkarnTaskCompletionSource<T>` |

## Namespace

```csharp
// पहले
using Cysharp.Threading.Tasks;

// बाद में
using UnaPartidaMas.Valkarn.Tasks;
```

## Auto-cancel

UniTask को manual `CancellationTokenSource` management की आवश्यकता है। Valkarn Tasks इसे generate करता है:

```csharp
// UniTask (मैनुअल)
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

// Valkarn Tasks (source-generated)
public partial class EnemyAI : MonoBehaviour
{
    async ValkarnTask Chase()
    {
        while (true)
        {
            Move();
            await ValkarnTask.Yield(); // Destroy पर स्वचालित रूप से cancel होता है
        }
    }
}
```

## PlayerLoop timings

Valkarn Tasks 10 से 16 timings तक expand करता है। सभी UniTask timings सीधे map होती हैं:

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

Valkarn Tasks में नए: `PreUpdate`, `LastPreUpdate`, `LastPreLateUpdate`, `PostLateUpdate`, `TimeUpdate`, `LastTimeUpdate`।

## Fire-and-forget

```csharp
// UniTask
UniTask.Void(async () => { ... });

// Valkarn Tasks
ValkarnTask.Forget(MyMethod());
// या method को decorate करें:
[FireAndForget]
async ValkarnTask MyMethod() { ... }
```

## Valkarn Tasks में क्या बेहतर है

- **Auto-cancel** source generator के माध्यम से — कोई boilerplate नहीं
- **17 analyzer नियम** — compile time पर bugs पकड़ें
- **Burst / ECS** — native timer heap, async ECS systems
- **6 अतिरिक्त PlayerLoop timings**
- **Thread-aware pool** — मुख्य thread पर कोई atomics नहीं
