---
sidebar_position: 1
title: Unity PlayerLoop-Integration
---

# Unity PlayerLoop-Integration

ValkarnTasks verwendet keine Threads oder Hintergrund-Scheduler, um Ihre `async`-Methoden fortzusetzen. Alles läuft auf Unitys Haupt-Thread, angetrieben durch Unitys eigenes PlayerLoop-System. Das Verständnis davon hilft Ihnen, den richtigen Zeitpunkt für Ihren Anwendungsfall zu wählen und genau zu verstehen, wann Ihr Code nach einem `await` fortgesetzt wird.

## Was der Unity PlayerLoop ist

Unitys PlayerLoop ist die interne Engine-Schleife, die jeden Frame antreibt. Es ist kein einzelner `Update()`-Aufruf — es ist eine hierarchische Sequenz von Phasen, die in einer definierten Reihenfolge jeden Frame ablaufen:

```
Initialization
EarlyUpdate
FixedUpdate      (wiederholt, wenn Physik fortschreitet)
PreUpdate
Update
PreLateUpdate
PostLateUpdate
TimeUpdate
```

Jede dieser Top-Level-Phasen enthält Subsysteme, die Unity (und Drittanbieter-Pakete) einfügen, um ihre Logik an bestimmten Punkten auszuführen. `MonoBehaviour.Update()` läuft zum Beispiel innerhalb der `Update`-Phase. `MonoBehaviour.LateUpdate()` läuft innerhalb von `PreLateUpdate`.

Da ValkarnTasks direkt in diese Schleife einhakt, blockiert `await VlkTask.Yield()` keinen Thread — es registriert einen Callback, den Unity beim nächsten Tick der gewählten Phase aufruft, und kehrt dann sofort zurück.

## Die 16 PlayerLoop-Zeitpunkte

ValkarnTasks injiziert an 16 Punkten: einen am **Anfang** und einen am **Ende** jeder der 8 Unity PlayerLoop-Phasen. Die `Last`-Varianten werden am Ende der Subsystem-Liste ihrer übergeordneten Phase angehängt; die einfachen Varianten werden am Anfang vorangestellt.

| Wert | Enum-Ganzzahl | Übergeordnete Phase | Position in der übergeordneten Phase |
|---|---|---|---|
| `Initialization` | 0 | `UnityEngine.PlayerLoop.Initialization` | Erste |
| `LastInitialization` | 1 | `UnityEngine.PlayerLoop.Initialization` | Letzte |
| `EarlyUpdate` | 2 | `UnityEngine.PlayerLoop.EarlyUpdate` | Erste |
| `LastEarlyUpdate` | 3 | `UnityEngine.PlayerLoop.EarlyUpdate` | Letzte |
| `FixedUpdate` | 4 | `UnityEngine.PlayerLoop.FixedUpdate` | Erste |
| `LastFixedUpdate` | 5 | `UnityEngine.PlayerLoop.FixedUpdate` | Letzte |
| `PreUpdate` | 6 | `UnityEngine.PlayerLoop.PreUpdate` | Erste |
| `LastPreUpdate` | 7 | `UnityEngine.PlayerLoop.PreUpdate` | Letzte |
| `Update` | 8 | `UnityEngine.PlayerLoop.Update` | Erste |
| `LastUpdate` | 9 | `UnityEngine.PlayerLoop.Update` | Letzte |
| `PreLateUpdate` | 10 | `UnityEngine.PlayerLoop.PreLateUpdate` | Erste |
| `LastPreLateUpdate` | 11 | `UnityEngine.PlayerLoop.PreLateUpdate` | Letzte |
| `PostLateUpdate` | 12 | `UnityEngine.PlayerLoop.PostLateUpdate` | Erste |
| `LastPostLateUpdate` | 13 | `UnityEngine.PlayerLoop.PostLateUpdate` | Letzte |
| `TimeUpdate` | 14 | `UnityEngine.PlayerLoop.TimeUpdate` | Erste |
| `LastTimeUpdate` | 15 | `UnityEngine.PlayerLoop.TimeUpdate` | Letzte |

`Update` (Wert 8) ist der **Standard** für alle ValkarnTasks-Operationen — `Yield()`, `Delay()`, `WaitUntil()`, `WaitWhile()`, `NextFrame()` und `DelayFrame()`.

## Marker-Structs und idempotente Injektion

Um in Unitys PlayerLoop zu injizieren, erstellt ValkarnTasks ein `PlayerLoopSystem` pro Zeitpunkt. Jedes System wird durch eine eindeutige Marker-Struct identifiziert, die in `PlayerLoopHelper` definiert ist:

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

Vor der Injektion prüft `PlayerLoopHelper`, ob `VlkTaskUpdate` bereits irgendwo im aktuellen PlayerLoop-Baum erscheint. Wenn ja, wird die Injektion übersprungen. Dies macht die Injektion **idempotent** — `Init()` mehrmals aufzurufen (was im Editor vorkommen kann) führt nie dazu, dass doppelte Systeme registriert werden.

Die Injektion liest immer den **aktuellen** PlayerLoop mit `PlayerLoop.GetCurrentPlayerLoop()`, nie `GetDefaultPlayerLoop()`. Das bedeutet, dass alle zuvor von anderen Paketen installierten Systeme erhalten bleiben.

## ContinuationQueue — Einmalige Callbacks

Wenn Sie `await VlkTask.Yield()` aufrufen (oder irgendetwas, das für genau einen Tick suspendiert), ruft die compilergenerierte Zustandsmaschine `OnCompleted` auf dem Awaiter auf. Der Awaiter ruft `PlayerLoopHelper.AddContinuation(timing, action, state)` auf, das den Callback in die `ContinuationQueue` für diesen Zeitpunkt einreiht.

`ContinuationQueue` verwendet ein **Doppelpuffer**-Design:

1. **Aktiver Puffer** (`actionList`): hält Fortsetzungen, die vor und während des aktuellen Drainens eingereiht wurden.
2. **Wartepuffer** (`waitingList`): fängt Fortsetzungen auf, die _während_ des Drainens des aktiven Puffers eingereiht werden (d. h. re-entrante Einreihungen aus einer Fortsetzung selbst).
3. **Cross-Thread-Treiber-Stack** (`crossThreadHead`): Fortsetzungen, die von Hintergrund-Threads gepostet werden, landen hier über ein sperrfreies Compare-and-Swap. Zu Beginn jedes Drainens wird der gesamte Stack atomar beansprucht und in den aktiven Puffer verschoben.

Bei jedem PlayerLoop-Tick führt `ContinuationQueue.Run()` diese Sequenz aus:

```
1. crossThreadHead → actionList abschöpfen     (sperrfreies atomares Nehmen, dann unter SpinLock kopieren)
2. Anzahl snapshot, isDraining = true setzen   (unter SpinLock)
3. Alle Callbacks ausführen                    (außerhalb der Sperre, Refs für GC gelöscht)
4. actionList ↔ waitingList tauschen           (unter SpinLock, isDraining = false setzen)
```

Nach dem Aufwärmen von etwa einem oder zwei Frames stabilisieren sich die internen Arrays auf ihrem Höchststand und die Queue läuft mit **null Allokationen**.

Cross-Thread-Knoten werden in einem begrenzten sperrfreien Pool (max. 1.024 Knoten) gepooolt, um Allokationen bei jedem Hintergrund-Thread-Einreihen zu vermeiden.

## PlayerLoopRunner — Wiederkehrende Elemente

Einige Operationen müssen bei jedem Tick geprüft werden, bis sie abgeschlossen sind: `Delay()`, `DelayFrame()`, `WaitUntil()`, `WaitWhile()` und `NextFrame()`. Diese implementieren das `IPlayerLoopItem`-Interface:

```csharp
internal interface IPlayerLoopItem
{
    // true zurückgeben, um weiterzulaufen; false zurückgeben, um aus dem Runner zu entfernen.
    bool MoveNext();
}
```

Elemente werden zu `PlayerLoopRunner` via `PlayerLoopHelper.AddAction(timing, item)` hinzugefügt. Bei jedem Tick iteriert `PlayerLoopRunner.Run()` alle registrierten Elemente und ruft `MoveNext()` auf jedem auf. Elemente, die `false` zurückgeben, werden über **In-Place-Kompaktierung** entfernt — das Array wird in einem einzigen Durchlauf kompaktiert und die Einfügereihenfolge wird beibehalten. Während eines `Run()`-Aufrufs hinzugefügte Elemente werden sicher angehängt und beim nächsten Tick aufgenommen.

```
Tick N:
  ContinuationQueue.Run()   → alle einmaligen Awaiter fortsetzen
  PlayerLoopRunner.Run()    → alle wiederkehrenden Elemente ticken (Delay, WaitUntil, ...)
```

Beide laufen für jeden der 16 Zeitpunkte, in der Reihenfolge, in der die Engine sie aufruft.

Der `Update`-Zeitpunkt verfolgt auch die globale Frame-Anzahl und löst periodisch das Trimmen des Objekt-Pools aus (alle `VlkTask.TrimCheckInterval` Frames).

## Initialisierung — RuntimeInitializeOnLoadMethod

ValkarnTasks initialisiert sich bei `RuntimeInitializeLoadType.SubsystemRegistration`, dem frühesten Hook, den Unity bietet, der vor `Awake()` auf jedem Szenenobjekt ausgelöst wird:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void Init()
{
    // 1. Haupt-Thread-ID für Thread-Sicherheitsprüfungen erfassen
    // 2. ContinuationQueue und PlayerLoopRunner für alle 16 Zeitpunkte erstellen
    // 3. In PlayerLoop injizieren (idempotent)
    // 4. Play-Mode-State-Changed-Callback registrieren (nur Editor)
}
```

Die Haupt-Thread-ID wird zu diesem Zeitpunkt erfasst und mit allen Pool- und Queue-Subsystemen geteilt, damit diese Haupt-Thread-Einreihungen (SpinLock-Pfad) von Cross-Thread-Einreihungen (Treiber-Stack-Pfad) unterscheiden können.

## Domain-Reload-Behandlung (Editor)

Im Unity Editor löst das Eintreten oder Verlassen des Play-Modus einen Domain-Reload aus. Statischer Zustand aus der vorherigen Spielsitzung würde ansonsten bestehen bleiben und veraltete Referenzen verursachen.

ValkarnTasks behandelt dies mit einem `EditorApplication.playModeStateChanged`-Callback, der während `Init()` registriert wird. Wenn der Zustand zu `ExitingPlayMode` oder `EnteredEditMode` übergeht, wird folgendes Cleanup ausgeführt:

```csharp
// Alle Queues und Runner zurücksetzen
for (int i = 0; i < 16; i++)
{
    s_continuationQueues[i] = null;
    s_playerLoopRunners[i] = null;
}

// Andere Subsysteme zurücksetzen
VlkTask.ResetStatics();
TimeProvider.ResetToDefault();   // setzt auf UnityTimeProvider zurück
PoolRegistry.Clear();
ContinuationQueue.ContinuationNode.ResetPool();
```

Wenn der Play-Modus anschließend wieder betreten wird, wird `Init()` über `RuntimeInitializeOnLoadMethod` ausgelöst und frische Queues und Runner werden alloziert. Die PlayerLoop-Injektionsprüfung (`HasVlkTaskSystems`) verhindert, dass die Marker-Systeme ein zweites Mal eingefügt werden, wenn die Domain nicht vollständig entladen wurde.

## Den richtigen Zeitpunkt wählen

Die meisten Codes sollten den Standard-`Update`-Zeitpunkt verwenden. Greifen Sie nur auf andere zurück, wenn Sie einen spezifischen Grund haben.

| Zeitpunkt | Wann zu verwenden |
|---|---|
| `Initialization` / `LastInitialization` | Sehr frühes Frame-Setup; selten in Spielcode benötigt |
| `EarlyUpdate` / `LastEarlyUpdate` | Eingabe-Sampling; läuft vor Physik und vor `Update` |
| `FixedUpdate` / `LastFixedUpdate` | Physik-synchronisierte Logik; entspricht dem `MonoBehaviour.FixedUpdate`-Takt |
| `PreUpdate` / `LastPreUpdate` | Vor Unitys Haupt-Update-Dispatch; nützlich für benutzerdefinierte Scheduler-Pre-Pass |
| **`Update`** (Standard) | **Standardmäßige Spiellogik; entspricht `MonoBehaviour.Update`** |
| `LastUpdate` | Logik, die nach allen `MonoBehaviour.Update`-Aufrufen laufen muss |
| `PreLateUpdate` / `LastPreLateUpdate` | Entspricht `MonoBehaviour.LateUpdate`; Kamera- und Transform-Folgebewegung |
| `PostLateUpdate` / `LastPostLateUpdate` | Nach dem Rendering-Einreichen; UI-Finalisierung, Screenshot-Aufnahme |
| `TimeUpdate` / `LastTimeUpdate` | Zeitsynchronisation; sehr selten benötigt |

```csharp
// Beim nächsten Update-Tick fortsetzen (Standard)
await VlkTask.Yield();

// Am Anfang des nächsten FixedUpdate fortsetzen (physik-sicher)
await VlkTask.Yield(PlayerLoopTiming.FixedUpdate);

// 2 Sekunden warten, mit unskalierter Zeit, in LateUpdate geprüft
await VlkTask.Delay(
    TimeSpan.FromSeconds(2),
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.PreLateUpdate);

// Warten, bis eine Bedingung wahr ist, nach allen LateUpdate-Aufrufen geprüft
await VlkTask.WaitUntil(() => _ready, PlayerLoopTiming.LastPreLateUpdate);
```
