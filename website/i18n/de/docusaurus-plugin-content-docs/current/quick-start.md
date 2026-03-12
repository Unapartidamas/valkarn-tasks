---
sidebar_position: 2
title: Schnellstart
---

# Schnellstart

## Einfache asynchrone Methode

```csharp
using UnaPartidaMas.Valkarn.Tasks;

public class MyBehaviour : MonoBehaviour
{
    async ValkarnTask Start()
    {
        await ValkarnTask.Delay(1000); // 1 Sekunde, null Allokation
        Debug.Log("Fertig!");
    }
}
```

## Auto-Abbruch bei Destroy

Markieren Sie die Klasse als `partial` — der Quellgenerator übernimmt den Rest:

```csharp
public partial class EnemyAI : MonoBehaviour
{
    async ValkarnTask ChasePlayer()
    {
        while (true)
        {
            MoveTowards(player.position);
            await ValkarnTask.Yield(); // wird automatisch abgebrochen, wenn dieses GameObject zerstört wird
        }
    }
}
```

Kein `CancellationTokenSource`, kein `OnDestroy`-Override, kein Boilerplate.

## WhenAll

```csharp
// Auf mehrere Tasks warten — Destructuring wird unterstützt
var (texture, audio, data) = await ValkarnTask.WhenAll(
    LoadTexture(),
    LoadAudio(),
    LoadData()
);
```

## WhenAny

```csharp
// Der erste gewinnt
var result = await ValkarnTask.WhenAny(FromCache(), FromNetwork());
```

## Einen Wert zurückgeben

```csharp
async ValkarnTask<Texture2D> LoadTexture()
{
    await ValkarnTask.SwitchToThreadPool();
    var bytes = File.ReadAllBytes(path);
    await ValkarnTask.SwitchToMainThread();
    return CreateTexture(bytes);
}
```

## Channels

```csharp
var channel = Channel.CreateBounded<int>(capacity: 16);

// Produzent
await channel.Writer.WriteAsync(42);

// Konsument
var value = await channel.Reader.ReadAsync();
```

## PlayerLoop-Zeitpunkt

```csharp
// Am Anfang des nächsten FixedUpdate fortsetzen
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// Nach LateUpdate fortsetzen
await ValkarnTask.Yield(PlayerLoopTiming.LastUpdate);
```

## Nächste Schritte

- [Kernkonzepte — Struct Tasks](./concepts/struct-tasks)
- [API-Referenz — ValkarnTask](./api/vlk-task)
- [Erweitert — Burst & ECS](./advanced/burst-ecs)
