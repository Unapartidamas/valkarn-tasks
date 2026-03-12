---
sidebar_position: 1
title: Règles d'analyseur
---

# Règles d'analyseur

Valkarn Tasks inclut deux packages d'analyseur Roslyn qui s'activent automatiquement lors de l'importation du package :

- **`UnaPartidaMas.Valkarn.Tasks.SourceGen.dll`** — Règles spécifiques à la correction de VlkTask et au cycle de vie Unity. Les identifiants de règle commencent par `TT`.
- **`UnaPartidaMas.Valkarn.Tasks.Analyzer.dll`** — Règles de migration pour les bases de code passant depuis UniTask. Les identifiants de règle commencent par `MIG`. Ces règles se déclenchent **uniquement quand `Cysharp.Threading.Tasks.UniTask` est présent** dans la compilation, donc elles sont silencieuses dans les nouveaux projets.

Les deux packages sont des DLL pré-compilées situées dans le dossier `Analyzers/` du package. Unity les charge comme analyseurs Roslyn via la référence `.asmdef` ; le projet `_TestRunner~` les charge via des éléments `<Analyzer>` dans `TestRunner.csproj`.

---

## Règles de correction (TT)

Ces règles détectent les bugs liés à la nature de consommation unique de `VlkTask` et à la mauvaise utilisation du fire-and-forget.

---

### TT001 — ValkarnTask déjà attendu

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Avertissement          |
| Catégorie  | ValkarnTask            |
| Correction | Non                    |

`VlkTask` est à consommation unique : une fois attendu, le token interne devient périmé. Un second `await` sur la même variable rencontrera ce token périmé et lèvera une exception. L'analyseur détecte quand la même variable locale, paramètre ou champ de type `VlkTask`/`VlkTask<T>` est attendu plus d'une fois à l'intérieur de la même méthode.

**Se déclenche sur :**
```csharp
async VlkTask Bad()
{
    VlkTask work = DoWorkAsync();
    await work;   // premier await — correct
    await work;   // TT001: déjà attendu
}
```

**Correction :** Si vous avez besoin de brancher sur le résultat, capturez-le avec `.AsResult()` avant le premier await, ou restructurez pour que la tâche soit attendue exactement une fois.
```csharp
async VlkTask Good()
{
    var result = await DoWorkAsync().AsResult();
    // utiliser result dans les deux branches
}
```

---

### TT002 — ValkarnTask non attendu ou non rejeté

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | **Erreur**             |
| Catégorie  | ValkarnTask            |
| Correction | Non                    |

Un appel de méthode retournant `VlkTask` utilisé comme instruction d'expression — non attendu, non assigné, et non suivi de `.Forget()` — est un bug silencieux. Les exceptions levées à l'intérieur de la tâche ne sont jamais observées, et la machine d'état en pool de la tâche n'est jamais retournée au pool.

L'analyseur vérifie les instructions d'expression où le type de l'expression se résout à `VlkTask` ou `VlkTask<T>`. Il ignore :
- Les expressions d'assignation (`tasks[i] = DoWork()`)
- Les chaînes se terminant par `.Forget()` (fire-and-forget intentionnel)

**Se déclenche sur :**
```csharp
void Bad()
{
    LoadDataAsync();      // TT002: non attendu ou rejeté
    ProcessItemAsync();   // TT002
}
```

**Correction — l'attendre :**
```csharp
async VlkTask Good()
{
    await LoadDataAsync();
    await ProcessItemAsync();
}
```

**Correction — fire-and-forget explicite :**
```csharp
void GoodFireAndForget()
{
    LoadDataAsync().Forget();
}
```

---

### TT013 — ValkarnTask retourné mais jamais consommé

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Avertissement          |
| Catégorie  | ValkarnTask            |
| Correction | Non                    |

Cet identifiant de règle est **réservé pour une future analyse de flux de données**. Il est destiné à détecter le pattern assigné-mais-jamais-attendu — stocker un `VlkTask` dans une variable puis ne jamais l'attendre ou le rejeter — ce que TT002 ne couvre pas car TT002 n'examine que les instructions d'expression nues.

L'implémentation actuelle n'enregistre aucune action syntaxique. Quand l'analyse de flux de données sera implémentée, TT013 complétera TT002 en couvrant :
```csharp
async VlkTask Bad()
{
    VlkTask task = DoWorkAsync();  // assigné mais jamais attendu
    DoOtherThing();
    // task est abandonné — TT013 (futur)
}
```

Les méthodes décorées avec `[FireAndForget]` seront exemptées.

---

## Règles de cycle de vie (TT)

Ces règles sont liées à la gestion de la durée de vie des MonoBehaviour et à l'annulation.

---

### TT010 — Auto-annulation active pour une méthode async MonoBehaviour

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Info                   |
| Catégorie  | ValkarnTask            |
| Correction | Non                    |

Un indice informationnel. Toute méthode `async VlkTask` (ou `async VlkTask<T>`) à l'intérieur d'une classe qui hérite de `UnityEngine.MonoBehaviour` sera automatiquement annulée quand l'objet est détruit, **sauf si** la méthode est décorée avec `[NoAutoCancel]`. Ce diagnostic rend ce comportement visible dans l'IDE sans que le développeur ait à s'en souvenir.

**Se déclenche sur :**
```csharp
public class MyBehaviour : MonoBehaviour
{
    async VlkTask LoadLevel()  // TT010: auto-annulation active
    {
        await VlkTask.Delay(2000);
    }
}
```

**Pour désactiver** (et supprimer TT010 pour cette méthode), ajoutez `[NoAutoCancel]` :
```csharp
[NoAutoCancel]
async VlkTask LoadLevel(CancellationToken ct)
{
    await VlkTask.Delay(2000, ct);
}
```

---

### TT011 — WhenAll mélange différentes portées de durée de vie

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Avertissement          |
| Catégorie  | ValkarnTask            |
| Correction | Non                    |

`VlkTask.WhenAll` appelé avec des tâches de différentes portées de durée de vie peut produire un comportement d'annulation partielle surprenant. Si une tâche est liée à un `MonoBehaviour` (retournée par une méthode d'instance d'une classe qui hérite de `MonoBehaviour`) et qu'une autre est non liée (une tâche statique, ou depuis une classe non-MonoBehaviour), la destruction de l'objet annulera une tâche mais pas l'autre, laissant le combinateur dans un état indéterminé.

**Se déclenche sur :**
```csharp
// En supposant que EnemyAI : MonoBehaviour
await VlkTask.WhenAll(
    enemyAI.PatrolAsync(),   // lié : auto-annulé à Destroy
    GlobalMusic.FadeAsync()  // non lié : vit indéfiniment
);
// TT011: WhenAll mélange les durées de vie — PatrolAsync() vs GlobalMusic.FadeAsync()
```

**Correction — donner un token d'annulation partagé aux deux tâches :**
```csharp
using var cts = new CancellationTokenSource();
await VlkTask.WhenAll(
    enemyAI.PatrolAsync(cts.Token),
    GlobalMusic.FadeAsync(cts.Token)
);
```

---

### TT012 — Boucle async sans vérification d'annulation

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Avertissement          |
| Catégorie  | ValkarnTask            |
| Correction | Non                    |

Une méthode `async VlkTask` contenant une boucle `for`, `while`, `foreach`, ou `do`-`while` dont le corps n'a aucune vérification d'annulation est une "boucle zombie" : si l'annulation du cycle de vie se déclenche (par exemple, un MonoBehaviour est détruit), le signal d'auto-annulation ne peut pas rompre la boucle. La boucle continue à s'exécuter même si l'objet propriétaire a disparu.

L'analyseur considère qu'un corps de boucle a une vérification d'annulation s'il contient l'un des éléments suivants :
- Une expression `await` (l'opération attendue peut observer le token et lancer `OperationCanceledException`)
- `ThrowIfCancellationRequested` (comme identifiant ou accès de membre)
- `IsCancellationRequested` (comme identifiant ou accès de membre)

La vérification ne descend **pas** dans les lambdas imbriquées ou les fonctions locales.

**Se déclenche sur :**
```csharp
async VlkTask PollForever(CancellationToken ct)
{
    while (true)                     // TT012: pas de vérification d'annulation dans le corps
    {
        ProcessNextItem();
        Thread.Sleep(16);            // synchrone — pas un await
    }
}
```

**Correction — ajouter un await ou une vérification explicite :**
```csharp
async VlkTask PollForever(CancellationToken ct)
{
    while (true)
    {
        ct.ThrowIfCancellationRequested();
        ProcessNextItem();
        await VlkTask.Yield();       // satisfait également la vérification
    }
}
```

---

### TT014 — [NoAutoCancel] sans paramètre CancellationToken

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Avertissement          |
| Catégorie  | ValkarnTask            |
| Correction | Non                    |

`[NoAutoCancel]` sur une méthode `async VlkTask` dans un `MonoBehaviour` exempte la méthode de l'annulation automatique du cycle de vie. Mais si la méthode n'a pas de paramètre `CancellationToken`, elle n'a aucun mécanisme pour observer l'annulation du tout, rendant l'attribut inutile et indiquant presque certainement un paramètre oublié.

**Se déclenche sur :**
```csharp
public class Enemy : MonoBehaviour
{
    [NoAutoCancel]
    async VlkTask Chase()      // TT014: [NoAutoCancel] mais pas de paramètre CancellationToken
    {
        await VlkTask.Delay(1000);
    }
}
```

**Correction — ajouter un paramètre `CancellationToken` :**
```csharp
[NoAutoCancel]
async VlkTask Chase(CancellationToken ct)
{
    await VlkTask.Delay(1000, ct);
}
```

---

## Règles informatives (TT)

---

### TT015 — Adaptateur de pont Awaitable généré

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Info                   |
| Catégorie  | ValkarnTask            |
| Correction | Non                    |

Quand vous `await` un `Awaitable` Unity (depuis `UnityEngine`) à l'intérieur d'une méthode `async VlkTask`, Valkarn Tasks génère automatiquement un adaptateur de pont via `AwaitableBridge`. Ce diagnostic est informatif : il confirme que le pont est en vigueur. Aucune action n'est requise.

**Se déclenche sur :**
```csharp
async VlkTask LoadScene()
{
    await SceneManager.LoadSceneAsync("Main");  // TT015: adaptateur de pont généré
}
```

Aucun changement n'est nécessaire. Le pont gère la conversion de manière transparente.

---

## Règles de qualité du code (TT)

---

### TT016 — Méthode async sans await

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Avertissement          |
| Catégorie  | ValkarnTask            |
| Correction | Non                    |

Une méthode `async VlkTask` (ou `async VlkTask<T>`) qui ne contient aucune expression `await` — y compris `await foreach`, `await using`, et `await` à l'intérieur des déclarations `using` — supporte toute la surcharge d'allocation de machine d'état sans bénéfice. Le compilateur génère quand même une machine d'état, mais puisqu'il n'y a pas de point de suspension, la méthode se termine toujours de manière synchrone.

L'analyseur vérifie : `AwaitExpressionSyntax`, `foreach` avec le mot-clé `await`, les déclarations `using` avec le mot-clé `await`, et les instructions `using` avec le mot-clé `await`.

**Se déclenche sur :**
```csharp
async VlkTask<int> ComputeTotal()  // TT016: pas d'await dans le corps
{
    return items.Sum(x => x.Value);
}
```

**Correction — supprimer `async` et retourner une tâche complétée :**
```csharp
VlkTask<int> ComputeTotal()
{
    return VlkTask.FromResult(items.Sum(x => x.Value));
}
```

Ou pour un retour void :
```csharp
VlkTask DoSetup()
{
    Initialize();
    return VlkTask.CompletedTask;
}
```

---

### TT017 — [FireAndForget] sur ValkarnTask\<T\>

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Avertissement          |
| Catégorie  | ValkarnTask            |
| Correction | Non                    |

`[FireAndForget]` signale qu'une méthode est intentionnellement exécutée sans attendre son résultat. L'appliquer à une méthode qui retourne `VlkTask<T>` (résultat typé) rejette toujours `T`, rendant le retour typé sans signification. La méthode devrait retourner `VlkTask` (void) à la place.

**Se déclenche sur :**
```csharp
[FireAndForget]
async VlkTask<int> SendReport()   // TT017: la valeur de retour est rejetée
{
    await UploadAsync();
    return 42;                     // ce 42 n'est jamais vu
}
```

**Correction — changer le type de retour en `VlkTask` :**
```csharp
[FireAndForget]
async VlkTask SendReport()
{
    await UploadAsync();
}
```

---

## Règles de migration (MIG)

L'analyseur de migration s'active **uniquement quand le type `Cysharp.Threading.Tasks.UniTask` est présent** dans la compilation. Les règles sont informatives ou des avertissements pour guider la transition depuis UniTask vers Valkarn Tasks. Aucune de ces règles n'a de correction automatique ; les descriptions ci-dessous expliquent le changement manuel.

---

### MIG001 — Type UniTask détecté

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Info                   |
| Catégorie  | ValkarnTaskMigration   |

Une utilisation d'un type `UniTask` (la struct elle-même ou `UniTask<T>`) a été détectée. Remplacez-la par l'équivalent `VlkTask` ou `VlkTask<T>`.

```csharp
// Avant
UniTask<Sprite> LoadSprite(string path) { ... }

// Après
VlkTask<Sprite> LoadSprite(string path) { ... }
```

---

### MIG002 — Le paramètre cancelImmediately est inutile

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Info                   |
| Catégorie  | ValkarnTaskMigration   |

Les méthodes `Delay` et autres méthodes basées sur le temps d'UniTask acceptent un paramètre `cancelImmediately` pour opter pour une annulation rapide. Valkarn Tasks annule immédiatement par défaut — il n'y a pas de paramètre `cancelImmediately`. Supprimez l'argument.

```csharp
// Avant
await UniTask.Delay(1000, cancelImmediately: true, cancellationToken: ct);

// Après
await VlkTask.Delay(1000, ct);
```

---

### MIG003 — SuppressCancellationThrow() détecté

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Avertissement          |
| Catégorie  | ValkarnTaskMigration   |

`.SuppressCancellationThrow()` d'UniTask convertit une tâche annulée en tuple `(bool isCancelled, T result)` sans lever d'exception. Valkarn Tasks utilise `.AsResult()` dans le même but, qui retourne une struct `Result<T>`.

```csharp
// Avant
var (isCancelled, value) = await myUniTask.SuppressCancellationThrow();

// Après
var result = await myVlkTask.AsResult();
if (result.IsCanceled) { ... }
T value = result.Value;
```

---

### MIG004 — Type de retour Awaitable détecté

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Info                   |
| Catégorie  | ValkarnTaskMigration   |

Une méthode retourne le type `Awaitable` d'Unity. Envisagez de le remplacer par `VlkTask`, qui s'intègre nativement avec le runtime Valkarn Tasks et supporte l'auto-annulation, la mise en pool, et l'ensemble complet des combinateurs.

---

### MIG005 — Canal SingleConsumerUnbounded détecté

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Avertissement          |
| Catégorie  | ValkarnTaskMigration   |

`Channel.CreateSingleConsumerUnbounded<T>()` d'UniTask crée un canal sans contre-pression. Envisagez de le remplacer par `VlkTask.Channel.CreateBounded<T>(capacity)` pour ajouter de la contre-pression et prévenir une croissance mémoire non bornée sous charge.

```csharp
// Avant
var ch = Channel.CreateSingleConsumerUnbounded<Event>();

// Après (avec contre-pression)
var ch = VlkTask.Channel.CreateBounded<Event>(capacity: 256);

// Ou si non borné est intentionnel
var ch = VlkTask.Channel.CreateUnbounded<Event>();
```

---

### MIG006 — PlayerLoopTiming UniTask détecté

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Info                   |
| Catégorie  | ValkarnTaskMigration   |

Une valeur d'enum `PlayerLoopTiming` depuis l'espace de noms `Cysharp.Threading.Tasks` a été détectée. Changez la directive `using` vers `UnaPartidaMas.Valkarn.Tasks` ; les noms des valeurs d'enum sont identiques.

```csharp
// Avant
using Cysharp.Threading.Tasks;
timing = PlayerLoopTiming.Update;

// Après
using UnaPartidaMas.Valkarn.Tasks;
timing = PlayerLoopTiming.Update;  // même nom, espace de noms différent
```

---

### MIG007 — async UniTaskVoid détecté

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Avertissement          |
| Catégorie  | ValkarnTaskMigration   |

`async UniTaskVoid` était le pattern de méthode fire-and-forget d'UniTask. Valkarn Tasks le remplace par deux options :

```csharp
// Avant
async UniTaskVoid StartLoadAsync() { ... }

// Après — option 1 : attribut [FireAndForget]
[FireAndForget]
async VlkTask StartLoadAsync() { ... }

// Après — option 2 : VlkTask standard + .Forget() au site d'appel
async VlkTask StartLoadAsync() { ... }
// Appelé comme :
StartLoadAsync().Forget();
```

---

### MIG008 — Awaitable MainThreadAsync() détecté

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Info                   |
| Catégorie  | ValkarnTaskMigration   |

`Awaitable.MainThreadAsync()` était utilisé dans l'API `Awaitable` d'Unity pour revenir sur le thread principal. Valkarn Tasks exécute les continuations sur le thread principal par défaut via l'intégration PlayerLoop, donc les appels explicites à `MainThreadAsync()` sont généralement inutiles et peuvent être supprimés.

---

### MIG009 — UniTask.RunOnThreadPool détecté

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Avertissement          |
| Catégorie  | ValkarnTaskMigration   |

Remplacez par `VlkTask.RunOnThreadPool`. L'API est identique.

```csharp
// Avant
await UniTask.RunOnThreadPool(() => HeavyWork());

// Après
await VlkTask.RunOnThreadPool(() => HeavyWork());
```

---

### MIG010 — .ToCoroutine() détecté

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Avertissement          |
| Catégorie  | ValkarnTaskMigration   |

`.ToCoroutine()` était un pont UniTask pour les appelants de coroutines legacy. Réécrivez le code consommateur comme une méthode `async VlkTask` à la place.

```csharp
// Avant
IEnumerator LegacyCaller() { yield return MyUniTask().ToCoroutine(); }

// Après
async VlkTask ModernCaller() { await MyVlkTask(); }
```

---

### MIG011 — UniTask.Create() détecté

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Avertissement          |
| Catégorie  | ValkarnTaskMigration   |

`UniTask.Create(Func<UniTask>)` enveloppe un délégué factory. Remplacez par le pattern `VlkTask.Promise<T>` pour une complétion contrôlée manuellement.

```csharp
// Avant
var task = UniTask.Create(async () => { await DoWork(); return 42; });

// Après
var promise = new VlkTaskCompletionSource<int>();
DoWorkThenComplete(promise);
var task = promise.Task;
```

---

### MIG012 — UniTask.Lazy/Defer détecté

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Info                   |
| Catégorie  | ValkarnTaskMigration   |

`UniTask.Lazy<T>` et `UniTask.Defer` existaient pour éviter l'allocation quand une tâche pourrait se terminer de manière synchrone. Valkarn Tasks dispose d'un chemin rapide synchrone zéro-allocation intégré : retourner `VlkTask.CompletedTask` ou `VlkTask.FromResult(value)` n'alloue jamais. Supprimez les wrappers `Lazy`/`Defer`.

---

### MIG013 — .ToUniTask()/.AsUniTask() détecté

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Info                   |
| Catégorie  | ValkarnTaskMigration   |

Appels de conversion depuis Unity `Awaitable` vers UniTask. Supprimez-les ; Valkarn Tasks connecte `Awaitable` nativement (voir TT015).

---

### MIG014 — UniTaskAsyncEnumerable détecté

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Avertissement          |
| Catégorie  | ValkarnTaskMigration   |

UniTask incluait ses propres utilitaires `IUniTaskAsyncEnumerable<T>` et `UniTaskAsyncEnumerable`. Utilisez `IAsyncEnumerable<T>` de la BCL avec `System.Linq.Async` à la place. `IAsyncEnumerable<T>` est supporté nativement par C# `await foreach`.

```csharp
// Avant
IUniTaskAsyncEnumerable<int> GetItems() { ... }

// Après
IAsyncEnumerable<int> GetItems() { ... }
```

---

### MIG015 — TimeoutController détecté

| Propriété  | Valeur                 |
|------------|------------------------|
| Sévérité   | Info                   |
| Catégorie  | ValkarnTaskMigration   |

Le `TimeoutController` d'UniTask était un helper pour les timeouts réutilisables. Remplacez-le par un `CancellationTokenSource` standard construit avec un `TimeSpan`, que la BCL supporte directement.

```csharp
// Avant
var controller = new TimeoutController();
var ct = controller.Timeout(TimeSpan.FromSeconds(5));

// Après
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var ct = cts.Token;
```

---

## Suppression des règles

Les mécanismes de suppression Roslyn standard fonctionnent pour toutes les règles :

```csharp
// Supprimer une seule occurrence en ligne
#pragma warning disable TT012
while (true) { DoSomething(); }
#pragma warning restore TT012
```

Ou via `.editorconfig` pour supprimer à l'échelle du projet :
```ini
[*.cs]
dotnet_diagnostic.TT012.severity = none
```

Les règles de migration (`MIG*`) peuvent être supprimées de la même façon, ou désactivées globalement une fois la migration terminée en définissant la sévérité sur `none` dans `.editorconfig`.
