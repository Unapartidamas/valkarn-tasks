---
sidebar_position: 2
title: Mise en pool des objets
---

# Mise en pool des objets

Valkarn Tasks élimine les allocations GC sur les chemins async courants en mettant en pool les objets qui soutiennent chaque `VlkTask`. Cette page explique l'architecture du pool — comment les objets sont stockés, comment ils sont acquis et retournés, et quelles garanties de cycle de vie le système fournit.

---

## Vue d'ensemble

Quand une méthode `async` se suspend, la bibliothèque a besoin d'un endroit pour stocker la machine d'état générée par le compilateur et un mécanisme de completion auquel l'awaiter peut s'abonner. Dans `System.Threading.Tasks`, c'est l'objet `Task` lui-même — une allocation sur le tas par appel. Dans Valkarn Tasks, ce rôle est joué par des objets mis en pool qui implémentent `VlkTask.ISource`.

La conception du pool a trois objectifs :

1. **Zéro atomiques sur le chemin chaud du thread principal.** La boucle de jeu Unity est mono-threadée par convention. La location et le retour depuis le thread principal doivent être de simples lectures et écritures.
2. **Accès cross-thread sûr.** Les tâches en arrière-plan utilisant `VlkTask.Run` opèrent sur des threads pool. Le pool doit gérer correctement la location/retour concurrent.
3. **Croissance bornée avec réduction adaptative.** Les pools ne doivent pas croître sans limite après un pic de trafic, mais ils ne doivent pas non plus se réduire si agressivement qu'ils réallouent constamment.

---

## `VlkTaskPool<T>`

`VlkTaskPool<T>` est la classe de pool centrale. Elle est `internal sealed` — vous n'interagissez pas directement avec elle, mais la comprendre explique où vont vos allocations.

```
VlkTaskPool<T>
  |
  +-- fastItem: T            (cache à un seul slot, thread principal uniquement, lecture/écriture simple)
  |
  +-- stackHead: T           (tête de pile Treiber, basée sur CAS pour la sécurité cross-thread)
  +-- stackSize: int
  |
  +-- maxSize: int           (borné par VlkTask.DefaultMaxPoolSize)
  +-- totalCreated: int      (suit les allocations à vie pour le ratio de réduction)
```

### Slot rapide (thread principal)

Le champ `fastItem` est un seul slot réservé pour l'objet retourné le plus récemment. Sur le thread principal, la location et le retour sont une simple lecture et écriture — pas d'atomiques, pas de spinning. Cela couvre la grande majorité des opérations de boucle de jeu Unity.

```
Location (thread principal) :
  fastItem != null  →  le prendre (fastItem = null), le retourner   [zéro atomiques]
  fastItem == null  →  tomber dans la pile Treiber

Retour (thread principal) :
  fastItem == null  →  fastItem = item                               [zéro atomiques]
  fastItem != null  →  tomber dans la pile Treiber
```

### Pile Treiber (débordement / threads en arrière-plan)

Quand le slot rapide est occupé (ou quand le thread appelant n'est pas le thread principal), le pool utilise une pile Treiber sans verrou — une liste chaînée intrusive classique utilisant compare-and-swap (CAS) :

```
Location (n'importe quel thread) :
  while (true):
    head = Volatile.Read(stackHead)
    si head == null : retourner null (pool vide)
    next = head.NextNode
    si CAS(stackHead, next, head) == head : retourner head  // a gagné la course
    spinner.SpinOnce()                                       // a perdu, réessayer

Retour (n'importe quel thread) :
  si stackSize >= maxSize : retourner false (pool plein, abandonner l'item)
  while (true):
    head = Volatile.Read(stackHead)
    item.NextNode = head
    si CAS(stackHead, item, head) == head : stackSize++; retourner true
    spinner.SpinOnce()
```

La pile est intrusive : chaque objet mis en pool stocke son propre pointeur `NextNode`, donc aucun nœud d'enveloppe externe n'est nécessaire. Cela est imposé par l'interface `IPoolNode<T>`.

### Routage des threads

```csharp
internal static class VlkTaskPoolShared
{
    internal static volatile int MainThreadId;
}
```

Toutes les instances de pool partagent un seul `MainThreadId`. Les opérations de location/retour vérifient `Thread.CurrentThread.ManagedThreadId == MainThreadId` pour router vers le bon chemin. Le champ `volatile` garantit la visibilité cross-thread après que l'ID est publié au démarrage.

---

## `IPoolNode<T>`

Tout type qui participe au pool doit implémenter cette interface :

```csharp
internal interface IPoolNode<T> where T : class
{
    ref T NextNode { get; }
}
```

`ref T NextNode` retourne une référence au champ à l'intérieur de l'objet qui stocke le pointeur suivant. Le pool écrit directement dans ce champ via la ref, éliminant tout nœud d'enveloppe séparé. Tous les types mis en pool dans la bibliothèque — runners, promises, combinateurs — implémentent cette interface en déclarant un champ privé et en l'exposant :

```csharp
// Exemple de PooledPromise<T>
PooledPromise<T> nextNode;
public ref PooledPromise<T> NextNode => ref nextNode;
```

---

## Cycle de vie du pool : acquérir, utiliser, retourner

Le cycle de vie complet d'un objet mis en pool est :

```
L'appelant invoque la méthode async
  |
  +--> AsyncVlkTaskMethodBuilder.AwaitUnsafeOnCompleted()
        |
        +--> runner == null? OUI --> Pool.TryRent() ?? Pool.TrackNew(new Runner())
              |
              +--> runner stocké dans le builder; machine d'état copiée dans le runner
              |
              +--> awaiter.UnsafeOnCompleted(runner.MoveNextAction)
              |
              [... travail async continue ...]
              |
              +--> runner.SetResult() / runner.SetException()
                    |
                    +--> VlkTaskCompletionCore.TrySetResult() --> continuation invoquée
                          |
                          +--> l'awaiter de l'appelant appelle GetResult(token)
                                |
                                +--> core.GetResult(token) -- lit le résultat ou relance
                                |
                                +--> TryReturn():
                                      stateMachine = default
                                      core.Reset()           // incrémente la génération
                                      Pool.TryReturn(this)
```

La méthode `TryReturn` efface toujours la machine d'état avant d'appeler `core.Reset()`. Cet ordre est important : `Reset()` incrémente le compteur de génération, rendant le slot visible comme disponible pour les locataires concurrents. Si la machine d'état était effacée après `Reset()`, un locataire sur un autre thread pourrait obtenir le slot et voir sa machine d'état écrasée.

---

## `VlkTaskCompletionCore<TResult>`

`VlkTaskCompletionCore<TResult>` est une `struct` interne embarquée dans chaque objet mis en pool. C'est la machine d'état réelle pour la promise — suivant l'état de completion, stockant les résultats et les erreurs, et résolvant la course entre `OnCompleted` (enregistrement d'une continuation) et `TrySetResult` (signalisation de la completion).

```
Champs :
  result:          TResult     -- la valeur de succès
  error:           object      -- ExceptionDispatchInfo ou OperationCanceledException
  errorKind:       byte        -- 0=aucun, 1=faulté, 2=annulé(OCE), 3=annulé(EDI)
  generation:      int         -- monotone croissant; casté en uint pour comparaison de token
  completedCount:  int         -- 0=en attente, 1=réclamé, 2=terminé (publication en deux phases)
  continuation:    Action<object>
  continuationState: object
  hasUnhandledError: bool
```

### Protocole de completion en deux phases

La completion utilise un CAS en deux phases pour être sûre sur ARM64 où des paires store-release / load-acquire sont nécessaires :

```
TrySetResult(value):
  Phase 1 : CAS(completedCount, 0 -> 1)   -- réclamer la propriété exclusive
  Phase 2 : écrire result
  Phase 3 : Volatile.Write(completedCount, 2)  -- publier avec sémantique release
  Phase 4 : InvokeContinuation()
```

Les lecteurs utilisent `Volatile.Read(completedCount)` (sémantique acquire) avant de lire le résultat, garantissant qu'ils voient la valeur écrite en Phase 2.

### Résolution de course entre `OnCompleted` et `TrySetResult`

Trois patterns peuvent se produire :

```
Pattern A — OnCompleted en premier :
  OnCompleted stocke la continuation via CAS(continuation, null -> cont)
  TrySetResult lit la continuation non-null -> l'invoque

Pattern B — TrySetResult en premier (chemin rapide synchro) :
  TrySetResult place ContinuationSentinel via CAS(continuation, null -> sentinel)
  OnCompleted lit le sentinel -> invoque la continuation inline immédiatement

Pattern C (course concurrente) :
  C.1 : OnCompleted gagne CAS -> TrySetResult le lit -> invoque
  C.2 : TrySetResult gagne CAS (place le sentinel) -> OnCompleted détecte le sentinel -> invoque inline
```

Le sentinel est un objet `Action<object>` statique utilisé purement comme valeur marqueur — il n'est jamais réellement invoqué comme délégué.

### Validation du token et sécurité ABA

Chaque appel à `GetStatus`, `GetResult`, et `OnCompleted` valide le `uint token` contre la `generation` actuelle. Quand `Reset()` appelle `Interlocked.Increment(ref generation)`, tout struct `VlkTask` détenant l'ancien token recevra une `InvalidOperationException` plutôt que d'opérer silencieusement sur un état recyclé. Un compteur de génération 32 bits qui boucle (nécessitant ~4 milliards de réutilisations d'un seul slot) est considéré comme effectivement impossible en pratique.

### Réinitialisation et signalement d'erreur non observée

`Reset()` est appelé au moment du retour au pool. Avant d'incrémenter la génération, il vérifie si une erreur a été stockée mais jamais observée (c'est-à-dire que `GetResult` n'a jamais été appelé après une faute). Si c'est le cas, il publie l'exception via `VlkTask.UnobservedException`. Les erreurs d'annulation ne sont signalées que si `LogUnobservedCancellations` est activé dans `VlkTaskSettings`, car l'annulation est souvent intentionnelle.

Pour les objets `Promise` et `Promise<T>` non mis en pool, le signalement d'erreur non observée se produit depuis le finaliseur via `ReportUnobservedIfNeeded()`, qui suit la même logique sans effacer l'état.

---

## Configuration du pool

Trois paramètres contrôlent le dimensionnement du pool. Dans les builds Unity, ils sont lus depuis un asset `VlkTaskSettings` ScriptableObject (avec des valeurs par défaut de secours), et peuvent être surchargés à l'exécution via des propriétés statiques :

```csharp
// Nombre maximum d'objets par type de pool (par TStateMachine ou par type de promise)
VlkTask.DefaultMaxPoolSize = 256;   // par défaut : 256

// Combien de frames entre les vérifications de réduction (frames PlayerLoop Unity)
VlkTask.TrimCheckInterval = 300;    // par défaut : 300 (~5 secondes à 60fps)

// Nombre minimum d'objets à conserver après une passe de réduction
VlkTask.MinPoolSize = 8;            // par défaut : 8
```

`DefaultMaxPoolSize` est le plafond appliqué au moment de la construction du pool. Il est appliqué par instance de pool, pas globalement — un pool pour `AsyncVlkTaskRunner<LoadSceneStateMachine>` et un pool pour `AsyncVlkTaskRunner<FetchDataStateMachine>` ont chacun leur propre plafond.

### Réduction du pool

Le `PlayerLoopHelper` invoque `PoolRegistry.TrimAll(minPoolSize)` tous les `TrimCheckInterval` frames sur le thread principal. Chaque pool utilise une stratégie d'hystérésis :

```
Chaque vérification de réduction :
  currentSize = fastItem.count + stackSize
  si currentSize <= MinPoolSize : réinitialiser le compteur consécutif, ignorer

  ratio = currentSize / totalCreated
  si ratio > 0.5 (le pool détient > 50% de tous les objets jamais créés) :
    trimConsecutiveCount++
    si trimConsecutiveCount >= hysteresisThreshold :
      libérer une certaine fraction (releaseRatio) des objets excédentaires de la pile
      (fastItem est préservé — c'est le slot le plus cache-friendly)
  sinon :
    réinitialiser trimConsecutiveCount
```

L'hystérésis empêche un bref pic de trafic de provoquer immédiatement l'allocation puis la réduction immédiate de tous les objets. Le slot rapide est toujours préservé lors de la réduction parce qu'il représente l'item le plus récemment utilisé et est donc le plus susceptible d'être à nouveau nécessaire.

---

## `PoolRegistry` et monitoring

Chaque `VlkTaskPool<T>` s'enregistre auprès du `PoolRegistry` global au moment de la construction. Le registre maintient une liste de références `IPoolInfo`, qui exposent :

```csharp
internal interface IPoolInfo
{
    Type PooledType { get; }
    int Size { get; }
    int MaxSize { get; }
    bool IsAlive { get; }
    int Trim(int minPoolSize);
}
```

Vous pouvez énumérer tous les pools actifs à l'exécution en utilisant l'API publique :

```csharp
foreach (var (type, size, maxSize) in VlkTask.GetPoolInfo())
{
    Debug.Log($"{type.Name}: {size}/{maxSize}");
}
```

Ce sont les mêmes données affichées par la fenêtre **Task Tracker** dans l'éditeur Unity. La fenêtre interroge `GetPoolInfo()` et affiche un tableau en direct de l'occupation des pools, vous permettant de voir si les pools sont préchauffés, si un type atteint systématiquement son plafond, et si la réduction fonctionne comme prévu.

Les entrées de pool mortes (où `IsAlive` retourne false) sont paresseusement purgées de la liste du registre lors des appels à `GetAll()` et `TrimAll()`, empêchant le registre de croître indéfiniment si les instances de pool sont récupérées par le GC.

---

## `PooledPromise` et `PooledPromise<T>`

Ce sont les sources de completion mises en pool destinées à être utilisées dans des patterns async personnalisés — par exemple, encapsuler une API basée sur des callbacks ou un canal producteur/consommateur répétitif.

```csharp
// Acquérir une promise en attente depuis le pool
var promise = VlkTask.PooledPromise<string>.Create(out uint token);
VlkTask<string> task = promise.Task;

// Remettre la tâche au consommateur
// ... plus tard, depuis n'importe quel thread ...
promise.TrySetResult("hello");

// Quand le consommateur attend la tâche et que GetResult est appelé,
// la promise se réinitialise et retourne au pool automatiquement.
```

Caractéristiques clés :

- `Create(out uint token)` loue depuis le pool ou alloue une nouvelle instance suivie par le pool.
- `CreateCompleted(T result, out uint token)` fait de même mais signale immédiatement le résultat, donc la tâche est déjà terminée quand elle est retournée.
- Après que `GetResult` est appelé sur la tâche de support, `TryReturn()` se déclenche : la promise appelle `core.Reset()` et se retourne elle-même au pool.
- Une garde contre le double-retour (`Interlocked.Exchange(ref returned, 1)`) empêche la corruption du pool si `GetResult` est appelé deux fois.

**Alternative non mise en pool : `Promise` et `Promise<T>`.** Ce sont des classes allouées sur le tas non retournées à un pool. Utilisez-les pour les opérations longue durée où la durée de vie est imprévisible ou où la même source doit survivre à plusieurs cycles d'await. Elles s'appuient sur un finaliseur pour signaler les exceptions non observées.

---

## Pools des combinateurs

Les combinateurs `WhenAll` et `WhenAny` utilisent également des pools. Chaque arité et combinaison de types a son propre pool :

| Combinateur | Type de pool |
|-------------|-------------|
| `WhenAll(task1, task2)` (typé) | `VlkTaskPool<WhenAllPromise<T1, T2>>` |
| `WhenAll(IEnumerable<VlkTask<T>>)` | `VlkTaskPool<WhenAllArrayPromise<T>>` |
| `WhenAll(task1, task2)` (void) | `VlkTaskPool<WhenAllVoidPromise2>` |
| `WhenAny(task1, task2)` (typé) | `VlkTaskPool<WhenAnyPromise2<T>>` |
| `WhenAny(IEnumerable<VlkTask<T>>)` | `VlkTaskPool<WhenAnyArrayPromise<T>>` |

Les combinateurs basés sur des tableaux (`WhenAll<T>(IEnumerable<...>)` et `WhenAny<T>(IEnumerable<...>)`) utilisent `System.Buffers.ArrayPool<T>.Shared` pour leurs tableaux de sources/tokens internes, donc ces tableaux sont également recyclés plutôt qu'alloués à nouveau par appel.

Tous les combinateurs appliquent le même court-circuit zéro-allocation : si toutes les entrées sont synchronement terminées au moment où `WhenAll` ou `WhenAny` est appelé, un nouvel objet mis en pool n'est jamais créé.

```csharp
// Zéro allocation — les deux tâches sont synchronement terminées
var t1 = VlkTask.FromResult(1);
var t2 = VlkTask.FromResult(2);
VlkTask<(int, int)> combined = VlkTask.WhenAll(t1, t2);
// combined.source == null; résultat est (1, 2) stocké inline
```
