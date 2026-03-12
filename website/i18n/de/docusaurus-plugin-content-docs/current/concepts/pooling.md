---
sidebar_position: 2
title: Object-Pooling
---

# Object-Pooling

Valkarn Tasks eliminiert GC-Allokationen auf gängigen asynchronen Pfaden, indem es die Objekte poolt, die jede `VlkTask` unterstützen. Diese Seite erklärt die Pool-Architektur — wie Objekte gespeichert werden, wie sie erworben und zurückgegeben werden, und welche Lebenszyklus-Garantien das System bietet.

---

## Überblick

Wenn eine `async`-Methode suspendiert, benötigt die Bibliothek einen Ort zur Speicherung der compilergenerieren Zustandsmaschine und einen Abschlussmechanismus, den der Awaiter abonnieren kann. In `System.Threading.Tasks` ist das das `Task`-Objekt selbst — eine Heap-Allokation pro Aufruf. In Valkarn Tasks übernehmen gepoolte Objekte, die `VlkTask.ISource` implementieren, diese Rolle.

Das Pool-Design hat drei Ziele:

1. **Null Atomics auf dem Haupt-Thread-Hotpfad.** Unitys Spielschleife ist per Konvention single-threaded. Ausleihen und Zurückgeben vom Haupt-Thread sollten einfache Lese- und Schreibvorgänge sein.
2. **Sicherer Cross-Thread-Zugriff.** Hintergrundtasks, die `VlkTask.Run` verwenden, arbeiten auf Thread-Pool-Threads. Der Pool muss gleichzeitiges Ausleihen/Zurückgeben korrekt handhaben.
3. **Begrenztes Wachstum mit adaptivem Trimmen.** Pools sollten nach einem Traffic-Spike nicht unbegrenzt wachsen, aber auch nicht so aggressiv schrumpfen, dass sie ständig neu allozieren.

---

## `VlkTaskPool<T>`

`VlkTaskPool<T>` ist die zentrale Pool-Klasse. Sie ist `internal sealed` — Sie interagieren nicht direkt damit, aber das Verständnis erklärt, wohin Ihre Allokationen gehen.

```
VlkTaskPool<T>
  |
  +-- fastItem: T            (Einzel-Slot-Cache, nur Haupt-Thread, einfaches Lesen/Schreiben)
  |
  +-- stackHead: T           (Treiber-Stack-Kopf, CAS-basiert für Cross-Thread-Sicherheit)
  +-- stackSize: int
  |
  +-- maxSize: int           (begrenzt durch VlkTask.DefaultMaxPoolSize)
  +-- totalCreated: int      (verfolgt Lifetime-Allokationen für Trim-Verhältnis)
```

### Schnell-Slot (Haupt-Thread)

Das `fastItem`-Feld ist ein einzelner reservierter Slot für das zuletzt zurückgegebene Objekt. Auf dem Haupt-Thread sind Ausleihen und Zurückgeben ein einfaches Lesen und Schreiben — keine Atomics, kein Spinning. Dies deckt die überwiegende Mehrheit der Unity-Spielschleifen-Operationen ab.

```
Ausleihen (Haupt-Thread):
  fastItem != null  →  nehmen (fastItem = null), zurückgeben   [null Atomics]
  fastItem == null  →  zum Treiber-Stack übergehen

Zurückgeben (Haupt-Thread):
  fastItem == null  →  fastItem = item                          [null Atomics]
  fastItem != null  →  zum Treiber-Stack übergehen
```

### Treiber-Stack (Überlauf / Hintergrund-Threads)

Wenn der schnelle Slot belegt ist (oder wenn der aufrufende Thread nicht der Haupt-Thread ist), verwendet der Pool einen sperrfreien Treiber-Stack — eine klassische intrusive verknüpfte Liste mit Compare-and-Swap (CAS):

```
Ausleihen (beliebiger Thread):
  while (true):
    head = Volatile.Read(stackHead)
    wenn head == null: null zurückgeben (Pool leer)
    next = head.NextNode
    wenn CAS(stackHead, next, head) == head: head zurückgeben  // Rennen gewonnen
    spinner.SpinOnce()                                          // verloren, wiederholen

Zurückgeben (beliebiger Thread):
  wenn stackSize >= maxSize: false zurückgeben (Pool voll, Element verwerfen)
  while (true):
    head = Volatile.Read(stackHead)
    item.NextNode = head
    wenn CAS(stackHead, item, head) == head: stackSize++; true zurückgeben
    spinner.SpinOnce()
```

Der Stack ist intrusive: jedes gepoolte Objekt speichert seinen eigenen `NextNode`-Zeiger, sodass kein externer Wrapper-Knoten benötigt wird. Dies wird durch das `IPoolNode<T>`-Interface erzwungen.

### Thread-Routing

```csharp
internal static class VlkTaskPoolShared
{
    internal static volatile int MainThreadId;
}
```

Alle Pool-Instanzen teilen eine einzelne `MainThreadId`. Ausleihen/Zurückgeben-Operationen prüfen `Thread.CurrentThread.ManagedThreadId == MainThreadId`, um zum richtigen Pfad zu routen. Das `volatile`-Feld stellt die Cross-Thread-Sichtbarkeit sicher, nachdem die ID beim Start veröffentlicht wurde.

---

## `IPoolNode<T>`

Jeder Typ, der am Pool teilnimmt, muss dieses Interface implementieren:

```csharp
internal interface IPoolNode<T> where T : class
{
    ref T NextNode { get; }
}
```

`ref T NextNode` gibt eine Referenz auf das Feld innerhalb des Objekts zurück, das den Next-Zeiger speichert. Der Pool schreibt direkt über die Referenz in dieses Feld und eliminiert jeden separaten Wrapper-Knoten. Alle gepoolten Typen in der Bibliothek — Runner, Promises, Kombinatoren — implementieren dieses Interface, indem sie ein privates Feld deklarieren und es verfügbar machen:

```csharp
// Beispiel aus PooledPromise<T>
PooledPromise<T> nextNode;
public ref PooledPromise<T> NextNode => ref nextNode;
```

---

## Pool-Lebenszyklus: Erwerben, Verwenden, Zurückgeben

Der vollständige Lebenszyklus eines gepoolten Objekts ist:

```
Aufrufer ruft async-Methode auf
  |
  +--> AsyncVlkTaskMethodBuilder.AwaitUnsafeOnCompleted()
        |
        +--> runner == null? JA --> Pool.TryRent() ?? Pool.TrackNew(new Runner())
              |
              +--> runner in Builder gespeichert; Zustandsmaschine in Runner kopiert
              |
              +--> awaiter.UnsafeOnCompleted(runner.MoveNextAction)
              |
              [... asynchrone Arbeit läuft ...]
              |
              +--> runner.SetResult() / runner.SetException()
                    |
                    +--> VlkTaskCompletionCore.TrySetResult() --> Fortsetzung aufgerufen
                          |
                          +--> Aufrufers Awaiter ruft GetResult(token) auf
                                |
                                +--> core.GetResult(token) -- liest Ergebnis oder wirft neu
                                |
                                +--> TryReturn():
                                      stateMachine = default
                                      core.Reset()           // erhöht Generation
                                      Pool.TryReturn(this)
```

Die `TryReturn`-Methode löscht immer die Zustandsmaschine, bevor `core.Reset()` aufgerufen wird. Diese Reihenfolge ist wichtig: `Reset()` erhöht den Generationszähler und macht den Slot für gleichzeitige Mieter als verfügbar sichtbar. Wenn die Zustandsmaschine nach `Reset()` gelöscht würde, könnte ein Mieter auf einem anderen Thread den Slot erhalten und seine Zustandsmaschine überschrieben bekommen.

---

## `VlkTaskCompletionCore<TResult>`

`VlkTaskCompletionCore<TResult>` ist ein `internal struct`, das in jedes gepoolte Objekt eingebettet ist. Es ist die eigentliche Zustandsmaschine für das Promise — verfolgt den Abschlusszustand, speichert Ergebnisse und Fehler, und löst das Rennen zwischen `OnCompleted` (Fortsetzung registrieren) und `TrySetResult` (Abschluss signalisieren) auf.

```
Felder:
  result:          TResult     -- der Erfolgswert
  error:           object      -- ExceptionDispatchInfo oder OperationCanceledException
  errorKind:       byte        -- 0=keiner, 1=gefaultet, 2=abgebrochen(OCE), 3=abgebrochen(EDI)
  generation:      int         -- monoton steigend; als uint für Token-Vergleich gecastet
  completedCount:  int         -- 0=ausstehend, 1=beansprucht, 2=abgeschlossen (zweiphasige Veröffentlichung)
  continuation:    Action<object>
  continuationState: object
  hasUnhandledError: bool
```

### Zweiphasiges Abschlussprotokoll

Der Abschluss verwendet ein zweiphasiges CAS, um auf ARM64 sicher zu sein, wo Store-Release/Load-Acquire-Paare benötigt werden:

```
TrySetResult(value):
  Phase 1: CAS(completedCount, 0 -> 1)   -- exklusive Eigentumsübertragung
  Phase 2: Ergebnis schreiben
  Phase 3: Volatile.Write(completedCount, 2)  -- mit Release-Semantik veröffentlichen
  Phase 4: InvokeContinuation()
```

Leser verwenden `Volatile.Read(completedCount)` (Acquire-Semantik), bevor sie das Ergebnis lesen, um sicherzustellen, dass sie den in Phase 2 geschriebenen Wert sehen.

### Rennen-Auflösung zwischen `OnCompleted` und `TrySetResult`

Drei Muster können auftreten:

```
Muster A — OnCompleted zuerst:
  OnCompleted speichert Fortsetzung via CAS(continuation, null -> cont)
  TrySetResult liest nicht-null Fortsetzung -> ruft sie auf

Muster B — TrySetResult zuerst (sync schneller Pfad):
  TrySetResult platziert ContinuationSentinel via CAS(continuation, null -> sentinel)
  OnCompleted liest sentinel -> ruft Fortsetzung sofort inline auf

Muster C (gleichzeitiges Rennen):
  C.1: OnCompleted gewinnt CAS -> TrySetResult liest es -> ruft auf
  C.2: TrySetResult gewinnt CAS (platziert Sentinel) -> OnCompleted erkennt Sentinel -> ruft inline auf
```

Der Sentinel ist ein statisches `Action<object>`-Objekt, das rein als Markierungswert verwendet wird — es wird nie tatsächlich als Delegate aufgerufen.

### Token-Validierung und ABA-Sicherheit

Jeder Aufruf von `GetStatus`, `GetResult` und `OnCompleted` validiert das `uint token` gegen die aktuelle `generation`. Wenn `Reset()` `Interlocked.Increment(ref generation)` aufruft, erhält jedes ausstehende `VlkTask`-Struct, das das alte Token hält, eine `InvalidOperationException`, anstatt still auf recyceltem Zustand zu operieren. Ein 32-Bit-Generationszähler, der überläuft (was ~4 Milliarden Wiederverwendungen eines einzelnen Slots erfordert), gilt in der Praxis als effektiv unmöglich.

### Reset und unbeobachtete Fehlerberichterstattung

`Reset()` wird zum Zeitpunkt der Pool-Rückgabe aufgerufen. Bevor die Generation erhöht wird, prüft es, ob ein Fehler gespeichert wurde, aber nie beobachtet wurde (d. h. `GetResult` wurde nach einem Fehler nie aufgerufen). Wenn ja, veröffentlicht es die Ausnahme über `VlkTask.UnobservedException`. Abbruchfehler werden nur gemeldet, wenn `LogUnobservedCancellations` in `VlkTaskSettings` aktiviert ist, da Abbruch oft beabsichtigt ist.

Für nicht-gepoolte `Promise`- und `Promise<T>`-Objekte erfolgt die unbeobachtete Fehlerberichterstattung über den Finalizer mittels `ReportUnobservedIfNeeded()`, das dieselbe Logik ohne Zustandslöschung befolgt.

---

## Pool-Konfiguration

Drei Einstellungen steuern die Pool-Größe. In Unity-Builds werden diese aus einem `VlkTaskSettings` ScriptableObject-Asset gelesen (mit Fallback-Standardwerten) und können zur Laufzeit über statische Eigenschaften überschrieben werden:

```csharp
// Maximale Objekte pro Pool-Typ (pro TStateMachine oder pro Promise-Typ)
VlkTask.DefaultMaxPoolSize = 256;   // Standard: 256

// Wie viele Frames zwischen Trim-Prüfungen (Unity PlayerLoop-Frames)
VlkTask.TrimCheckInterval = 300;    // Standard: 300 (~5 Sekunden bei 60fps)

// Minimale Objekte, die nach einem Trim-Durchlauf behalten werden
VlkTask.MinPoolSize = 8;            // Standard: 8
```

`DefaultMaxPoolSize` ist die Obergrenze, die zur Pool-Konstruktionszeit angewendet wird. Sie wird pro Pool-Instanz durchgesetzt, nicht global — ein Pool für `AsyncVlkTaskRunner<LoadSceneStateMachine>` und ein Pool für `AsyncVlkTaskRunner<FetchDataStateMachine>` haben jeweils ihre eigene Obergrenze.

### Pool-Trimmen

`PlayerLoopHelper` ruft `PoolRegistry.TrimAll(minPoolSize)` alle `TrimCheckInterval` Frames auf dem Haupt-Thread auf. Jeder Pool verwendet eine Hysterese-Strategie:

```
Jede Trim-Prüfung:
  currentSize = fastItem.count + stackSize
  wenn currentSize <= MinPoolSize: aufeinanderfolgenden Zähler zurücksetzen, überspringen

  ratio = currentSize / totalCreated
  wenn ratio > 0,5 (Pool hält > 50% aller je erstellten Objekte):
    trimConsecutiveCount++
    wenn trimConsecutiveCount >= hysteresisThreshold:
      einige Brucheile (releaseRatio) überschüssiger Objekte aus dem Stack freigeben
      (fastItem wird bewahrt — es ist der cache-freundlichste Slot)
  sonst:
    trimConsecutiveCount zurücksetzen
```

Die Hysterese verhindert, dass ein kurzer Traffic-Spike sofort dazu führt, dass alle Objekte alloziert und dann sofort getrimmt werden. Der schnelle Slot wird beim Trimmen immer bewahrt, da er das zuletzt verwendete Element darstellt und daher am wahrscheinlichsten wieder benötigt wird.

---

## `PoolRegistry` und Überwachung

Jeder `VlkTaskPool<T>` registriert sich zur Konstruktionszeit beim globalen `PoolRegistry`. Die Registry pflegt eine Liste von `IPoolInfo`-Referenzen, die folgendes verfügbar machen:

```csharp
internal interface IPoolInfo
{
    Type PooledType { get; }
    int Size { get; }
    int MaxSize { get; }
    bool IsAlive { get; }
    int Trim(int minPoolSize);
}
```

Sie können alle aktiven Pools zur Laufzeit über die öffentliche API aufzählen:

```csharp
foreach (var (type, size, maxSize) in VlkTask.GetPoolInfo())
{
    Debug.Log($"{type.Name}: {size}/{maxSize}");
}
```

Dies sind dieselben Daten, die das **Task-Tracker**-Fenster im Unity Editor anzeigt. Das Fenster ruft `GetPoolInfo()` ab und zeigt eine Live-Tabelle der Pool-Belegung an, sodass Sie sehen können, ob Pools aufgewärmt sind, ob ein Typ ständig seine Obergrenze erreicht und ob das Trimmen wie erwartet funktioniert.

Tote Pool-Einträge (wo `IsAlive` false zurückgibt) werden während `GetAll()`- und `TrimAll()`-Aufrufen lazy aus der Registry-Liste entfernt, um zu verhindern, dass die Registry unbegrenzt wächst, wenn Pool-Instanzen irgendwie durch den GC gesammelt werden.

---

## `PooledPromise` und `PooledPromise<T>`

Dies sind die gepoolten Abschluss-Quellen, die für die Verwendung in benutzerdefinierten asynchronen Mustern bestimmt sind — zum Beispiel das Umhüllen einer callback-basierten API oder eines sich wiederholenden Producer/Consumer-Channels.

```csharp
// Ein ausstehendes Promise aus dem Pool erwerben
var promise = VlkTask.PooledPromise<string>.Create(out uint token);
VlkTask<string> task = promise.Task;

// Task an einen Konsumenten übergeben
// ... später, von einem beliebigen Thread ...
promise.TrySetResult("hello");

// Wenn der Konsument die Task abwartet und GetResult aufgerufen wird,
// setzt sich das Promise zurück und kehrt automatisch in den Pool zurück.
```

Hauptmerkmale:

- `Create(out uint token)` leiht aus dem Pool oder alloziert eine neue, vom Pool verfolgte Instanz.
- `CreateCompleted(T result, out uint token)` macht dasselbe, signalisiert aber sofort das Ergebnis, sodass die Task bereits abgeschlossen ist, wenn sie zurückgegeben wird.
- Nachdem `GetResult` auf der Backing-Task aufgerufen wurde, wird `TryReturn()` ausgelöst: Das Promise ruft `core.Reset()` auf und gibt sich selbst in den Pool zurück.
- Ein Doppelrückgabe-Schutz (`Interlocked.Exchange(ref returned, 1)`) verhindert Pool-Korruption, wenn `GetResult` zweimal aufgerufen wird.

**Nicht-gepoolte Alternative: `Promise` und `Promise<T>`.** Dies sind heap-allozierte Klassen, die nicht an einen Pool zurückgegeben werden. Verwenden Sie sie für langlebige Operationen, bei denen die Lebensdauer unvorhersehbar ist oder bei denen dieselbe Quelle mehrere Await-Zyklen überleben muss. Sie verlassen sich auf einen Finalizer, um unbeobachtete Ausnahmen zu melden.

---

## Kombinator-Pools

`WhenAll`- und `WhenAny`-Kombinatoren verwenden ebenfalls Pools. Jede Arität- und Typ-Kombination hat ihren eigenen Pool:

| Kombinator | Pool-Typ |
|-----------|----------|
| `WhenAll(task1, task2)` (typisiert) | `VlkTaskPool<WhenAllPromise<T1, T2>>` |
| `WhenAll(IEnumerable<VlkTask<T>>)` | `VlkTaskPool<WhenAllArrayPromise<T>>` |
| `WhenAll(task1, task2)` (void) | `VlkTaskPool<WhenAllVoidPromise2>` |
| `WhenAny(task1, task2)` (typisiert) | `VlkTaskPool<WhenAnyPromise2<T>>` |
| `WhenAny(IEnumerable<VlkTask<T>>)` | `VlkTaskPool<WhenAnyArrayPromise<T>>` |

Array-basierte Kombinatoren (`WhenAll<T>(IEnumerable<...>)` und `WhenAny<T>(IEnumerable<...>)`) verwenden `System.Buffers.ArrayPool<T>.Shared` für ihre internen Source/Token-Arrays, sodass diese Arrays ebenfalls recycelt statt pro Aufruf frisch alloziert werden.

Alle Kombinatoren wenden denselben null-Allokations-Kurzschluss an: Wenn alle Eingaben zum Zeitpunkt des `WhenAll`- oder `WhenAny`-Aufrufs synchron abgeschlossen sind, wird nie ein neues gepooltes Objekt erstellt.

```csharp
// Null Allokation — beide Tasks sind synchron abgeschlossen
var t1 = VlkTask.FromResult(1);
var t2 = VlkTask.FromResult(2);
VlkTask<(int, int)> combined = VlkTask.WhenAll(t1, t2);
// combined.source == null; Ergebnis ist (1, 2) inline gespeichert
```
