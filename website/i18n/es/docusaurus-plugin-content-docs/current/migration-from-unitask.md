---
sidebar_position: 3
title: Migración desde UniTask
---

# Migración desde UniTask

Valkarn Tasks es compatible con la API de UniTask en la mayoría de los casos comunes. Esta guía cubre las diferencias.

## Renombrado de tipos

| UniTask | Valkarn Tasks |
|---------|--------------|
| `UniTask` | `ValkarnTask` |
| `UniTask<T>` | `ValkarnTask<T>` |
| `UniTaskCompletionSource` | `ValkarnTaskCompletionSource` |
| `UniTaskCompletionSource<T>` | `ValkarnTaskCompletionSource<T>` |

## Espacio de nombres

```csharp
// Antes
using Cysharp.Threading.Tasks;

// Después
using UnaPartidaMas.Valkarn.Tasks;
```

## Auto-cancel

UniTask requiere gestión manual de `CancellationTokenSource`. Valkarn Tasks lo genera automáticamente:

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

// Valkarn Tasks (generado por código fuente)
public partial class EnemyAI : MonoBehaviour
{
    async ValkarnTask Chase()
    {
        while (true)
        {
            Move();
            await ValkarnTask.Yield(); // cancela automáticamente al Destroy
        }
    }
}
```

## Timings de PlayerLoop

Valkarn Tasks amplía de 10 a 16 timings. Todos los timings de UniTask se mapean directamente:

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

Nuevos en Valkarn Tasks: `PreUpdate`, `LastPreUpdate`, `LastPreLateUpdate`, `PostLateUpdate`, `TimeUpdate`, `LastTimeUpdate`.

## Fire-and-forget

```csharp
// UniTask
UniTask.Void(async () => { ... });

// Valkarn Tasks
ValkarnTask.Forget(MyMethod());
// o decora el método:
[FireAndForget]
async ValkarnTask MyMethod() { ... }
```

## Qué mejora en Valkarn Tasks

- **Auto-cancel** vía generador de código fuente — sin código repetitivo
- **17 reglas del analizador** — detecta errores en tiempo de compilación
- **Burst / ECS** — montón de temporizadores nativo, sistemas ECS asíncronos
- **6 timings de PlayerLoop adicionales**
- **Grupo consciente de hilos** — sin operaciones atómicas en el hilo principal
