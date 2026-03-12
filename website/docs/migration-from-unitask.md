---
sidebar_position: 3
title: Migration from UniTask
---

# Migration from UniTask

Valkarn Tasks is API-compatible with UniTask in most common cases. This guide covers the differences.

## Type rename

| UniTask | Valkarn Tasks |
|---------|--------------|
| `UniTask` | `ValkarnTask` |
| `UniTask<T>` | `ValkarnTask<T>` |
| `UniTaskCompletionSource` | `ValkarnTaskCompletionSource` |
| `UniTaskCompletionSource<T>` | `ValkarnTaskCompletionSource<T>` |

## Namespace

```csharp
// Before
using Cysharp.Threading.Tasks;

// After
using UnaPartidaMas.Valkarn.Tasks;
```

## Auto-cancel

UniTask requires manual `CancellationTokenSource` management. Valkarn Tasks generates it:

```csharp
// UniTask (manual)
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
            await ValkarnTask.Yield(); // cancels automatically on Destroy
        }
    }
}
```

## PlayerLoop timings

Valkarn Tasks expands from 10 to 16 timings. All UniTask timings map directly:

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

New in Valkarn Tasks: `PreUpdate`, `LastPreUpdate`, `LastPreLateUpdate`, `PostLateUpdate`, `TimeUpdate`, `LastTimeUpdate`.

## Fire-and-forget

```csharp
// UniTask
UniTask.Void(async () => { ... });

// Valkarn Tasks
ValkarnTask.Forget(MyMethod());
// or decorate the method:
[FireAndForget]
async ValkarnTask MyMethod() { ... }
```

## What's better in Valkarn Tasks

- **Auto-cancel** via source generator — no boilerplate
- **17 analyzer rules** — catch bugs at compile time
- **Burst / ECS** — native timer heap, async ECS systems
- **6 extra PlayerLoop timings**
- **Thread-aware pool** — no atomics on main thread
