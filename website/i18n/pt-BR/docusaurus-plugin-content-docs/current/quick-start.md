---
sidebar_position: 2
title: Início Rápido
---

# Início Rápido

## Método async básico

```csharp
using UnaPartidaMas.Valkarn.Tasks;

public class MyBehaviour : MonoBehaviour
{
    async VlkTask Start()
    {
        await VlkTask.Delay(1000); // 1 segundo, zero alocação
        Debug.Log("Concluído!");
    }
}
```

## Cancelamento automático ao destruir

Marque a classe como `partial` — o gerador de código-fonte faz o resto:

```csharp
public partial class EnemyAI : MonoBehaviour
{
    async VlkTask ChasePlayer()
    {
        while (true)
        {
            MoveTowards(player.position);
            await VlkTask.Yield(); // cancela automaticamente quando este GameObject é destruído
        }
    }
}
```

Sem `CancellationTokenSource`, sem sobrescrita de `OnDestroy`, sem boilerplate.

## WhenAll

```csharp
// Aguardar múltiplas tasks — desestruturação suportada
var (texture, audio, data) = await VlkTask.WhenAll(
    LoadTexture(),
    LoadAudio(),
    LoadData()
);
```

## WhenAny

```csharp
// O primeiro a concluir vence
var result = await VlkTask.WhenAny(FromCache(), FromNetwork());
```

## Retornando um valor

```csharp
async VlkTask<Texture2D> LoadTexture()
{
    await VlkTask.SwitchToThreadPool();
    var bytes = File.ReadAllBytes(path);
    await VlkTask.SwitchToMainThread();
    return CreateTexture(bytes);
}
```

## Canais

```csharp
var channel = Channel.CreateBounded<int>(capacity: 16);

// Produtor
await channel.Writer.WriteAsync(42);

// Consumidor
var value = await channel.Reader.ReadAsync();
```

## Timing do PlayerLoop

```csharp
// Continuar no início do próximo FixedUpdate
await VlkTask.Yield(PlayerLoopTiming.FixedUpdate);

// Continuar após o LateUpdate
await VlkTask.Yield(PlayerLoopTiming.LastUpdate);
```

## Próximos passos

- [Conceitos fundamentais — Struct Tasks](./concepts/struct-tasks)
- [Referência da API — VlkTask](./api/vlk-task)
- [Avançado — Burst & ECS](./advanced/burst-ecs)
