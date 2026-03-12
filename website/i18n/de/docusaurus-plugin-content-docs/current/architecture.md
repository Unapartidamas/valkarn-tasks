---
sidebar_position: 5
title: Architektur
---

# Architektur

Technische Übersicht über die internen Komponenten von Valkarn Tasks.

---

## Übergeordnete Struktur

```
┌─────────────────────────────────────────────────────────────────┐
│                    COMPILEZEIT                                    │
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
│                    LAUFZEIT                                       │
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

### Assembly-Layout

```
ValkarnTask.Runtime      — wird mit dem Spiel ausgeliefert
ValkarnTask.SourceGen    — nur zur Compilezeit (Source-Generator)
ValkarnTask.Analyzer     — nur zur Compilezeit (Diagnosen + Code-Korrekturen)
ValkarnTask.Testing      — TestClock + Test-Utilities
```

---

## ValkarnTask-Struct

```csharp
[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask
{
    internal readonly ValkarnTask.ISource source;
    internal readonly ulong token; // gepackt: obere 32 Bits = Generation, untere 32 = Slot-Index
}

[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
{
    internal readonly ValkarnTask.ISource<T> source;
    internal readonly T result;  // inline auf dem synchronen Schnellpfad
    internal readonly ulong token;
}
```

**Schlüsselinvariante:** `source == null` bedeutet, dass der Task synchron ohne Fehler abgeschlossen hat — kein Heap-Objekt beteiligt. `ValkarnTask.CompletedTask` ist `default(ValkarnTask)`; `ValkarnTask.FromResult(value)` speichert das Ergebnis inline.

### Generationales Token

```csharp
// Packen
ulong token = ((ulong)generation << 32) | slotIndex;

// Entpacken
uint slotIndex  = (uint)(token & 0xFFFFFFFF);
uint generation = (uint)(token >> 32);
```

Bei jedem ISource-Aufruf validiert: `slots[slotIndex].generation == expectedGeneration`. Eine veraltete Referenz auf einen recycelten Pool-Slot wirft sofort `InvalidOperationException`. 4 Milliarden Generationen pro Slot — eine Kollision ist in der Praxis unmöglich. (UniTask verwendet `short` — Kollision nach ~18 Minuten.)

---

## ISource-Vertrag

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

Jedes Objekt, das `ISource` implementiert, kann einen `ValkarnTask` unterstützen. Eingebaute Implementierungen:

| Typ | Zweck |
|------|---------|
| `AsyncValkarnRunner<TStateMachine>` | Unterstützt jede `async ValkarnTask`-Methode |
| `AsyncValkarnRunner<TStateMachine, T>` | Unterstützt jede `async ValkarnTask<T>`-Methode |
| `ValkarnTask.PooledPromise[<T>]` | Manuelle Vervollständigung, automatische Pool-Rückgabe |
| `ValkarnTask.Promise[<T>]` | Manuelle Vervollständigung, nicht gepooled (langlebig) |
| `ExceptionSource` | Unterstützt `FromException` |
| `CanceledSource` | Unterstützt `FromCanceled` |
| `NeverSource` | Singleton — wechselt nie aus Pending |

---

## Async-Methoden-Builder

Der C#-Compiler steuert das Builder-Protokoll für benutzerdefinierte async-Rückgabetypen:

```
Create()
  └─ gibt Struct-Builder zurück (null Allokation)

Start(ref stateMachine)
  └─ führt State Machine synchron aus
     ├─ schließt ohne Suspendierung ab → SetResult(), Runner bleibt null
     │  └─ Task gibt default(ValkarnTask) zurück  ← null Allokation
     └─ trifft auf unvollständiges Await → AwaitUnsafeOnCompleted()
           └─ leiht AsyncValkarnRunner aus Pool
              kopiert State Machine in Runner (per Wert, kein Boxing)
              registriert Fortsetzung auf Awaitable
              └─ Task umschließt Runner als ISource  ← async-Pfad
```

Der Builder selbst ist ein `struct` — keine Allokation nur für den Builder. Der `runner` wird **lazy** allokiert: wenn eine Methode synchron abschließt, findet nie ein Pool-Leihen statt.

---

## State-Machine-Runner und Pool

`AsyncValkarnRunner<TStateMachine>` speichert die vom Compiler generierte State Machine **per Wert** (kein Boxing) und fungiert als `ISource`. Er wird bei der ersten Suspendierung aus `ValkarnPool<T>` geliehen und bei `GetResult` zurückgegeben.

Da `TStateMachine` ein eindeutiger Typ pro async-Methode ist (pro geschlossener generischer Instanziierung), erhält jede async-Methode über die generische Spezialisierung von C# automatisch ihren eigenen Pool.

### ValkarnPool\<T\>

| Kontext | Struktur | Grund |
|---------|-----------|--------|
| Unity-Haupt-Thread | Einzel-Thread-Stack | Keine Synchronisation — schnellstmöglich |
| Hintergrund-Threads | Treiber lock-freier Stack | CAS-Operationen, keine Locks |

Thread-Kontext wird über `Thread.CurrentThread.IsBackground` erkannt. Pool-Form (Kapazität, Trimmrate) wird über `ValkarnTaskSettings` konfiguriert.

---

## ValkarnCompletionCore\<T\>

Gemeinsamer Zustand innerhalb jeder `ISource`-Implementierung:

- Aktueller `Status` (Pending / Succeeded / Faulted / Canceled)
- Ergebniswert (generische Quellen)
- Ausnahme oder `OperationCanceledException` (Fehlerpfade)
- Registrierter Fortsetzungs-Delegate + Zustand

Statusübergänge verwenden `Interlocked.CompareExchange` — lock-frei, thread-safe. Eine **Doppelt-Abschluss-Schutzvorrichtung** stellt sicher, dass nur der erste `TrySet*`-Aufruf gewinnt; nachfolgende Aufrufe sind stille No-Ops.

---

## PlayerLoop-Integration

`PlayerLoopHelper` fügt beim Start leichtgewichtige Runner-Callbacks in Unitys PlayerLoop ein (`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]`).

Jeder `PlayerLoopTiming`-Wert entspricht einer Phase. Wenn `await ValkarnTask.Yield(timing)` aufgerufen wird, wird die Fortsetzung in den Runner dieser Phase eingereiht und beim nächsten Erreichen durch Unity ausgeliefert.

```
Initialization  →  EarlyUpdate  →  FixedUpdate  →  PreUpdate
→  Update  →  PreLateUpdate  →  PostLateUpdate  →  TimeUpdate
  (+ Last*-Varianten für jede Phase)
```

---

## Source-Generator

Der Roslyn-Source-Generator läuft zur Compilezeit. Für jede `partial`-Klasse, die `MonoBehaviour` mit `async ValkarnTask`-Methoden erweitert, generiert er eine partielle Klassendatei, die:

1. Das Feld `_valkarnCancelToken` deklariert
2. Es in `Awake` aus `destroyCancellationToken` zuweist
3. Jede async-Methode umschließt, um das Token automatisch durchzureichen

Die generierte Datei wird nie im Debugger angezeigt und modifiziert nie den Benutzerquellcode.

Der Generator erzeugt außerdem:

- **Awaitable-Brückenadapter** — wenn `Awaitable` innerhalb von `async ValkarnTask` awaitet wird
- **Async-Job-Wrapper** — wenn `IJob`- / `IJobParallelFor`-Typen erkannt werden
- **Kombinator-Pools** — typisierte `WhenAll`/`WhenAny`-Quellen für 2–8-Aritäts-Tupel

---

## Roslyn-Analyzer

17 `DiagnosticAnalyzer`-Regeln werden in `Analyzers/netstandard2.0/` ausgeliefert. Sie laufen während des C#-Compiler-Durchlaufs im Unity-Editor und auf CI:

- Alle verwenden `SemanticModel` für die Typauflösung (kein String-Matching)
- Das gemeinsame Hilfsprogramm `ValkarnTypeHelper` erkennt jede `ValkarnTask`-Variante
- Der Zombie-Loop-Analyzer überspringt korrekt verschachtelte lokale Funktionen und Lambdas
- Migrations-Analyzer (MIG001–MIG015) aktivieren sich automatisch nur, wenn UniTask / Awaitable referenziert werden — andernfalls inaktiv

---

## Burst- und ECS-Schicht

Drei optionale Module, jedes durch `#if`-Direktiven geschützt:

| Modul | Erfordert | Zweck |
|--------|----------|---------|
| `JobBridge` | `Unity.Jobs` | Umschließt `JobHandle` als Awaitable; fragt `handle.IsCompleted` bei jedem PlayerLoop-Tick ab |
| `AsyncSystemBase` | `Unity.Entities` | ECS-System-Basisklasse mit Async-Unterstützung |
| `BurstScheduler` | `Unity.Burst` + `Unity.Collections` | Plant Burst-Jobs aus async-Kontext; verwaltet `NativeTimerHeap` |

`NativeTimerHeap` ist ein Burst-kompatibler Min-Heap für hochpräzise Timer, der Allokationen auf dem managed Heap vollständig vermeidet.

---

## Editor-Integration

Der **Valkarn Hub** (`Tools → Valkarn → Hub`) verwendet `TypeCache.GetTypesDerivedFrom<IValkarnEditorPanel>()`, um alle installierten Valkarn-Pakete automatisch zu entdecken. Keine manuelle Registrierung erforderlich.

Das `TasksTrackerPanel` abonniert `EditorApplication.update`, um Pool-Diagnosen alle 0,5 s (konfigurierbar) zu aktualisieren, und stellt die `ValkarnTaskSettings`-Asset-Referenz für schnellen Zugriff bereit.

---

## IL2CPP-Überlegungen

- State Machines werden **per Wert** in Runnern gespeichert — kein Boxing, IL2CPP verarbeitet dies korrekt
- Der Runner jeder async-Methode ist eine separate generische Spezialisierung — typsicher, keine Kreuzkontamination
- Awaiter-Structs implementieren `ICriticalNotifyCompletion` — der Compiler ruft `UnsafeOnCompleted` auf und überspringt dabei die `ExecutionContext`-Erfassung (kein Overhead in Unitys Standardkonfiguration)
- Wenn aggressives Stripping aktiviert ist, das Runtime-Assembly erhalten:

```xml
<!-- link.xml -->
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```
