---
sidebar_position: 1
title: Intégration Burst & ECS
---

# Intégration Burst & ECS

Valkarn Tasks inclut une intégration optionnelle avec le compilateur Burst d'Unity, Unity Collections et le package Entities (ECS). Toute cette fonctionnalité est compilée conditionnellement — elle n'est active que lorsque les packages requis sont présents et que les symboles de définition de script correspondants sont définis.

## Prérequis

| Fonctionnalité | Package requis | Définition de script |
|---|---|---|
| `NativeTimerHeap`, `NativeScheduler`, `BurstSchedulerRunner` | Unity Burst 1.8+, Unity Collections 2.0+ | `VTASKS_HAS_BURST` et `VTASKS_HAS_COLLECTIONS` |
| `AsyncSystemUtilities` | Unity Entities 1.0+ | `VTASKS_HAS_ENTITIES` |

Tous les fichiers source Burst/ECS sont enveloppés dans des gardes `#if` correspondant à ces définitions. Rien dans ces fichiers ne compile ou ne lie à moins que les définitions soient présentes.

### Configuration

1. Installez les packages requis via le Unity Package Manager :
   - `com.unity.burst` 1.8 ou version ultérieure
   - `com.unity.collections` 2.0 ou version ultérieure
   - `com.unity.entities` 1.0 ou version ultérieure (pour les utilitaires ECS uniquement)

2. Ajoutez les symboles de définition de script dans **Project Settings > Player > Scripting Define Symbols** :
   ```
   VTASKS_HAS_BURST;VTASKS_HAS_COLLECTIONS;VTASKS_HAS_ENTITIES
   ```
   Vous n'avez besoin d'ajouter que les définitions pour les packages que vous avez installés.

---

## NativeTimerHeap

**Espace de noms :** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Garde :** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeTimerHeap` est un tas binaire min compatible Burst pour planifier des timers. Il stocke des valeurs `TimerEntry` ordonnées par deadline, donnant une insertion O(log n) et un retrait O(log n) par timer expiré.

### Types clés

```csharp
// Entrée stockée dans le tas. Ordonnée par Deadline.
public struct TimerEntry : IComparable<TimerEntry>
{
    public long Deadline;
    public int  Id;
}

public struct NativeTimerHeap : IDisposable
{
    public bool IsCreated { get; }
    public int  Count     { get; }

    // Crée le tas. Utilisez Allocator.Persistent pour les tas longue durée.
    public NativeTimerHeap(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // Insère un nouveau timer. Retourne l'ID du timer (utilisé pour identifier le callback).
    // La deadline et la valeur passée à DrainExpired doivent utiliser la même unité
    // (BurstSchedulerRunner utilise les ticks DateTime via Time.realtimeSinceStartupAsDouble).
    [BurstCompile]
    public int Schedule(long deadline);

    // Supprime et ajoute les IDs de tous les timers dont Deadline <= currentTimestamp.
    // Retourne le nombre de timers drainés.
    [BurstCompile]
    public int DrainExpired(long currentTimestamp, NativeList<int> expiredIds);

    public void Dispose();
}
```

`NativeTimerHeap` est une struct non managée. Elle ne peut pas stocker de délégués managés — les IDs sont associés aux callbacks managés dans le dictionnaire `BurstSchedulerRunner` sur le thread principal.

---

## NativeScheduler

**Espace de noms :** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Garde :** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeScheduler` est une file de travail compatible Burst soutenue par `NativeQueue<ScheduledWork>`. Les jobs compilés Burst mettent en file des éléments de travail ; le thread principal les draine à chaque frame.

### Types clés

```csharp
public enum WorkType : byte
{
    TimerExpired  = 0,
    JobCompleted  = 1,
    Custom        = 2
}

public struct ScheduledWork
{
    public int      Id;
    public WorkType Type;
    public long     Payload;
}

public struct NativeScheduler : IDisposable
{
    public bool IsCreated { get; }

    public NativeScheduler(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // Met en file un élément de travail depuis un job compilé Burst.
    [BurstCompile]
    public void Enqueue(ScheduledWork work);

    // Draine tout le travail en attente dans `results`. Appeler uniquement depuis le thread principal.
    // Retourne le nombre d'éléments drainés.
    public int Drain(NativeList<ScheduledWork> results);

    // Retourne un parallel writer adapté pour une utilisation dans des jobs IJobParallelFor.
    public NativeQueue<ScheduledWork>.ParallelWriter AsParallelWriter();

    public void Dispose();
}
```

La file est le point de croisement entre le monde Burst et le monde managé. `Enqueue` est appelable depuis Burst ; `Drain` est uniquement pour le thread principal.

---

## BurstSchedulerRunner

**Espace de noms :** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Garde :** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`BurstSchedulerRunner` est le pont managé entre le planificateur natif / le tas de timers et le reste de votre jeu. Il implémente `IPlayerLoopItem`, donc Unity appelle `MoveNext()` une fois par frame au timing enregistré. À chaque frame il :

1. Draine `NativeScheduler` et déclenche tous les callbacks managés enregistrés correspondant par ID de travail.
2. Draine `NativeTimerHeap` pour les timers expirés et déclenche leurs callbacks managés.

Les exceptions levées par les callbacks sont transmises à `VlkTask.PublishUnobservedException` plutôt que de se propager à travers le PlayerLoop.

### API

```csharp
public sealed class BurstSchedulerRunner : IPlayerLoopItem, IDisposable
{
    // Crée un runner et l'enregistre sur le PlayerLoop. Retourne l'instance.
    // Disposez le runner retourné quand il n'est plus nécessaire.
    public static BurstSchedulerRunner Create(
        PlayerLoopTiming timing       = PlayerLoopTiming.Update,
        int initialCapacity           = 64);

    // Accès direct au NativeScheduler pour la mise en file depuis des jobs Burst.
    public NativeScheduler Scheduler { get; }

    // Planifie un callback de timer managé. Doit être appelé depuis le thread principal.
    // Retourne un ID de timer (pour identification uniquement ; il n'y a pas d'API d'annulation).
    public int ScheduleTimer(TimeSpan delay, Action callback);

    // Associe un callback managé avec un ID de travail mis en file par un job Burst.
    // Doit être appelé depuis le thread principal, avant ou pendant la frame où le job se termine.
    public void RegisterCallback(int workId, Action callback);

    // Dispose tous les conteneurs natifs et se désenregistre du PlayerLoop.
    public void Dispose();
}
```

### Comment BurstSchedulerRunner diffère du planificateur par défaut

Le planificateur par défaut de Valkarn Tasks s'intègre directement avec les machines d'état `async/await` et gère la distribution des continuations via `PlayerLoopHelper`. `BurstSchedulerRunner` ajoute une voie séparée spécifiquement pour la signalisation depuis des jobs compilés Burst :

| | Planificateur par défaut | BurstSchedulerRunner |
|---|---|---|
| Type de continuation | `Action` managée via `ISource` | `Action` managée enregistrée par ID |
| Source du signal | Pattern awaiter C# | `NativeScheduler.Enqueue` non managé |
| Source du timer | `VlkTask.Delay` (managé) | `NativeTimerHeap` (non managé) |
| Thread-safety | Continuations thread principal | Enqueue est sûr Burst ; drain est thread principal uniquement |

### Pattern d'utilisation

```csharp
// 1. Créer le runner une fois (ex. dans un MonoBehaviour bootstrap ou ISystem.OnCreate).
var runner = BurstSchedulerRunner.Create(PlayerLoopTiming.Update, initialCapacity: 128);

// 2. Planifier un callback de timer (thread principal).
runner.ScheduleTimer(TimeSpan.FromSeconds(2.0), () =>
{
    Debug.Log("Deux secondes écoulées (timer non managé).");
});

// 3. Depuis un job Burst, mettre en file un élément de travail.
//    NativeScheduler.ParallelWriter est sûr à utiliser depuis IJobParallelFor.
var writer = runner.Scheduler.AsParallelWriter();
// Dans Execute(int index) :
writer.Enqueue(new ScheduledWork { Id = myWorkId, Type = WorkType.Custom });

// 4. Enregistrer le callback managé sur le thread principal avant que le job se termine.
runner.RegisterCallback(myWorkId, () =>
{
    Debug.Log($"Signal de completion du job reçu pour le travail {myWorkId}.");
});

// 5. Disposer le runner quand terminé (ex. OnDestroy ou rechargement de domaine).
runner.Dispose();
```

---

## AsyncSystemUtilities

**Espace de noms :** `UnaPartidaMas.Valkarn.Tasks.ECS`
**Garde :** `#if VTASKS_HAS_ENTITIES`

`AsyncSystemUtilities` fournit deux helpers d'extension pour écrire des systèmes ECS asynchrones.

### GetWorldCancellationToken

```csharp
public static CancellationToken GetWorldCancellationToken(
    this World world,
    PlayerLoopTiming timing = PlayerLoopTiming.Update)
```

Retourne un `CancellationToken` qui est automatiquement annulé quand le `World` donné est détruit. En interne, il démarre un `VlkTask` fire-and-forget qui appelle `VlkTask.Yield(timing)` à chaque frame pendant que `world.IsCreated` est vrai, puis annule un `CancellationTokenSource` quand la boucle se termine.

Si le monde est déjà détruit quand vous appelez cette méthode, elle retourne un token qui est déjà dans l'état annulé.

Passez ce token à chaque méthode async que vous lancez depuis un système afin que le travail en cours soit automatiquement arrêté quand le monde disparaît.

### SafeEntityExists

```csharp
public static bool SafeEntityExists(this EntityManager entityManager, Entity entity)
```

Appelle `entityManager.Exists(entity)` et retourne `false` si une `ObjectDisposedException` est levée. Cela peut se produire quand `EntityManager` est accédé après que le monde a été disposé, ce qui est une vraie condition de course dans le code async qui survit à travers les limites de frame.

Utilisez ceci après chaque point `await` avant d'écrire sur une entité.

---

## Exemple fonctionnel : Système ECS asynchrone

L'exemple suivant est tiré de `Samples~/ECS/AsyncLoadSystem.cs`. Il démontre le pattern canonique pour l'initialisation async à usage unique depuis un `ISystem`.

```csharp
#if VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Entities;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.ECS;

public partial struct AsyncLoadSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // Obtenir un token d'annulation lié à la durée de vie de ce World.
        // Si le World est détruit, tout le travail async lancé avec ce token
        // sera automatiquement annulé.
        var worldCt = state.World.GetWorldCancellationToken();

        // Lancer l'initialisation async et oublier la tâche.
        // Forget() achemine toute exception non gérée vers VlkTask.PublishUnobservedException.
        InitializeAsync(state.WorldUnmanaged, worldCt).Forget();
    }

    public void OnUpdate(ref SystemState state) { }
    public void OnDestroy(ref SystemState state) { }

    static async VlkTask InitializeAsync(WorldUnmanaged world, CancellationToken ct)
    {
        // Phase 1 : Charger les données sur un thread en arrière-plan.
        // RunOnThreadPool bascule vers un thread worker, exécute le délégué,
        // et revient automatiquement sur le thread principal.
        var configData = await VlkTask.RunOnThreadPool(
            () => LoadFromDisk(),
            cancellationToken: ct);

        // Phase 2 : Appliquer les résultats sur le thread principal.
        // Vérifier l'annulation au cas où le World aurait été détruit pendant le chargement.
        ct.ThrowIfCancellationRequested();
        ApplyConfiguration(world, configData);
    }

    static ConfigData LoadFromDisk()
    {
        // C# pur uniquement — pas d'appels API Unity ou ECS autorisés ici.
        return new ConfigData { MaxEnemies = 100, SpawnRadius = 50f };
    }

    static void ApplyConfiguration(WorldUnmanaged world, ConfigData data)
    {
        // Sûr : nous sommes sur le thread principal.
        UnityEngine.Debug.Log($"Config chargée : MaxEnemies={data.MaxEnemies}");
    }

    struct ConfigData
    {
        public int   MaxEnemies;
        public float SpawnRadius;
    }
}
#endif
```

L'exemple de throttling AI (`Samples~/ECS/AISystemExample.cs`) s'appuie sur ce pattern et ajoute `AsyncThrottle` pour limiter le nombre de tâches async concurrentes en vol. Voir la documentation Throttling pour les détails sur ce pattern.

---

## Limitations

Les contraintes suivantes s'appliquent à tout le code async Burst/ECS. Les violer produira des erreurs d'éditeur, des violations de sécurité des jobs, ou une corruption silencieuse des données.

### Dans le code compilé Burst

- **Pas de types managés.** Burst ne peut pas compiler du code qui alloue, accède ou référence des objets managés (classes, délégués, tableaux, chaînes, `List<T>`, etc.). Seuls les structs blittables et les conteneurs natifs sont autorisés.
- **Pas d'exceptions.** Burst ne supporte pas `try`/`catch`/`throw`. Utilisez des codes de retour ou des flags pour communiquer les erreurs.
- **Pas d'`async`/`await`.** Les machines d'état async C# sont managées et ne peuvent pas être compilées par Burst. Le `NativeScheduler` et le `NativeTimerHeap` fournissent un canal latéral pour signaler les continuations managées, mais les continuations elles-mêmes s'exécutent sur le thread principal.
- **Pas d'état managé mutable statique.** Les jobs Burst peuvent lire des champs statiques readonly mais ne doivent pas écrire dans des statiques managées.

### À travers les points await dans les systèmes ECS

- **Durée de vie des entités.** Les entités peuvent être détruites pendant qu'une méthode async est suspendue. Appelez toujours `entityManager.SafeEntityExists(entity)` après chaque `await` avant d'écrire sur une entité.
- **Obsolescence de ComponentLookup.** `ComponentLookup`, `RefRW` et autres types de pointeurs de chunk deviennent invalides après des changements structurels, qui peuvent se produire dans n'importe quelle frame. Ne les mettez pas en cache à travers les points `await`. Réacquérez depuis `SystemState` après la reprise, ou utilisez `EntityManager` directement.
- **Paramètres `ref`.** Les méthodes async ne peuvent pas avoir de paramètres `ref`, `in` ou `out` (erreur C# CS1988). Extrayez toutes les données ECS de manière synchrone dans la méthode synchrone `OnUpdate` et passez-les à la méthode async par valeur.
- **`SystemAPI` dans les méthodes async.** `SystemAPI` est généré par la source et ne fonctionne qu'à l'intérieur des méthodes partielles `ISystem`. Il n'est pas disponible dans les méthodes `async`. Effectuez toutes les requêtes `SystemAPI` avant le premier `await`.
- **Thread-safety.** `EntityManager`, `ComponentLookup` et les changements structurels sont réservés au thread principal. Utilisez `VlkTask.RunOnThreadPool` uniquement pour le calcul C# pur sans appels API Unity ou ECS.
