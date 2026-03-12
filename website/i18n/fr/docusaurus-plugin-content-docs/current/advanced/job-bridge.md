---
sidebar_position: 2
title: Ponts Job & Awaitable
---

# Ponts Job & Awaitable

Valkarn Tasks fournit un ensemble de types pont qui connectent le système de Jobs Unity et l'API `Awaitable` au pipeline `ValkarnTask`. Chaque pont est une couche mince minimisant les allocations ; il n'y a pas de magie cachée.

Tous les types décrits ici sont dans l'espace de noms `UnaPartidaMas.Valkarn.Tasks.Bridge` et sont gardés par `#if UNITY_5_3_OR_NEWER` (ou `#if UNITY_2023_1_OR_NEWER` pour le support Awaitable).

---

## JobHandleExtensions — attendre un seul JobHandle

Le pont le plus simple. Appelez `.ToValkarnTask()` sur n'importe quel `JobHandle` pour obtenir en retour un `ValkarnTask` qui se termine quand le job se termine.

```csharp
public static ValkarnTask ToValkarnTask(
    this JobHandle handle,
    PlayerLoopTiming timing          = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

### Comment ça fonctionne

1. **Chemin rapide.** Si `handle.IsCompleted` est déjà vrai, `handle.Complete()` est appelé immédiatement et `ValkarnTask.CompletedTask` est retourné — zéro allocation, pas d'enregistrement PlayerLoop.
2. **Chemin normal.** Un `JobHandlePromise` mis en pool est loué, enregistré sur le PlayerLoop au `timing` donné, et retourné enveloppé dans un `ValkarnTask`. À chaque frame, `MoveNext()` appelle `JobHandle.ScheduleBatchedJobs()` (pour vider la file de jobs en mode édition et batch) puis vérifie `handle.IsCompleted`. Quand le handle est terminé, la promise termine la tâche et se retourne elle-même au pool.
3. **Annulation.** Si le `CancellationToken` se déclenche, le handle est forcément terminé (`handle.Complete()` est toujours appelé pour éviter les fuites du système de jobs) et la tâche passe à l'état annulé.

### Utilisation de base

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Jobs;

// Planifier un job et l'attendre immédiatement.
var handle = myJob.Schedule();
await handle.ToValkarnTask();

// Avec un timing non par défaut et une annulation.
await handle.ToValkarnTask(PlayerLoopTiming.PreUpdate, destroyCancellationToken);
```

---

## JobHandleWhenAll — attendre plusieurs JobHandles en parallèle

Quand vous avez besoin de planifier plusieurs jobs indépendants et de ne reprendre qu'après que tous sont terminés, utilisez `JobHandleExtensions.WhenAll`.

```csharp
// Surcharge la plus simple : attend tous les handles au timing Update.
public static ValkarnTask WhenAll(params JobHandle[] handles)

// Surcharge complète : timing et annulation configurables.
public static ValkarnTask WhenAll(
    JobHandle[]       handles,
    PlayerLoopTiming  timing,
    CancellationToken cancellationToken = default)

// Alias méthode d'extension pour la surcharge complète.
public static ValkarnTask ToValkarnTask(
    this JobHandle[]  handles,
    PlayerLoopTiming  timing              = PlayerLoopTiming.Update,
    CancellationToken cancellationToken   = default)
```

### Comment ça fonctionne

- **Chemin rapide.** Si chaque handle dans le tableau est déjà terminé, tous les handles sont finalisés et `ValkarnTask.CompletedTask` est retourné immédiatement.
- **Tableau vide.** Retourne `ValkarnTask.CompletedTask`.
- **Chemin normal.** Un `JobHandleArrayPromise` mis en pool est créé. En interne il loue un `JobHandle[]` depuis `ArrayPool<JobHandle>.Shared` (évitant l'allocation sur le tas par appel), copie les handles d'entrée dedans, et s'enregistre sur le PlayerLoop. À chaque frame il itère uniquement les handles encore en attente en utilisant une boucle de compaction-swap-and-shrink, et appelle `JobHandle.ScheduleBatchedJobs()` pour garder les workers en marche.
- **Annulation.** Tous les handles restants sont forcément terminés et la tâche est annulée.

### Utilisation

```csharp
var handleA = jobA.Schedule();
var handleB = jobB.Schedule();
var handleC = jobC.Schedule();

// Attendre les trois.
await JobHandleExtensions.WhenAll(handleA, handleB, handleC);

// Ou en utilisant la méthode d'extension sur un tableau.
JobHandle[] handles = { handleA, handleB, handleC };
await handles.ToValkarnTask();
```

---

## TempNativeArrayScope — durée de vie NativeArray à travers les points await

### Le problème

`NativeArray<T>` alloué avec `Allocator.TempJob` a une durée de vie courte. Si vous en allouez un, planifiez un job, `await` le handle du job, et oubliez de disposer le tableau, le système de sécurité Unity signalera une fuite mémoire. Utiliser un simple `try`/`finally` fonctionne mais est facile à rater dans une longue méthode async.

`TempNativeArrayScope<T>` est une struct qui enveloppe un `NativeArray<T>` et le dispose quand la portée se termine, en utilisant l'instruction `using` — le pattern RAII appliqué à la mémoire native.

### API

```csharp
public struct TempNativeArrayScope<T> : IDisposable where T : struct
{
    // Accède au tableau enveloppé. Lève ObjectDisposedException si déjà disposé.
    public NativeArray<T> Array { get; }

    // True si la portée n'a pas été disposée et que le tableau est créé.
    public bool IsCreated { get; }

    // Alloue un nouveau NativeArray<T> avec Allocator.TempJob et en prend la propriété.
    public static TempNativeArrayScope<T> Create(int length);

    // Prend la propriété d'un NativeArray<T> déjà alloué.
    public static TempNativeArrayScope<T> Wrap(NativeArray<T> existing);

    // Dispose le tableau. Idempotent : sûr à appeler plusieurs fois.
    public void Dispose();
}

// Helper non-générique (commodité d'inférence de type).
public static class TempNativeArrayScope
{
    public static TempNativeArrayScope<T> Create<T>(int length) where T : struct;
    public static TempNativeArrayScope<T> Wrap<T>(NativeArray<T> existing) where T : struct;
}
```

`Dispose` utilise un flag `int` simple plutôt que `Interlocked` parce que la portée est conçue pour une utilisation mono-threadée sur le thread principal via `using var`.

### Utilisation

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Collections;
using Unity.Jobs;

async ValkarnTask ProcessDataAsync(int count, CancellationToken ct)
{
    // L'instruction using garantit que Dispose() est appelé quand la portée se termine,
    // que ce soit par completion normale, exception ou annulation.
    using var inputScope  = TempNativeArrayScope.Create<float>(count);
    using var outputScope = TempNativeArrayScope.Create<float>(count);

    NativeArray<float> input  = inputScope.Array;
    NativeArray<float> output = outputScope.Array;

    // Remplir l'entrée, planifier le job.
    var job = new MyProcessingJob { Input = input, Output = output };
    var handle = job.Schedule(count, 64);

    // Attendre sans bloquer le thread principal.
    // Les NativeArrays restent valides — le job est toujours en cours.
    await handle.ToValkarnTask(cancellationToken: ct);

    // Le job est terminé. Lire les résultats ici.
    float total = 0f;
    for (int i = 0; i < count; i++)
        total += output[i];

    UnityEngine.Debug.Log($"Somme : {total}");

    // inputScope.Dispose() et outputScope.Dispose() s'exécutent automatiquement ici.
}
```

Vous pouvez également envelopper un tableau que vous avez déjà alloué :

```csharp
var existing = new NativeArray<int>(1024, Allocator.TempJob);
using var scope = TempNativeArrayScope.Wrap(existing);
// scope possède existing et le disposera.
```

### Piège courant : durée de vie NativeArray sans portée

Ce pattern est cassé et causera une erreur du système de sécurité :

```csharp
// MAUVAIS : le tableau peut survivre au job ou être perdu si une exception se produit.
var array = new NativeArray<float>(1024, Allocator.TempJob);
var handle = job.Schedule(1024, 64);
await handle.ToValkarnTask(); // point de suspension — le tableau doit rester en vie
array.Dispose();          // jamais atteint si une exception se déclenche ci-dessus
```

Utilisez `TempNativeArrayScope` ou un `try`/`finally` pour garantir la disposition dans tous les chemins de code.

---

## AwaitableBridge — convertir Unity Awaitable en ValkarnTask

`AwaitableBridge` fournit des méthodes d'extension pour convertir les types `Awaitable` et `Awaitable<T>` d'Unity (disponibles depuis Unity 2023.1) en awaiters compatibles `ValkarnTask`.

**Remarque :** `Awaitable` a aussi son propre `GetAwaiter()`. Parce que la résolution de surcharge C# préfère toujours les méthodes d'instance aux méthodes d'extension, écrire `await myAwaitable` dans une méthode `async ValkarnTask` fonctionne déjà correctement — l'awaiter d'Unity implémente `ICriticalNotifyCompletion` et le builder `ValkarnTask` l'accepte. Les méthodes d'extension `.AsValkarnTask()` ne sont nécessaires que quand vous voulez passer un `Awaitable` à un combinateur (`ValkarnTask.WhenAll`, `ValkarnTask.WhenAny`) ou le stocker comme variable `ValkarnTask`.

Ce fichier est gardé par `#if UNITY_2023_1_OR_NEWER`.

### API

```csharp
// Convertir Awaitable en un awaiter compatible ValkarnTask.
public static AwaitableValkarnTaskAwaiter   AsValkarnTask(this Awaitable awaitable)

// Convertir Awaitable<T> en un awaiter compatible ValkarnTask.
public static AwaitableValkarnTaskAwaiter<T> AsValkarnTask<T>(this Awaitable<T> awaitable)
```

Les deux awaiters implémentent `ICriticalNotifyCompletion`, ce qui saute la capture d'`ExecutionContext`. Ils délèguent `IsCompleted`, `GetResult` et `OnCompleted` directement à l'awaiter Unity enveloppé.

### Utilisation

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnityEngine;

// Await direct — fonctionne sans conversion dans une méthode async ValkarnTask.
async ValkarnTask DirectExample()
{
    await Awaitable.NextFrameAsync();       // Pas de conversion nécessaire.
    await Awaitable.WaitForSecondsAsync(1f);
}

// Conversion explicite — nécessaire pour les combinateurs et le stockage.
async ValkarnTask CombinatorExample()
{
    ValkarnTask a = Awaitable.NextFrameAsync().AsValkarnTask();
    ValkarnTask b = Awaitable.WaitForSecondsAsync(2f).AsValkarnTask();
    await ValkarnTask.WhenAll(a, b);
}

// Version générique avec un type de résultat.
async ValkarnTask<Texture2D> LoadTextureExample(string path)
{
    Awaitable<Texture2D> loadOp = LoadTextureAsync(path);
    return await loadOp.AsValkarnTask();
}
```

---

## JobBridge — le wrapper généré par la source

`JobBridge.cs` définit `JobPromise<TJob>`, le type de promise mis en pool générique utilisé par le générateur de source. C'est un détail d'implémentation ; vous ne l'instancierez normalement pas vous-même.

```csharp
// Interroge un JobHandle à chaque frame. Utilisé par les méthodes ScheduleAsync générées par la source.
public sealed class JobPromise<TJob> : ValkarnTask.ISource, IPlayerLoopItem, IPoolNode<JobPromise<TJob>>
    where TJob : struct
{
    public static JobPromise<TJob> Create(JobHandle handle, CancellationToken ct, out uint token);
}
```

Le comportement est identique à `JobHandlePromise` (voir `JobHandleExtensions`), sauf qu'il est générique sur le type de job pour l'isolation du pool — chaque type de job obtient son propre pool.

---

## Générateur de source : JobBridgeGenerator

Le `JobBridgeGenerator` est un générateur de source Roslyn incrémental (classe `UnaPartidaMas.Valkarn.Tasks.SourceGen.Generators.JobBridgeGenerator`) qui produit automatiquement des méthodes d'extension `ScheduleAsync` pour vos types de jobs.

### Ce qu'il détecte

Le générateur scanne toutes les structs **publiques** dans la compilation qui implémentent l'une des :
- `Unity.Jobs.IJob`
- `Unity.Jobs.IJobParallelFor`
- `Unity.Jobs.IJobFor`

Les structs de jobs privées et internes sont ignorées. Si une struct est imbriquée dans un type non public, elle est également ignorée.

Le générateur ne fait rien si `UnaPartidaMas.Valkarn.Tasks.ValkarnTask` n'est pas trouvé dans la compilation, donc il est sûr dans les assemblies qui ne référencent pas Valkarn Tasks.

### Ce qu'il génère

Le fichier de sortie est `ValkarnTask.JobBridge.Generated.g.cs`. Pour chaque type de job détecté, il émet une `public static class __<TypeName>_AsyncExt` contenant :

| Interface de job | Signature de méthode générée |
|---|---|
| `IJob` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, CancellationToken ct = default)` |
| `IJobParallelFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleParallelAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |

Chaque méthode générée planifie le job en utilisant les méthodes d'extension Unity standard, puis enveloppe le `JobHandle` résultant dans un `JobPromise<TJob>` et retourne un `ValkarnTask`.

Pour les types imbriqués (ex. une struct de job dans une classe externe), le nom de classe généré utilise des underscores : `__Outer_Inner_AsyncExt`.

### Utilisation des méthodes générées

```csharp
// Exemple IJob
public struct MyCalculationJob : IJob
{
    public NativeArray<float> Data;
    public void Execute() { /* ... */ }
}

// Le générateur produit :
//   public static ValkarnTask ScheduleAsync(this ref MyCalculationJob job, CancellationToken ct = default)

async ValkarnTask RunCalculation(CancellationToken ct)
{
    using var scope = TempNativeArrayScope.Create<float>(1024);
    var job = new MyCalculationJob { Data = scope.Array };
    await job.ScheduleAsync(ct);  // méthode d'extension générée
    // Lire les résultats depuis scope.Array ici.
}

// Exemple IJobParallelFor
public struct MyParallelJob : IJobParallelFor
{
    public NativeArray<float> Input;
    public NativeArray<float> Output;
    public void Execute(int index) { Output[index] = Input[index] * 2f; }
}

async ValkarnTask RunParallel(int length, CancellationToken ct)
{
    using var inputScope  = TempNativeArrayScope.Create<float>(length);
    using var outputScope = TempNativeArrayScope.Create<float>(length);
    var job = new MyParallelJob { Input = inputScope.Array, Output = outputScope.Array };
    await job.ScheduleAsync(length, innerLoopBatchCount: 64, ct);
}
```

---

## Générateur de source : AwaitableBridgeGenerator

Le `AwaitableBridgeGenerator` détecte si `UnityEngine.Awaitable` et `UnityEngine.Awaitable<T>` sont présents dans la compilation et émet des méthodes d'extension `AsValkarnTask()` quand ils le sont.

Le fichier de sortie est `ValkarnTask.AwaitableBridge.Generated.g.cs`. Le code généré vit dans `namespace UnaPartidaMas.Valkarn.Tasks.Bridge` sous la classe `AwaitableBridgeExtensions`.

Méthodes générées :

```csharp
// Émis quand UnityEngine.Awaitable est trouvé :
public static async ValkarnTask AsValkarnTask(this Awaitable awaitable)
{
    await awaitable;
}

// Émis quand UnityEngine.Awaitable<T> est trouvé :
public static async ValkarnTask<T> AsValkarnTask<T>(this Awaitable<T> awaitable)
{
    return await awaitable;
}
```

Ce sont des méthodes `async ValkarnTask`, donc elles passent par le builder async mis en pool de Valkarn Tasks — sur un pool préchauffé elles sont zéro-allocation.

Le générateur est gardé : si `ValkarnTask` n'est pas dans la compilation, aucun code n'est émis. Cela empêche les erreurs CS0246 dans les assemblies qui référencent Unity mais pas Valkarn Tasks.

---

## Exemple complet fonctionnel : pont Job dans un système ECS

Ce qui suit est tiré de `Samples~/ECS/JobBridgeExample.cs`. Il montre le pattern complet pour planifier un job parallel Burst depuis un `ISystem`, l'attendre sans bloquer, et écrire les résultats en retour.

```csharp
#if UNITY_5_3_OR_NEWER && VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnaPartidaMas.Valkarn.Tasks.ECS;

public partial struct JobBridgeExample : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<HealthData>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var worldCt = state.World.GetWorldCancellationToken();

        // Extraire toutes les données d'entité de manière synchrone dans OnUpdate.
        // Les méthodes async ne peuvent pas avoir de paramètres ref (CS1988), donc les données
        // doivent être copiées ici et passées par valeur à la méthode async.
        var query       = SystemAPI.QueryBuilder().WithAll<HealthData>().Build();
        var entityCount = query.CalculateEntityCount();
        if (entityCount == 0) return;

        var entities    = query.ToEntityArray(Allocator.TempJob);
        var healthArray = query.ToComponentDataArray<HealthData>(Allocator.TempJob);
        var results     = new NativeArray<float>(entityCount, Allocator.TempJob);

        // La méthode async prend la propriété des NativeArrays et les dispose.
        ProcessHealthAsync(state.EntityManager, entities, healthArray, results, worldCt).Forget();
        state.Enabled = false;
    }

    public void OnDestroy(ref SystemState state) { }

    static async ValkarnTask ProcessHealthAsync(
        EntityManager entityManager,
        NativeArray<Entity> entities,
        NativeArray<HealthData> healthArray,
        NativeArray<float> results,
        CancellationToken ct)
    {
        try
        {
            // Phase 1 : Planifier le job Burst.
            var job = new HealthProcessingJob
            {
                HealthInputs     = healthArray,
                ProcessedOutputs = results,
            };
            var handle = job.Schedule(entities.Length, batchSize: 64);

            // Phase 2 : Attendre la completion sans bloquer le thread principal.
            await handle.ToValkarnTask(cancellationToken: ct);

            // Phase 3 : Appliquer les résultats. Nous sommes de retour sur le thread principal.
            ct.ThrowIfCancellationRequested();

            for (int i = 0; i < entities.Length; i++)
            {
                // L'entité peut avoir été détruite pendant que le job s'exécutait.
                if (!entityManager.SafeEntityExists(entities[i]))
                    continue;

                entityManager.SetComponentData(entities[i], new HealthData
                {
                    CurrentHealth = results[i],
                });
            }
        }
        finally
        {
            // Toujours disposer les NativeArrays — s'exécute en cas de succès, exception ou annulation.
            if (entities.IsCreated)    entities.Dispose();
            if (healthArray.IsCreated) healthArray.Dispose();
            if (results.IsCreated)     results.Dispose();
        }
    }

    [BurstCompile]
    struct HealthProcessingJob : IJobParallelFor
    {
        [ReadOnly]  public NativeArray<HealthData> HealthInputs;
        [WriteOnly] public NativeArray<float>      ProcessedOutputs;

        public void Execute(int index)
        {
            var h = HealthInputs[index];
            var newHealth = h.CurrentHealth + h.RegenRate;
            if (newHealth > h.MaxHealth) newHealth = h.MaxHealth;
            ProcessedOutputs[index] = newHealth;
        }
    }

    struct HealthData : IComponentData
    {
        public float CurrentHealth;
        public float MaxHealth;
        public float RegenRate;
    }
}
#endif
```

---

## Résumé des types de pont

| Type | Rôle | Allocation |
|---|---|---|
| `JobHandleExtensions.ToValkarnTask()` | Attendre un seul `JobHandle` | Zéro sur le chemin rapide ; promise mise en pool sinon |
| `JobHandleExtensions.WhenAll()` | Attendre plusieurs `JobHandle` en parallèle | Zéro sur le chemin rapide ; promise mise en pool + location ArrayPool sinon |
| `TempNativeArrayScope<T>` | Gestion de durée de vie RAII pour `NativeArray` | Aucune (struct) |
| `AwaitableBridge.AsValkarnTask()` | Convertir `Awaitable`/`Awaitable<T>` en ValkarnTask | Aucune (struct awaiter) |
| `ScheduleAsync()` généré | Attendre un job typé directement | `JobPromise<TJob>` mis en pool |
