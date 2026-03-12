---
sidebar_position: 1
title: Burst & ECS-Integration
---

# Burst & ECS-Integration

Valkarn Tasks enthält optionale Integration mit Unitys Burst-Compiler, Unity Collections und dem Entities (ECS)-Paket. Diese gesamte Funktionalität wird bedingt kompiliert — sie ist nur aktiv, wenn die erforderlichen Pakete vorhanden und die entsprechenden Scripting-Define-Symbole gesetzt sind.

## Voraussetzungen

| Funktion | Erforderliches Paket | Scripting-Define |
|---|---|---|
| `NativeTimerHeap`, `NativeScheduler`, `BurstSchedulerRunner` | Unity Burst 1.8+, Unity Collections 2.0+ | `VTASKS_HAS_BURST` und `VTASKS_HAS_COLLECTIONS` |
| `AsyncSystemUtilities` | Unity Entities 1.0+ | `VTASKS_HAS_ENTITIES` |

Alle Burst/ECS-Quelldateien sind in `#if`-Wächtern eingewickelt, die diesen Defines entsprechen. Nichts in diesen Dateien wird kompiliert oder gelinkt, es sei denn, die Defines sind vorhanden.

### Einrichtung

1. Installieren Sie die erforderlichen Pakete über den Unity Package Manager:
   - `com.unity.burst` 1.8 oder neuer
   - `com.unity.collections` 2.0 oder neuer
   - `com.unity.entities` 1.0 oder neuer (nur für ECS-Utilities)

2. Fügen Sie die Scripting-Define-Symbole zu **Project Settings > Player > Scripting Define Symbols** hinzu:
   ```
   VTASKS_HAS_BURST;VTASKS_HAS_COLLECTIONS;VTASKS_HAS_ENTITIES
   ```
   Sie müssen nur die Defines für die installierten Pakete hinzufügen.

---

## NativeTimerHeap

**Namespace:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Wächter:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeTimerHeap` ist ein Burst-kompatibler binärer Min-Heap zur Timer-Planung. Er speichert `TimerEntry`-Werte, geordnet nach Deadline, und bietet O(log n) Einfügen und O(log n) Entfernen pro abgelaufenem Timer.

### Schlüsseltypen

```csharp
// Im Heap gespeicherter Eintrag. Nach Deadline geordnet.
public struct TimerEntry : IComparable<TimerEntry>
{
    public long Deadline;
    public int  Id;
}

public struct NativeTimerHeap : IDisposable
{
    public bool IsCreated { get; }
    public int  Count     { get; }

    // Erstellt den Heap. Verwenden Sie Allocator.Persistent für langlebige Heaps.
    public NativeTimerHeap(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // Fügt einen neuen Timer ein. Gibt die Timer-ID zurück (zur Identifikation des Callbacks).
    // Die Deadline und der an DrainExpired übergebene Wert müssen dieselbe Einheit verwenden
    // (BurstSchedulerRunner verwendet DateTime-Ticks via Time.realtimeSinceStartupAsDouble).
    [BurstCompile]
    public int Schedule(long deadline);

    // Entfernt und hängt IDs aller Timer an, deren Deadline <= currentTimestamp ist.
    // Gibt die Anzahl der abgeschöpften Timer zurück.
    [BurstCompile]
    public int DrainExpired(long currentTimestamp, NativeList<int> expiredIds);

    public void Dispose();
}
```

`NativeTimerHeap` ist ein unverwaltetes Struct. Es kann keine verwalteten Delegates speichern — IDs werden im `BurstSchedulerRunner`-Dictionary auf dem Haupt-Thread mit verwalteten Callbacks abgeglichen.

---

## NativeScheduler

**Namespace:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Wächter:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeScheduler` ist eine Burst-kompatible Arbeitswarteschlange, die durch `NativeQueue<ScheduledWork>` unterstützt wird. Burst-kompilierte Jobs stellen Arbeitselemente in die Warteschlange; der Haupt-Thread schöpft sie jeden Frame ab.

### Schlüsseltypen

```csharp
public enum WorkType : byte
{
    TimerExpired  = 0,
    JobCompleted  = 1,
    Custom        = 2
}

public struct ScheduledWork
{
    public int      Id;
    public WorkType Type;
    public long     Payload;
}

public struct NativeScheduler : IDisposable
{
    public bool IsCreated { get; }

    public NativeScheduler(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // Reiht ein Arbeitselement aus einem Burst-kompilierten Job ein.
    [BurstCompile]
    public void Enqueue(ScheduledWork work);

    // Schöpft alle ausstehenden Arbeiten in `results` ab. Nur vom Haupt-Thread aufrufen.
    // Gibt die Anzahl der abgeschöpften Elemente zurück.
    public int Drain(NativeList<ScheduledWork> results);

    // Gibt einen parallelen Schreiber zurück, der in IJobParallelFor-Jobs verwendet werden kann.
    public NativeQueue<ScheduledWork>.ParallelWriter AsParallelWriter();

    public void Dispose();
}
```

Die Warteschlange ist der Übergang zwischen der Burst-Welt und der verwalteten Welt. `Enqueue` ist Burst-aufrufbar; `Drain` ist nur für den Haupt-Thread.

---

## BurstSchedulerRunner

**Namespace:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Wächter:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`BurstSchedulerRunner` ist die verwaltete Brücke zwischen dem nativen Scheduler/Timer-Heap und dem Rest Ihres Spiels. Es implementiert `IPlayerLoopItem`, sodass Unity `MoveNext()` einmal pro Frame zum registrierten Zeitpunkt aufruft. Jeden Frame:

1. Schöpft `NativeScheduler` ab und löst alle registrierten verwalteten Callbacks aus, die nach Arbeits-ID abgeglichen werden.
2. Schöpft `NativeTimerHeap` für abgelaufene Timer ab und löst deren verwaltete Callbacks aus.

Ausnahmen, die von Callbacks geworfen werden, werden an `VlkTask.PublishUnobservedException` weitergeleitet, anstatt sich durch den PlayerLoop zu propagieren.

### API

```csharp
public sealed class BurstSchedulerRunner : IPlayerLoopItem, IDisposable
{
    // Erstellt einen Runner und registriert ihn auf dem PlayerLoop. Gibt die Instanz zurück.
    // Den zurückgegebenen Runner entsorgen, wenn er nicht mehr benötigt wird.
    public static BurstSchedulerRunner Create(
        PlayerLoopTiming timing       = PlayerLoopTiming.Update,
        int initialCapacity           = 64);

    // Direkter Zugriff auf den NativeScheduler zur Einreihung aus Burst-Jobs.
    public NativeScheduler Scheduler { get; }

    // Plant einen verwalteten Timer-Callback. Muss vom Haupt-Thread aufgerufen werden.
    // Gibt eine Timer-ID zurück (nur zur Identifikation; es gibt keine Abbruch-API).
    public int ScheduleTimer(TimeSpan delay, Action callback);

    // Verknüpft einen verwalteten Callback mit einer Arbeits-ID, die von einem Burst-Job eingereiht wurde.
    // Muss vom Haupt-Thread aufgerufen werden, vor oder während des Frames, in dem der Job abschließt.
    public void RegisterCallback(int workId, Action callback);

    // Entsorgt alle nativen Container und hebt die Registrierung beim PlayerLoop auf.
    public void Dispose();
}
```

### Wie sich BurstSchedulerRunner vom Standard-Scheduler unterscheidet

Der Standard-Valkarn Tasks-Scheduler integriert sich direkt mit `async/await`-Zustandsmaschinen und verwaltet die Fortsetzungs-Dispatch über `PlayerLoopHelper`. `BurstSchedulerRunner` fügt eine separate Spur speziell für die Signalisierung aus Burst-kompilierten Jobs hinzu:

| | Standard-Scheduler | BurstSchedulerRunner |
|---|---|---|
| Fortsetzungstyp | Verwaltetes `Action` via `ISource` | Verwaltetes `Action`, per ID registriert |
| Signal-Quelle | C#-Awaiter-Muster | Unverwaltetes `NativeScheduler.Enqueue` |
| Timer-Quelle | `VlkTask.Delay` (verwaltet) | `NativeTimerHeap` (unverwaltet) |
| Thread-Sicherheit | Haupt-Thread-Fortsetzungen | Enqueue ist Burst-sicher; Drain ist nur Haupt-Thread |

### Verwendungsmuster

```csharp
// 1. Runner einmal erstellen (z. B. in einem Bootstrap-MonoBehaviour oder ISystem.OnCreate).
var runner = BurstSchedulerRunner.Create(PlayerLoopTiming.Update, initialCapacity: 128);

// 2. Timer-Callback planen (Haupt-Thread).
runner.ScheduleTimer(TimeSpan.FromSeconds(2.0), () =>
{
    Debug.Log("Zwei Sekunden verstrichen (unverwalteter Timer).");
});

// 3. Aus einem Burst-Job ein Arbeitselement einreihen.
//    NativeScheduler.ParallelWriter ist sicher für IJobParallelFor.
var writer = runner.Scheduler.AsParallelWriter();
// Innerhalb Execute(int index):
writer.Enqueue(new ScheduledWork { Id = myWorkId, Type = WorkType.Custom });

// 4. Verwalteten Callback auf dem Haupt-Thread registrieren, bevor der Job abschließt.
runner.RegisterCallback(myWorkId, () =>
{
    Debug.Log($"Job-Abschluss-Signal für Arbeit {myWorkId} empfangen.");
});

// 5. Runner entsorgen, wenn fertig (z. B. OnDestroy oder Domain-Reload).
runner.Dispose();
```

---

## AsyncSystemUtilities

**Namespace:** `UnaPartidaMas.Valkarn.Tasks.ECS`
**Wächter:** `#if VTASKS_HAS_ENTITIES`

`AsyncSystemUtilities` bietet zwei Erweiterungs-Hilfsmethoden für das Schreiben asynchroner ECS-Systeme.

### GetWorldCancellationToken

```csharp
public static CancellationToken GetWorldCancellationToken(
    this World world,
    PlayerLoopTiming timing = PlayerLoopTiming.Update)
```

Gibt ein `CancellationToken` zurück, das automatisch abgebrochen wird, wenn die angegebene `World` zerstört wird. Intern startet es ein Fire-and-Forget-`VlkTask`, das jeden Frame `VlkTask.Yield(timing)` aufruft, während `world.IsCreated` wahr ist, dann eine `CancellationTokenSource` abbricht, wenn die Schleife endet.

Wenn die World bereits zerstört ist, wenn Sie diese Methode aufrufen, gibt sie ein Token zurück, das bereits im abgebrochenen Zustand ist.

Übergeben Sie dieses Token an jede asynchrone Methode, die Sie aus einem System starten, damit laufende Arbeit automatisch gestoppt wird, wenn die World verschwindet.

### SafeEntityExists

```csharp
public static bool SafeEntityExists(this EntityManager entityManager, Entity entity)
```

Ruft `entityManager.Exists(entity)` auf und gibt `false` zurück, wenn eine `ObjectDisposedException` geworfen wird. Dies kann passieren, wenn der `EntityManager` nach der Entsorgung der World zugegriffen wird, was eine echte Race-Condition in asynchronem Code ist, der über Frame-Grenzen hinweg überlebt.

Verwenden Sie dies nach jedem `await`-Punkt, bevor Sie in eine Entity zurückschreiben.

---

## Funktionsfähiges Beispiel: Asynchrones ECS-System

Das folgende Beispiel stammt aus `Samples~/ECS/AsyncLoadSystem.cs`. Es demonstriert das kanonische Muster für einmalige asynchrone Initialisierung aus einem `ISystem`.

```csharp
#if VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Entities;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.ECS;

public partial struct AsyncLoadSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // Ein Abbruch-Token erhalten, das an die Lebensdauer dieser World gebunden ist.
        // Wenn die World zerstört wird, wird alle asynchrone Arbeit, die mit diesem Token gestartet wurde,
        // automatisch abgebrochen.
        var worldCt = state.World.GetWorldCancellationToken();

        // Die asynchrone Initialisierung starten und die Task vergessen.
        // Forget() leitet alle unbehandelten Ausnahmen an VlkTask.PublishUnobservedException weiter.
        InitializeAsync(state.WorldUnmanaged, worldCt).Forget();
    }

    public void OnUpdate(ref SystemState state) { }
    public void OnDestroy(ref SystemState state) { }

    static async VlkTask InitializeAsync(WorldUnmanaged world, CancellationToken ct)
    {
        // Phase 1: Daten auf einem Hintergrund-Thread laden.
        // RunOnThreadPool wechselt zu einem Worker-Thread, führt das Delegate aus
        // und kehrt automatisch zum Haupt-Thread zurück.
        var configData = await VlkTask.RunOnThreadPool(
            () => LoadFromDisk(),
            cancellationToken: ct);

        // Phase 2: Ergebnisse auf dem Haupt-Thread anwenden.
        // Abbruch prüfen, falls die World während des Ladens zerstört wurde.
        ct.ThrowIfCancellationRequested();
        ApplyConfiguration(world, configData);
    }

    static ConfigData LoadFromDisk()
    {
        // Nur reines C# — hier sind keine Unity- oder ECS-API-Aufrufe erlaubt.
        return new ConfigData { MaxEnemies = 100, SpawnRadius = 50f };
    }

    static void ApplyConfiguration(WorldUnmanaged world, ConfigData data)
    {
        // Sicher: wir sind auf dem Haupt-Thread.
        UnityEngine.Debug.Log($"Konfiguration geladen: MaxEnemies={data.MaxEnemies}");
    }

    struct ConfigData
    {
        public int   MaxEnemies;
        public float SpawnRadius;
    }
}
#endif
```

Das KI-Drosselungs-Beispiel (`Samples~/ECS/AISystemExample.cs`) baut auf diesem Muster auf und fügt `AsyncThrottle` hinzu, um zu begrenzen, wie viele gleichzeitige asynchrone Tasks aktiv sind. Weitere Details zu diesem Muster finden Sie in der Drosselungs-Dokumentation.

---

## Einschränkungen

Die folgenden Einschränkungen gelten für den gesamten Burst/ECS-asynchronen Code. Deren Verletzung führt zu Editor-Fehlern, Job-Sicherheitsverletzungen oder stiller Datenbeschädigung.

### Innerhalb von Burst-kompiliertem Code

- **Keine verwalteten Typen.** Burst kann Code nicht kompilieren, der verwaltete Objekte (Klassen, Delegates, Arrays, Strings, `List<T>` usw.) alloziert, darauf zugreift oder referenziert. Nur blittable Structs und native Container sind erlaubt.
- **Keine Ausnahmen.** Burst unterstützt kein `try`/`catch`/`throw`. Verwenden Sie Rückgabecodes oder Flags, um Fehler zu kommunizieren.
- **Kein `async`/`await`.** C#-async-Zustandsmaschinen sind verwaltet und können nicht von Burst kompiliert werden. `NativeScheduler` und `NativeTimerHeap` bieten einen Seitenkanal für die Signalisierung verwalteter Fortsetzungen, aber die Fortsetzungen selbst laufen auf dem Haupt-Thread.
- **Kein statisch veränderbarer verwalteter Zustand.** Burst-Jobs können statische schreibgeschützte Felder lesen, dürfen aber nicht in verwaltete Statiken schreiben.

### Über await-Punkte in ECS-Systemen hinweg

- **Entity-Lebensdauer.** Entities können zerstört werden, während eine asynchrone Methode suspendiert ist. Rufen Sie immer `entityManager.SafeEntityExists(entity)` nach jedem `await` auf, bevor Sie zurückschreiben.
- **ComponentLookup-Veraltung.** `ComponentLookup`, `RefRW` und andere Chunk-Zeiger-Typen werden nach Strukturänderungen ungültig, die in jedem Frame auftreten können. Cachen Sie diese nicht über `await`-Punkte hinweg. Erwerben Sie sie von `SystemState` nach der Fortsetzung neu, oder verwenden Sie direkt `EntityManager`.
- **`ref`-Parameter.** Asynchrone Methoden können keine `ref`-, `in`- oder `out`-Parameter haben (C#-Fehler CS1988). Extrahieren Sie alle ECS-Daten synchron in der synchronen `OnUpdate`-Methode und übergeben Sie sie als Wert an die asynchrone Methode.
- **`SystemAPI` in asynchronen Methoden.** `SystemAPI` ist quellgeneriert und funktioniert nur innerhalb partieller `ISystem`-Methoden. Es ist in `async`-Methoden nicht verfügbar. Führen Sie alle `SystemAPI`-Abfragen vor dem ersten `await` durch.
- **Thread-Sicherheit.** `EntityManager`, `ComponentLookup` und Strukturänderungen sind nur für den Haupt-Thread. Verwenden Sie `VlkTask.RunOnThreadPool` nur für reine C#-Berechnungen ohne Unity- oder ECS-API-Aufrufe.
