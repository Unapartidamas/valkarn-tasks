---
sidebar_position: 2
title: PlayerLoopTiming
---

# PlayerLoopTiming

```csharp
public enum PlayerLoopTiming : byte
```

Espace de noms : `UnaPartidaMas.Valkarn.Tasks`

Spécifie quelle phase du PlayerLoop Unity une opération ValkarnTasks utilise pour reprendre après la suspension. Passer une valeur `PlayerLoopTiming` contrôle **quand dans la frame** votre continuation `await` s'exécute, et **quand** les éléments récurrents tels que les délais et les conditions d'attente sont vérifiés.

Les valeurs de l'enum correspondent exactement à l'enum `PlayerLoopTiming` d'UniTask, ce qui simplifie la migration depuis UniTask.

---

## Valeur par défaut

`PlayerLoopTiming.Update` (valeur `8`) est la valeur par défaut pour toutes les opérations. Si vous ne passez pas d'argument de timing, `Update` est utilisé.

---

## Valeurs

### Initialization

```
Valeur : 0
Phase parente : UnityEngine.PlayerLoop.Initialization
Position : premier sous-système dans la phase
```

S'exécute au tout début de la phase Initialization, avant les propres sous-systèmes d'initialisation d'Unity dans cette phase. Cela se déclenche une fois par frame, avant `EarlyUpdate`. C'est rarement utile pour le code de gameplay mais peut être pertinent pour les systèmes qui doivent lire ou configurer l'état avant que quoi que ce soit d'autre dans la frame le touche.

---

### LastInitialization

```
Valeur : 1
Phase parente : UnityEngine.PlayerLoop.Initialization
Position : dernier sous-système dans la phase
```

S'exécute à la fin de la phase Initialization, après les sous-systèmes d'initialisation intégrés d'Unity. Préférez ceci à `Initialization` si vous avez besoin du timing de la phase d'initialisation mais voulez laisser les propres systèmes d'Unity s'exécuter en premier.

---

### EarlyUpdate

```
Valeur : 2
Phase parente : UnityEngine.PlayerLoop.EarlyUpdate
Position : premier sous-système dans la phase
```

S'exécute au début d'EarlyUpdate, avant qu'Unity traite les événements d'entrée et avant l'étape de simulation physique. C'est le point le plus précoce dans la frame où les données d'entrée de la frame précédente sont disponibles. Utile pour les systèmes qui doivent échantillonner les entrées avant que tout code de gameplay s'exécute.

---

### LastEarlyUpdate

```
Valeur : 3
Phase parente : UnityEngine.PlayerLoop.EarlyUpdate
Position : dernier sous-système dans la phase
```

S'exécute à la fin d'EarlyUpdate, après que les propres sous-systèmes d'early-update d'Unity ont été complétés.

---

### FixedUpdate

```
Valeur : 4
Phase parente : UnityEngine.PlayerLoop.FixedUpdate
Position : premier sous-système dans la phase
```

S'exécute au début de chaque étape FixedUpdate. Cela correspond au timing de `MonoBehaviour.FixedUpdate()`. Unity peut exécuter zéro ou plusieurs étapes FixedUpdate par frame rendue selon comment le pas de temps physique s'aligne avec le delta de frame.

Utilisez ce timing pour tout ce qui doit rester synchronisé avec la simulation physique : lire les vélocités des `Rigidbody`, appliquer des forces, ou attendre un nombre d'étapes déterminé par la physique.

```csharp
// Attendre 10 étapes physiques
await ValkarnTask.DelayFrame(10, PlayerLoopTiming.FixedUpdate, ct);

// Attendre jusqu'à ce qu'une condition physique soit vraie, vérifiée à chaque étape physique
await ValkarnTask.WaitUntil(() => rb.velocity.magnitude < 0.1f,
    PlayerLoopTiming.FixedUpdate, ct);
```

---

### LastFixedUpdate

```
Valeur : 5
Phase parente : UnityEngine.PlayerLoop.FixedUpdate
Position : dernier sous-système dans la phase
```

S'exécute à la fin de la phase FixedUpdate, après que les sous-systèmes physiques d'Unity (y compris `Physics.Simulate`) ont été complétés. Utilisez ceci pour lire les résultats physiques après que la simulation a avancé.

---

### PreUpdate

```
Valeur : 6
Phase parente : UnityEngine.PlayerLoop.PreUpdate
Position : premier sous-système dans la phase
```

S'exécute au début de la phase PreUpdate, qui s'exécute après FixedUpdate et avant Update. Unity utilise PreUpdate pour des tâches telles que les mises à jour de zones de vent et le traitement des événements réseau.

---

### LastPreUpdate

```
Valeur : 7
Phase parente : UnityEngine.PlayerLoop.PreUpdate
Position : dernier sous-système dans la phase
```

S'exécute à la fin de PreUpdate.

---

### Update

```
Valeur : 8  (par défaut)
Phase parente : UnityEngine.PlayerLoop.Update
Position : premier sous-système dans la phase
```

Le **timing par défaut** pour toutes les opérations ValkarnTasks. S'exécute au début de la phase Update, qui est là où `MonoBehaviour.Update()` se déclenche. C'est le bon choix pour la grande majorité du code de gameplay.

```csharp
// Tous ceux-ci utilisent Update par défaut
await ValkarnTask.Yield();
await ValkarnTask.Delay(1000, ct);
await ValkarnTask.WaitUntil(() => _ready, ct: ct);
await ValkarnTask.NextFrame(ct: ct);
await ValkarnTask.DelayFrame(3, ct: ct);

// Explicite — identique à ce qui précède
await ValkarnTask.Yield(PlayerLoopTiming.Update);
```

---

### LastUpdate

```
Valeur : 9
Phase parente : UnityEngine.PlayerLoop.Update
Position : dernier sous-système dans la phase
```

S'exécute à la fin de la phase Update, après tous les appels `MonoBehaviour.Update()` et tout autre sous-système Update ont été complétés. Utilisez ceci quand votre continuation doit observer les résultats des méthodes `Update()` des autres scripts — par exemple, interroger une valeur qu'un autre script écrit pendant `Update`.

```csharp
// Reprendre après tous les appels MonoBehaviour.Update() de cette frame
await ValkarnTask.Yield(PlayerLoopTiming.LastUpdate);
```

---

### PreLateUpdate

```
Valeur : 10
Phase parente : UnityEngine.PlayerLoop.PreLateUpdate
Position : premier sous-système dans la phase
```

S'exécute au début de la phase PreLateUpdate, qui est là où `MonoBehaviour.LateUpdate()` se déclenche. Utilisez ceci pour le suivi de caméra, les ajustements de transform, et tout ce qui doit réagir aux positions finales définies pendant `Update`.

```csharp
// Reprendre au même point que MonoBehaviour.LateUpdate()
await ValkarnTask.Yield(PlayerLoopTiming.PreLateUpdate);
```

---

### LastPreLateUpdate

```
Valeur : 11
Phase parente : UnityEngine.PlayerLoop.PreLateUpdate
Position : dernier sous-système dans la phase
```

S'exécute à la fin de la phase PreLateUpdate, après tous les appels `MonoBehaviour.LateUpdate()`. Utilisez ceci quand vous avez besoin de lire des valeurs que les scripts ont définies pendant leur `LateUpdate`.

---

### PostLateUpdate

```
Valeur : 12
Phase parente : UnityEngine.PlayerLoop.PostLateUpdate
Position : premier sous-système dans la phase
```

S'exécute au début de PostLateUpdate. Cette phase se déclenche après que le rendu a été soumis pour la frame. Unity l'utilise pour des tâches telles que la présentation du frame buffer et le nettoyage de l'état de rendu. C'est aussi là que les callbacks `Camera.onPostRender` se déclenchent.

Utilisez ce timing pour les opérations de fin de frame : capture d'écran, déchargements de niveaux en streaming, ou tout travail qui doit se produire après que la scène a été entièrement rendue mais avant le début de la prochaine frame.

---

### LastPostLateUpdate

```
Valeur : 13
Phase parente : UnityEngine.PlayerLoop.PostLateUpdate
Position : dernier sous-système dans la phase
```

Le dernier point dans la boucle de frame standard avant qu'Unity réinitialise l'état local à la frame et commence la prochaine frame. C'est la fin absolue d'une frame à des fins de timing.

---

### TimeUpdate

```
Valeur : 14
Phase parente : UnityEngine.PlayerLoop.TimeUpdate
Position : premier sous-système dans la phase
```

S'exécute au début de la phase TimeUpdate. Unity utilise cette phase pour avancer l'état lié au temps (comme `Time.time`). Ce timing se déclenche avant `EarlyUpdate` dans les versions récentes d'Unity.

Ce timing est rarement nécessaire dans le code de jeu. Il existe principalement pour les systèmes qui doivent intercepter ou observer l'avancement du temps avant que tout autre système lise les nouvelles valeurs de temps.

---

### LastTimeUpdate

```
Valeur : 15
Phase parente : UnityEngine.PlayerLoop.TimeUpdate
Position : dernier sous-système dans la phase
```

S'exécute à la fin de la phase TimeUpdate, après que les sous-systèmes liés au temps d'Unity ont été complétés.

---

## Tableau récapitulatif

| Valeur | Nom | Phase Unity | Position | Callback MonoBehaviour comparable |
|---|---|---|---|---|
| 0 | `Initialization` | `Initialization` | Premier | — |
| 1 | `LastInitialization` | `Initialization` | Dernier | — |
| 2 | `EarlyUpdate` | `EarlyUpdate` | Premier | — |
| 3 | `LastEarlyUpdate` | `EarlyUpdate` | Dernier | — |
| 4 | `FixedUpdate` | `FixedUpdate` | Premier | `FixedUpdate()` |
| 5 | `LastFixedUpdate` | `FixedUpdate` | Dernier | après `FixedUpdate()` |
| 6 | `PreUpdate` | `PreUpdate` | Premier | — |
| 7 | `LastPreUpdate` | `PreUpdate` | Dernier | — |
| **8** | **`Update`** (par défaut) | **`Update`** | **Premier** | **`Update()`** |
| 9 | `LastUpdate` | `Update` | Dernier | après tous les `Update()` |
| 10 | `PreLateUpdate` | `PreLateUpdate` | Premier | `LateUpdate()` |
| 11 | `LastPreLateUpdate` | `PreLateUpdate` | Dernier | après tous les `LateUpdate()` |
| 12 | `PostLateUpdate` | `PostLateUpdate` | Premier | après le rendu |
| 13 | `LastPostLateUpdate` | `PostLateUpdate` | Dernier | fin de frame |
| 14 | `TimeUpdate` | `TimeUpdate` | Premier | — |
| 15 | `LastTimeUpdate` | `TimeUpdate` | Dernier | — |

---

## Quelles APIs acceptent PlayerLoopTiming

Toutes les APIs ValkarnTasks basées sur le temps et les conditions acceptent un paramètre `PlayerLoopTiming` optionnel. La valeur par défaut est toujours `Update`.

| Méthode | Signature |
|---|---|
| `ValkarnTask.Yield` | `Yield(PlayerLoopTiming timing = Update)` |
| `ValkarnTask.NextFrame` | `NextFrame(PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.DelayFrame` | `DelayFrame(int frameCount, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.Delay(int)` | `Delay(int ms, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.Delay(TimeSpan)` | `Delay(TimeSpan delay, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.WaitUntil` | `WaitUntil(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.WaitWhile` | `WaitWhile(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |

---

## Exemples de code

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using System.Threading;

// Céder au prochain tick Update (par défaut)
await ValkarnTask.Yield();

// Céder au prochain tick FixedUpdate — utiliser dans le code piloté par la physique
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// Attendre 500 ms en utilisant le deltaTime échelonné, vérifié à chaque Update
await ValkarnTask.Delay(500, ct: destroyCancellationToken);

// Attendre 500 ms en utilisant le deltaTime non échelonné — non affecté par Time.timeScale
await ValkarnTask.Delay(
    500,
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// Attendre 500 ms en utilisant le temps réel (basé sur Stopwatch)
await ValkarnTask.Delay(
    500,
    DelayType.Realtime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// Attendre jusqu'à ce qu'un flag soit défini, vérifié dans LateUpdate
bool _isReady;
await ValkarnTask.WaitUntil(() => _isReady, PlayerLoopTiming.PreLateUpdate, destroyCancellationToken);

// Attendre pendant le chargement, vérifié à la toute fin de la frame
await ValkarnTask.WaitWhile(() => _loading, PlayerLoopTiming.LastPostLateUpdate, destroyCancellationToken);

// Sauter exactement 5 étapes physiques
await ValkarnTask.DelayFrame(5, PlayerLoopTiming.FixedUpdate, destroyCancellationToken);

// Avancer d'une frame rendue complète
await ValkarnTask.NextFrame(PlayerLoopTiming.Update, destroyCancellationToken);
```
