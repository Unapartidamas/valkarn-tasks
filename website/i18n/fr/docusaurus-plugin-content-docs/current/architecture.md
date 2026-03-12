---
sidebar_position: 5
title: Architecture
---

# Architecture

Vue d'ensemble technique des composants internes de Valkarn Tasks.

---

## Structure de haut niveau

```
┌─────────────────────────────────────────────────────────────────┐
│                    TEMPS DE COMPILATION                           │
│                                                                   │
│  ┌──────────────┐  ┌──────────────────┐  ┌───────────────────┐  │
│  │  Lifecycle    │  │  Awaitable       │  │  Diagnostics      │  │
│  │  Analyzer     │  │  Bridge Gen      │  │  (TT001–TT017)    │  │
│  └──────────────┘  └──────────────────┘  └───────────────────┘  │
│                                                                   │
│  ┌──────────────┐  ┌──────────────────┐                          │
│  │  Job Bridge   │  │  Combinator      │                          │
│  │  Gen          │  │  Gen             │                          │
│  └──────────────┘  └──────────────────┘                          │
├─────────────────────────────────────────────────────────────────┤
│                    TEMPS D'EXÉCUTION                              │
│                                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐  │
│  │ ValkarnTask │  │ Result<T>  │  │ ValkarnPool│  │ Completion│  │
│  │  struct    │  │  struct    │  │  bounded   │  │  Core<T>  │  │
│  └────────────┘  └────────────┘  └────────────┘  └───────────┘  │
│                                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐  │
│  │ PlayerLoop │  │Continuation│  │  Channels  │  │ TestClock │  │
│  │  Helper    │  │   Queue    │  │            │  │           │  │
│  └────────────┘  └────────────┘  └────────────┘  └───────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### Disposition des assemblages

```
ValkarnTask.Runtime      — livré avec le jeu
ValkarnTask.SourceGen    — compilation uniquement (générateur de source)
ValkarnTask.Analyzer     — compilation uniquement (diagnostics + corrections de code)
ValkarnTask.Testing      — TestClock + utilitaires de test
```

---

## Struct ValkarnTask

```csharp
[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask
{
    internal readonly ValkarnTask.ISource source;
    internal readonly ulong token; // empaqueté : 32 bits hauts = génération, 32 bas = index de slot
}

[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
{
    internal readonly ValkarnTask.ISource<T> source;
    internal readonly T result;  // en ligne sur le chemin rapide synchrone
    internal readonly ulong token;
}
```

**Invariant clé :** `source == null` signifie que la tâche s'est terminée de façon synchrone sans erreur — aucun objet sur le tas impliqué. `ValkarnTask.CompletedTask` est `default(ValkarnTask)` ; `ValkarnTask.FromResult(value)` stocke le résultat en ligne.

### Token générationnel

```csharp
// Empaqueter
ulong token = ((ulong)generation << 32) | slotIndex;

// Dépaqueter
uint slotIndex  = (uint)(token & 0xFFFFFFFF);
uint generation = (uint)(token >> 32);
```

Validé à chaque appel ISource : `slots[slotIndex].generation == expectedGeneration`. Une référence obsolète à un slot de pool recyclé lève immédiatement `InvalidOperationException`. 4 milliards de générations par slot — la collision est impossible en pratique. (UniTask utilise `short` — collision après ~18 minutes.)

---

## Contrat ISource

```csharp
public interface ISource
{
    Status GetStatus(ulong token);
    void   GetResult(ulong token);
    void   OnCompleted(Action<object> continuation, object state, ulong token);
    Status UnsafeGetStatus();
}

public interface ISource<out T> : ISource
{
    new T GetResult(ulong token);
}
```

Tout objet implémentant `ISource` peut soutenir un `ValkarnTask`. Implémentations intégrées :

| Type | Rôle |
|------|---------|
| `AsyncValkarnRunner<TStateMachine>` | Soutient chaque méthode `async ValkarnTask` |
| `AsyncValkarnRunner<TStateMachine, T>` | Soutient chaque méthode `async ValkarnTask<T>` |
| `ValkarnTask.PooledPromise[<T>]` | Complétion manuelle, retour automatique au pool |
| `ValkarnTask.Promise[<T>]` | Complétion manuelle, non poolée (longue durée) |
| `ExceptionSource` | Soutient `FromException` |
| `CanceledSource` | Soutient `FromCanceled` |
| `NeverSource` | Singleton — ne transite jamais depuis Pending |

---

## Builder de méthode async

Le compilateur C# pilote le protocole builder pour les types de retour async personnalisés :

```
Create()
  └─ retourne un struct builder (zéro allocation)

Start(ref stateMachine)
  └─ exécute la machine à états de façon synchrone
     ├─ se termine sans se suspendre → SetResult(), le runner reste null
     │  └─ Task retourne default(ValkarnTask)  ← zéro allocation
     └─ rencontre un await incomplet → AwaitUnsafeOnCompleted()
           └─ emprunte AsyncValkarnRunner du pool
              copie la machine à états dans le runner (par valeur, sans boxing)
              enregistre la continuation sur l'awaitable
              └─ Task enveloppe le runner comme ISource  ← chemin async
```

Le builder lui-même est un `struct` — pas d'allocation juste pour le builder. Le `runner` est alloué de façon **paresseuse** : si une méthode se termine de façon synchrone, aucun emprunt au pool ne se produit jamais.

---

## Runner de machine à états et pool

`AsyncValkarnRunner<TStateMachine>` stocke la machine à états générée par le compilateur **par valeur** (sans boxing) et agit comme `ISource`. Il est emprunté de `ValkarnPool<T>` à la première suspension et retourné lors de `GetResult`.

Comme `TStateMachine` est un type unique par méthode async (par instanciation générique fermée), chaque méthode async obtient son propre pool automatiquement via la spécialisation générique de C#.

### ValkarnPool\<T\>

| Contexte | Structure | Raison |
|---------|-----------|--------|
| Thread principal Unity | Pile mono-thread | Pas de synchronisation — vitesse maximale possible |
| Threads en arrière-plan | Pile Treiber sans verrou | Opérations CAS, pas de verrous |

Le contexte du thread est détecté via `Thread.CurrentThread.IsBackground`. La forme du pool (capacité, taux d'écrêtage) est configurée via `ValkarnTaskSettings`.

---

## ValkarnCompletionCore\<T\>

État partagé à l'intérieur de chaque implémentation d'`ISource` :

- `Status` actuel (Pending / Succeeded / Faulted / Canceled)
- Valeur du résultat (sources génériques)
- Exception ou `OperationCanceledException` (chemins d'erreur)
- Délégué de continuation enregistré + état

Les transitions de statut utilisent `Interlocked.CompareExchange` — sans verrou, thread-safe. Un **guard contre la double-complétion** garantit que seul le premier appel à `TrySet*` gagne ; les appels suivants sont des no-ops silencieux.

---

## Intégration PlayerLoop

`PlayerLoopHelper` insère des callbacks de runner légers dans le PlayerLoop d'Unity au démarrage (`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]`).

Chaque valeur de `PlayerLoopTiming` correspond à une phase. Quand `await ValkarnTask.Yield(timing)` est appelé, la continuation est mise en file d'attente dans le runner de cette phase et dispatchée la prochaine fois qu'Unity l'atteint.

```
Initialization  →  EarlyUpdate  →  FixedUpdate  →  PreUpdate
→  Update  →  PreLateUpdate  →  PostLateUpdate  →  TimeUpdate
  (+ variantes Last* pour chaque phase)
```

---

## Générateur de source

Le générateur de source Roslyn s'exécute au moment de la compilation. Pour chaque classe `partial` étendant `MonoBehaviour` avec des méthodes `async ValkarnTask`, il génère un fichier de classe partielle qui :

1. Déclare le champ `_valkarnCancelToken`
2. L'assigne depuis `destroyCancellationToken` dans `Awake`
3. Enveloppe chaque méthode async pour propager le token automatiquement

Le fichier généré n'est jamais affiché dans le débogueur et ne modifie jamais le code source utilisateur.

Le générateur produit également :

- **Adaptateurs de pont Awaitable** — quand `Awaitable` est attendu à l'intérieur d'`async ValkarnTask`
- **Wrappers async de jobs** — quand des types `IJob` / `IJobParallelFor` sont détectés
- **Pools de combinateurs** — sources `WhenAll`/`WhenAny` typées pour les tuples d'arité 2–8

---

## Analyseurs Roslyn

17 règles `DiagnosticAnalyzer` sont livrées dans `Analyzers/netstandard2.0/`. Elles s'exécutent pendant la passe du compilateur C# dans l'Éditeur Unity et en CI :

- Toutes utilisent `SemanticModel` pour la résolution de types (pas de correspondance de chaînes)
- L'utilitaire partagé `ValkarnTypeHelper` détecte toute variante de `ValkarnTask`
- L'analyseur de boucles zombies ignore correctement les fonctions locales et lambdas imbriquées
- Les analyseurs de migration (MIG001–MIG015) s'activent automatiquement uniquement quand UniTask / Awaitable sont référencés — inertes sinon

---

## Couche Burst et ECS

Trois modules optionnels, chacun protégé par des vérifications de directive `#if` :

| Module | Requiert | Rôle |
|--------|----------|---------|
| `JobBridge` | `Unity.Jobs` | Enveloppe `JobHandle` comme awaitable ; interroge `handle.IsCompleted` à chaque tick du PlayerLoop |
| `AsyncSystemBase` | `Unity.Entities` | Classe de base de système ECS avec support async |
| `BurstScheduler` | `Unity.Burst` + `Unity.Collections` | Planifie des jobs Burst depuis un contexte async ; gère `NativeTimerHeap` |

`NativeTimerHeap` est un min-heap compatible Burst pour les timers haute précision qui évite entièrement les allocations sur le tas managé.

---

## Intégration éditeur

Le **Valkarn Hub** (`Tools → Valkarn → Hub`) utilise `TypeCache.GetTypesDerivedFrom<IValkarnEditorPanel>()` pour découvrir automatiquement tous les packages Valkarn installés. Aucun enregistrement manuel requis.

Le `TasksTrackerPanel` s'abonne à `EditorApplication.update` pour actualiser les diagnostics du pool toutes les 0,5 s (configurable) et expose la référence à l'asset `ValkarnTaskSettings` pour un accès rapide.

---

## Considérations IL2CPP

- Les machines à états sont stockées **par valeur** dans les runners — pas de boxing, IL2CPP gère correctement
- Le runner de chaque méthode async est une spécialisation générique séparée — type-safe, sans contamination croisée
- Les structs d'awaiter implémentent `ICriticalNotifyCompletion` — le compilateur appelle `UnsafeOnCompleted`, en sautant la capture d'`ExecutionContext` (pas de surcharge dans la configuration par défaut d'Unity)
- Si le stripping agressif est activé, préservez l'assemblage runtime :

```xml
<!-- link.xml -->
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```
