---
sidebar_position: 1
title: Intégration du PlayerLoop Unity
---

# Intégration du PlayerLoop Unity

ValkarnTasks n'utilise pas de threads ni de planificateurs en arrière-plan pour reprendre vos méthodes `async`. Tout s'exécute sur le thread principal d'Unity, piloté par le système PlayerLoop d'Unity lui-même. Comprendre comment cela fonctionne vous aidera à choisir le bon timing pour votre cas d'usage et à raisonner sur le moment exact où votre code reprend après un `await`.

## Qu'est-ce que le PlayerLoop Unity

Le PlayerLoop Unity est la boucle interne du moteur qui pilote chaque frame. Ce n'est pas un simple appel `Update()` — c'est une séquence hiérarchique de phases qui s'exécutent dans un ordre défini à chaque frame :

```
Initialization
EarlyUpdate
FixedUpdate      (répété si la physique progresse)
PreUpdate
Update
PreLateUpdate
PostLateUpdate
TimeUpdate
```

Chacune de ces phases de premier niveau contient des sous-systèmes qu'Unity (et les packages tiers) insèrent pour exécuter leur logique à des points spécifiques. `MonoBehaviour.Update()`, par exemple, s'exécute dans la phase `Update`. `MonoBehaviour.LateUpdate()` s'exécute dans `PreLateUpdate`.

Parce que ValkarnTasks se branche directement dans cette boucle, `await VlkTask.Yield()` ne bloque pas un thread — il enregistre un callback qu'Unity appelle au prochain tick de la phase choisie, puis retourne immédiatement.

## Les 16 Timings PlayerLoop

ValkarnTasks s'injecte en 16 points : un au **début** et un à la **fin** de chacune des 8 phases du PlayerLoop Unity. Les variantes `Last` sont ajoutées à la fin de la liste de sous-systèmes de leur phase parente ; les variantes simples sont ajoutées au début.

| Valeur | Entier de l'enum | Phase parente | Position dans le parent |
|---|---|---|---|
| `Initialization` | 0 | `UnityEngine.PlayerLoop.Initialization` | Premier |
| `LastInitialization` | 1 | `UnityEngine.PlayerLoop.Initialization` | Dernier |
| `EarlyUpdate` | 2 | `UnityEngine.PlayerLoop.EarlyUpdate` | Premier |
| `LastEarlyUpdate` | 3 | `UnityEngine.PlayerLoop.EarlyUpdate` | Dernier |
| `FixedUpdate` | 4 | `UnityEngine.PlayerLoop.FixedUpdate` | Premier |
| `LastFixedUpdate` | 5 | `UnityEngine.PlayerLoop.FixedUpdate` | Dernier |
| `PreUpdate` | 6 | `UnityEngine.PlayerLoop.PreUpdate` | Premier |
| `LastPreUpdate` | 7 | `UnityEngine.PlayerLoop.PreUpdate` | Dernier |
| `Update` | 8 | `UnityEngine.PlayerLoop.Update` | Premier |
| `LastUpdate` | 9 | `UnityEngine.PlayerLoop.Update` | Dernier |
| `PreLateUpdate` | 10 | `UnityEngine.PlayerLoop.PreLateUpdate` | Premier |
| `LastPreLateUpdate` | 11 | `UnityEngine.PlayerLoop.PreLateUpdate` | Dernier |
| `PostLateUpdate` | 12 | `UnityEngine.PlayerLoop.PostLateUpdate` | Premier |
| `LastPostLateUpdate` | 13 | `UnityEngine.PlayerLoop.PostLateUpdate` | Dernier |
| `TimeUpdate` | 14 | `UnityEngine.PlayerLoop.TimeUpdate` | Premier |
| `LastTimeUpdate` | 15 | `UnityEngine.PlayerLoop.TimeUpdate` | Dernier |

`Update` (valeur 8) est le **timing par défaut** pour toutes les opérations ValkarnTasks — `Yield()`, `Delay()`, `WaitUntil()`, `WaitWhile()`, `NextFrame()`, et `DelayFrame()`.

## Structs marqueurs et injection idempotente

Pour s'injecter dans le PlayerLoop Unity, ValkarnTasks crée un `PlayerLoopSystem` par timing. Chaque système est identifié par une struct marqueur unique définie dans `PlayerLoopHelper` :

```csharp
struct VlkTaskInitialization     { }
struct VlkTaskLastInitialization { }
struct VlkTaskEarlyUpdate        { }
struct VlkTaskLastEarlyUpdate    { }
struct VlkTaskFixedUpdate        { }
struct VlkTaskLastFixedUpdate    { }
struct VlkTaskPreUpdate          { }
struct VlkTaskLastPreUpdate      { }
struct VlkTaskUpdate             { }
struct VlkTaskLastUpdate         { }
struct VlkTaskPreLateUpdate      { }
struct VlkTaskLastPreLateUpdate  { }
struct VlkTaskPostLateUpdate     { }
struct VlkTaskLastPostLateUpdate { }
struct VlkTaskTimeUpdate         { }
struct VlkTaskLastTimeUpdate     { }
```

Avant l'injection, `PlayerLoopHelper` vérifie si `VlkTaskUpdate` apparaît déjà quelque part dans l'arbre PlayerLoop actuel. Si c'est le cas, l'injection est ignorée. Cela rend l'injection **idempotente** — appeler `Init()` plusieurs fois (ce qui peut arriver dans l'éditeur) ne résulte jamais en des systèmes dupliqués enregistrés.

L'injection lit toujours le PlayerLoop **actuel** avec `PlayerLoop.GetCurrentPlayerLoop()`, jamais `GetDefaultPlayerLoop()`. Cela signifie que tous les systèmes précédemment installés par d'autres packages sont préservés.

## ContinuationQueue — Callbacks à usage unique

Quand vous faites `await VlkTask.Yield()` (ou quelque chose qui se suspend pour exactement un tick), la machine d'état générée par le compilateur appelle `OnCompleted` sur l'awaiter. L'awaiter appelle `PlayerLoopHelper.AddContinuation(timing, action, state)`, qui met en file d'attente le callback dans la `ContinuationQueue` pour ce timing.

`ContinuationQueue` utilise une conception à **double tampon** :

1. **Tampon actif** (`actionList`) : contient les continuations mises en file avant et pendant le drain actuel.
2. **Tampon d'attente** (`waitingList`) : capture les continuations mises en file _pendant_ que le tampon actif est drainé (c'est-à-dire les mises en file réentrantes depuis une continuation elle-même).
3. **Pile Treiber cross-thread** (`crossThreadHead`) : les continuations postées depuis des threads en arrière-plan atterrissent ici via un compare-and-swap sans verrou. Au début de chaque drain, la pile entière est atomiquement réclamée et déplacée dans le tampon actif.

À chaque tick PlayerLoop, `ContinuationQueue.Run()` exécute cette séquence :

```
1. Drainer crossThreadHead → actionList     (prise atomique sans verrou, puis copie sous SpinLock)
2. Capturer le compte, définir isDraining = true  (sous SpinLock)
3. Exécuter tous les callbacks                  (hors verrou, refs effacées pour le GC)
4. Échanger actionList ↔ waitingList          (sous SpinLock, définir isDraining = false)
```

Après le préchauffage d'environ une ou deux frames, les tableaux internes se stabilisent à leur niveau maximal et la file d'attente fonctionne avec **zéro allocation**.

Les nœuds cross-thread sont mis en pool dans un pool sans verrou borné (max 1 024 nœuds) pour éviter d'allouer à chaque mise en file d'un thread en arrière-plan.

## PlayerLoopRunner — Éléments récurrents

Certaines opérations doivent être vérifiées à chaque tick jusqu'à leur completion : `Delay()`, `DelayFrame()`, `WaitUntil()`, `WaitWhile()`, et `NextFrame()`. Celles-ci implémentent l'interface `IPlayerLoopItem` :

```csharp
internal interface IPlayerLoopItem
{
    // Retourner true pour continuer à s'exécuter ; retourner false pour se retirer du runner.
    bool MoveNext();
}
```

Les éléments sont ajoutés à `PlayerLoopRunner` via `PlayerLoopHelper.AddAction(timing, item)`. À chaque tick, `PlayerLoopRunner.Run()` itère tous les éléments enregistrés et appelle `MoveNext()` sur chacun. Les éléments qui retournent `false` sont retirés par **compaction sur place** — le tableau est compacté en un seul passage, préservant l'ordre d'insertion. Les éléments ajoutés pendant un appel `Run()` sont ajoutés en toute sécurité et seront pris en compte au tick suivant.

```
Tick N :
  ContinuationQueue.Run()   → reprendre tous les awaiters à usage unique
  PlayerLoopRunner.Run()    → ticker tous les éléments récurrents (Delay, WaitUntil, ...)
```

Les deux s'exécutent pour chacun des 16 timings, dans l'ordre où le moteur les appelle.

Le timing `Update` suit également le nombre global de frames et déclenche périodiquement la réduction du pool d'objets (tous les `VlkTask.TrimCheckInterval` frames).

## Initialisation — RuntimeInitializeOnLoadMethod

ValkarnTasks s'initialise à `RuntimeInitializeLoadType.SubsystemRegistration`, le hook le plus précoce qu'Unity fournit, qui se déclenche avant `Awake()` sur tout objet de scène :

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void Init()
{
    // 1. Capturer l'ID du thread principal pour les vérifications de thread-safety
    // 2. Créer ContinuationQueue et PlayerLoopRunner pour les 16 timings
    // 3. S'injecter dans le PlayerLoop (idempotent)
    // 4. Enregistrer le callback de changement d'état du mode play (éditeur uniquement)
}
```

L'ID du thread principal est capturé à ce point et partagé avec tous les sous-systèmes de pool et de file d'attente afin qu'ils puissent distinguer les mises en file du thread principal (chemin SpinLock) des mises en file cross-thread (chemin pile Treiber).

## Gestion du rechargement de domaine (Éditeur)

Dans l'éditeur Unity, entrer ou quitter le mode Play déclenche un rechargement de domaine. L'état statique de la session play précédente persisterait autrement et causerait des références périmées.

ValkarnTasks gère cela avec un callback `EditorApplication.playModeStateChanged` enregistré pendant `Init()`. Quand l'état passe à `ExitingPlayMode` ou `EnteredEditMode`, le nettoyage suivant s'exécute :

```csharp
// Réinitialiser toutes les files d'attente et runners
for (int i = 0; i < 16; i++)
{
    s_continuationQueues[i] = null;
    s_playerLoopRunners[i] = null;
}

// Réinitialiser les autres sous-systèmes
VlkTask.ResetStatics();
TimeProvider.ResetToDefault();   // revient à UnityTimeProvider
PoolRegistry.Clear();
ContinuationQueue.ContinuationNode.ResetPool();
```

Quand le mode Play est à nouveau entré, `Init()` se déclenche via `RuntimeInitializeOnLoadMethod` et de nouvelles files d'attente et runners sont alloués. La vérification d'injection (`HasVlkTaskSystems`) empêche les systèmes marqueurs d'être insérés une deuxième fois si le domaine n'a pas été entièrement déchargé.

## Choisir le bon timing

La plupart du code devrait utiliser le timing `Update` par défaut. Ne recourez aux autres que lorsque vous avez une raison spécifique.

| Timing | Quand l'utiliser |
|---|---|
| `Initialization` / `LastInitialization` | Configuration très précoce de frame ; rarement nécessaire dans le code de jeu |
| `EarlyUpdate` / `LastEarlyUpdate` | Échantillonnage d'entrée ; s'exécute avant la physique et avant `Update` |
| `FixedUpdate` / `LastFixedUpdate` | Logique synchronisée avec la physique ; correspond au rythme de `MonoBehaviour.FixedUpdate` |
| `PreUpdate` / `LastPreUpdate` | Avant la distribution principale de mise à jour Unity ; utile pour une pré-passe de planificateur personnalisé |
| **`Update`** (par défaut) | **Logique de jeu standard ; correspond à `MonoBehaviour.Update`** |
| `LastUpdate` | Logique qui doit s'exécuter après tous les appels `MonoBehaviour.Update` |
| `PreLateUpdate` / `LastPreLateUpdate` | Correspond à `MonoBehaviour.LateUpdate` ; suivi de caméra et de transform |
| `PostLateUpdate` / `LastPostLateUpdate` | Après la soumission du rendu ; finalisation UI, capture d'écran |
| `TimeUpdate` / `LastTimeUpdate` | Synchronisation des valeurs de temps ; très rarement nécessaire |

```csharp
// Reprendre au prochain tick Update (par défaut)
await VlkTask.Yield();

// Reprendre au début du prochain FixedUpdate (sûr pour la physique)
await VlkTask.Yield(PlayerLoopTiming.FixedUpdate);

// Attendre 2 secondes, avançant avec le temps non échelonné, vérifié dans LateUpdate
await VlkTask.Delay(
    TimeSpan.FromSeconds(2),
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.PreLateUpdate);

// Attendre jusqu'à ce qu'une condition soit vraie, vérifiée après tous les appels LateUpdate
await VlkTask.WaitUntil(() => _ready, PlayerLoopTiming.LastPreLateUpdate);
```
