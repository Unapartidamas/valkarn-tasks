---
sidebar_position: 2
title: Canaux
---

# Canaux

Les canaux fournissent un pipeline thread-safe et capable d'async pour passer des données entre producteurs et consommateurs. Ils sont particulièrement adaptés aux systèmes de jeu où le travail est généré d'un côté (threads en arrière-plan, callbacks d'événements, completions de jobs) et doit être consommé de l'autre (thread principal, pools de workers).

## Créer un canal

Les canaux sont créés via la classe de fabrique statique `ValkarnTask.Channel`. Il n'y a pas de constructeur public.

### Canal non borné

```csharp
Channel<T> channel = ValkarnTask.Channel.CreateUnbounded<T>(bool multiConsumer = false);
```

Un canal non borné n'a pas de limite de capacité. `WriteAsync` et `TryWrite` réussissent toujours immédiatement tant que le canal n'a pas été terminé. Les éléments s'accumulent dans une `Queue<T>` interne jusqu'à ce qu'un consommateur les lise.

**Paramètres**

| Paramètre | Défaut | Description |
|-----------|--------|-------------|
| `multiConsumer` | `false` | Quand `true`, plusieurs appels `ReadAsync` concurrents sont supportés (consommateurs concurrents). Quand `false`, un seul lecteur peut attendre `ReadAsync` à la fois. |

### Canal borné

```csharp
Channel<T> channel = ValkarnTask.Channel.CreateBounded<T>(int capacity, bool multiConsumer = false);
```

Un canal borné contient au maximum `capacity` éléments dans un tampon circulaire de taille fixe. Quand le tampon est plein, `WriteAsync` suspend le code appelant jusqu'à ce que de l'espace devienne disponible (contre-pression). `TryWrite` retourne `false` immédiatement quand il est plein plutôt que d'attendre.

**Paramètres**

| Paramètre | Défaut | Description |
|-----------|--------|-------------|
| `capacity` | requis | Nombre maximum d'éléments que le tampon peut contenir. Doit être supérieur à zéro. |
| `multiConsumer` | `false` | Identique au non-borné — active plusieurs lecteurs concurrents. |

**Choisir entre borné et non borné**

Utilisez `CreateBounded` quand vous avez besoin d'appliquer une contre-pression — c'est-à-dire quand vous voulez que les producteurs ralentissent automatiquement si les consommateurs prennent du retard. Utilisez `CreateUnbounded` quand le taux de production est naturellement borné (comme les événements d'entrée) et qu'une mise en mémoire tampon non bornée est acceptable, ou quand vous avez déjà pris en compte la croissance de la mémoire.

---

## Channel&lt;T&gt;

`Channel<T>` est un conteneur qui expose deux côtés du pipeline comme objets séparés.

```csharp
public sealed class Channel<T>
{
    public ChannelReader<T> Reader { get; }
    public ChannelWriter<T> Writer { get; }
}
```

Gardez la référence `Writer` côté producteur et la référence `Reader` côté consommateur. Il n'y a pas d'exigence qu'ils soient sur le même thread.

---

## ChannelWriter&lt;T&gt;

`ChannelWriter<T>` est le côté écriture du canal. Obtenez-le depuis `channel.Writer`.

### TryWrite

```csharp
public abstract bool TryWrite(T item);
```

Tente d'écrire un élément sans se suspendre. Retourne `true` si l'élément a été accepté ; retourne `false` si le canal est plein (borné) ou a été terminé.

Utilisez `TryWrite` sur les chemins chauds où vous pouvez vous permettre d'abandonner des éléments, ou quand vous interrogez dans une boucle et voulez éviter l'allocation de machines d'état async.

```csharp
if (!channel.Writer.TryWrite(item))
{
    // le canal est plein ou fermé — gérer en conséquence
}
```

### WriteAsync

```csharp
public abstract ValkarnTask WriteAsync(T item);
```

Écrit un élément dans le canal, suspendant l'appelant de manière asynchrone si nécessaire.

- **Canaux non bornés** : se termine toujours de manière synchrone (chemin rapide zéro-allocation) tant que le canal est ouvert.
- **Canaux bornés** : se termine de manière synchrone quand il y a de l'espace dans le tampon ; suspend l'appelant et met en file d'attente un enregistrement de writer en attente quand le tampon est plein. L'appelant reprend dès qu'un consommateur lit un élément et libère un slot.

Lève `ChannelClosedException` si le canal a été terminé via `Complete()` avant ou pendant l'écriture.

```csharp
await channel.Writer.WriteAsync(item);
```

Plusieurs producteurs peuvent appeler `WriteAsync` en concurrence sur un canal borné. Chaque writer suspendu est mis en file d'attente et débloqué dans l'ordre FIFO dès que de l'espace devient disponible.

### Complete

```csharp
public abstract void Complete();
```

Signale qu'aucun autre élément ne sera écrit. Après l'appel de `Complete()` :

- Tous les éléments déjà écrits restent dans le tampon et peuvent encore être consommés.
- Les nouveaux appels à `WriteAsync` ou `TryWrite` échoueront avec `ChannelClosedException`.
- Une fois le tampon entièrement drainé, `ChannelReader<T>.Completion` se termine et tout appel `ReadAsync` en attente ou futur lève `ChannelClosedException`.

`Complete()` est idempotent sur un seul appel — l'appeler plusieurs fois est sûr (les appels suivants sont ignorés).

```csharp
// Signaler la fin du travail
channel.Writer.Complete();
```

---

## ChannelReader&lt;T&gt;

`ChannelReader<T>` est le côté lecture du canal. Obtenez-le depuis `channel.Reader`.

### ReadAsync

```csharp
public abstract ValkarnTask<T> ReadAsync();
```

Lit le prochain élément du canal. Si aucun élément n'est actuellement disponible, l'appelant se suspend de manière asynchrone jusqu'à ce qu'un arrive. Quand le canal est à la fois terminé et entièrement drainé, lève `ChannelClosedException`.

```csharp
T item = await channel.Reader.ReadAsync();
```

**Mode consommateur unique** (par défaut) : un seul `ReadAsync` peut être en vol à la fois. Tenter de démarrer un second `ReadAsync` concurrent lève immédiatement. Cette restriction permet une optimisation zéro-allocation interne — le cœur lecteur est embarqué directement dans l'implémentation du canal plutôt que d'être alloué depuis un pool par appel.

**Mode multi-consommateur** (`multiConsumer: true`) : n'importe quel nombre d'appels `ReadAsync` peut être en attente simultanément. Chaque appelant en attente est mis en file d'attente et résolu dans l'ordre FIFO dès que des éléments deviennent disponibles.

### TryRead

```csharp
public abstract bool TryRead(out T item);
```

Tente de lire un élément sans se suspendre. Retourne `true` et peuple `item` si un élément était disponible ; retourne `false` si le canal est vide (définit `item` à `default`).

`TryRead` ne distingue pas entre un canal vide-mais-ouvert et un canal vide-et-fermé. Utilisez `Completion` pour détecter l'état fermé quand vous utilisez `TryRead` dans une boucle d'interrogation.

### ReadAllAsync

```csharp
public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default);
```

Retourne un `IAsyncEnumerable<T>` qui itère sur tous les éléments jusqu'à ce que le canal soit terminé et drainé. L'énumération se termine proprement sans propager `ChannelClosedException` — elle s'arrête simplement.

```csharp
await foreach (var item in channel.Reader.ReadAllAsync(ct))
{
    Process(item);
}
// Arrivé ici quand le canal est terminé et vide
```

Le token d'annulation passé à `ReadAllAsync` est utilisé comme secours. Si `GetAsyncEnumerator` reçoit également un token (comme le fait `await foreach` via `WithCancellation`), le token côté `foreach` a la priorité.

### Completion

```csharp
public abstract ValkarnTask Completion { get; }
```

Un `ValkarnTask` qui se termine quand le canal est entièrement drainé après que `Complete()` a été appelé. Spécifiquement :

- Si `Complete()` est appelé sur un canal déjà vide, `Completion` se résout immédiatement.
- Si `Complete()` est appelé pendant que des éléments restent dans le tampon, `Completion` se résout seulement après que le dernier élément est consommé.

Attendre `Completion` est la façon canonique d'attendre qu'un pipeline se termine.

```csharp
channel.Writer.Complete();
await channel.Reader.Completion;
// Tous les éléments ont été consommés
```

---

## ChannelClosedException

```csharp
public sealed class ChannelClosedException : InvalidOperationException
```

Levée dans deux circonstances :

1. **Lecture depuis un canal terminé et drainé** — `ReadAsync()` lève quand le canal a été marqué terminé et qu'aucun élément ne reste.
2. **Écriture vers un canal terminé** — `WriteAsync()` lève quand `Complete()` a été appelé avant l'écriture.

`ChannelClosedException` hérite d'`InvalidOperationException`. Elle n'est pas levée par `TryRead` ou `TryWrite`, qui retournent `false` à la place.

Constructeurs :

```csharp
new ChannelClosedException()
new ChannelClosedException(string message)
new ChannelClosedException(Exception innerException)
```

---

## Canal borné : Contre-pression en détail

Quand le tampon d'un canal borné est plein et que `WriteAsync` est appelé, le writer est suspendu et un enregistrement de writer en attente est mis en file d'attente en interne. Le writer conserve son élément. Quand un consommateur appelle `ReadAsync` ou `TryRead` et défile un élément :

1. Le slot libéré est immédiatement réclamé par le writer en attente le plus ancien.
2. L'élément de ce writer est placé dans le tampon.
3. Le code en attente du writer reprend.

Cela signifie qu'un canal borné plein ne perd jamais d'éléments et ne gaspille jamais la capacité du tampon — il y a toujours une correspondance un-à-un entre un slot qui se libère et un writer bloqué qui reprend. Dans les cas où un lecteur arrive pendant que des writers en attente attendent mais que le tampon est vide, l'élément est transféré directement sans toucher le tampon du tout.

---

## Patterns

### Producteur/consommateur de base

```csharp
var channel = ValkarnTask.Channel.CreateUnbounded<WorkItem>();

// Producteur (ex. s'exécute sur un thread en arrière-plan ou depuis des callbacks)
async ValkarnTask ProduceAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var work = await FetchNextWorkItemAsync(ct);
        await channel.Writer.WriteAsync(work);
    }
    channel.Writer.Complete();
}

// Consommateur (s'exécute où vous choisissez de l'appeler)
async ValkarnTask ConsumeAsync()
{
    await foreach (var item in channel.Reader.ReadAllAsync())
    {
        await ProcessAsync(item);
    }
}
```

### Plusieurs producteurs, consommateur unique

Plusieurs producteurs peuvent chacun détenir une référence à `channel.Writer` et appeler `WriteAsync` en concurrence. Toutes les opérations de canal sont protégées par un verrou interne, donc c'est sûr.

```csharp
var channel = ValkarnTask.Channel.CreateBounded<Event>(capacity: 64);

// Plusieurs producteurs écrivant en concurrence
ValkarnTask ProducerA() => ProduceFrom(sourceA, channel.Writer);
ValkarnTask ProducerB() => ProduceFrom(sourceB, channel.Writer);
ValkarnTask ProducerC() => ProduceFrom(sourceC, channel.Writer);

// Consommateur unique (par défaut — pas besoin du flag multiConsumer)
async ValkarnTask ConsumerAsync()
{
    await foreach (var ev in channel.Reader.ReadAllAsync())
        HandleEvent(ev);
}
```

Quand vous utilisez plusieurs producteurs avec un canal borné, coordonnez `Complete()` attentivement — appelez-le seulement après que tous les producteurs ont fini d'écrire, sinon certains writers peuvent recevoir `ChannelClosedException`.

### Plusieurs producteurs, plusieurs consommateurs

```csharp
// multiConsumer: true active ReadAsync concurrent depuis plusieurs consommateurs
var channel = ValkarnTask.Channel.CreateUnbounded<Job>(multiConsumer: true);

async ValkarnTask WorkerAsync(int id, CancellationToken ct)
{
    try
    {
        while (true)
        {
            var job = await channel.Reader.ReadAsync();
            await ExecuteJobAsync(job, ct);
        }
    }
    catch (ChannelClosedException)
    {
        // Le canal est terminé — sortir proprement
    }
}
```

Chaque élément est livré à exactement un consommateur. Les consommateurs se disputent les éléments dans l'ordre FIFO (le consommateur qui attend depuis le plus longtemps obtient le prochain élément disponible).

### Arrêt propre

La séquence d'arrêt recommandée est :

1. Signaler à tous les producteurs de s'arrêter (ex. annuler leur `CancellationToken`).
2. Appeler `channel.Writer.Complete()` après que tous les producteurs ont arrêté d'écrire.
3. Attendre `channel.Reader.Completion` pour confirmer que tous les éléments ont été consommés.

```csharp
cts.Cancel();                        // arrêter les producteurs
await allProducersTask;              // attendre qu'ils quittent
channel.Writer.Complete();           // fermer le canal
await channel.Reader.Completion;     // drainer les éléments restants
```

Si vous utilisez `ReadAllAsync`, l'étape 3 se produit automatiquement — la boucle `await foreach` quitte quand le canal est terminé et vide.

---

## Comparaison avec System.Threading.Channels

| Fonctionnalité | Valkarn Channels | System.Threading.Channels |
|----------------|-----------------|---------------------------|
| Type de retour | `ValkarnTask` / `ValkarnTask<T>` | `ValueTask` / `ValueTask<T>` |
| Allocation (chemin chaud) | Zéro (non borné consommateur unique) | Quasi-zéro |
| `WaitToReadAsync` | Absent — utilisez `ReadAsync` ou `ReadAllAsync` | Présent |
| `TryComplete(Exception)` | Absent — utilisez `Complete()` | Présent |
| `Count` / `CanCount` | Non exposé | Présent sur certains types de canaux |
| Politique de abandon quand plein | Non supporté — `WriteAsync` bloque | `DropWrite`, `DropNewest`, `DropOldest`, `Wait` |
| Énumérable async | `ReadAllAsync()` | `ReadAllAsync()` |
| Thread-safety | Complète (basée sur verrous) | Complète (basée sur verrous) |

La différence principale est que Valkarn Channels s'intègre nativement avec `ValkarnTask` pour un await sans surcharge dans les builds Unity, et le chemin consommateur unique évite une location/retour de pool à chaque appel `ReadAsync` en embarquant le cœur de completion directement dans le canal.
