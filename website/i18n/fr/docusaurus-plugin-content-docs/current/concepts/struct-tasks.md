---
sidebar_position: 1
title: Tâches Struct
---

# Tâches Struct

`VlkTask` et `VlkTask<T>` sont les types de retour async de base dans Valkarn Tasks. Contrairement à `System.Threading.Tasks.Task`, qui est un type référence allouant toujours sur le tas, les deux types de tâches Valkarn sont des valeurs `readonly struct`. Cette page explique ce que cela signifie en pratique, comment fonctionne le chemin rapide zéro-allocation, et comment le compilateur s'intègre avec la machinerie async/await.

---

## Pourquoi un `readonly struct` ?

Une tâche basée sur une classe comme `Task<T>` doit être allouée sur le tas à chaque appel d'une méthode async, même pour les méthodes qui se terminent de manière synchrone. Dans une boucle de jeu Unity tournant à 60 fps, des centaines de petites opérations async par frame peuvent engendrer une pression GC mesurable.

`VlkTask` et `VlkTask<T>` sont déclarés comme `readonly partial struct` :

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncVlkTaskMethodBuilder))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct VlkTask
{
    internal readonly ISource source;
    internal readonly uint token;
}
```

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncVlkTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct VlkTask<T>
{
    internal readonly VlkTask.ISource<T> source;
    internal readonly T result;
    internal readonly uint token;
}
```

Être une struct signifie que la valeur de la tâche elle-même vit sur la pile (ou inline dans son objet parent) plutôt que sur le tas. Le modificateur `readonly` garantit que le compilateur peut raisonner sur l'immuabilité et prévient les bugs de copie accidentels. `StructLayout.Auto` permet au runtime d'optimiser l'ordre des champs pour la plateforme cible.

### L'invariant clé : `source == null`

La conception est construite autour d'un seul invariant :

> Quand `source` est `null`, la tâche est terminée de manière synchrone sans erreur. Aucun objet sur le tas n'est impliqué.

`VlkTask.CompletedTask` est `default(VlkTask)` — son champ `source` est null, donc il ne coûte rien. `VlkTask<T>` porte son résultat inline dans le champ `result`, ce qui rend également `VlkTask.FromResult(value)` un appel zéro-allocation :

```csharp
// Zéro allocation — source est null, résultat stocké inline
VlkTask<int> task = VlkTask.FromResult(42);

// Aussi zéro allocation — source est null
VlkTask done = VlkTask.CompletedTask;
```

---

## Le chemin rapide zéro-allocation

Quand une méthode `async` se termine sans jamais se suspendre (aucun `await` ne cède à une opération incomplète), la méthode entière s'exécute de manière synchrone sur le thread appelant. Le builder détecte cela et retourne une tâche avec `source == null`.

L'awaiter vérifie cela immédiatement :

```csharp
public bool IsCompleted
{
    get
    {
        var s = task.source;
        return s == null || s.GetStatus(task.token).IsCompleted();
    }
}
```

Quand `IsCompleted` est vrai avant que `OnCompleted` soit jamais appelé, la machine d'état n'enregistre pas de continuation. `GetResult` est appelé immédiatement, et pour `VlkTask<T>` avec `source == null`, le résultat est lu depuis le champ `result` inline de la struct :

```csharp
public T GetResult()
{
    var s = task.source;
    if (s == null)
        return task.result;   // inline, pas d'appel ISource
    return s.GetResult(task.token);
}
```

Aucun objet n'est créé, aucun dispatch d'interface ne se produit, et aucun délégué de continuation n'est alloué. L'await entier se résout comme une lecture directe de valeur.

### Quand une source est nécessaire

Si une méthode async se suspend (attend quelque chose qui n'est pas encore terminé), le builder alloue un objet `AsyncVlkTaskRunner<TStateMachine>` mis en pool (ou `AsyncVlkTaskRunner<TStateMachine, TResult>` pour la variante générique). Cet objet joue un double rôle : il contient la machine d'état générée par le compilateur par valeur et implémente `VlkTask.ISource`, afin de pouvoir être utilisé directement comme source de support de la tâche. La tâche retournée aux appelants enveloppe ce runner avec un jeton `uint` générationnel.

À la completion, quand l'appelant appelle `GetResult` sur l'awaiter, le runner se réinitialise et retourne à son pool — donc l'allocation est amortie sur de nombreuses invocations de méthode.

---

## L'interface `ISource`

Le contrat entre une struct `VlkTask` et son objet de support asynchrone est `VlkTask.ISource` :

```csharp
public interface ISource
{
    VlkTask.Status GetStatus(uint token);
    void GetResult(uint token);
    void OnCompleted(Action<object> continuation, object state, uint token);
    VlkTask.Status UnsafeGetStatus();
}

public interface ISource<out T> : ISource
{
    new T GetResult(uint token);
}
```

Tout objet qui implémente `ISource` peut soutenir un `VlkTask`. La bibliothèque inclut plusieurs implémentations :

| Type | Rôle |
|------|------|
| `AsyncVlkTaskRunner<TStateMachine>` | Soutient chaque méthode `async VlkTask` (interne) |
| `AsyncVlkTaskRunner<TStateMachine, TResult>` | Soutient chaque méthode `async VlkTask<T>` (interne) |
| `VlkTask.PooledPromise` | Source de completion manuelle avec retour automatique au pool |
| `VlkTask.PooledPromise<T>` | Variante générique de ce qui précède |
| `VlkTask.Promise` | Source de completion manuelle sans pool (opérations longues) |
| `VlkTask.Promise<T>` | Variante générique de ce qui précède |

Le paramètre `uint token` est une garde générationnelle. Quand une source mise en pool est réinitialisée pour réutilisation, son compteur de génération s'incrémente. Tout struct `VlkTask` détenant l'ancien token recevra immédiatement une `InvalidOperationException` plutôt que de lire silencieusement un état recyclé.

---

## `VlkTask` vs `VlkTask<T>`

| Fonctionnalité | `VlkTask` | `VlkTask<T>` |
|----------------|-----------|-------------|
| Valeur de retour | Aucune (équivalent void) | `T` |
| Stockage inline du résultat | Pas de champ `result` | Champ `result` (type `T`) |
| `GetResult` de l'awaiter | `void` | Retourne `T` |
| Type de builder | `AsyncVlkTaskMethodBuilder` | `AsyncVlkTaskMethodBuilder<TResult>` |
| Valeur complétée synchro | `VlkTask.CompletedTask` | `VlkTask.FromResult(value)` |
| Conversion en non-générique | Non applicable | `.AsNonGeneric()` |

Utilisez `VlkTask` quand une méthode async n'a pas de valeur de retour significative, et `VlkTask<T>` quand elle produit un résultat. Vous pouvez toujours réduire un `VlkTask<T>` à un `VlkTask` via `AsNonGeneric()` quand vous avez besoin de mélanger des tâches typées et non typées dans des combinateurs comme `WhenAll`.

---

## Comment fonctionne le builder de méthode async

Le compilateur C# cherche le type nommé dans `[AsyncMethodBuilder(...)]` sur le type de retour. Pour `VlkTask`, c'est `AsyncVlkTaskMethodBuilder`. Pour `VlkTask<T>`, c'est `AsyncVlkTaskMethodBuilder<TResult>`.

Le builder est lui-même une struct pour éviter une allocation sur le tas uniquement pour l'objet builder. Il a deux champs (trois pour la variante générique) :

```csharp
public struct AsyncVlkTaskMethodBuilder
{
    IStateMachineRunnerPromise runner;   // null jusqu'à la première suspension
    Exception syncException;            // seulement défini sur le chemin faulté synchro
}

public struct AsyncVlkTaskMethodBuilder<TResult>
{
    IStateMachineRunnerPromise<TResult> runner;
    Exception syncException;
    TResult result;                     // seulement défini sur le chemin de succès synchro
}
```

### Cycle de vie du builder

Le compilateur appelle ces méthodes dans l'ordre :

**1. `Create()`** — retourne un builder par défaut (tous les champs null/default). Pas d'allocation.

**2. `Start(ref stateMachine)`** — appelle `stateMachine.MoveNext()` de manière synchrone. Si la méthode se termine sans atteindre un `await` incomplet, `SetResult`/`SetException` est appelé et `runner` reste null.

**3. `AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine)`** — appelé quand la méthode rencontre un `await` incomplet. Si `runner` est null (première suspension), il loue ou crée un `AsyncVlkTaskRunner` et copie la machine d'état dedans. Ensuite il appelle `awaiter.UnsafeOnCompleted(runner.MoveNextAction)` pour enregistrer la continuation de la machine d'état.

**4. `SetResult()` / `SetException(exception)`** — signale la completion dans le `VlkTaskCompletionCore` du runner, ce qui réveille tout awaiter enregistré.

**5. Propriété `Task`** — vérifiée par l'appelant pour obtenir la valeur `VlkTask`. Sur le chemin de succès synchro (`runner == null && syncException == null`), retourne `default` (ou `new VlkTask<T>(result)` pour la variante générique) — zéro allocation. Sur le chemin async, enveloppe le runner comme source.

L'optimisation critique est que `runner` est alloué paresseusement. Si une méthode se termine de manière synchrone (le cas courant pour les hits de cache, les gardes, les retours anticipés), aucun objet mis en pool n'est jamais loué.

---

## États de `VlkTaskStatus`

Le statut est représenté par un enum de taille `byte` imbriqué dans `VlkTask` :

```csharp
public enum Status : byte
{
    Pending   = 0,   // pas encore terminé
    Succeeded = 1,   // terminé normalement
    Faulted   = 2,   // terminé avec une exception non gérée
    Canceled  = 3    // terminé via OperationCanceledException
}
```

Vous pouvez vérifier le statut directement :

```csharp
VlkTask task = SomeOperation();
VlkTask.Status status = task.GetStatus();

switch (status)
{
    case VlkTask.Status.Pending:
        // Toujours en cours — impossible d'appeler GetResult
        break;
    case VlkTask.Status.Succeeded:
        // Terminé normalement
        break;
    case VlkTask.Status.Faulted:
        // Terminé avec exception — GetResult relancera
        break;
    case VlkTask.Status.Canceled:
        // Terminé avec OperationCanceledException
        break;
}
```

Pour le chemin rapide complété synchro (où `source == null`), `GetStatus()` retourne `Succeeded` sans aucun appel d'interface :

```csharp
public Status GetStatus()
{
    if (source == null) return Status.Succeeded;
    return source.GetStatus(token);
}
```

La propriété `IsCompleted` suit le même pattern et retourne `true` pour tout état non-`Pending`.

---

## Implications IL2CPP

IL2CPP compile le C# en C++ avant de compiler en code natif. Les types valeur génériques — y compris les structs — sont entièrement spécialisés dans le code généré, ce qui a des conséquences importantes pour cette bibliothèque.

**Spécialisation de la machine d'état.** Le compilateur génère une struct de machine d'état unique par méthode async. `AsyncVlkTaskRunner<TStateMachine>` est donc aussi unique par méthode async, et `VlkTaskPool<AsyncVlkTaskRunner<TStateMachine>>` est un pool séparé par méthode. C'est en réalité bénéfique : le pool n'est jamais partagé entre des types incompatibles, éliminant tout risque de confusion de type.

**Pas de boxing de la machine d'état.** La machine d'état est stockée par valeur dans l'objet runner, pas boxée. IL2CPP gère cela correctement parce que le runner est une `sealed class` avec un champ `TStateMachine` concret.

**Protection contre le stripping.** L'attribut `[AsyncMethodBuilder]` maintient les types de builder en vie. Cependant, si vous utilisez `VlkTask.ISource` via une référence d'interface en IL2CPP avec un stripping agressif, ajoutez une entrée `link.xml` préservant l'assembly `UnaPartidaMas.Valkarn.Tasks` :

```xml
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```

**`ICriticalNotifyCompletion`.** Les structs d'awaiter implémentent `ICriticalNotifyCompletion`, ce qui indique au compilateur d'appeler `UnsafeOnCompleted` au lieu de `OnCompleted`. La variante "unsafe" ignore intentionnellement la capture de `ExecutionContext`. C'est correct pour Unity — il n'y a pas de `SynchronizationContext` dans la configuration par défaut d'Unity, et en capturer un ajouterait une surcharge sans bénéfice. Sous IL2CPP, cela évite également la surcharge du chemin `ExecutionContext.Run` que `Task` standard paie toujours.

---

## Exemples pratiques

### Retour anticipé sans allocation

```csharp
// async VlkTask<int> qui se termine synchronement sur le chemin chaud
async VlkTask<int> GetCachedValue(string key)
{
    if (_cache.TryGetValue(key, out var value))
        return value;            // le compilateur appelle SetResult(value); source reste null

    var result = await FetchFromDatabaseAsync(key);
    _cache[key] = result;
    return result;
}
```

Quand la valeur est en cache, la méthode ne se suspend jamais. Le `VlkTask<int>` retourné a `source == null` et porte le résultat inline. Aucune allocation sur le tas ne se produit sur ce chemin.

### Vérifier IsCompleted avant d'attendre

```csharp
VlkTask<Texture2D> loadTask = LoadTextureAsync("sprites/hero.png");

if (loadTask.IsCompleted)
{
    // Déjà terminé — GetAwaiter().GetResult() lit le résultat inline sans appel ISource
    Texture2D tex = loadTask.GetAwaiter().GetResult();
    ApplyTexture(tex);
}
else
{
    // Vraiment async — enregistrer la continuation
    ApplyTextureAsync(loadTask).Forget();
}
```

### Observer les exceptions non gérées

Les tâches fautées qui ne sont jamais attendues (patterns fire-and-forget) signalent leurs exceptions via l'événement `VlkTask.UnobservedException`. Ceci est déclenché de manière déterministe au moment du retour au pool pour les sources mise en pool, ou depuis le finaliseur pour les tâches soutenues par `Promise`.

```csharp
VlkTask.UnobservedException += ex =>
{
    Debug.LogError($"[VlkTask] Non observé : {ex}");
};
```

L'événement est thread-safe ; les gestionnaires peuvent être ajoutés ou supprimés depuis n'importe quel thread en utilisant une boucle compare-exchange sans verrou.
