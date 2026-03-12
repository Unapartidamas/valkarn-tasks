---
sidebar_position: 2
title: Démarrage rapide
---

# Démarrage rapide

## Méthode async de base

```csharp
using UnaPartidaMas.Valkarn.Tasks;

public class MyBehaviour : MonoBehaviour
{
    async VlkTask Start()
    {
        await VlkTask.Delay(1000); // 1 seconde, zéro allocation
        Debug.Log("Terminé !");
    }
}
```

## Auto-annulation à la destruction

Déclarez la classe `partial` — le générateur de source fait le reste :

```csharp
public partial class EnemyAI : MonoBehaviour
{
    async VlkTask ChasePlayer()
    {
        while (true)
        {
            MoveTowards(player.position);
            await VlkTask.Yield(); // annulé automatiquement quand ce GameObject est détruit
        }
    }
}
```

Pas de `CancellationTokenSource`, pas de surcharge de `OnDestroy`, pas de code passe-partout.

## WhenAll

```csharp
// Attendre plusieurs tâches — déstructuration supportée
var (texture, audio, data) = await VlkTask.WhenAll(
    LoadTexture(),
    LoadAudio(),
    LoadData()
);
```

## WhenAny

```csharp
// Le premier qui termine gagne
var result = await VlkTask.WhenAny(FromCache(), FromNetwork());
```

## Retourner une valeur

```csharp
async VlkTask<Texture2D> LoadTexture()
{
    await VlkTask.SwitchToThreadPool();
    var bytes = File.ReadAllBytes(path);
    await VlkTask.SwitchToMainThread();
    return CreateTexture(bytes);
}
```

## Canaux

```csharp
var channel = Channel.CreateBounded<int>(capacity: 16);

// Producteur
await channel.Writer.WriteAsync(42);

// Consommateur
var value = await channel.Reader.ReadAsync();
```

## Timing PlayerLoop

```csharp
// Reprendre au début du prochain FixedUpdate
await VlkTask.Yield(PlayerLoopTiming.FixedUpdate);

// Reprendre après LateUpdate
await VlkTask.Yield(PlayerLoopTiming.LastUpdate);
```

## Prochaines étapes

- [Concepts fondamentaux — Tâches Struct](./concepts/struct-tasks)
- [Référence API — VlkTask](./api/vlk-task)
- [Avancé — Burst & ECS](./advanced/burst-ecs)
