---
sidebar_position: 3
title: Sources de completion
---

# ValkarnTaskCompletionSource

`ValkarnTaskCompletionSource<T>` et le non-générique `ValkarnTaskCompletionSource` vous donnent un contrôle manuel sur un `ValkarnTask`. Ce sont l'équivalent async d'écrire le résultat vous-même — vous détenez un objet source, distribuez son `.Task` aux appelants, puis le résolvez, le fautez ou l'annulez depuis un site d'appel séparé.

C'est l'équivalent Valkarn Tasks de `TaskCompletionSource<T>` de la BCL, mais soutenu par `ValkarnTask.Promise<T>` pour maintenir le modèle d'allocation cohérent avec le reste de la bibliothèque.

---

## ValkarnTaskCompletionSource&lt;T&gt;

```csharp
public class ValkarnTaskCompletionSource<T>
{
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled();
}
```

### Task

```csharp
public ValkarnTask<T> Task { get; }
```

La tâche que le code en attente observera. Distribuez-la à tous les appelants qui ont besoin d'attendre le résultat. Plusieurs appelants peuvent `await` la même tâche en concurrence.

### TrySetResult

```csharp
public bool TrySetResult(T result);
```

Termine la tâche avec succès avec la valeur donnée. Toutes les continuations en attente reprennent. Retourne `true` si la completion a été acceptée ; retourne `false` si la tâche était déjà terminée (par tout appel `TrySet*` précédent). Ne lève jamais d'exception.

### TrySetException

```csharp
public bool TrySetException(Exception ex);
```

Faulte la tâche avec l'exception fournie. Le code en attente reçoit l'exception quand il appelle `await`. Lève `ArgumentNullException` si `ex` est `null`. Retourne `true` si acceptée, `false` si déjà terminée.

### TrySetCanceled

```csharp
public bool TrySetCanceled();
```

Annule la tâche. Le code en attente reçoit une `OperationCanceledException`. Retourne `true` si acceptée, `false` si déjà terminée.

---

## ValkarnTaskCompletionSource non-générique

Il n'y a pas de classe `ValkarnTaskCompletionSource` non-générique séparée dans l'API publique. Pour les tâches manuelles retournant void, utilisez `ValkarnTask.Promise` directement :

```csharp
var promise = new ValkarnTask.Promise();
ValkarnTask task = promise.Task;

promise.TrySetResult();    // terminer
promise.TrySetException(ex);
promise.TrySetCanceled();
```

`ValkarnTask.Promise` expose la même surface `TrySet*` et la même protection contre la double completion, mais produit un `ValkarnTask` non-générique plutôt qu'un `ValkarnTask<T>`.

---

## Protection contre la double completion

Toutes les méthodes `TrySet*` sont sûres à appeler depuis n'importe quel thread à tout moment, y compris en concurrence. Le premier appel qui gagne un compare-and-swap sur la machine d'état interne réussit ; chaque appel suivant retourne `false` et n'a aucun effet. Cela signifie :

- Appeler `TrySetResult` deux fois ne fait rien au second appel.
- Appeler `TrySetResult` après `TrySetException` ne fait rien.
- Deux threads qui se disputent pour terminer la même source simultanément sont sûrs — l'un gagne, l'autre est silencieusement ignoré.

Si vous avez besoin de savoir quel appelant a "gagné", vérifiez la valeur de retour. Si vous n'en avez pas besoin (signal fire-and-forget), vous pouvez l'ignorer en toute sécurité.

```csharp
// Course sûre — seulement l'un d'eux terminera réellement la tâche
_ = source.TrySetResult(value);
_ = source.TrySetCanceled();
```

---

## Patterns courants

### Ponter une API de callback

De nombreuses APIs Unity et plateformes livrent des résultats via des callbacks plutôt qu'async/await. `ValkarnTaskCompletionSource<T>` vous permet de les encapsuler proprement.

```csharp
public ValkarnTask<Texture2D> LoadTextureAsync(string url)
{
    var tcs = new ValkarnTaskCompletionSource<Texture2D>();

    StartCoroutine(LoadCoroutine(url, tcs));

    return tcs.Task;
}

IEnumerator LoadCoroutine(string url, ValkarnTaskCompletionSource<Texture2D> tcs)
{
    var request = UnityWebRequestTexture.GetTexture(url);
    yield return request.SendWebRequest();

    if (request.result == UnityWebRequest.Result.Success)
        tcs.TrySetResult(DownloadHandlerTexture.GetContent(request));
    else
        tcs.TrySetException(new Exception(request.error));
}
```

Les appelants peuvent maintenant simplement `await` la tâche retournée :

```csharp
Texture2D tex = await LoadTextureAsync("https://example.com/image.png");
```

### Signal à usage unique (porte async)

Utilisez `ValkarnTask.Promise` quand vous avez besoin d'un signal qui se déclenche une fois et débloque n'importe quel nombre d'attenteurs. C'est similaire à `ManualResetEventSlim` mais natif async.

```csharp
public class AsyncGate
{
    readonly ValkarnTask.Promise _promise = new();

    // N'importe quel nombre d'appelants peut attendre ceci
    public ValkarnTask WaitAsync() => _promise.Task;

    // Appeler une fois pour débloquer tout le monde
    public void Open() => _promise.TrySetResult();
}

// Utilisation
var gate = new AsyncGate();

// Plusieurs systèmes attendent la porte indépendamment
async ValkarnTask SystemAAsync()
{
    await gate.WaitAsync();
    // procéder après l'ouverture de la porte
}

async ValkarnTask SystemBAsync()
{
    await gate.WaitAsync();
    // procède en même temps que SystemA
}

// Ailleurs — ouvrir la porte
gate.Open();
```

Une fois que `TrySetResult()` est appelé, tous les appels `await` actuels et futurs sur la tâche se terminent immédiatement (de manière synchrone si la tâche est déjà terminée au moment où ils s'exécutent).

### Encapsuler une opération async tierce avec support d'annulation

```csharp
public ValkarnTask<Result> RunWithTimeoutAsync(
    Func<ValkarnTask<Result>> operation,
    float timeoutSeconds,
    CancellationToken ct)
{
    var tcs = new ValkarnTaskCompletionSource<Result>();

    RunCoreAsync(tcs, operation, timeoutSeconds, ct).Forget();

    return tcs.Task;
}

async ValkarnTask RunCoreAsync(
    ValkarnTaskCompletionSource<Result> tcs,
    Func<ValkarnTask<Result>> operation,
    float timeout,
    CancellationToken ct)
{
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
    linked.CancelAfter(TimeSpan.FromSeconds(timeout));

    try
    {
        var result = await operation();
        tcs.TrySetResult(result);
    }
    catch (OperationCanceledException)
    {
        tcs.TrySetCanceled();
    }
    catch (Exception ex)
    {
        tcs.TrySetException(ex);
    }
}
```

### Porte d'initialisation différée

Un pattern Unity courant est d'exposer une tâche "ready" que les composants peuvent attendre, que l'initialisation ait démarré ou non.

```csharp
public class ServiceBus : MonoBehaviour
{
    readonly ValkarnTask.Promise _readyPromise = new();

    // N'importe qui peut attendre ceci à n'importe quel moment — avant ou après Initialize()
    public ValkarnTask Ready => _readyPromise.Task;

    async void Start()
    {
        await LoadConfigAsync();
        await ConnectAsync();
        _readyPromise.TrySetResult();  // débloque tous les attenteurs
    }
}

// Dans tout autre composant
async ValkarnTask OnEnableAsync()
{
    await ServiceBus.Instance.Ready;  // attend si pas prêt, retourne instantanément si déjà prêt
    DoWork();
}
```

---

## Relation avec ValkarnTask.Promise

`ValkarnTaskCompletionSource<T>` est un mince wrapper public autour de `ValkarnTask.Promise<T>`. Les deux fournissent la même fonctionnalité. La différence est la convention de nommage :

| Type | Retourne | Utilisation courante |
|------|---------|---------------------|
| `ValkarnTaskCompletionSource<T>` | `ValkarnTask<T>` | API publique, reflète le style BCL `TaskCompletionSource<T>` |
| `ValkarnTask.Promise<T>` | `ValkarnTask<T>` | Usage interne, légèrement plus direct |
| `ValkarnTask.Promise` | `ValkarnTask` | Signaux void (portes, événements) |

Les trois soutiennent leur tâche avec un `ValkarnTaskCompletionCore<T>`, la machine d'état interne basée sur struct qui gère la thread-safety et la course continuation/résultat en utilisant un protocole en deux phases basé sur CAS.
