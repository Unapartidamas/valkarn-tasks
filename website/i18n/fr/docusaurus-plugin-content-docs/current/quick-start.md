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
    async ValkarnTask Start()
    {
        await ValkarnTask.Delay(1000); // 1 seconde, zéro allocation
        Debug.Log("Terminé !");
    }
}
```

## Auto-annulation à la destruction

Déclarez la classe `partial` — le générateur de source fait le reste :

```csharp
public partial class EnemyAI : MonoBehaviour
{
    async ValkarnTask ChasePlayer()
    {
        while (true)
        {
            MoveTowards(player.position);
            await ValkarnTask.Yield(); // annulé automatiquement quand ce GameObject est détruit
        }
    }
}
```

Pas de `CancellationTokenSource`, pas de surcharge de `OnDestroy`, pas de code passe-partout.

## WhenAll

```csharp
// Attendre plusieurs tâches — déstructuration supportée
var (texture, audio, data) = await ValkarnTask.WhenAll(
    LoadTexture(),
    LoadAudio(),
    LoadData()
);
```

## WhenAny

```csharp
// Le premier qui termine gagne
var result = await ValkarnTask.WhenAny(FromCache(), FromNetwork());
```

## Retourner une valeur

```csharp
async ValkarnTask<Texture2D> LoadTexture()
{
    await ValkarnTask.SwitchToThreadPool();
    var bytes = File.ReadAllBytes(path);
    await ValkarnTask.SwitchToMainThread();
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
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// Reprendre après LateUpdate
await ValkarnTask.Yield(PlayerLoopTiming.LastUpdate);
```

## Prochaines étapes

- [Concepts fondamentaux — Tâches Struct](./concepts/struct-tasks)
- [Référence API — ValkarnTask](./api/vlk-task)
- [Avancé — Burst & ECS](./advanced/burst-ecs)
