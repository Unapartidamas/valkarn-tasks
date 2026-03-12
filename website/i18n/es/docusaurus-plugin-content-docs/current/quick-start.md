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
    async ValkarnTask Start()
    {
        await ValkarnTask.Delay(1000); // 1 segundo, cero asignaciones
        Debug.Log("¡Listo!");
    }
}
```

## Auto-cancel al destruir

Marca la clase como `partial` — el generador de código fuente hace el resto:

```csharp
public partial class EnemyAI : MonoBehaviour
{
    async ValkarnTask ChasePlayer()
    {
        while (true)
        {
            MoveTowards(player.position);
            await ValkarnTask.Yield(); // cancela automáticamente cuando este GameObject es destruido
        }
    }
}
```

Sin `CancellationTokenSource`, sin override de `OnDestroy`, sin código repetitivo.

## WhenAll

```csharp
// Espera múltiples tareas — desestructuración compatible
var (texture, audio, data) = await ValkarnTask.WhenAll(
    LoadTexture(),
    LoadAudio(),
    LoadData()
);
```

## WhenAny

```csharp
// El primero gana
var result = await ValkarnTask.WhenAny(FromCache(), FromNetwork());
```

## Devolver un valor

```csharp
async ValkarnTask<Texture2D> LoadTexture()
{
    await ValkarnTask.SwitchToThreadPool();
    var bytes = File.ReadAllBytes(path);
    await ValkarnTask.SwitchToMainThread();
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
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// Continuar después de LateUpdate
await ValkarnTask.Yield(PlayerLoopTiming.LastUpdate);
```

## Próximos pasos

- [Conceptos fundamentales — Tareas Struct](./concepts/struct-tasks)
- [Referencia API — ValkarnTask](./api/vlk-task)
- [Avanzado — Burst y ECS](./advanced/burst-ecs)
