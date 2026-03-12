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
    async VlkTask Start()
    {
        await VlkTask.Delay(1000); // 1 Sekunde, null Allokation
        Debug.Log("Fertig!");
    }
}
```

## Auto-Abbruch bei Destroy

Markieren Sie die Klasse als `partial` — der Quellgenerator übernimmt den Rest:

```csharp
public partial class EnemyAI : MonoBehaviour
{
    async VlkTask ChasePlayer()
    {
        while (true)
        {
            MoveTowards(player.position);
            await VlkTask.Yield(); // wird automatisch abgebrochen, wenn dieses GameObject zerstört wird
        }
    }
}
```

Kein `CancellationTokenSource`, kein `OnDestroy`-Override, kein Boilerplate.

## WhenAll

```csharp
// Auf mehrere Tasks warten — Destructuring wird unterstützt
var (texture, audio, data) = await VlkTask.WhenAll(
    LoadTexture(),
    LoadAudio(),
    LoadData()
);
```

## WhenAny

```csharp
// Der erste gewinnt
var result = await VlkTask.WhenAny(FromCache(), FromNetwork());
```

## Einen Wert zurückgeben

```csharp
async VlkTask<Texture2D> LoadTexture()
{
    await VlkTask.SwitchToThreadPool();
    var bytes = File.ReadAllBytes(path);
    await VlkTask.SwitchToMainThread();
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
await VlkTask.Yield(PlayerLoopTiming.FixedUpdate);

// Nach LateUpdate fortsetzen
await VlkTask.Yield(PlayerLoopTiming.LastUpdate);
```

## Nächste Schritte

- [Kernkonzepte — Struct Tasks](./concepts/struct-tasks)
- [API-Referenz — VlkTask](./api/vlk-task)
- [Erweitert — Burst & ECS](./advanced/burst-ecs)
