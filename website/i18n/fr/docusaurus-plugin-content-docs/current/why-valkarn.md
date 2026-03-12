---
sidebar_position: 6
title: Pourquoi Valkarn Tasks
---

# Pourquoi Valkarn Tasks

> Async/await sans allocation pour Unity. Plus rapide qu'UniTask. Plus intelligent qu'Awaitable.

---

## Le problème : l'async dans Unity est cassé

Les développeurs Unity ont besoin d'opérations asynchrones partout : chargement de scènes, téléchargement d'assets, attente d'animations, délai de spawning, communication avec des serveurs. Aujourd'hui, les options sont :

| Option | Problème |
|--------|---------|
| **Coroutines** | Pas de valeurs de retour, pas de gestion d'erreurs, pas d'annulation, pas de composition |
| **System.Threading.Tasks** | Alloue à chaque appel `async` (~144–232 octets), déclenche le GC, pas de gestion du cycle de vie Unity |
| **UniTask** | Bien — mais : collision de tokens après 18 minutes, pas de diagnostics à la compilation, rapport d'erreurs dépendant des finaliseurs |
| **Unity Awaitable** (2023+) | Basé sur une classe (alloue), pas de combinateurs, pas de canaux, pas de support de tests |

Valkarn Tasks résout tous ces problèmes en un seul package généré par source, sans allocation.

---

## Zéro allocation — pourquoi chaque octet compte

### Ce que signifie l'allocation dans les jeux

Chaque `new object()`, `new List<T>()` ou appel `async Task` alloue sur le **tas managé**, suivi par le Garbage Collector. Unity utilise le GC Boehm qui présente deux problèmes critiques :

1. **Pauses stop-the-world** — lorsque le GC s'exécute, le jeu se fige. Une pause de 2 ms à 60 fps coûte 12% du budget d'image ; à 120 fps (VR) elle coûte 24%.
2. **Timing imprévisible** — le GC peut se déclencher pendant un combat de boss, une cinématique ou un match compétitif.

### Ce que fait Valkarn Tasks différemment

| Scénario | `System.Task` | UniTask | **Valkarn Tasks** |
|----------|--------------|---------|------------------|
| `async Method()` — complète de façon synchrone | 144 octets | **0 octet** | **0 octet** |
| `async Method()` — se suspend une fois | 232+ octets | 0 octet (poolé) | **0 octet (poolé)** |
| `WhenAll(a, b)` | 232 octets | 0 octet (poolé) | **0 octet (source-gen poolé)** |
| `WhenAny(a, b)` | 144 octets | 72 octets | **0 octet** |
| Promise (complétion manuelle) | 144 octets | 104 octets | **88 octets** |
| Promise poolée (réutilisable) | 144 octets | 0 octet | **0 octet** |

Dans une image de jeu typique avec 50–100 opérations async, `System.Task` génère 7–23 Ko de déchets. Valkarn Tasks en génère **zéro**.

### Ce que cela signifie pour votre jeu

- **Pas de saccades GC** — framerate stable sans à-coups causés par les opérations async
- **Compatible VR** — objectifs 90/120 fps sans pics de GC
- **Optimisé mobile** — moins de pression mémoire sur les appareils à RAM limitée
- **Certifié console** — le comportement mémoire prévisible aide à satisfaire les exigences de certification

---

## Performance — comparé aux meilleurs

Benchmarks : BenchmarkDotNet v0.14.0, .NET 9.0, Intel Core i7-10875H.

### Async/await de base — 2× plus rapide que ValueTask

| Benchmark | ValueTask | UniTask | **Valkarn Tasks** | vs ValueTask |
|-----------|-----------|---------|------------------|--------------|
| 100 tâches en bloc | 956 ns | 508 ns | **489 ns** | **1.95×** |
| 1 000 tâches en bloc | 9 697 ns | 5 016 ns | **4 728 ns** | **2.05×** |
| Avec CancellationToken | 38,8 ns | 36,2 ns | **29,6 ns** | **1.31×** |
| Gestion des exceptions | 10 399 ns | 8 247 ns | **9 248 ns** | **1.12×** |

Tous les chemins : **0 octet alloué**.

### Combinateurs — jusqu'à 9,6× plus rapide que Task

| Benchmark | Task | UniTask | **Valkarn Tasks** | vs Task |
|-----------|------|---------|------------------|---------|
| WhenAll (2 tâches) | 117 ns / 232o | 13,6 ns / 0o | **12,1 ns / 0o** | **9.6×** |
| WhenAll (5 tâches) | 156 ns / 272o | 25,1 ns / 0o | **25,3 ns / 0o** | **6.2×** |
| WhenAny (2 tâches) | 39,0 ns / 144o | 60,0 ns / 72o | **11,6 ns / 0o** | **3.4×** |
| Promise poolée | 59,5 ns / 144o | 53,6 ns / 0o | **38,3 ns / 0o** | **1.55×** |

WhenAny est **5,2× plus rapide qu'UniTask** et n'alloue aucun octet.

### Pool d'objets — 4,3 nanosecondes

| Opération | Temps | Allocation |
|-----------|------|------------|
| Emprunt + retour sur slot rapide du thread principal | **4,3 ns** | 0 octet |
| Pile Treiber inter-threads | ~15 ns | 0 octet |

Zéro opération atomique sur le thread principal — critique pour IL2CPP où `Volatile.Read` est 9,2× plus lent.

### Impact en conditions réelles (50 opérations async/image à 60 fps)

| Bibliothèque | Temps/image | Budget d'image | GC/seconde |
|---------|-----------|-------------|-----------|
| System.Task | ~48 µs | 0,29% | ~430 Ko/s |
| UniTask | ~25 µs | 0,15% | ~3,6 Ko/s |
| **Valkarn Tasks** | **~24 µs** | **0,14%** | **0 Ko/s** |

En 10 minutes, `System.Task` génère **~258 Mo** de déchets async. Valkarn Tasks en génère **zéro**.

---

## Fonctionnalités qu'aucune autre bibliothèque ne possède

### Annulation automatique du cycle de vie

```csharp
public partial class EnemySpawner : MonoBehaviour
{
    async ValkarnTask Start()
    {
        while (true)
        {
            await ValkarnTask.Delay(3000);
            SpawnWave();
        }
        // Annulé automatiquement quand ce GameObject est détruit.
        // Pas de CancellationToken. Pas de fuites mémoire. Pas de tâches zombies.
    }
}
```

Plus de `MissingReferenceException` causées par des méthodes async qui s'exécutent après la destruction. Plus de nettoyage manuel dans `OnDestroy`. Plus de `CancellationTokenSource` oubliés.

### Sections critiques

```csharp
async ValkarnTask SaveProgress()
{
    var data = await LoadPlayerData();       // annulable

    await using (ValkarnTask.Critical())
    {
        await db.Insert(data);              // se termine même si le GO est détruit
        await db.Commit();
    }                                       // l'annulation en attente s'applique maintenant

    await SendNotification();               // annulable à nouveau
}
```

Les écritures en base de données, les requêtes réseau et les sauvegardes de fichiers se terminent même lorsque le joueur quitte ou qu'une scène se décharge. Plus de sauvegardes corrompues. Plus d'analytics à moitié écrites. Plus de reçus perdus.

### Diagnostics à la compilation

| Diagnostic | Ce qu'il détecte |
|------------|----------------|
| TT001 | Double-await sur un ValkarnTask (bug use-after-free) |
| TT002 | Oubli d'attendre une tâche (échec silencieux) |
| TT012 | Boucles async sans vérification d'annulation (boucles zombies) |
| TT013 | Tâche retournée mais non attendue (bug fire-and-forget) |
| TT016 | Méthode async sans await (surcharge inutile) |
| TT017 | `[FireAndForget]` sur `ValkarnTask<T>` (perte d'un résultat) |

Bugs détectés dans l'IDE comme soulignements rouges — pas des crashes à l'exécution 20 minutes après le début des tests.

### Result\<T\> — gestion d'erreurs sans try/catch

```csharp
var result = await loadTask.AsResult();

if (result.Succeeded)       Use(result.Value);
else if (result.IsCanceled) ShowRetryDialog();
else                         Debug.LogError(result.Error);
```

Chaque chemin d'erreur est explicite. Pas d'exceptions avalées. Pas de gestionnaires manquants.

### Canaux avec contre-pression

```csharp
var channel = ValkarnTask.Channel.CreateBounded<EnemySpawnRequest>(capacity: 10);

// Producteur (logique de jeu)
await channel.Writer.WriteAsync(new EnemySpawnRequest { type = EnemyType.Dragon });

// Consommateur (système de spawn)
await foreach (var request in channel.Reader.ReadAllAsync())
    await SpawnEnemy(request);
```

Découple les systèmes proprement. Limite le taux de spawn. Met en file d'attente les messages réseau. Tamponne les événements d'entrée. Le producteur ralentit quand le consommateur ne peut pas suivre — évitant les pics de mémoire.

### Tests déterministes avec TestClock

```csharp
[Test]
public void Respawn_WaitsThreeSeconds()
{
    var clock = new TestClock();
    var task = spawner.RespawnEnemy();

    clock.Advance(TimeSpan.FromSeconds(2));
    Assert.IsFalse(task.IsCompleted);

    clock.Advance(TimeSpan.FromSeconds(1));
    Assert.IsTrue(task.IsCompleted);
}
```

Testez la logique dépendante du temps instantanément. Plus de `yield return new WaitForSeconds(3)` dans les tests. Plus de timing CI instable.

### Sécurité des tokens générationnels

UniTask utilise un token `short` (16 bits). Après 65 536 cycles de pool (~18 minutes de travail async actif), une référence obsolète lit silencieusement le résultat d'une autre tâche — un bug use-after-free pratiquement impossible à reproduire.

Valkarn Tasks utilise un compteur de génération `uint` par slot de pool : **4 294 967 296 cycles par slot** avant collision. Impossible dans tout scénario réaliste.

---

## Migration en minutes, pas en semaines

### Depuis UniTask

```
Étape 1 : Installez Valkarn Tasks
Étape 2 : Des ampoules jaunes apparaissent sur les usages UniTask dans l'IDE
Étape 3 : Clic droit → "Corriger toutes les occurrences dans la solution" (Ctrl+.)
Étape 4 : Supprimez la référence au package UniTask
```

**15 diagnostics de migration** (MIG001–MIG015) couvrent automatiquement toute l'API UniTask. Un projet typique avec 500–2 000 méthodes async migre en moins de 5 minutes. 95%+ entièrement automatisé.

### Depuis Unity Awaitable

La même migration en un clic :
- `async Awaitable` → `async ValkarnTask`
- `Awaitable.NextFrameAsync()` → `ValkarnTask.NextFrame()`
- `Awaitable.BackgroundThreadAsync()` → `ValkarnTask.SwitchToThreadPool()`
- `Awaitable.MainThreadAsync()` → supprimé (Valkarn s'exécute sur le thread principal par défaut)

---

## Matrice de comparaison

| Fonctionnalité | System.Task | UniTask | Awaitable | **Valkarn Tasks** |
|---------|------------|---------|-----------|------------------|
| Chemin sync sans allocation | Non | Oui | Non | **Oui** |
| Combinateurs sans allocation | Non | Non | Non | **Oui (source gen)** |
| Basé sur struct | Non | Oui | Non | **Oui** |
| Annulation automatique du cycle de vie | Non | Manuel | Partiel | **Automatique** |
| Pas d'annulation de tâches sœurs | Non | Non | Non | **Oui** |
| Sections critiques | Non | Non | Non | **Oui** |
| Result\<T\> (sans exception) | Non | Partiel | Non | **Oui** |
| TestClock | Non | Non | Non | **Oui** |
| Pont avec Job System | Non | Non | Non | **Oui** |
| Diagnostics à la compilation | Non | Non | Non | **Oui (17 règles)** |
| Pools bornés + écrêtage | Non | Non | N/A | **Oui** |
| Rapport d'erreurs déterministe | Non | Non (finaliseur) | Partiel | **Oui (retour au pool)** |
| Canaux complets | Oui (.NET) | Minimal | Non | **Oui** |
| Pont avec Awaitable | N/A | Minimal | Natif | **Transparent** |
| Pool optimisé IL2CPP | Non | Non (Volatile à chaque op) | N/A | **Oui (zéro atomique)** |
| Sécurité contre les collisions de tokens | N/A | 18 min (short) | N/A | **Jamais (uint gen)** |
| Migration automatique depuis UniTask | N/A | N/A | N/A | **Oui (15 corrections)** |
| Migration automatique depuis Awaitable | N/A | N/A | N/A | **Oui (8 corrections)** |

---

**Votre jeu mérite un async qui ne bégaie pas.**
