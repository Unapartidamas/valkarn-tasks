---
sidebar_position: 2
title: PlayerLoopTiming
---

# PlayerLoopTiming

```csharp
public enum PlayerLoopTiming : byte
```

Namespace: `UnaPartidaMas.Valkarn.Tasks`

Gibt an, welche Unity PlayerLoop-Phase eine ValkarnTasks-Operation verwendet, um nach der Suspension fortzusetzen. Das Übergeben eines `PlayerLoopTiming`-Werts steuert, **wann im Frame** Ihre `await`-Fortsetzung läuft und **wann** wiederkehrende Elemente wie Delays und Wartebedingungen geprüft werden.

Die Enum-Werte stimmen genau mit UniTasks `PlayerLoopTiming`-Enum überein, was die Migration von UniTask vereinfacht.

---

## Standard

`PlayerLoopTiming.Update` (Wert `8`) ist der Standard für alle Operationen. Wenn Sie kein Timing-Argument übergeben, wird `Update` verwendet.

---

## Werte

### Initialization

```
Wert: 0
Übergeordnete Phase: UnityEngine.PlayerLoop.Initialization
Position: erstes Subsystem in der Phase
```

Läuft ganz zu Beginn der Initialization-Phase, vor Unitys eigenen Initialisierungs-Subsystemen in dieser Phase. Dies wird einmal pro Frame ausgelöst, vor `EarlyUpdate`. Für Gameplay-Code ist es selten nützlich, kann aber für Systeme relevant sein, die Zustand lesen oder einrichten müssen, bevor irgendetwas anderes im Frame ihn berührt.

---

### LastInitialization

```
Wert: 1
Übergeordnete Phase: UnityEngine.PlayerLoop.Initialization
Position: letztes Subsystem in der Phase
```

Läuft am Ende der Initialization-Phase, nach Unitys eingebauten Initialisierungs-Subsystemen. Bevorzugen Sie dies gegenüber `Initialization`, wenn Sie Initialisierungs-Phasen-Timing benötigen, aber möchten, dass Unitys eigene Systeme zuerst laufen.

---

### EarlyUpdate

```
Wert: 2
Übergeordnete Phase: UnityEngine.PlayerLoop.EarlyUpdate
Position: erstes Subsystem in der Phase
```

Läuft zu Beginn von EarlyUpdate, bevor Unity Eingabe-Events verarbeitet und bevor der Physik-Simulationsschritt. Dies ist der früheste Punkt im Frame, an dem Eingabedaten aus dem vorherigen Frame verfügbar sind. Nützlich für Systeme, die Eingaben sampeln müssen, bevor Gameplay-Code läuft.

---

### LastEarlyUpdate

```
Wert: 3
Übergeordnete Phase: UnityEngine.PlayerLoop.EarlyUpdate
Position: letztes Subsystem in der Phase
```

Läuft am Ende von EarlyUpdate, nachdem Unitys eigene Early-Update-Subsysteme abgeschlossen sind.

---

### FixedUpdate

```
Wert: 4
Übergeordnete Phase: UnityEngine.PlayerLoop.FixedUpdate
Position: erstes Subsystem in der Phase
```

Läuft zu Beginn jedes FixedUpdate-Schritts. Dies entspricht dem Timing von `MonoBehaviour.FixedUpdate()`. Unity kann je nachdem, wie der Physik-Zeitstempel mit dem Frame-Delta-Time übereinstimmt, null oder mehr FixedUpdate-Schritte pro gerendertem Frame ausführen.

Verwenden Sie dieses Timing für alles, was mit der Physik-Simulation synchron bleiben muss: `Rigidbody`-Geschwindigkeiten lesen, Kräfte anwenden oder auf eine physikbestimmte Anzahl von Schritten warten.

```csharp
// 10 Physik-Schritte warten
await ValkarnTask.DelayFrame(10, PlayerLoopTiming.FixedUpdate, ct);

// Warten, bis eine Physik-Bedingung wahr ist, bei jedem Physik-Schritt geprüft
await ValkarnTask.WaitUntil(() => rb.velocity.magnitude < 0.1f,
    PlayerLoopTiming.FixedUpdate, ct);
```

---

### LastFixedUpdate

```
Wert: 5
Übergeordnete Phase: UnityEngine.PlayerLoop.FixedUpdate
Position: letztes Subsystem in der Phase
```

Läuft am Ende der FixedUpdate-Phase, nachdem Unitys Physik-Subsysteme (einschließlich `Physics.Simulate`) abgeschlossen sind. Verwenden Sie dies, um Physik-Ergebnisse zu lesen, nachdem die Simulation fortgeschritten ist.

---

### PreUpdate

```
Wert: 6
Übergeordnete Phase: UnityEngine.PlayerLoop.PreUpdate
Position: erstes Subsystem in der Phase
```

Läuft zu Beginn der PreUpdate-Phase, die nach FixedUpdate und vor Update läuft. Unity verwendet PreUpdate für Aufgaben wie Wind-Zonen-Updates und Netzwerk-Event-Verarbeitung.

---

### LastPreUpdate

```
Wert: 7
Übergeordnete Phase: UnityEngine.PlayerLoop.PreUpdate
Position: letztes Subsystem in der Phase
```

Läuft am Ende von PreUpdate.

---

### Update

```
Wert: 8  (Standard)
Übergeordnete Phase: UnityEngine.PlayerLoop.Update
Position: erstes Subsystem in der Phase
```

Das **Standard-Timing** für alle ValkarnTasks-Operationen. Läuft zu Beginn der Update-Phase, wo `MonoBehaviour.Update()` ausgelöst wird. Dies ist die richtige Wahl für die große Mehrheit des Gameplay-Codes.

```csharp
// All diese verwenden standardmäßig Update
await ValkarnTask.Yield();
await ValkarnTask.Delay(1000, ct);
await ValkarnTask.WaitUntil(() => _ready, ct: ct);
await ValkarnTask.NextFrame(ct: ct);
await ValkarnTask.DelayFrame(3, ct: ct);

// Explizit — identisch mit dem Obigen
await ValkarnTask.Yield(PlayerLoopTiming.Update);
```

---

### LastUpdate

```
Wert: 9
Übergeordnete Phase: UnityEngine.PlayerLoop.Update
Position: letztes Subsystem in der Phase
```

Läuft am Ende der Update-Phase, nachdem alle `MonoBehaviour.Update()`-Aufrufe und andere Update-Subsysteme abgeschlossen sind. Verwenden Sie dies, wenn Ihre Fortsetzung die Ergebnisse der `Update()`-Methoden anderer Scripts beobachten muss — zum Beispiel das Abfragen eines Werts, den ein anderes Script während `Update` schreibt.

```csharp
// Nach allen MonoBehaviour.Update()-Aufrufen in diesem Frame fortsetzen
await ValkarnTask.Yield(PlayerLoopTiming.LastUpdate);
```

---

### PreLateUpdate

```
Wert: 10
Übergeordnete Phase: UnityEngine.PlayerLoop.PreLateUpdate
Position: erstes Subsystem in der Phase
```

Läuft zu Beginn der PreLateUpdate-Phase, wo `MonoBehaviour.LateUpdate()` ausgelöst wird. Verwenden Sie dies für Kamera-Folgebewegungen, Transform-Anpassungen und alles, was auf die endgültigen Positionen reagieren soll, die während `Update` gesetzt wurden.

```csharp
// Zum selben Zeitpunkt wie MonoBehaviour.LateUpdate() fortsetzen
await ValkarnTask.Yield(PlayerLoopTiming.PreLateUpdate);
```

---

### LastPreLateUpdate

```
Wert: 11
Übergeordnete Phase: UnityEngine.PlayerLoop.PreLateUpdate
Position: letztes Subsystem in der Phase
```

Läuft am Ende der PreLateUpdate-Phase, nach allen `MonoBehaviour.LateUpdate()`-Aufrufen. Verwenden Sie dies, wenn Sie Werte lesen müssen, die Scripts während ihres `LateUpdate` setzen.

---

### PostLateUpdate

```
Wert: 12
Übergeordnete Phase: UnityEngine.PlayerLoop.PostLateUpdate
Position: erstes Subsystem in der Phase
```

Läuft zu Beginn von PostLateUpdate. Diese Phase wird ausgelöst, nachdem Rendering für den Frame eingereicht wurde. Unity verwendet sie für Aufgaben wie das Präsentieren des Frame-Buffers und das Aufräumen des Render-Zustands. Hier werden auch `Camera.onPostRender`-Callbacks ausgelöst.

Verwenden Sie dieses Timing für End-of-Frame-Operationen: Screenshot-Aufnahme, Streaming-Level-Entladungen oder jede Arbeit, die nach vollständigem Rendern der Szene, aber vor Beginn des nächsten Frames passieren muss.

---

### LastPostLateUpdate

```
Wert: 13
Übergeordnete Phase: UnityEngine.PlayerLoop.PostLateUpdate
Position: letztes Subsystem in der Phase
```

Der letzte Punkt in der Standard-Frame-Schleife, bevor Unity frame-lokalen Zustand zurücksetzt und den nächsten Frame beginnt. Dies ist das absolute Ende eines Frames für Timing-Zwecke.

---

### TimeUpdate

```
Wert: 14
Übergeordnete Phase: UnityEngine.PlayerLoop.TimeUpdate
Position: erstes Subsystem in der Phase
```

Läuft zu Beginn der TimeUpdate-Phase. Unity verwendet diese Phase, um zeitbezogenen Zustand zu aktualisieren (wie `Time.time`). Dieses Timing wird in neueren Unity-Versionen vor `EarlyUpdate` ausgelöst.

Dieses Timing wird im Gameplay-Code selten benötigt. Es existiert primär für Systeme, die Zeitfortschritt abfangen oder beobachten müssen, bevor ein anderes System die neuen Zeitwerte liest.

---

### LastTimeUpdate

```
Wert: 15
Übergeordnete Phase: UnityEngine.PlayerLoop.TimeUpdate
Position: letztes Subsystem in der Phase
```

Läuft am Ende der TimeUpdate-Phase, nachdem Unitys zeitbezogene Subsysteme abgeschlossen sind.

---

## Zusammenfassungstabelle

| Wert | Name | Unity-Phase | Position | Vergleichbarer MonoBehaviour-Callback |
|---|---|---|---|---|
| 0 | `Initialization` | `Initialization` | Erste | — |
| 1 | `LastInitialization` | `Initialization` | Letzte | — |
| 2 | `EarlyUpdate` | `EarlyUpdate` | Erste | — |
| 3 | `LastEarlyUpdate` | `EarlyUpdate` | Letzte | — |
| 4 | `FixedUpdate` | `FixedUpdate` | Erste | `FixedUpdate()` |
| 5 | `LastFixedUpdate` | `FixedUpdate` | Letzte | nach `FixedUpdate()` |
| 6 | `PreUpdate` | `PreUpdate` | Erste | — |
| 7 | `LastPreUpdate` | `PreUpdate` | Letzte | — |
| **8** | **`Update`** (Standard) | **`Update`** | **Erste** | **`Update()`** |
| 9 | `LastUpdate` | `Update` | Letzte | nach allen `Update()` |
| 10 | `PreLateUpdate` | `PreLateUpdate` | Erste | `LateUpdate()` |
| 11 | `LastPreLateUpdate` | `PreLateUpdate` | Letzte | nach allen `LateUpdate()` |
| 12 | `PostLateUpdate` | `PostLateUpdate` | Erste | nach dem Rendering |
| 13 | `LastPostLateUpdate` | `PostLateUpdate` | Letzte | Ende des Frames |
| 14 | `TimeUpdate` | `TimeUpdate` | Erste | — |
| 15 | `LastTimeUpdate` | `TimeUpdate` | Letzte | — |

---

## Welche APIs akzeptieren PlayerLoopTiming

Alle zeit- und bedingungsbasierten ValkarnTasks-APIs akzeptieren einen optionalen `PlayerLoopTiming`-Parameter. Der Standard ist immer `Update`.

| Methode | Signatur |
|---|---|
| `ValkarnTask.Yield` | `Yield(PlayerLoopTiming timing = Update)` |
| `ValkarnTask.NextFrame` | `NextFrame(PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.DelayFrame` | `DelayFrame(int frameCount, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.Delay(int)` | `Delay(int ms, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.Delay(TimeSpan)` | `Delay(TimeSpan delay, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.WaitUntil` | `WaitUntil(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.WaitWhile` | `WaitWhile(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |

---

## Code-Beispiele

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using System.Threading;

// Zum nächsten Update-Tick abgeben (Standard)
await ValkarnTask.Yield();

// Zum nächsten FixedUpdate-Tick abgeben — innerhalb physik-gesteuertem Code verwenden
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// 500 ms mit skalierten deltaTime warten, bei jedem Update geprüft
await ValkarnTask.Delay(500, ct: destroyCancellationToken);

// 500 ms mit unskalierten deltaTime warten — nicht von Time.timeScale beeinflusst
await ValkarnTask.Delay(
    500,
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// 500 ms mit echter Wanduhrzeit warten (Stopwatch-basiert)
await ValkarnTask.Delay(
    500,
    DelayType.Realtime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// Warten, bis ein Flag gesetzt ist, in LateUpdate geprüft
bool _isReady;
await ValkarnTask.WaitUntil(() => _isReady, PlayerLoopTiming.PreLateUpdate, destroyCancellationToken);

// Warten, während geladen wird, ganz am Ende des Frames geprüft
await ValkarnTask.WaitWhile(() => _loading, PlayerLoopTiming.LastPostLateUpdate, destroyCancellationToken);

// Genau 5 Physik-Schritte überspringen
await ValkarnTask.DelayFrame(5, PlayerLoopTiming.FixedUpdate, destroyCancellationToken);

// Einen vollständigen gerenderten Frame voranschreiten
await ValkarnTask.NextFrame(PlayerLoopTiming.Update, destroyCancellationToken);
```
