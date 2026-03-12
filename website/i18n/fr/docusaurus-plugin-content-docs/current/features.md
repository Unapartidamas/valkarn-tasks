---
sidebar_position: 4
title: Fonctionnalités
---

# Fonctionnalités

Référence complète des fonctionnalités de Valkarn Tasks.

---

## Primitive async principale

### Struct ValkarnTask

Un type de retour async basé sur struct et sans allocation qui remplace à la fois `UniTask` et `Awaitable` d'Unity.

```csharp
async ValkarnTask LoadLevel() { ... }
async ValkarnTask<int> CountEnemies() { ... }
```

- **Chemin rapide synchrone sans allocation** — si la méthode se termine sans jamais se suspendre, aucune allocation sur le tas ne se produit.
- **Chemin async avec pool** — si la méthode se suspend, le runner de la machine à états est extrait d'un pool borné et réductible. Zéro boxing, pools bornés avec écrêtage automatique.
- **Pool optimisé IL2CPP** — les opérations de pool sur le thread principal utilisent zéro atomique. IL2CPP pénalise `Volatile` de 9,2× et `Interlocked` de 2,9× par rapport à Mono ; Valkarn évite les deux sur le chemin critique.
- **Sécurité des tokens générationnels** — un compteur de génération `uint` par slot de pool. 4 294 967 296 cycles par slot avant collision — impossible en pratique. (UniTask utilise un token `short` : collision après ~18 minutes de travail async actif.)

### Result\<T\> — gestion d'erreurs sans exception

```csharp
var result = await loadTask.AsResult();

if (result.Succeeded)    Use(result.Value);
else if (result.IsCanceled) ShowRetry();
else                         Debug.LogError(result.Error);
```

`Result<T>` et `Result` sont des valeurs `readonly struct` qui représentent le résultat d'une tâche sans lancer d'exception. Les deux supportent la conversion implicite en `bool` (vrai en cas de succès).

### Sources de complétion manuelle

```csharp
var promise = new ValkarnTask.Promise<string>();
promise.TrySetResult("done");
string value = await promise.Task;
```

Supporte `TrySetResult`, `TrySetException` et `TrySetCanceled`. Le rapport d'exceptions non observées basé sur les finaliseurs garantit que les erreurs ne sont jamais perdues silencieusement.

### Sources de complétion poolées

Variantes poolées avec auto-reset pour les modèles répétitifs (utilisées en interne par les canaux et les combinateurs) :

```csharp
var source = ValkarnTask.PooledPromise<int>.Create(out uint token);
source.TrySetResult(42);
int value = await source.Task; // la source retourne au pool automatiquement
```

---

## Pont avec Awaitable

Interopérabilité transparente avec `Awaitable` d'Unity — sans conversion manuelle :

```csharp
async ValkarnTask LoadGame()
{
    await SceneManager.LoadSceneAsync("Level2"); // Unity Awaitable — fonctionne directement
    await Resources.LoadAsync<Texture2D>("hero"); // Unity Awaitable — fonctionne directement
    await ValkarnTask.Delay(1000);                // natif Valkarn
}
```

Le générateur de source détecte les awaits `Awaitable` et génère l'adaptateur automatiquement.

---

## Annulation du cycle de vie

### Automatique (générée par source)

Marquez la classe `partial` — le générateur de source s'occupe du reste :

```csharp
public partial class EnemySpawner : MonoBehaviour
{
    async ValkarnTask Start()
    {
        while (true)
        {
            await ValkarnTask.Delay(3000);
            SpawnWave();
            // Annulé automatiquement quand ce GameObject est détruit.
        }
    }
}
```

- **MonoBehaviour** — lié à `destroyCancellationToken`
- **ScriptableObject** — lié à la durée de vie de l'application
- **Classe simple** — pas de liaison automatique (token manuel requis)

### Désactivation

```csharp
[NoAutoCancel]
async ValkarnTask BackgroundSync()
{
    await SyncToServer(); // pas d'annulation automatique à la destruction
}
```

### Override manuel de CancellationToken

Passer un `CancellationToken` explicite remplace le token de cycle de vie injecté automatiquement :

```csharp
async ValkarnTask DoWork(CancellationToken ct)
{
    await ValkarnTask.Delay(1000, ct);
}
```

### Pas d'annulation de tâches sœurs

Les tâches n'annulent jamais les tâches sœurs. `WhenAll` attend TOUTES les tâches ; `WhenAny` retourne le premier résultat mais les tâches perdantes continuent de s'exécuter. Cela prévient la corruption de données lorsque les tâches ont des effets de bord.

---

## Sections critiques

Pour les opérations qui ne doivent pas être interrompues par l'annulation du cycle de vie :

```csharp
async ValkarnTask SaveProgress()
{
    var data = await LoadPlayerData();        // annulable

    await using (ValkarnTask.Critical())
    {
        await db.Insert(data);               // PAS annulé même si le GO est détruit
        await db.Commit();
    }                                        // l'annulation en attente s'applique ici

    await SendNotification();                // annulable à nouveau
}
```

À l'intérieur d'une section critique, l'annulation est **différée** — pas ignorée. Quand la section se termine, l'annulation en attente est appliquée.

---

## Combinateurs

### WhenAll (typé)

```csharp
// Direct — lève une exception si une tâche échoue
var (enemies, map) = await ValkarnTask.WhenAll(LoadEnemies(), LoadMap());

// Sûr — enveloppez avec AsResult() pour un comportement sans exception
var (a, b) = await ValkarnTask.WhenAll(
    LoadEnemies().AsResult(), LoadMap().AsResult());
```

Chemin rapide synchrone sans allocation quand toutes les tâches sont déjà terminées. La surcharge `IEnumerable<ValkarnTask<T>>` utilise `ArrayPool<T>` pour les tableaux internes.

### WhenAll (void)

```csharp
await ValkarnTask.WhenAll(SaveA(), SaveB(), SaveC());
await ValkarnTask.WhenAll(taskList); // IEnumerable<ValkarnTask>
```

### WhenAny

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(DownloadFromA(), DownloadFromB());
```

Retourne le premier résultat complété. Les tâches perdantes continuent de s'exécuter normalement.

### Fire-and-forget

```csharp
SendAnalytics("event").Forget();

[FireAndForget]
async ValkarnTask SendAnalytics(string eventName) { ... }
// Les appelants n'ont pas besoin de .Forget() — aucun avertissement généré
```

`.Forget()` route les erreurs vers `ValkarnTask.UnobservedException`. Jamais avalées silencieusement.

### AsNonGeneric

```csharp
ValkarnTask voidTask = typedTask.AsNonGeneric();
```

---

## Temps et délais

```csharp
await ValkarnTask.Delay(1000);                                    // millisecondes
await ValkarnTask.Delay(TimeSpan.FromSeconds(2));                  // TimeSpan
await ValkarnTask.Delay(1000, DelayType.UnscaledDeltaTime);       // ignore le timescale
await ValkarnTask.Delay(1000, DelayType.Realtime);                 // basé sur Stopwatch

await ValkarnTask.Yield();                                         // prochain tick du PlayerLoop
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);             // timing spécifique
await ValkarnTask.NextFrame();                                      // image suivante garantie
await ValkarnTask.DelayFrame(5);                                    // N images

await ValkarnTask.WaitUntil(() => player.IsReady);
await ValkarnTask.WaitWhile(() => isLoading);
```

---

## Changement de thread

```csharp
async ValkarnTask ProcessData()
{
    var raw = await DownloadData();

    await ValkarnTask.SwitchToThreadPool();
    var processed = HeavyComputation(raw);  // thread en arrière-plan

    await ValkarnTask.SwitchToMainThread();
    ApplyToGameObject(processed);           // thread principal
}
```

---

## 16 timings du PlayerLoop

| Groupe | Timings |
|-------|---------|
| Initialization | `Initialization`, `LastInitialization` |
| EarlyUpdate | `EarlyUpdate`, `LastEarlyUpdate` |
| FixedUpdate | `FixedUpdate`, `LastFixedUpdate` |
| PreUpdate | `PreUpdate`, `LastPreUpdate` |
| Update | `Update`, `LastUpdate` |
| PreLateUpdate | `PreLateUpdate`, `LastPreLateUpdate` |
| PostLateUpdate | `PostLateUpdate`, `LastPostLateUpdate` |
| TimeUpdate | `TimeUpdate`, `LastTimeUpdate` |

Toutes les opérations utilisent `PlayerLoopTiming.Update` par défaut sauf indication contraire.

---

## Canaux

```csharp
// Non borné
var channel = ValkarnTask.Channel.CreateUnbounded<EnemySpawnRequest>();

// Borné — contre-pression quand plein
var channel = ValkarnTask.Channel.CreateBounded<LogEntry>(capacity: 100);

// Multi-consommateur
var channel = ValkarnTask.Channel.CreateUnbounded<WorkItem>(multiConsumer: true);

// Producteur
await channel.Writer.WriteAsync(entry);
bool accepted = channel.Writer.TryWrite(entry);

// Consommateur
await foreach (var item in channel.Reader.ReadAllAsync())
    Process(item);

// Complétion
channel.Writer.Complete();
await channel.Reader.Completion;
```

---

## Tests déterministes (TestClock)

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

Toutes les opérations dépendantes du temps lisent depuis `TimeProvider.Current`. Dans les tests, remplacez-le par `TestClock`. `AdvanceFrame()` simule un tick du player loop.

---

## Pont avec le Job System

```csharp
var job = new PathfindingJob { start = a, end = b, results = results };
await job.ScheduleAsync();
// le NativeArray de résultats est lisible immédiatement après l'await

await job.ScheduleParallelAsync(dataCount, batchSize); // IJobParallelFor

// Annulation — complète le job handle avant de signaler l'annulation (pas de fuite de job)
await job.ScheduleAsync(cancellationToken);
```

---

## Diagnostics à la compilation

| Code | Sévérité | Description |
|------|----------|-------------|
| TT001 | Avertissement | Double-await / use-after-free sur un `ValkarnTask` |
| TT002 | Erreur | Résultat d'`async ValkarnTask` utilisé comme instruction d'expression — doit être attendu ou avec `.Forget()` |
| TT010 | Info | Méthode async dans MonoBehaviour annulée automatiquement à la destruction |
| TT011 | Avertissement | `WhenAll` contient des tâches avec des durées de vie différentes |
| TT012 | Avertissement | Boucle async sans vérification d'annulation (boucle zombie potentielle) |
| TT013 | Avertissement | `ValkarnTask` retourné mais jamais attendu et pas explicitement ignoré |
| TT014 | Avertissement | `[NoAutoCancel]` sans paramètre `CancellationToken` manuel |
| TT015 | Info | Await d'`Awaitable` dans un `ValkarnTask` — adaptateur de pont généré |
| TT016 | Avertissement | Méthode async sans expression await |
| TT017 | Avertissement | `[FireAndForget]` sur `ValkarnTask<T>` — ignore la valeur de retour |

---

## Gestion du pool

Chaque runner de méthode async est mis en pool via `ValkarnPool<T>` :

- **Thread principal** — pile sans verrou, zéro atomique
- **Threads en arrière-plan** — pile Treiber sans verrou avec opérations CAS
- **Écrêtage basé sur les images** — toutes les 300 images (~5s à 60fps), les objets excédentaires sont libérés progressivement
- **Ne se réduit jamais** en dessous du minimum configurable (par défaut : 8)

Surveillez à l'exécution :

```csharp
foreach (var (type, size, maxSize) in ValkarnTask.GetPoolInfo())
    Debug.Log($"{type}: {size}/{maxSize}");
```

---

## ValkarnTaskSettings

Configurez via `ScriptableObject` (`Assets > Create > Valkarn > Tasks > Task Settings`, placez dans `Resources/`) :

| Paramètre | Par défaut | Description |
|---------|---------|-------------|
| `DefaultMaxPoolSize` | 256 | Nombre maximum d'éléments par type de pool |
| `MinPoolSize` | 8 | Ne jamais écrêter en dessous de cette valeur |
| `TrimCheckInterval` | 300 | Images entre les vérifications d'écrêtage |
| `TrimHysteresisCount` | 2 | Vérifications consécutives avant écrêtage |
| `TrimReleaseRatio` | 0,25 | Fraction de l'excédent libéré par cycle |
| `EnableAutoCancel` | true | Lie automatiquement les tâches MonoBehaviour à destroyCancellationToken |
| `LogUnobservedCancellations` | false | Journalise les annulations non observées en avertissements |
| `MaxExceptionLogsPerFrame` | 10 | Limite des logs d'exception par image |

---

## Gestion des erreurs

```csharp
// Exceptions non observées — déclenchées de façon déterministe au retour au pool
ValkarnTask.UnobservedException += ex =>
    Debug.LogError($"Unobserved: {ex}");
```

- `WhenAll` — lève la première exception, route les supplémentaires vers `UnobservedException`
- `WhenAny` — l'exception du gagnant est levée ; les défauts des perdants vont à `UnobservedException`
- Annulation du cycle de vie — `OperationCanceledException` supprimée par défaut (configurable)

### Méthodes de fabrique

```csharp
ValkarnTask.CompletedTask          // void, sans allocation
ValkarnTask.FromResult<T>(value)   // typé, sans allocation
ValkarnTask.FromException(ex)      // en échec
ValkarnTask.FromException<T>(ex)
ValkarnTask.FromCanceled()         // annulé
ValkarnTask.FromCanceled<T>()
ValkarnTask.Never                  // ne se termine jamais (sentinel pour WhenAny)
```
