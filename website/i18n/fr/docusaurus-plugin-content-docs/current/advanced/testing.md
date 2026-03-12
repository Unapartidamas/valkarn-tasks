---
sidebar_position: 3
title: Tests
---

# Tests

Valkarn Tasks inclut une infrastructure de test dédiée qui vous permet d'écrire des tests unitaires rapides et déterministes pour le code async — pas de vrais timers, pas d'attente de frames, pas d'éditeur Unity requis pour la suite de tests principale.

---

## Vue d'ensemble

Le support de test réside dans l'assembly `Testing/` (`UnaPartidaMas.Valkarn.Tasks.Testing`), qui est visible pour le runtime via `InternalsVisibleTo`. Il expose deux types publics :

| Type | Objectif |
|------|---------|
| `ValkarnTaskTestHelper` | Initialiser et démonter le runtime Valkarn Tasks pour une session de test |
| `TestClock` | Fournisseur de temps et de frames déterministe ; remplace le `TimeProvider` Unity pendant les tests |

---

## ValkarnTaskTestHelper

`ValkarnTaskTestHelper` est une classe utilitaire statique dans l'espace de noms `UnaPartidaMas.Valkarn.Tasks.Testing`.

### Ce qu'il fait

`Setup()` effectue trois choses :
1. Crée un `TestClock` et l'installe comme `TimeProvider.Current`, remplaçant tout fournisseur de temps Unity réel.
2. Appelle `PlayerLoopHelper.InitializeForTest()`, qui alloue les tableaux `ContinuationQueue` et `PlayerLoopRunner` pour les 16 timings PlayerLoop et marque le runtime comme initialisé.
3. Retourne le `TestClock` pour que votre test puisse contrôler le temps.

`Teardown()` inverse l'initialisation :
1. Installe un nouveau `TestClock` sans opération comme `TimeProvider.Current` pour empêcher un temps périmé de fuir entre les fixtures de test.
2. Appelle `PlayerLoopHelper.ShutdownForTest()`, qui démonte les files et les runners.

### Pattern d'utilisation

```csharp
using NUnit.Framework;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Testing;

[TestFixture]
public class MyAsyncTests
{
    TestClock clock;

    [SetUp]
    public void SetUp()
    {
        clock = ValkarnTaskTestHelper.Setup();
    }

    [TearDown]
    public void TearDown()
    {
        ValkarnTaskTestHelper.Teardown();
    }

    // Les tests vont ici
}
```

Appelez `Setup` une fois par test dans `[SetUp]` et `Teardown` une fois par test dans `[TearDown]`. Ce pattern garantit que chaque test démarre avec un état runtime propre et ne fuit pas dans le test suivant.

### Delta time par défaut

`Setup` accepte un paramètre optionnel `defaultDeltaTime` (par défaut : `1/60` secondes, soit 60 fps) :

```csharp
clock = ValkarnTaskTestHelper.Setup(defaultDeltaTime: 1f / 30f);  // simulation à 30 fps
```

Cette valeur est utilisée par `TestClock.AdvanceFrame()` et `TestClock.AdvanceFrames(n)`.

---

## TestClock

`TestClock` implémente `ITimeProvider` et donne aux tests un contrôle total sur :

- Le temps simulé (via `GetTimestamp()`, soutenu par les ticks `Stopwatch.Frequency`)
- `DeltaTime` et `UnscaledDeltaTime` pour la frame courante
- `FrameCount`

### Avancer dans le temps

#### `Advance(TimeSpan duration)`

Avance le temps de la durée donnée en une seule étape. Toute la durée est appliquée comme `DeltaTime` pour cette frame, `FrameCount` est incrémenté de 1, et tous les timings PlayerLoop sont traités. Après le traitement, `DeltaTime` est restauré à la valeur qu'il avait avant l'appel.

```csharp
var task = ValkarnTask.Delay(3000);  // délai de 3 secondes

clock.Advance(TimeSpan.FromSeconds(2));
Assert.IsFalse(task.IsCompleted);   // pas encore

clock.Advance(TimeSpan.FromSeconds(1));
Assert.IsTrue(task.IsCompleted);    // exactement à 3 secondes
```

Utilisez ceci quand vous voulez sauter à un point spécifique dans le temps sans simuler des frames individuelles.

#### `AdvanceFrame()`

Avance d'une frame en utilisant le `DeltaTime` actuel. `FrameCount` est incrémenté de 1, tous les timings PlayerLoop sont traités, et un `Thread.Sleep` de 1 ms est inséré avant le traitement. Le sleep existe car les threads workers en arrière-plan (utilisés par `RunOnThreadPool` et l'intégration Unity Job System) ont besoin d'une petite fenêtre pour se terminer entre les ticks de frame — sans cela, les tests exécutent des frames dos à dos sans intervalle, ce qui provoque des conditions de course qui ne se produisent pas dans les frames Unity réelles.

```csharp
clock.AdvanceFrame();
```

#### `AdvanceFrames(int count)`

Appelle `AdvanceFrame()` `count` fois.

```csharp
clock.AdvanceFrames(10);  // simuler 10 frames
```

#### `ProcessTick(PlayerLoopTiming timing)`

Traite une seule phase de timing PlayerLoop sans avancer le temps ou `FrameCount`. Utile quand vous testez du code planifié à un timing spécifique (ex. `PlayerLoopTiming.FixedUpdate`) et que vous ne voulez pas traiter tous les autres timings.

```csharp
clock.ProcessTick(PlayerLoopTiming.FixedUpdate);
```

### Contrôler le delta time

#### `SetDeltaTime(float deltaTime)`

Définit `DeltaTime` et `UnscaledDeltaTime` à la même valeur pour toutes les frames suivantes.

#### `SetDeltaTime(float deltaTime, float unscaledDeltaTime)`

Les définit indépendamment pour simuler un `Time.timeScale` non unitaire. Par exemple, pour tester du code qui utilise `DelayType.UnscaledDeltaTime` pendant que le temps est en pause :

```csharp
clock.SetDeltaTime(deltaTime: 0f, unscaledDeltaTime: 0.05f);

var scaledTask = ValkarnTask.Delay(500, DelayType.DeltaTime);
var unscaledTask = ValkarnTask.Delay(500, DelayType.UnscaledDeltaTime);

clock.AdvanceFrames(9);  // 450 ms non échelonné, 0 ms échelonné
Assert.IsFalse(scaledTask.IsCompleted);    // le temps de jeu en pause n'atteint jamais 500ms
Assert.IsFalse(unscaledTask.IsCompleted); // 450ms < 500ms

clock.AdvanceFrame();  // 500 ms non échelonné
Assert.IsTrue(unscaledTask.IsCompleted);  // délai non échelonné terminé
Assert.IsFalse(scaledTask.IsCompleted);  // échelonné ne bouge toujours pas
```

---

## Écriture de tests unitaires pour les méthodes async ValkarnTask

### Tests qui se terminent de manière synchrone

Certaines opérations `ValkarnTask` se terminent sans se suspendre — par exemple, attendre `ValkarnTask.CompletedTask`, `ValkarnTask.FromResult(value)`, ou un `ValkarnTaskCompletionSource` qui a été complété avant d'être attendu. Ceux-ci ne nécessitent pas d'horloge du tout et peuvent utiliser la classe interne `TestHelper` directement (interne à l'assembly de test) :

```csharp
[TestFixture]
public class SyncTests
{
    [SetUp]
    public void SetUp()
    {
        // Définit uniquement l'ID du thread principal — pas d'horloge ou de PlayerLoop nécessaire
        TestHelper.EnsureInitialized();
    }

    [Test]
    public void FromResult_ReturnsValue()
    {
        var task = ValkarnTask.FromResult(42);
        Assert.IsTrue(task.IsCompleted);
        Assert.AreEqual(42, task.GetAwaiter().GetResult());
    }
}
```

`TestHelper` (dans `Tests/Editor/TestHelper.cs`) est un helper interne utilisé par la suite de tests elle-même :

- `TestHelper.EnsureInitialized()` définit `ValkarnTaskPoolShared.MainThreadId` sur le thread courant. Cela est requis pour que les opérations de pool se dirigent vers le chemin rapide correct (non-atomique).
- `TestHelper.RunSync(ValkarnTask task)` affirme que la tâche est déjà complétée et appelle `GetResult()` — utile pour tester les chemins de code qui devraient se terminer de manière synchrone.
- `TestHelper.RunSync<T>(ValkarnTask<T> task)` fait de même et retourne la valeur résultante.
- `TestHelper.UnobservedExceptionCollector` s'abonne à `ValkarnTask.UnobservedException` pour la durée de sa portée, collectant toutes les exceptions non observées pour assertion.

### Tests qui nécessitent du temps

Tout test impliquant `ValkarnTask.Delay`, `ValkarnTask.Yield`, ou du code planifié à un timing PlayerLoop nécessite `ValkarnTaskTestHelper.Setup()` et le `TestClock` retourné.

**Exemple : tester une méthode basée sur un délai**

Supposez que vous avez :

```csharp
public static async ValkarnTask WaitAndLog(int ms)
{
    await ValkarnTask.Delay(ms);
    Log("done");
}
```

Testez-la sans attente réelle :

```csharp
[TestFixture]
public class DelayTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = ValkarnTaskTestHelper.Setup();

    [TearDown]
    public void TearDown() => ValkarnTaskTestHelper.Teardown();

    [Test]
    public void WaitAndLog_CompletesAfterDelay()
    {
        var task = WaitAndLog(3000);
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(task.IsCompleted);

        // Récupérer le résultat (lève si faulté)
        task.GetAwaiter().GetResult();
    }

    [Test]
    public void WaitAndLog_ZeroDelay_CompletesImmediately()
    {
        var task = WaitAndLog(0);
        // ValkarnTask.Delay(0) retourne CompletedTask immédiatement
        Assert.IsTrue(task.IsCompleted);
    }
}
```

### Tester l'annulation

```csharp
[Test]
public void Delay_CancelledMidway_ThrowsOCE()
{
    var cts = new CancellationTokenSource();
    var task = ValkarnTask.Delay(5000, cts.Token);
    Assert.IsFalse(task.IsCompleted);

    cts.Cancel();
    // L'annulation se propage au prochain tick
    clock.AdvanceFrame();

    Assert.IsTrue(task.IsCompleted);
    Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
}
```

### Collecter les exceptions non observées

```csharp
[Test]
public void FaultedTask_PublishesUnobservedException()
{
    using var collector = new TestHelper.UnobservedExceptionCollector();

    var tcs = new ValkarnTaskCompletionSource();
    var task = tcs.Task;

    // Ne pas attendre — fire and forget
    task.Forget();

    // Compléter avec exception — déclenche le chemin non observé
    tcs.TrySetException(new InvalidOperationException("oops"));

    Assert.AreEqual(1, collector.Exceptions.Count);
    Assert.IsInstanceOf<InvalidOperationException>(collector.Exceptions[0]);
}
```

---

## Exemple : Tester un producteur/consommateur de canal

```csharp
[TestFixture]
public class ChannelPipelineTests
{
    [SetUp]
    public void SetUp() => TestHelper.EnsureInitialized();

    [Test]
    public void Producer_WritesItems_ConsumerReadsInOrder()
    {
        var channel = ValkarnTask.Channel.CreateUnbounded<int>();

        // Producteur : écrire de manière synchrone
        channel.Writer.TryWrite(1);
        channel.Writer.TryWrite(2);
        channel.Writer.TryWrite(3);
        channel.Writer.Complete();

        // Consommateur : ReadAsync sur un canal avec des éléments se termine de manière synchrone
        Assert.AreEqual(1, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(2, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(3, TestHelper.RunSync(channel.Reader.ReadAsync()));
    }

    [Test]
    public void ReadAsync_BeforeWrite_PendsThenCompletesOnWrite()
    {
        var channel = ValkarnTask.Channel.CreateUnbounded<string>();

        // Lecture émise avant toute écriture
        var readTask = channel.Reader.ReadAsync();
        Assert.IsFalse(readTask.IsCompleted);

        // L'écriture complète la lecture en attente de manière synchrone
        channel.Writer.TryWrite("hello");
        Assert.IsTrue(readTask.IsCompleted);
        Assert.AreEqual("hello", readTask.GetAwaiter().GetResult());
    }

    [Test]
    public void Complete_DrainedChannel_CompletionTaskCompletes()
    {
        var channel = ValkarnTask.Channel.CreateUnbounded<int>();
        channel.Writer.TryWrite(42);

        var completion = channel.Reader.Completion;
        Assert.IsFalse(completion.IsCompleted);

        channel.Writer.Complete();
        Assert.IsFalse(completion.IsCompleted);  // élément encore non lu

        channel.Reader.TryRead(out _);
        Assert.IsTrue(completion.IsCompleted);   // drainé — completion se déclenche
    }

    [Test]
    public void ClosedChannel_Read_ThrowsChannelClosedException()
    {
        var channel = ValkarnTask.Channel.CreateUnbounded<int>();
        channel.Writer.Complete();

        var readTask = channel.Reader.ReadAsync();
        Assert.IsTrue(readTask.IsCompleted);
        Assert.Throws<ChannelClosedException>(() => readTask.GetAwaiter().GetResult());
    }
}
```

---

## Exemple : Tester ValkarnTask.Delay avec avancement de frames

Cet exemple démontre le test d'une méthode qui attend un délai configurable, vérifiant qu'un avancement partiel ne complète pas la tâche prématurément.

```csharp
[TestFixture]
public class DelayFrameTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = ValkarnTaskTestHelper.Setup(defaultDeltaTime: 1f / 60f);

    [TearDown]
    public void TearDown() => ValkarnTaskTestHelper.Teardown();

    [Test]
    public void Delay_500ms_RequiresTenFramesAt50ms()
    {
        clock.SetDeltaTime(0.05f);  // 50 ms par frame
        var task = ValkarnTask.Delay(500);

        clock.AdvanceFrames(9);
        Assert.IsFalse(task.IsCompleted, "450 ms écoulés — ne devrait pas être terminé");

        clock.AdvanceFrame();
        Assert.IsTrue(task.IsCompleted, "500 ms écoulés — devrait être terminé");
    }

    [Test]
    public void Delay_Realtime_UsesTimestampNotDeltaTime()
    {
        // Le délai Realtime ignore DeltaTime ; il utilise le timestamp Stopwatch
        clock.SetDeltaTime(0f);  // DeltaTime gelé
        var task = ValkarnTask.Delay(200, DelayType.Realtime);

        // Advance ne fait bouger que le timestamp via TimeSpan
        clock.Advance(TimeSpan.FromMilliseconds(199));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(task.IsCompleted);
    }
}
```

---

## Intégration NUnit

La suite de tests utilise **NUnit 3.x** (fixé à 3.x dans `TestRunner.csproj`). NUnit 4 a supprimé l'API classique `Assert.IsTrue` / `Assert.AreEqual` que les tests utilisent ; ne mettez pas à jour vers NUnit 4 sans mettre à jour les assertions vers l'API basée sur les contraintes.

### Unity Test Framework

Dans l'éditeur Unity, les tests dans `Tests/Editor/` sont découverts et exécutés par le **Unity Test Framework** (UTF), qui est basé sur NUnit. L'intégration du test runner fonctionne comme suit :

- UTF exécute chaque `[Test]` sur le thread principal.
- `ValkarnTaskTestHelper.Setup()` installe le `TestClock` avant chaque test ; l'attribut `[SetUp]` d'UTF l'appelle.
- `ValkarnTaskTestHelper.Teardown()` supprime l'horloge dans `[TearDown]`.
- Aucune coroutine ou `[UnityTest]` n'est nécessaire : tous les tests Valkarn Tasks utilisent des méthodes NUnit `[Test]` synchrones. Le `TestClock` pilote le temps manuellement, donc il n'y a pas besoin d'avancement de frames réel.

---

## Le projet _TestRunner~

Le répertoire `_TestRunner~/` contient un projet .NET 8 autonome qui compile et exécute toute la suite de tests **en dehors d'Unity**, en utilisant le SDK .NET et `dotnet test`. Il est utilisé pour le CI et le développement local des contributeurs.

### Structure

```
_TestRunner~/
  TestRunner.csproj   — fichier projet .NET 8
  bin~/               — sortie de build (gitignored)
  obj~/               — sortie intermédiaire (gitignored)
```

### Comment ça fonctionne

`TestRunner.csproj` compile quatre ensembles de sources dans un seul assembly :

| Ensemble de sources | Chemin | Notes |
|---------------------|--------|-------|
| Runtime | `../Runtime/**/*.cs` | Tous les fichiers runtime |
| Testing | `../Testing/**/*.cs` | `ValkarnTaskTestHelper`, `TestClock` |
| Tests | `../Tests/Editor/**/*.cs` | Tous les fichiers de test `.cs` |
| Exclusions | `UnityTimeProvider.cs`, `Bridge/`, `Burst/`, `ECS/` | Nécessite des API Unity réelles — exclu |

Le projet **ne définit pas** `UNITY_5_3_OR_NEWER`, `VTASKS_HAS_BURST`, `VTASKS_HAS_COLLECTIONS`, ni `VTASKS_HAS_ENTITIES`, donc toutes les branches `#if` spécifiques à Unity sont compilées. Cela signifie que les tests exercent les chemins de code C# pur.

Les DLL d'analyseur sont chargées comme éléments `<Analyzer>`, donc les mêmes règles qui se déclenchent dans l'IDE se déclenchent également pendant `dotnet build` du projet de test.

### Exécuter les tests

```bash
cd _TestRunner~
dotnet test
```

Ou depuis la racine du dépôt :

```bash
dotnet test _TestRunner~/TestRunner.csproj
```

### Fichiers de test exclus

Trois sous-répertoires de test sont exclus du projet `_TestRunner~` car ils nécessitent de vraies API runtime Unity :

| Répertoire | Raison |
|------------|--------|
| `Tests/Editor/Bridge/` | Tests pour le pont Job System ; nécessite `Unity.Jobs` |
| `Tests/Editor/Burst/` | Tests pour le planificateur Burst ; nécessite `Unity.Burst` |
| `Tests/Editor/ECS/` | Tests pour les utilitaires ECS ; nécessite `Unity.Entities` |

Ces tests s'exécutent uniquement dans l'éditeur Unity via le Unity Test Framework.

### Ajouter un nouveau test

1. Ajoutez un nouveau fichier `.cs` sous `Tests/Editor/` (pas dans un sous-répertoire d'API Unity).
2. Décorez la classe avec `[TestFixture]` et les méthodes avec `[Test]`.
3. Si le test nécessite un contrôle du temps, utilisez `ValkarnTaskTestHelper.Setup()` / `Teardown()` dans `[SetUp]` / `[TearDown]`.
4. Si le test nécessite uniquement des assertions de tâches synchrones (sans délai), utilisez `TestHelper.EnsureInitialized()` dans `[SetUp]` — aucun teardown n'est nécessaire.
5. Exécutez `dotnet test _TestRunner~/TestRunner.csproj` pour vérifier en dehors d'Unity.
6. Exécutez le Unity Test Framework pour vérifier dans Unity.
