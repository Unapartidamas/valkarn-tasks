---
sidebar_position: 2
title: ValkarnTask<T>
---

# `ValkarnTask<T>`

`ValkarnTask<T>` est le type de tâche async retournant une valeur dans Valkarn Tasks. C'est un `readonly struct`, portant soit un résultat inline (quand terminé de manière synchrone) soit une référence à un objet source mis en pool (quand terminé de manière asynchrone).

**Espace de noms :** `UnaPartidaMas.Valkarn.Tasks`

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncValkarnTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
```

Il n'y a pas de contraintes génériques sur `T`. Tout type — type valeur, type référence, struct ou classe — est valide.

---

## Créer des instances

### Tâches terminées de manière synchrone

Ces méthodes de fabrique retournent un `ValkarnTask<T>` sans objet source de support. Zéro allocation.

#### `ValkarnTask.FromResult<T>(T value)`

Retourne un `ValkarnTask<T>` terminé portant `value` inline. Déclaré comme méthode statique sur le type `ValkarnTask` non-générique.

```csharp
public static ValkarnTask<T> FromResult<T>(T value)
```

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(42);
ValkarnTask<string> name = ValkarnTask.FromResult("Valkarn");
ValkarnTask<Vector3> pos = ValkarnTask.FromResult(transform.position);
```

La struct retournée a `source == null`. L'attendre n'entraîne pas d'allocation de continuation — le compilateur voit `IsCompleted == true` immédiatement.

#### `ValkarnTask.FromException<T>(Exception exception)`

Retourne un `ValkarnTask<T>` faulté. L'attendre relance l'exception avec sa trace de pile originale préservée via `ExceptionDispatchInfo`.

```csharp
public static ValkarnTask<T> FromException<T>(Exception exception)
```

```csharp
ValkarnTask<Texture2D> LoadTexture(string path)
{
    if (string.IsNullOrEmpty(path))
        return ValkarnTask.FromException<Texture2D>(
            new ArgumentException("Le chemin ne doit pas être vide.", nameof(path)));

    return LoadTextureAsync(path);
}
```

#### `ValkarnTask.FromCanceled<T>(CancellationToken ct = default)`

Retourne un `ValkarnTask<T>` annulé. L'attendre lève `OperationCanceledException`.

```csharp
public static ValkarnTask<T> FromCanceled<T>(CancellationToken ct = default)
```

```csharp
ValkarnTask<byte[]> Download(string url, CancellationToken ct)
{
    if (ct.IsCancellationRequested)
        return ValkarnTask.FromCanceled<byte[]>(ct);

    return DownloadAsync(url, ct);
}
```

### Via des méthodes `async`

Toute méthode `async` déclarée pour retourner `ValkarnTask<T>` utilise automatiquement `AsyncValkarnTaskMethodBuilder<TResult>` :

```csharp
async ValkarnTask<int> ComputeAsync()
{
    await ValkarnTask.Yield();
    return 42;
}
```

Le compilateur génère une machine d'état. Si la méthode se termine de manière synchrone (ne se suspend jamais), `AsyncValkarnTaskMethodBuilder<T>.Task` retourne `new ValkarnTask<T>(result)` avec `source == null` — zéro allocation.

---

## Exécuter du travail sur le thread pool

Ces méthodes exécutent un délégué sur le thread pool .NET et retournent le résultat sur le thread principal (au `PlayerLoopTiming` spécifié). Ce sont des raccourcis autour des variantes au nom plus long `RunOnThreadPool`.

#### `ValkarnTask.Run<T>(Func<T> func, PlayerLoopTiming timing, CancellationToken ct)`

Exécute un `Func<T>` synchrone sur le thread pool.

```csharp
public static ValkarnTask<T> Run<T>(
    Func<T> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
// Calculer sur le thread pool, le résultat revient sur le thread principal au prochain Update
int hash = await ValkarnTask.Run(() => ComputeExpensiveHash(data));
```

#### `ValkarnTask.Run<T>(Func<ValkarnTask<T>> func, PlayerLoopTiming timing, CancellationToken ct)`

Exécute un `Func<ValkarnTask<T>>` async sur le thread pool. Utilisez ceci quand le travail lui-même est async (ex. : I/O fichier).

```csharp
public static ValkarnTask<T> Run<T>(
    Func<ValkarnTask<T>> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
string json = await ValkarnTask.Run(async () =>
{
    using var reader = File.OpenText("data.json");
    return await reader.ReadToEndAsync();
});
```

Les deux surcharges de `Run` annulent tôt si le token est déjà annulé, et reviennent au thread principal au `timing` après que le travail est terminé.

---

## Membres d'instance

### `IsCompleted`

```csharp
public bool IsCompleted { get; }
```

Retourne `true` si la tâche s'est terminée dans un état terminal quelconque (Succeeded, Faulted ou Canceled). Pour les tâches terminées de manière synchrone (`source == null`), retourne toujours `true` sans dispatch d'interface.

```csharp
var task = SomeLongOperation();
if (task.IsCompleted)
{
    int result = task.GetAwaiter().GetResult();
    Use(result);
}
```

### `GetStatus()`

```csharp
public ValkarnTask.Status GetStatus()
```

Retourne le `ValkarnTask.Status` actuel. Valeurs possibles : `Pending`, `Succeeded`, `Faulted`, `Canceled`. Pour `source == null`, retourne toujours `Succeeded`.

### `GetAwaiter()`

```csharp
public Awaiter GetAwaiter()
```

Retourne une struct `Awaiter`. Utilisé par le compilateur pour implémenter `await`. Vous pouvez également l'appeler directement pour obtenir le résultat de manière synchrone (seulement sûr quand `IsCompleted` est vrai).

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(10);
int value = task.GetAwaiter().GetResult(); // sûr — terminé synchro
```

Appeler `GetResult()` sur une tâche en attente lève une `InvalidOperationException`.

### `AsNonGeneric()`

```csharp
public ValkarnTask AsNonGeneric()
```

Convertit ce `ValkarnTask<T>` en un `ValkarnTask` non-générique, abandonnant le type de résultat. La tâche résultante partage la même source et le même token sous-jacents, donc elle se termine en même temps.

```csharp
ValkarnTask<int> typedTask = ComputeAsync();
ValkarnTask voidTask = typedTask.AsNonGeneric();
await voidTask;   // attend la completion, ignore le résultat
```

C'est utile quand vous passez des tâches de types mixtes aux combinateurs ou quand vous ne vous souciez que du timing de completion, pas de la valeur.

---

## Combinateurs retournant `ValkarnTask<T>`

### `WhenAll` — surcharge typée à deux tâches

```csharp
public static ValkarnTask<(T1, T2)> WhenAll<T1, T2>(
    ValkarnTask<T1> task1, ValkarnTask<T2> task2)
```

Exécute les deux tâches en concurrence et retourne un tuple de résultats. Si une tâche est faultée ou annulée, la première exception gagne et l'erreur de l'autre est signalée via `ValkarnTask.UnobservedException`.

**La déstructuration de tuple** fonctionne naturellement avec la déconstruction C# :

```csharp
var (profile, inventory) = await ValkarnTask.WhenAll(
    FetchProfileAsync(userId),
    FetchInventoryAsync(userId)
);
```

**Chemin rapide zéro-allocation :** si les deux tâches sont terminées de manière synchrone au point d'appel, aucun objet mis en pool n'est créé et le tuple de résultats est retourné inline.

### `WhenAll` — surcharge de collection typée

```csharp
public static ValkarnTask<T[]> WhenAll<T>(IEnumerable<ValkarnTask<T>> tasks)
```

Attend toutes les tâches de la collection en concurrence et retourne un `T[]` dans l'ordre des index.

```csharp
var urls = new[] { "https://a.com", "https://b.com", "https://c.com" };
ValkarnTask<string>[] downloads = urls.Select(u => DownloadAsync(u)).ToArray();
string[] results = await ValkarnTask.WhenAll(downloads);
```

Si la collection est vide, retourne `ValkarnTask.FromResult(Array.Empty<T>())` — zéro allocation. Si toutes les tâches sont déjà terminées de manière synchrone, un tableau de résultats est construit inline sans créer une promise de combinateur.

### `WhenAny` — surcharge typée à deux tâches

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    ValkarnTask<T> task1, ValkarnTask<T> task2)
```

Retourne dès que l'une des tâches se termine. Le tuple de résultats contient l'index 0-basé du gagnant et sa valeur. Les tâches perdantes continuent à s'exécuter ; leurs erreurs (le cas échéant) sont signalées via `ValkarnTask.UnobservedException`. Les annulations perdantes ne sont intentionnellement pas signalées.

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(
    FetchFromCacheAsync(key),
    FetchFromNetworkAsync(key)
);

if (winnerIndex == 0)
    Debug.Log("Le cache a gagné");
```

### `WhenAny` — surcharge de collection typée

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    IEnumerable<ValkarnTask<T>> tasks)
```

Même sémantique que la surcharge à deux tâches, étendue à n'importe quel nombre de tâches. Requiert au moins une tâche ; lève `ArgumentException` pour les collections vides.

```csharp
var tasks = servers.Select(s => s.FetchAsync(query)).ToArray();
var (winnerIndex, data) = await ValkarnTask.WhenAny(tasks);
Debug.Log($"Le serveur {winnerIndex} a répondu en premier");
```

---

## Méthodes de fabrique utilitaires

### `ValkarnTask.Create<T>(Func<ValkarnTask<T>> factory)`

Invoque le délégué de fabrique et attend la tâche résultante. Utile quand vous voulez différer la construction d'une opération async.

```csharp
public static async ValkarnTask<T> Create<T>(Func<ValkarnTask<T>> factory)
```

```csharp
var result = await ValkarnTask.Create(() => LoadLevelDataAsync(levelId));
```

---

## Struct `Awaiter` (imbriqué)

`ValkarnTask<T>.Awaiter` est l'awaiter côté compilateur. C'est un `readonly struct` implémentant `ICriticalNotifyCompletion`. Vous n'interagissez normalement pas directement avec lui.

```csharp
public readonly struct Awaiter : ICriticalNotifyCompletion
{
    public bool IsCompleted { get; }
    public T GetResult();
    public void OnCompleted(Action continuation);
    public void UnsafeOnCompleted(Action continuation);
}
```

`UnsafeOnCompleted` est le chemin utilisé par `AsyncValkarnTaskMethodBuilder<T>`. Le label "unsafe" signifie que `ExecutionContext` n'est pas capturé — c'est intentionnel pour Unity où aucun `SynchronizationContext` n'est en effet.

Quand `IsCompleted` est vrai, appeler `GetResult()` lit le champ `result` inline (pour les tâches synchro) ou appelle via l'interface source (pour les tâches async). Les deux chemins sont `[MethodImpl(MethodImplOptions.AggressiveInlining)]`.

---

## Méthodes d'extension sur `ValkarnTask<T>`

### `AsResult<T>()`

```csharp
public static ValkarnTask<Result<T>> AsResult<T>(this ValkarnTask<T> task)
```

Enveloppe un `ValkarnTask<T>` dans un `ValkarnTask<Result<T>>`, capturant toute exception ou annulation et l'encodant dans la valeur `Result<T>`. Cela évite try/catch au site d'appel.

```csharp
Result<string> result = await FetchDataAsync(url).AsResult();

if (result.IsSuccess)
    Process(result.Value);
else if (result.IsCanceled)
    Debug.Log("Annulé");
else
    Debug.LogError(result.Exception);
```

**Chemin rapide synchro :** si la tâche source est déjà terminée de manière synchrone, `AsResult` retourne de manière synchrone sans machinerie async impliquée.

---

## `Promise<T>` — source de completion manuelle

`ValkarnTask.Promise<T>` est une source de completion manuelle allouée sur le tas pour les cas où vous avez besoin de contrôler quand un `ValkarnTask<T>` se termine, et la durée de vie n'est pas bornée par une seule opération async.

```csharp
public class Promise<T>
{
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

```csharp
// Encapsuler une API basée sur des callbacks
var promise = new ValkarnTask.Promise<string>();

SomeCallbackApi.OnComplete += value => promise.TrySetResult(value);
SomeCallbackApi.OnError   += ex    => promise.TrySetException(ex);

string result = await promise.Task;
```

Contrairement à `PooledPromise<T>`, `Promise<T>` n'est pas mis en pool. Il utilise un finaliseur pour détecter et signaler les exceptions non observées si la tâche est faultée et que l'appelant ne l'attend jamais.

Pour les patterns à haute fréquence (boucles producteur/consommateur, opérations par frame), préférez `ValkarnTask.PooledPromise<T>`, qui retourne automatiquement au pool après que `GetResult` est appelé.

---

## `PooledPromise<T>` — source de completion manuelle mise en pool

```csharp
public sealed class PooledPromise<T> : ValkarnTask.ISource<T>, IPoolNode<PooledPromise<T>>
{
    public static PooledPromise<T> Create(out uint token);
    public static PooledPromise<T> CreateCompleted(T result, out uint token);
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

Après que `GetResult` est appelé sur la tâche de support, la promise réinitialise son `ValkarnTaskCompletionCore<T>` et se retourne elle-même au pool. Une garde contre le double-retour garantit que cela se produit au plus une fois même si `GetResult` est appelé en concurrence.

```csharp
// Pattern : produire un ValkarnTask<T> qui se termine quand il est prêt
var promise = ValkarnTask.PooledPromise<int>.Create(out uint token);
ValkarnTask<int> task = promise.Task;

// Distribuer le travail de manière asynchrone
ThreadPool.QueueUserWorkItem(_ =>
{
    int result = DoWork();
    promise.TrySetResult(result);
});

// Le consommateur attend ; à la completion, la promise retourne au pool automatiquement
int value = await task;
```

---

## Résumé des moyens d'obtenir un `ValkarnTask<T>`

| Méthode | Quand l'utiliser |
|---------|-----------------|
| `return value` dans `async ValkarnTask<T>` | Méthodes async normales |
| `ValkarnTask.FromResult(value)` | Retours rapides synchrones |
| `ValkarnTask.FromException<T>(ex)` | Tâches pré-faultées |
| `ValkarnTask.FromCanceled<T>(ct)` | Tâches pré-annulées |
| `ValkarnTask.Run<T>(Func<T>, ...)` | Déchargement sur le thread pool |
| `ValkarnTask.Run<T>(Func<ValkarnTask<T>>, ...)` | Travail async sur le thread pool |
| `ValkarnTask.WhenAll<T1,T2>(t1, t2)` | Attendre deux tâches typées, obtenir un tuple |
| `ValkarnTask.WhenAll<T>(IEnumerable<...>)` | Attendre N tâches typées, obtenir un tableau |
| `ValkarnTask.WhenAny<T>(t1, t2)` | Première de deux tâches typées |
| `ValkarnTask.WhenAny<T>(IEnumerable<...>)` | Première de N tâches typées |
| `task.AsResult<T>()` | Encapsulation sûre aux exceptions |
| `new ValkarnTask.Promise<T>()` → `.Task` | Completion manuelle longue durée |
| `ValkarnTask.PooledPromise<T>.Create(...)` → `.Task` | Completion manuelle à haute fréquence |
