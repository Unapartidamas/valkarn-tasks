---
sidebar_position: 2
title: Compatibilité IL2CPP
---

# Compatibilité IL2CPP

Valkarn Tasks est conçu pour fonctionner correctement sous IL2CPP dès le départ. Cette page décrit chaque mesure prise et ce que vous devez faire — ou ne pas faire — pour publier en toute sécurité sur les plateformes IL2CPP telles qu'iOS, WebGL et les consoles.

---

## Pourquoi IL2CPP nécessite une attention particulière

IL2CPP convertit l'IL C# en code source C++ puis le compile avec un compilateur natif. Deux caractéristiques du pipeline sont pertinentes pour une bibliothèque async :

1. **L'élimination du code.** L'éliminateur de code managé Unity (utilisant le linker d'IL2CPP) supprime les types, méthodes et champs qui ne sont pas référencés par un graphe d'appel analysable statiquement. Les types accessibles uniquement via le dispatch d'interface, le partage générique, ou la réflexion — ce qui inclut les classes de promesses en pool et les implémentations `ISource` — peuvent être silencieusement supprimés.

2. **Le partage générique.** IL2CPP ne génère pas de binaire natif séparé pour chaque instanciation générique. À la place, il partage le code entre les types référence et utilise des instanciations spécifiques pour les types valeur. Cela peut masquer des bugs en développement (Mono) qui ne se manifestent que dans les builds IL2CPP.

---

## Le fichier link.xml

La principale défense contre l'élimination est le fichier `link.xml`, situé à :

```
Runtime/link.xml
```

Son contenu :

```xml
<linker>
  <!-- Préserver tous les types dans l'assembly runtime Valkarn.Tasks.
       L'élimination de code IL2CPP peut supprimer les types internes accessibles uniquement via
       le dispatch d'interface, le partage générique, ou la réflexion (ex. classes de
       promesses, runners en pool, implémentations ISource). -->
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks.Burst" preserve="all"/>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks.ECS" preserve="all"/>
</linker>
```

`preserve="all"` demande au linker de conserver chaque type, méthode, champ et constructeur dans ces assemblies indépendamment de ce que l'analyse statique trouve. C'est le paramètre le plus sûr pour une bibliothèque dont les types internes sont accessibles via des paramètres génériques que l'éliminateur ne peut pas tracer.

Unity découvre et applique les fichiers `link.xml` automatiquement quand ils sont placés dans un dossier de package importé dans le projet. Aucune étape manuelle n'est requise.

Si vous **forkez** ou **intégrez** la source plutôt que d'utiliser le package, copiez `link.xml` dans un dossier adjacent à `Resources` ou placez-le n'importe où où Unity le trouvera selon la [documentation sur l'élimination de code managé Unity](https://docs.unity3d.com/Manual/ManagedCodeStripping.html).

---

## Types internes qui seraient supprimés sans link.xml

Les catégories suivantes de types internes constituent le principal risque d'élimination :

### Classes de promesses en pool (implémentations ISource)

Chaque combinateur et type de délai crée une classe de promesse en pool qui implémente `VlkTask.ISource` ou `VlkTask.ISource<T>` :

- `AsyncVlkTaskRunner<TStateMachine>` — le runner de machine d'état en pool, un par méthode async (spécialisé sur `TStateMachine`)
- `WhenAllPromise<T1, T2>`, `WhenAllArrayPromise<T>`, `WhenAllVoidPromise2`, `WhenAllVoidPromise3`, `WhenAllVoidArrayPromise`
- `DeltaTimeDelayPromise`, `UnscaledDeltaTimeDelayPromise`, `RealtimeDelayPromise`
- `CanceledSource`, `CanceledSource<T>`, `ExceptionSource`, `ExceptionSource<T>`, `NeverSource`
- Internes de canaux : `BoundedChannel<T>`, `UnboundedChannel<T>`, et leurs types reader/writer

Ces types sont instanciés via des méthodes factory génériques (`VlkTaskPool<T>.GetOrCreate`). Le graphe d'appel d'analyse statique commence à un appel de méthode générique et ne peut pas tracer de manière fiable toutes les instanciations concrètes de `T`, donc sans `link.xml` n'importe lequel de ces types peut être supprimé.

### VlkTaskPool\<T\>

```csharp
internal sealed class VlkTaskPool<T> : IPoolInfo where T : class, IPoolNode<T>
```

Le pool est générique sur son type d'élément. Chaque classe de promesse a son propre champ de pool statique. Si un type de promesse donné n'est pas utilisé dans une scène, le pool et la classe de promesse peuvent tous deux être supprimés ensemble.

### VlkTaskCompletionCore\<T\>

Le cœur de complétion interne est un type valeur utilisé dans chaque promesse. Il contient le callback de continuation, le token et l'état de complétion. Il n'est jamais référencé par son nom depuis l'extérieur de la bibliothèque.

---

## Contraintes de types génériques et IL2CPP

Le builder de méthode async personnalisé est générique sur le type de machine d'état :

```csharp
public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
    ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : ICriticalNotifyCompletion
    where TStateMachine : IAsyncStateMachine
```

Et le runner est générique sur la machine d'état :

```csharp
internal sealed class AsyncVlkTaskRunner<TStateMachine>
    : IStateMachineRunnerPromise, IPoolNode<AsyncVlkTaskRunner<TStateMachine>>
    where TStateMachine : IAsyncStateMachine
```

Sous IL2CPP, un nouveau type concret est généré pour chaque `TStateMachine` distinct (car les machines d'état sont des structs, qui nécessitent une spécialisation générique complète). Cela signifie :

- Chaque méthode `async VlkTask` dans votre projet produit un type natif `AsyncVlkTaskRunner<TYourStateMachine>` séparé.
- Si l'éliminateur supprime `AsyncVlkTaskRunner<T>` avant de voir toutes les instanciations, certaines méthodes async peuvent planter à l'exécution.
- `preserve="all"` dans `link.xml` empêche cela.

---

## Niveau d'élimination du code managé

Le niveau d'élimination Unity est défini dans **Paramètres du player → Autres paramètres → Niveau d'élimination du code managé**.

| Niveau      | Statut avec Valkarn Tasks                                                             |
|-------------|--------------------------------------------------------------------------------------|
| Désactivé   | Sûr. Aucune élimination ne se produit.                                               |
| Faible      | Sûr. Supprime uniquement les assemblies clairement inutilisées.                      |
| Moyen       | Sûr **avec `link.xml`**. Le `link.xml` inclus préserve tous les types runtime.      |
| Élevé       | Sûr **avec `link.xml`**. Même couverture ; `link.xml` est la couche de protection.  |

Tous les niveaux d'élimination jusqu'à **Élevé** inclus sont sûrs tant que le fichier `link.xml` est présent et appliqué. Ne supprimez pas ou ne modifiez pas `link.xml` sans comprendre quels types internes vous exposez à l'éliminateur.

---

## Utilisation de l'attribut [Preserve]

Une recherche dans le code source Runtime ne trouve aucun attribut `[UnityEngine.Scripting.Preserve]` appliqué à des membres individuels. L'approche choisie est la préservation au niveau de l'assembly via `link.xml` plutôt que des attributs par membre. C'est intentionnel :

- `[Preserve]` par membre nécessite d'annoter chaque classe en pool individuellement, y compris tous les ajouts futurs.
- `preserve="all"` au niveau de l'assembly dans `link.xml` est plus simple, moins sujet aux erreurs, et garanti de couvrir les types ajoutés dans les versions futures.

Si vous devez intégrer Valkarn Tasks dans un fichier `link.xml` plus grand plutôt que d'utiliser celui fourni avec le package, la directive équivalente est :

```xml
<assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
```

---

## Conception du pool orientée IL2CPP

Le pool d'objets (`VlkTaskPool<T>`) contient une note explicite dans son commentaire de documentation :

> IL2CPP-first : les opérations sur le thread principal utilisent zéro atomique.

Le pool utilise deux chemins d'accès :

- **Chemin rapide (thread principal) :** Un seul champ `fastItem` est lu/écrit avec un accès champ simple — aucune opération `Volatile` ou `Interlocked`. Cela évite la surcharge des opérations atomiques sur le thread principal, où IL2CPP ne peut pas toujours les optimiser.
- **Chemin de débordement (n'importe quel thread) :** Une pile lock-free Treiber avec `Interlocked.CompareExchange` pour la correction sous accès concurrent depuis des threads en arrière-plan (ex. `VlkTask.RunOnThreadPool`).

L'identité du thread principal est stockée dans un champ `volatile int` dans `VlkTaskPoolShared.MainThreadId`, publié une fois au démarrage via `RuntimeInitializeOnLoadMethod`. IL2CPP gère correctement les champs `volatile`.

---

## Considérations pour les plateformes console

Les plateformes console (PlayStation, Xbox, Nintendo Switch) utilisent IL2CPP exclusivement. Les éléments suivants s'appliquent :

- La couverture `link.xml` est la même que pour les autres cibles IL2CPP. Aucune préservation supplémentaire spécifique à la console n'est nécessaire.
- `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` se déclenche correctement sur toutes les plateformes supportées par Unity pour la certification console.
- Les opérations `Interlocked` et `Volatile` sont supportées sur toutes les cibles IL2CPP console. La pile Treiber du pool est sûre.
- Le partage générique pour les instanciations de `TStateMachine` struct s'applique sur les consoles. Chaque méthode `async VlkTask` génère son propre type natif — c'est le comportement attendu et est géré correctement.
- Si une plateforme console impose des exigences AOT supplémentaires, confirmez que votre `link.xml` est récupéré correctement en vérifiant le log de build pour "Stripping assembly: UnaPartidaMas.Valkarn.Tasks". Si l'assembly est supprimée, le `link.xml` n'a pas été découvert.

---

## Vérifier que rien n'a été supprimé

Après une build pour une cible IL2CPP :

1. **Vérifiez le log de build.** Unity imprime les décisions d'élimination. Recherchez `UnaPartidaMas.Valkarn.Tasks`. Si vous voyez des messages `Stripping class` pour des types internes Valkarn, le `link.xml` n'a pas été appliqué.

2. **Exécutez un test de fumée.** Un test minimal qui exercice `VlkTask.Delay`, `WhenAll`, et un `VlkTaskCompletionSource` personnalisé instanciera les types les plus fréquemment supprimés. Si ces trois scénarios survivent à la build, le cœur est intact.

3. **Activez "Strip Engine Code" de manière sélective.** Si vous devez utiliser l'élimination élevée et ne pouvez pas utiliser le `link.xml` fourni, activez l'élimination de manière incrémentale et exécutez des tests après chaque augmentation du niveau d'élimination.

4. **Build IL2CPP avec Development Build activé.** Les builds de développement incluent des diagnostics supplémentaires. Si un type est manquant, le runtime signalera `TypeInitializationException` ou une référence nulle au site d'appel du type manquant. Faites correspondre la trace de pile aux types listés dans la section "Types internes" ci-dessus.

---

## Liste de vérification récapitulative

- `Runtime/link.xml` est présent dans le package — vérifiez qu'il n'a pas été accidentellement supprimé.
- Le niveau d'élimination du code managé peut être défini à n'importe quel niveau ; Élevé est supporté.
- Aucun attribut `[Preserve]` n'a besoin d'être ajouté au code d'application.
- Aucun attribut d'indice AOT ni appel d'enregistrement de code ne sont nécessaires.
- Les builds console utilisent le même `link.xml` ; aucun ajout spécifique à la plateforme n'est requis.
- Si vous forkez la source, portez `link.xml` avec elle.
