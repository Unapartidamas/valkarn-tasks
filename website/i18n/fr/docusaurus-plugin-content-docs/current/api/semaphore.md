---
sidebar_position: 7
title: ValkarnSemaphore
---

# ValkarnSemaphore

`ValkarnSemaphore` est un sémaphore asynchrone à zéro allocation construit sur `ValkarnTask`. Il fonctionne comme `SemaphoreSlim` mais s'intègre nativement avec le pipeline Valkarn Tasks — aucune allocation de `Task`, aucune pression sur le GC en régime stable.

```csharp
namespace UnaPartidaMas.Valkarn.Tasks
```

**Chemin rapide (sans contention) :** retourne `ValkarnTask.CompletedTask` immédiatement — zéro allocation.
**Chemin lent (avec contention) :** les nœuds d'attente sont empruntés depuis un pool borné et y sont retournés à la complétion — zéro GC en régime stable.

---

## Constructeur

```csharp
public ValkarnSemaphore(int initialCount, int maxCount)
```

| Paramètre | Description |
|-----------|-------------|
| `initialCount` | Nombre de créneaux disponibles immédiatement. Doit être dans `[0, maxCount]`. |
| `maxCount` | Borne supérieure du nombre de créneaux. Doit être > 0. |

---

## Propriétés

```csharp
public int CurrentCount { get; } // Créneaux disponibles actuellement (thread-safe)
```

---

## Méthodes

### WaitAsync

```csharp
public ValkarnTask WaitAsync(CancellationToken ct = default)
```

Acquiert un créneau de manière asynchrone. Retourne un `ValkarnTask` complété immédiatement si un créneau est disponible (zéro allocation). Suspend l'appelant jusqu'à ce que `Release()` soit appelé si aucun créneau n'est disponible. Les waiters sont servis dans l'ordre FIFO.

Lève `OperationCanceledException` si `ct` se déclenche pendant l'attente.

### Wait

```csharp
public void Wait(CancellationToken ct = default)
```

Variante synchrone. Bloque le thread appelant si aucun créneau n'est disponible. À utiliser uniquement lorsque `await` ne peut pas être utilisé (p. ex. points d'entrée non asynchrones).

### Release

```csharp
public void Release(int releaseCount = 1)
```

Restitue `releaseCount` créneaux. Les waiters en attente sont complétés dans l'ordre FIFO, en dehors du verrou interne — les continuations ne s'exécutent jamais avec le verrou acquis.

Lève `InvalidOperationException` si la libération dépasserait `maxCount`.

### Dispose

```csharp
public void Dispose()
```

Annule tous les waiters en attente et libère les ressources. Idempotent.

---

## Utilisation

### Exclusion mutuelle (1 créneau)

```csharp
var sem = new ValkarnSemaphore(initialCount: 1, maxCount: 1);

async ValkarnTask WriteFileAsync(string path, string content, CancellationToken ct)
{
    await sem.WaitAsync(ct);
    try
    {
        await File.WriteAllTextAsync(path, content, ct);
    }
    finally
    {
        sem.Release();
    }
}
```

### Concurrence bornée (N créneaux)

```csharp
// Autorise jusqu'à 4 requêtes réseau concurrentes.
var sem = new ValkarnSemaphore(initialCount: 4, maxCount: 4);

async ValkarnTask FetchAsync(string url, CancellationToken ct)
{
    await sem.WaitAsync(ct);
    try
    {
        await DownloadAsync(url, ct);
    }
    finally
    {
        sem.Release();
    }
}
```

### Porte producteur / consommateur

```csharp
// Démarre avec 0 créneau — le consommateur attend jusqu'à ce que le producteur signale.
var gate = new ValkarnSemaphore(initialCount: 0, maxCount: 1);

async ValkarnTask ProducerAsync(CancellationToken ct)
{
    await ProduceDataAsync(ct);
    gate.Release(); // signaler le consommateur
}

async ValkarnTask ConsumerAsync(CancellationToken ct)
{
    await gate.WaitAsync(ct); // attendre le signal
    await ConsumeDataAsync(ct);
}
```

---

## Sécurité des threads

Toutes les opérations sont thread-safe. `Release()` peut être appelé depuis n'importe quel thread, y compris les threads en arrière-plan. Les complétions sont dispatchées en dehors du verrou interne — une continuation qui réentre dans le sémaphore ne provoquera pas de deadlock.

---

## Comparaison avec SemaphoreSlim

| | `ValkarnSemaphore` | `SemaphoreSlim` |
|---|---|---|
| Allocation chemin rapide | Zéro | Zéro |
| Allocation chemin lent | Nœud en pool, GC zéro | Allocation de `Task` |
| Intégration avec ValkarnTask | Oui | Nécessite `.AsValkarnTask()` |
| `IDisposable` | Oui | Oui |
| Ordre FIFO | Oui | Oui |
| Annulation | Oui | Oui |
