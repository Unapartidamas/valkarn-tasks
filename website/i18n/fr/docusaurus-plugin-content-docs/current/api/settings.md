---
sidebar_position: 4
title: Paramètres & Résultat
---

# ValkarnTaskSettings

`ValkarnTaskSettings` est un `ScriptableObject` Unity qui contrôle le comportement à l'exécution de Valkarn Tasks — principalement le pool d'objets qui recycle les machines d'état async et les objets promise pour éviter le garbage pendant le jeu.

---

## Créer l'asset de paramètres

1. Dans la fenêtre Project, faites un clic droit sur un dossier `Resources` (créez-en un si vous n'en avez pas).
2. Sélectionnez **Assets > Create > Valkarn Tasks > Task Settings**.
3. Nommez le fichier `ValkarnTaskSettings` et placez-le dans le dossier `Resources`.

Le fichier doit être nommé exactement `ValkarnTaskSettings` et doit résider dans un dossier nommé `Resources` n'importe où dans votre projet. L'asset est chargé à l'exécution avec `Resources.Load<ValkarnTaskSettings>("ValkarnTaskSettings")`.

Si aucun asset n'est trouvé, tous les paramètres reviennent à leurs valeurs par défaut intégrées. La bibliothèque fonctionne correctement sans l'asset — le créer est seulement nécessaire quand vous voulez changer les valeurs par défaut.

---

## Accéder aux paramètres à l'exécution

Dans les builds Unity, les paramètres sont lus depuis l'asset via un singleton mis en cache :

```csharp
ValkarnTaskSettings settings = ValkarnTaskSettings.Instance;
```

Les paramètres de pool les plus couramment nécessaires sont également exposés directement sur `ValkarnTask` pour la commodité :

```csharp
int max      = ValkarnTask.DefaultMaxPoolSize;   // lit depuis ValkarnTaskSettings.Instance
int min      = ValkarnTask.MinPoolSize;
int interval = ValkarnTask.TrimCheckInterval;
```

Dans les builds non-Unity (tests, .NET autonome), `ValkarnTaskSettings` est une classe statique avec des propriétés mutables au lieu d'un `ScriptableObject`. Les mêmes noms de propriétés s'appliquent et peuvent être écrits directement :

```csharp
// Builds non-Unity / test uniquement
ValkarnTask.DefaultMaxPoolSize = 512;
ValkarnTask.TrimCheckInterval  = 600;
ValkarnTask.MinPoolSize        = 16;
```

---

## Propriétés configurables

### Configuration du pool

#### DefaultMaxPoolSize

| | |
|-|-|
| Type | `int` |
| Défaut | `256` |
| Plage valide | `8` – `1024` |
| Infobulle Inspecteur | "Nombre maximum d'éléments par type de pool. Les éléments excédentaires sont réduits." |

Le nombre maximum d'objets retenus dans chaque pool par type. Chaque instanciation générique distincte (ex. `PooledPromise<int>`, `PooledPromise<string>`) a son propre pool plafonné à cette valeur.

Quand une tâche se termine et que son objet interne est retourné au pool, si le pool détient déjà `DefaultMaxPoolSize` éléments, l'objet retourné est abandonné (éligible pour le GC). Cela empêche la croissance illimitée de la mémoire après une rafale d'activité async.

Augmentez cette valeur si le profilage montre des allocations GC fréquentes pendant des charges de travail async à haut débit soutenu. Diminuez-la si la pression mémoire est une préoccupation et que les tâches ne sont pas fréquemment réutilisées.

#### MinPoolSize

| | |
|-|-|
| Type | `int` |
| Défaut | `8` |
| Plage valide | `1` – `64` |
| Infobulle Inspecteur | "Taille minimale du pool — ne jamais réduire en dessous de ceci." |

La passe de réduction du pool ne réduira jamais un pool en dessous de ce compte. Cela garantit qu'une ligne de base de pool préchauffée est toujours disponible, évitant des pics d'allocation après une période calme où la passe de réduction aurait pu tout libérer.

#### TrimCheckInterval

| | |
|-|-|
| Type | `int` |
| Défaut | `300` |
| Plage valide | `30` – `1000` |
| Infobulle Inspecteur | "Frames entre les vérifications de réduction. À 60fps, 300 ≈ 5 secondes." |

Combien de frames s'écoulent entre les passes de réduction du pool. La passe de réduction parcourt tous les pools enregistrés et libère les objets excédentaires (ceux au-dessus de `MinPoolSize`) si le pool a été constamment surdimensionné.

À 60 fps, la valeur par défaut de 300 équivaut à environ 5 secondes entre les vérifications. Diminuez cette valeur si vous voulez une réduction plus agressive et fréquente (au coût de travail de réduction plus fréquent). Augmentez-la si les passes de réduction apparaissent comme des pics dans le profilage.

#### TrimHysteresisCount

| | |
|-|-|
| Type | `int` |
| Défaut | `2` |
| Plage valide | `1` – `10` |
| Infobulle Inspecteur | "Nombre de vérifications consécutives au-dessus du seuil avant réduction." |

Un pool n'est réduit qu'après avoir été observé comme surdimensionné pendant ce nombre de cycles de réduction consécutifs. Cela empêche le thrashing — si le jeu a un bref pic suivi d'une période calme, un compte d'hystérésis de 2 signifie que le pool survit à un cycle calme avant de commencer à libérer des objets.

#### TrimReleaseRatio

| | |
|-|-|
| Type | `float` |
| Défaut | `0.25` |
| Plage valide | `0.1` – `1.0` |
| Infobulle Inspecteur | "Fraction de l'excédent à libérer par cycle de réduction (0.25 = 25%)." |

Quand un pool est réduit, cette fraction de la capacité excédentaire (éléments au-dessus de `MinPoolSize`) est libérée par cycle plutôt que tout à la fois. Une valeur de `0.25` signifie que chaque passe de réduction supprime 25% du surplus. Cette libération progressive évite une chute soudaine de la taille du pool qui pourrait causer un pic d'allocation si la charge reprend.

Définissez ceci à `1.0` si vous voulez que tout l'excédent soit libéré immédiatement à chaque cycle.

---

### Cycle de vie

#### EnableAutoCancel

| | |
|-|-|
| Type | `bool` |
| Défaut | `true` |
| Infobulle Inspecteur | "Auto-lier les tâches MonoBehaviour à destroyCancellationToken." |

Quand activé, les tâches démarrées depuis un `MonoBehaviour` sont automatiquement liées au `destroyCancellationToken` de ce `MonoBehaviour`. Si le `MonoBehaviour` est détruit pendant qu'une tâche s'exécute, la tâche est annulée plutôt que de continuer à s'exécuter contre un objet détruit.

Désactivez ceci seulement si vous gérez l'annulation manuellement et ne voulez pas de liaison automatique.

---

### Gestion des erreurs

#### LogUnobservedCancellations

| | |
|-|-|
| Type | `bool` |
| Défaut | `false` |
| Infobulle Inspecteur | "Journaliser les annulations non observées comme avertissements." |

Par défaut, une tâche qui est annulée mais jamais attendue (annulation non observée) est silencieusement ignorée. Activez ceci pour journaliser un avertissement quand cela se produit. Utile pendant le développement pour trouver les tâches fire-and-forget qui s'annulent silencieusement.

Les fautes non observées sont toujours signalées via l'événement `ValkarnTask.UnobservedException` quel que soit ce paramètre.

#### MaxExceptionLogsPerFrame

| | |
|-|-|
| Type | `int` |
| Défaut | `10` |
| Plage valide | `1` – `100` |
| Infobulle Inspecteur | "Nombre maximum de journaux d'exception par frame pour éviter le spam." |

Limite le nombre d'entrées de journal d'exception non observées émises dans une seule frame. Si de nombreuses tâches sont faultées dans la même frame (par exemple, après un échec réseau), cela empêche la console d'être inondée de centaines de traces de pile identiques.

---

## Comment fonctionne la réduction du pool à l'exécution

À chaque mise à jour de la boucle de jeu Unity, `PlayerLoopHelper` incrémente un compteur de frames. Quand le compteur atteint `TrimCheckInterval`, il appelle `PoolRegistry.TrimAll(MinPoolSize)`. Chaque pool qui a été enregistré vérifie s'il a été au-dessus de la capacité pendant au moins `TrimHysteresisCount` vérifications consécutives. Si c'est le cas, il libère `TrimReleaseRatio` de son excédent.

Les pools s'enregistrent automatiquement lors de la première utilisation. La méthode `ValkarnTask.GetPoolInfo()` retourne un instantané de tous les pools actuellement enregistrés avec leur type, taille actuelle et taille maximale — c'est ce que la fenêtre Task Tracker affiche.

```
Frame 0 ──────────────────────────────────────────────────────────
  La boucle de jeu s'exécute
  Pool A : taille 200, max 256 — normal, pas de réduction nécessaire

Frame 300 ────────────────────────────────────────────────────────
  TrimAll se déclenche
  Pool A : taille 18, max 256 — au-dessus de MinPoolSize(8), mais première vérification excédentaire
  hysteresisCount pour A = 1 (pas encore au seuil de 2)

Frame 600 ────────────────────────────────────────────────────────
  TrimAll se déclenche
  Pool A : taille 18, max 256 — au-dessus de MinPoolSize(8), deuxième vérification excédentaire
  hysteresisCount pour A = 2 — seuil atteint !
  Excédent = 18 - 8 = 10 ; libérer 10 * 0.25 = 2 objets
  Pool A : taille 16
```

---

## Fenêtre Task Tracker

Ouvrez via **Window > Valkarn Tasks > Task Tracker**.

Le Task Tracker est une fenêtre uniquement Éditeur qui affiche l'état en direct des pools pendant que vous êtes en mode Play. Il se rafraîchit à un intervalle configurable (0.5 secondes par défaut, ajustable de 0.1 à 5 secondes via le curseur dans la barre d'outils).

### Onglet Pools

Liste chaque type de pool qui a été actif depuis le dernier rechargement de domaine, trié par taille actuelle (le plus grand en premier). Chaque ligne affiche :

| Colonne | Description |
|---------|-------------|
| Type | Le nom du type d'objet mis en pool, avec les arguments génériques développés |
| Taille | Nombre actuel d'objets dans le pool |
| Max | Le plafond `DefaultMaxPoolSize` pour ce pool |
| Utilisation | Une barre de progression montrant `Taille / Max` en pourcentage |

Si aucun pool n'a encore été utilisé, un message indique "Aucun pool actif. Les pools sont créés lors de la première utilisation."

### Onglet Config

Affiche les trois paramètres de pool en direct lus depuis `ValkarnTask.DefaultMaxPoolSize`, `ValkarnTask.TrimCheckInterval` et `ValkarnTask.MinPoolSize`. Les valeurs ne sont affichées qu'en mode Play — en mode Édition, une note vous invite à entrer en mode Play pour voir les valeurs en direct.

L'onglet Config affiche également une référence à l'asset `ValkarnTaskSettings` (s'il en existe un dans `Resources`), afin que vous puissiez cliquer pour l'inspecter ou le modifier. Si aucun asset n'est trouvé, un avertissement vous dirige vers la création d'un.

---

## Result&lt;T&gt;

`Result<T>` est une union discriminée struct pour représenter le résultat d'une opération sans lancer d'exception. C'est le type de retour utilisé par les combinateurs `WhenAll` pour signaler les résultats par tâche, et est également disponible comme pattern de résultat général.

```csharp
public readonly struct Result<T>
{
    public bool IsSuccess  { get; }
    public bool IsFailure  { get; }   // true si faulté ou annulé
    public bool IsFaulted  { get; }
    public bool IsCanceled { get; }

    public ValkarnTask.Status Status { get; }
    public T Value    { get; }        // lève si pas réussi
    public Exception Error { get; }   // null si pas faulté

    public static Result<T> Success(T value);
    public static Result<T> Failure(string error);    // enveloppe dans InvalidOperationException
    public static Result<T> Faulted(Exception error);
    public static Result<T> Canceled(OperationCanceledException oce = null);

    public static implicit operator bool(Result<T> r);  // true si réussi
}
```

Un `Result` non-générique existe pour les tâches void avec la même forme mais sans `Value`.

### Méthodes d'extension AsResult

Convertissez tout `ValkarnTask` ou `ValkarnTask<T>` en `Result` sans `try/catch` dans votre propre code :

```csharp
public static ValkarnTask<Result<T>> AsResult<T>(this ValkarnTask<T> task);
public static ValkarnTask<Result>    AsResult(this ValkarnTask task);
```

Si la tâche sous-jacente est déjà terminée de manière synchrone (le chemin rapide zéro-allocation), `AsResult` retourne immédiatement sans créer de machine d'état async. Sinon il enveloppe dans une méthode async qui capture `OperationCanceledException` et toutes les autres exceptions, les traduisant en la variante `Result` appropriée.

### Quand utiliser Result&lt;T&gt;

Utilisez `Result<T>` au lieu de capturer des exceptions quand :

- Vous appelez plusieurs tâches en parallèle et voulez des résultats par tâche sans court-circuiter l'ensemble du lot.
- Vous voulez exprimer des opérations faillibles de manière type-safe sans flux de contrôle par exception.
- Vous retournez depuis une méthode que l'appelant peut ne pas vouloir envelopper dans `try/catch`.

```csharp
// Déclencher plusieurs tâches, obtenir tous les résultats indépendamment des échecs individuels
Result<int>[] results = await ValkarnTask.WhenAll(
    FetchScoreAsync(playerA).AsResult(),
    FetchScoreAsync(playerB).AsResult(),
    FetchScoreAsync(playerC).AsResult()
);

foreach (var r in results)
{
    if (r.IsSuccess)
        Debug.Log($"Score : {r.Value}");
    else if (r.IsFaulted)
        Debug.LogError($"Échec : {r.Error.Message}");
    else
        Debug.Log("Annulé");
}
```

Vérifier `IsSuccess` ou utiliser l'opérateur `bool` implicite sont les façons préférées de brancher sur un résultat :

```csharp
var result = await SomeOperationAsync().AsResult();

if (result)
{
    Use(result.Value);
}
```

Accéder à `result.Value` quand `IsSuccess` est `false` lève une `InvalidOperationException`.

### Méthodes de fabrique

| Méthode | Statut défini | Erreur définie |
|---------|--------------|---------------|
| `Result<T>.Success(value)` | `Succeeded` | aucune |
| `Result<T>.Failure(message)` | `Faulted` | `new InvalidOperationException(message)` |
| `Result<T>.Faulted(exception)` | `Faulted` | l'exception fournie |
| `Result<T>.Canceled(oce?)` | `Canceled` | l'`OperationCanceledException` fournie, ou `null` |

La propriété `Succeeded` existe sur `Result` et `Result<T>` mais est marquée `[Obsolete]` — utilisez `IsSuccess` à la place pour la cohérence avec `IsFailure`, `IsFaulted` et `IsCanceled`.
