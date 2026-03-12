---
sidebar_position: 2
title: Inicio Rápido
---

# Inicio Rápido

## Método async básico

```csharp
using UnaPartidaMas.Valkarn.Tasks;

public class MyBehaviour : MonoBehaviour
{
    async VlkTask Start()
    {
        await VlkTask.Delay(1000); // 1 segundo, cero asignaciones
        Debug.Log("¡Listo!");
    }
}
```

## Auto-cancel al destruir

Marca la clase como `partial` — el generador de código fuente hace el resto:

```csharp
public partial class EnemyAI : MonoBehaviour
{
    async VlkTask ChasePlayer()
    {
        while (true)
        {
            MoveTowards(player.position);
            await VlkTask.Yield(); // cancela automáticamente cuando este GameObject es destruido
        }
    }
}
```

Sin `CancellationTokenSource`, sin override de `OnDestroy`, sin código repetitivo.

## WhenAll

```csharp
// Espera múltiples tareas — desestructuración compatible
var (texture, audio, data) = await VlkTask.WhenAll(
    LoadTexture(),
    LoadAudio(),
    LoadData()
);
```

## WhenAny

```csharp
// El primero gana
var result = await VlkTask.WhenAny(FromCache(), FromNetwork());
```

## Devolver un valor

```csharp
async VlkTask<Texture2D> LoadTexture()
{
    await VlkTask.SwitchToThreadPool();
    var bytes = File.ReadAllBytes(path);
    await VlkTask.SwitchToMainThread();
    return CreateTexture(bytes);
}
```

## Canales

```csharp
var channel = Channel.CreateBounded<int>(capacity: 16);

// Productor
await channel.Writer.WriteAsync(42);

// Consumidor
var value = await channel.Reader.ReadAsync();
```

## Timing del PlayerLoop

```csharp
// Continuar al inicio del próximo FixedUpdate
await VlkTask.Yield(PlayerLoopTiming.FixedUpdate);

// Continuar después de LateUpdate
await VlkTask.Yield(PlayerLoopTiming.LastUpdate);
```

## Próximos pasos

- [Conceptos fundamentales — Tareas Struct](./concepts/struct-tasks)
- [Referencia API — VlkTask](./api/vlk-task)
- [Avanzado — Burst y ECS](./advanced/burst-ecs)
