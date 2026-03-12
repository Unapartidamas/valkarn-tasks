---
sidebar_position: 6
title: Warum Valkarn Tasks
---

# Warum Valkarn Tasks

> Zero-Allocation Async/Await für Unity. Schneller als UniTask. Intelligenter als Awaitable.

---

## Das Problem: Async in Unity ist kaputt

Unity-Entwickler benötigen asynchrone Operationen überall: Szenen laden, Assets herunterladen, auf Animationen warten, Spawns verzögern, mit Servern kommunizieren. Die heutigen Optionen sind:

| Option | Problem |
|--------|---------|
| **Coroutines** | Keine Rückgabewerte, keine Fehlerbehandlung, keine Stornierung, keine Komposition |
| **System.Threading.Tasks** | Allokiert bei jedem `async`-Aufruf (~144–232 Bytes), löst GC aus, keine Unity-Lifecycle-Unterstützung |
| **UniTask** | Gut — aber: Token-Kollision nach 18 Minuten, keine Compile-Zeit-Diagnosen, Fehlerberichte abhängig von Finalizern |
| **Unity Awaitable** (2023+) | Klassenbasiert (allokiert), keine Kombinatoren, keine Kanäle, kein Test-Support |

Valkarn Tasks löst all diese Probleme in einem einzigen, source-generierten, allokationsfreien Paket.

---

## Null Allokationen — warum jedes Byte zählt

### Was Allokationen in Spielen bedeuten

Jeder `new object()`-, `new List<T>()`- oder `async Task`-Aufruf allokiert auf dem **managed Heap**, der vom Garbage Collector verfolgt wird. Unity verwendet den Boehm-GC, der zwei kritische Probleme hat:

1. **Stop-the-World-Pausen** — wenn der GC läuft, friert das Spiel ein. Eine 2-ms-Pause bei 60 fps kostet 12% des Frame-Budgets; bei 120 fps (VR) kostet sie 24%.
2. **Unvorhersehbares Timing** — der GC kann während eines Boss-Kampfs, einer Zwischensequenz oder eines kompetitiven Matches ausgelöst werden.

### Was Valkarn Tasks anders macht

| Szenario | `System.Task` | UniTask | **Valkarn Tasks** |
|----------|--------------|---------|------------------|
| `async Method()` — schließt synchron ab | 144 Bytes | **0 Bytes** | **0 Bytes** |
| `async Method()` — suspendiert einmal | 232+ Bytes | 0 Bytes (gepooled) | **0 Bytes (gepooled)** |
| `WhenAll(a, b)` | 232 Bytes | 0 Bytes (gepooled) | **0 Bytes (source-gen gepooled)** |
| `WhenAny(a, b)` | 144 Bytes | 72 Bytes | **0 Bytes** |
| Promise (manuelle Vervollständigung) | 144 Bytes | 104 Bytes | **88 Bytes** |
| Gepoolte Promise (wiederverwendbar) | 144 Bytes | 0 Bytes | **0 Bytes** |

In einem typischen Spiele-Frame mit 50–100 asynchronen Operationen erzeugt `System.Task` 7–23 KB Garbage. Valkarn Tasks erzeugt **null**.

### Was das für Ihr Spiel bedeutet

- **Kein GC-Ruckeln** — stabiles Framerate ohne Aussetzer durch asynchrone Operationen
- **VR-sicher** — 90/120-fps-Ziele ohne GC-Spitzen
- **Mobilgeräte-freundlich** — weniger Speicherdruck auf Geräten mit begrenztem RAM
- **Konsolenzertifiziert** — vorhersehbares Speicherverhalten hilft bei der Zertifizierung

---

## Performance — gegen die Besten verglichen

Benchmarks: BenchmarkDotNet v0.14.0, .NET 9.0, Intel Core i7-10875H.

### Core Async/Await — 2× schneller als ValueTask

| Benchmark | ValueTask | UniTask | **Valkarn Tasks** | vs ValueTask |
|-----------|-----------|---------|------------------|--------------|
| 100 Tasks in Masse | 956 ns | 508 ns | **489 ns** | **1.95×** |
| 1.000 Tasks in Masse | 9.697 ns | 5.016 ns | **4.728 ns** | **2.05×** |
| Mit CancellationToken | 38,8 ns | 36,2 ns | **29,6 ns** | **1.31×** |
| Ausnahmebehandlung | 10.399 ns | 8.247 ns | **9.248 ns** | **1.12×** |

Alle Pfade: **0 Bytes allokiert**.

### Kombinatoren — bis zu 9,6× schneller als Task

| Benchmark | Task | UniTask | **Valkarn Tasks** | vs Task |
|-----------|------|---------|------------------|---------|
| WhenAll (2 Tasks) | 117 ns / 232B | 13,6 ns / 0B | **12,1 ns / 0B** | **9.6×** |
| WhenAll (5 Tasks) | 156 ns / 272B | 25,1 ns / 0B | **25,3 ns / 0B** | **6.2×** |
| WhenAny (2 Tasks) | 39,0 ns / 144B | 60,0 ns / 72B | **11,6 ns / 0B** | **3.4×** |
| Gepoolte Promise | 59,5 ns / 144B | 53,6 ns / 0B | **38,3 ns / 0B** | **1.55×** |

WhenAny ist **5,2× schneller als UniTask** und allokiert null Bytes.

### Objekt-Pool — 4,3 Nanosekunden

| Operation | Zeit | Allokation |
|-----------|------|------------|
| Haupt-Thread Fast-Slot Leihen + Zurückgeben | **4,3 ns** | 0 Bytes |
| Thread-übergreifender Treiber-Stack | ~15 ns | 0 Bytes |

Null atomare Operationen im Haupt-Thread — kritisch für IL2CPP, wo `Volatile.Read` 9,2× langsamer ist.

### Auswirkungen in der Praxis (50 asynchrone Ops/Frame bei 60 fps)

| Bibliothek | Zeit/Frame | Frame-Budget | GC/Sekunde |
|---------|-----------|-------------|-----------|
| System.Task | ~48 µs | 0,29% | ~430 KB/s |
| UniTask | ~25 µs | 0,15% | ~3,6 KB/s |
| **Valkarn Tasks** | **~24 µs** | **0,14%** | **0 KB/s** |

In 10 Minuten erzeugt `System.Task` **~258 MB** async-Garbage. Valkarn Tasks erzeugt **null**.

---

## Funktionen, die keine andere Bibliothek hat

### Automatische Lifecycle-Stornierung

```csharp
public partial class EnemySpawner : MonoBehaviour
{
    async ValkarnTask Start()
    {
        while (true)
        {
            await ValkarnTask.Delay(3000);
            SpawnWave();
        }
        // Automatisch storniert, wenn dieses GameObject zerstört wird.
        // Kein CancellationToken. Keine Speicherlecks. Keine Zombie-Tasks.
    }
}
```

Keine `MissingReferenceException` mehr durch async-Methoden, die nach der Zerstörung laufen. Keine manuelle `OnDestroy`-Bereinigung. Keine vergessenen `CancellationTokenSource`-Entsorgungen.

### Kritische Abschnitte

```csharp
async ValkarnTask SaveProgress()
{
    var data = await LoadPlayerData();       // stornierbar

    await using (ValkarnTask.Critical())
    {
        await db.Insert(data);              // wird auch abgeschlossen, wenn GO zerstört wird
        await db.Commit();
    }                                       // ausstehende Stornierung wird jetzt angewendet

    await SendNotification();               // wieder stornierbar
}
```

Datenbankschreibvorgänge, Netzwerkanfragen und Dateispeicherungen werden abgeschlossen, auch wenn der Spieler das Spiel verlässt oder eine Szene entladen wird. Keine korrupten Spielstände. Keine halbgeschriebenen Analysen. Keine verlorenen Belege.

### Compile-Zeit-Diagnosen

| Diagnose | Was sie erkennt |
|------------|----------------|
| TT001 | Doppeltes Await auf einem ValkarnTask (Use-after-free-Bug) |
| TT002 | Vergessen, auf einen Task zu warten (stiller Fehler) |
| TT012 | Async-Schleifen ohne Stornierungsprüfung (Zombie-Schleifen) |
| TT013 | Zurückgegebener, aber nicht erwarteter Task (Fire-and-forget-Bug) |
| TT016 | Async-Methode ohne Await (unnötiger Overhead) |
| TT017 | `[FireAndForget]` auf `ValkarnTask<T>` (Ergebnis wird verworfen) |

Bugs werden in der IDE als rote Unterstreichungen erkannt — nicht als Laufzeit-Abstürze 20 Minuten nach Testbeginn.

### Result\<T\> — Fehlerbehandlung ohne try/catch

```csharp
var result = await loadTask.AsResult();

if (result.Succeeded)       Use(result.Value);
else if (result.IsCanceled) ShowRetryDialog();
else                         Debug.LogError(result.Error);
```

Jeder Fehlerpfad ist explizit. Keine verschluckten Ausnahmen. Keine fehlenden Handler.

### Kanäle mit Gegendruck

```csharp
var channel = ValkarnTask.Channel.CreateBounded<EnemySpawnRequest>(capacity: 10);

// Produzent (Spiellogik)
await channel.Writer.WriteAsync(new EnemySpawnRequest { type = EnemyType.Dragon });

// Konsument (Spawn-System)
await foreach (var request in channel.Reader.ReadAllAsync())
    await SpawnEnemy(request);
```

Entkoppelt Systeme sauber. Begrenzt die Spawn-Rate. Stellt Netzwerknachrichten in eine Warteschlange. Puffert Eingabeereignisse. Der Produzent verlangsamt sich, wenn der Konsument nicht mithalten kann — verhindert Speicherspitzen.

### Deterministische Tests mit TestClock

```csharp
[Test]
public void Respawn_WaitsThreeSeconds()
{
    var clock = new TestClock();
    var task = spawner.RespawnEnemy();

    clock.Advance(TimeSpan.FromSeconds(2));
    Assert.IsFalse(task.IsCompleted);

    clock.Advance(TimeSpan.FromSeconds(1));
    Assert.IsTrue(task.IsCompleted);
}
```

Testen Sie zeitabhängige Logik sofort. Kein `yield return new WaitForSeconds(3)` in Tests. Kein instabiles CI-Timing.

### Generationales Token-Sicherheit

UniTask verwendet ein `short`-Token (16-Bit). Nach 65.536 Pool-Zyklen (~18 Minuten aktiver asynchroner Arbeit) liest eine veraltete Referenz stillschweigend das Ergebnis eines anderen Tasks — ein Use-after-free-Bug, der praktisch unmöglich zu reproduzieren ist.

Valkarn Tasks verwendet einen `uint`-Generationszähler pro Pool-Slot: **4.294.967.296 Zyklen pro Slot** vor einer Kollision. In jedem realistischen Szenario unmöglich.

---

## Migration in Minuten, nicht Wochen

### Von UniTask

```
Schritt 1: Valkarn Tasks installieren
Schritt 2: Gelbe Glühbirnen erscheinen bei UniTask-Verwendungen in der IDE
Schritt 3: Rechtsklick → "Alle Vorkommen in der Projektmappe korrigieren" (Strg+.)
Schritt 4: UniTask-Paketreferenz entfernen
```

**15 Migrations-Diagnosen** (MIG001–MIG015) decken jede UniTask-API automatisch ab. Ein typisches Projekt mit 500–2.000 asynchronen Methoden migriert in unter 5 Minuten. 95%+ vollständig automatisiert.

### Von Unity Awaitable

Gleiche Ein-Klick-Migration:
- `async Awaitable` → `async ValkarnTask`
- `Awaitable.NextFrameAsync()` → `ValkarnTask.NextFrame()`
- `Awaitable.BackgroundThreadAsync()` → `ValkarnTask.SwitchToThreadPool()`
- `Awaitable.MainThreadAsync()` → entfernt (Valkarn läuft standardmäßig im Haupt-Thread)

---

## Vergleichsmatrix

| Funktion | System.Task | UniTask | Awaitable | **Valkarn Tasks** |
|---------|------------|---------|-----------|------------------|
| Sync-Pfad ohne Allokation | Nein | Ja | Nein | **Ja** |
| Kombinatoren ohne Allokation | Nein | Nein | Nein | **Ja (Source Gen)** |
| Struct-basiert | Nein | Ja | Nein | **Ja** |
| Automatische Lifecycle-Stornierung | Nein | Manuell | Teilweise | **Automatisch** |
| Keine Geschwister-Stornierung | Nein | Nein | Nein | **Ja** |
| Kritische Abschnitte | Nein | Nein | Nein | **Ja** |
| Result\<T\> (kein Throw) | Nein | Teilweise | Nein | **Ja** |
| TestClock | Nein | Nein | Nein | **Ja** |
| Job-System-Brücke | Nein | Nein | Nein | **Ja** |
| Compile-Zeit-Diagnosen | Nein | Nein | Nein | **Ja (17 Regeln)** |
| Begrenzte Pools + Trimmen | Nein | Nein | N/A | **Ja** |
| Deterministischer Fehlerbericht | Nein | Nein (Finalizer) | Teilweise | **Ja (Pool-Rückgabe)** |
| Vollständige Kanäle | Ja (.NET) | Minimal | Nein | **Ja** |
| Awaitable-Brücke | N/A | Minimal | Nativ | **Transparent** |
| IL2CPP-optimiertes Pooling | Nein | Nein (Volatile bei jeder Op) | N/A | **Ja (null Atomics)** |
| Token-Kollisionssicherheit | N/A | 18 Min (short) | N/A | **Niemals (uint gen)** |
| Automatische Migration von UniTask | N/A | N/A | N/A | **Ja (15 Korrekturen)** |
| Automatische Migration von Awaitable | N/A | N/A | N/A | **Ja (8 Korrekturen)** |

---

**Ihr Spiel verdient ein Async, das nicht ruckelt.**
