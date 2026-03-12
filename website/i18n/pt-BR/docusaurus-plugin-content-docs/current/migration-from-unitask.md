---
sidebar_position: 3
title: Migração do UniTask
---

# Migração do UniTask

O Valkarn Tasks é compatível com a API do UniTask na maioria dos casos comuns. Este guia cobre as diferenças.

## Renomeação de tipos

| UniTask | Valkarn Tasks |
|---------|--------------|
| `UniTask` | `VlkTask` |
| `UniTask<T>` | `VlkTask<T>` |
| `UniTaskCompletionSource` | `VlkTaskCompletionSource` |
| `UniTaskCompletionSource<T>` | `VlkTaskCompletionSource<T>` |

## Namespace

```csharp
// Antes
using Cysharp.Threading.Tasks;

// Depois
using UnaPartidaMas.Valkarn.Tasks;
```

## Cancelamento automático

O UniTask exige gerenciamento manual de `CancellationTokenSource`. O Valkarn Tasks o gera:

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

// Valkarn Tasks (gerado por código-fonte)
public partial class EnemyAI : MonoBehaviour
{
    async VlkTask Chase()
    {
        while (true)
        {
            Move();
            await VlkTask.Yield(); // cancela automaticamente ao Destroy
        }
    }
}
```

## Timings do PlayerLoop

O Valkarn Tasks expande de 10 para 16 timings. Todos os timings do UniTask mapeiam diretamente:

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

Novos no Valkarn Tasks: `PreUpdate`, `LastPreUpdate`, `LastPreLateUpdate`, `PostLateUpdate`, `TimeUpdate`, `LastTimeUpdate`.

## Fire-and-forget

```csharp
// UniTask
UniTask.Void(async () => { ... });

// Valkarn Tasks
VlkTask.Forget(MyMethod());
// ou decore o método:
[FireAndForget]
async VlkTask MyMethod() { ... }
```

## O que é melhor no Valkarn Tasks

- **Cancelamento automático** via gerador de código-fonte — sem boilerplate
- **17 regras de analisador** — detecte bugs em tempo de compilação
- **Burst / ECS** — heap de timers nativa, sistemas ECS assíncronos
- **6 timings extras do PlayerLoop**
- **Pool consciente de thread** — sem operações atômicas na thread principal
