---
sidebar_position: 4
title: Funktionen
---

# Funktionen

Vollständige Funktionsreferenz für Valkarn Tasks.

---

## Kern-Async-Primitiv

### ValkarnTask-Struct

Ein allokationsfreier, struct-basierter asynchroner Rückgabetyp, der sowohl `UniTask` als auch Unitys `Awaitable` ersetzt.

```csharp
async ValkarnTask LoadLevel() { ... }
async ValkarnTask<int> CountEnemies() { ... }
```

- **Allokationsfreier synchroner Schnellpfad** — wenn die Methode abschließt, ohne jemals zu suspendieren, finden keine Heap-Allokationen statt.
- **Gepoolter async-Pfad** — wenn die Methode suspendiert, wird der State-Machine-Runner aus einem begrenzten, verkleinerbaren Pool entnommen. Kein Boxing, begrenzte Pools mit automatischer Trimmung.
- **IL2CPP-first-Pooling** — Pool-Operationen im Haupt-Thread verwenden null Atomics. IL2CPP bestraft `Volatile` um den Faktor 9,2× und `Interlocked` um 2,9× gegenüber Mono; Valkarn vermeidet beides auf dem Hot Path.
- **Generationale Token-Sicherheit** — ein `uint`-Generationszähler pro Pool-Slot. 4.294.967.296 Zyklen pro Slot vor einer Kollision — in der Praxis unmöglich. (UniTask verwendet ein `short`-Token: Kollision nach ~18 Minuten aktiver async-Arbeit.)

### Result\<T\> — ausnahmefreie Fehlerbehandlung

```csharp
var result = await loadTask.AsResult();

if (result.Succeeded)    Use(result.Value);
else if (result.IsCanceled) ShowRetry();
else                         Debug.LogError(result.Error);
```

`Result<T>` und `Result` sind `readonly struct`-Werte, die das Ergebnis eines Tasks ohne Ausnahmen darstellen. Beide unterstützen implizite Konvertierung zu `bool` (true bei Erfolg).

### Manuelle Vervollständigungsquellen

```csharp
var promise = new ValkarnTask.Promise<string>();
promise.TrySetResult("done");
string value = await promise.Task;
```

Unterstützt `TrySetResult`, `TrySetException` und `TrySetCanceled`. Finalizer-basiertes Reporting nicht beobachteter Ausnahmen stellt sicher, dass Fehler niemals still verloren gehen.

### Gepoolte Vervollständigungsquellen

Auto-Reset gepoolte Varianten für wiederkehrende Muster (intern von Kanälen und Kombinatoren verwendet):

```csharp
var source = ValkarnTask.PooledPromise<int>.Create(out uint token);
source.TrySetResult(42);
int value = await source.Task; // die Quelle kehrt automatisch in den Pool zurück
```

---

## Awaitable-Brücke

Transparente Interoperabilität mit Unitys `Awaitable` — keine manuelle Konvertierung:

```csharp
async ValkarnTask LoadGame()
{
    await SceneManager.LoadSceneAsync("Level2"); // Unity Awaitable — funktioniert direkt
    await Resources.LoadAsync<Texture2D>("hero"); // Unity Awaitable — funktioniert direkt
    await ValkarnTask.Delay(1000);                // Valkarn nativ
}
```

Der Source-Generator erkennt `Awaitable`-Awaits und generiert den Adapter automatisch.

---

## Lifecycle-Stornierung

### Automatisch (source-generiert)

Markieren Sie die Klasse als `partial` — der Source-Generator übernimmt den Rest:

```csharp
public partial class EnemySpawner : MonoBehaviour
{
    async ValkarnTask Start()
    {
        while (true)
        {
            await ValkarnTask.Delay(3000);
            SpawnWave();
            // Automatisch storniert, wenn dieses GameObject zerstört wird.
        }
    }
}
```

- **MonoBehaviour** — gebunden an `destroyCancellationToken`
- **ScriptableObject** — gebunden an die Anwendungslebensdauer
- **Einfache Klasse** — keine automatische Bindung (manuelles Token erforderlich)

### Deaktivierung

```csharp
[NoAutoCancel]
async ValkarnTask BackgroundSync()
{
    await SyncToServer(); // wird bei Zerstörung nicht automatisch storniert
}
```

### Manueller CancellationToken-Override

Das Übergeben eines expliziten `CancellationToken` überschreibt das automatisch injizierte Lifecycle-Token:

```csharp
async ValkarnTask DoWork(CancellationToken ct)
{
    await ValkarnTask.Delay(1000, ct);
}
```

### Keine Geschwister-Stornierung

Tasks stornieren niemals Geschwister-Tasks. `WhenAll` wartet auf ALLE Tasks; `WhenAny` gibt das erste Ergebnis zurück, aber verlierende Tasks laufen weiter. Dies verhindert Datenbeschädigung, wenn Tasks Seiteneffekte haben.

---

## Kritische Abschnitte

Für Operationen, die nicht durch Lifecycle-Stornierung unterbrochen werden dürfen:

```csharp
async ValkarnTask SaveProgress()
{
    var data = await LoadPlayerData();        // stornierbar

    await using (ValkarnTask.Critical())
    {
        await db.Insert(data);               // wird NICHT storniert, auch wenn GO zerstört wird
        await db.Commit();
    }                                        // ausstehende Stornierung wird hier angewendet

    await SendNotification();                // wieder stornierbar
}
```

Innerhalb eines kritischen Abschnitts wird die Stornierung **aufgeschoben** — nicht ignoriert. Wenn der Abschnitt endet, wird die ausstehende Stornierung angewendet.

---

## Kombinatoren

### WhenAll (typisiert)

```csharp
// Direkt — wirft eine Ausnahme, wenn ein Task fehlschlägt
var (enemies, map) = await ValkarnTask.WhenAll(LoadEnemies(), LoadMap());

// Sicher — mit AsResult() für nicht-werdendes Verhalten
var (a, b) = await ValkarnTask.WhenAll(
    LoadEnemies().AsResult(), LoadMap().AsResult());
```

Allokationsfreier synchroner Schnellpfad, wenn alle Tasks bereits abgeschlossen sind. Die `IEnumerable<ValkarnTask<T>>`-Überladung verwendet `ArrayPool<T>` für interne Arrays.

### WhenAll (void)

```csharp
await ValkarnTask.WhenAll(SaveA(), SaveB(), SaveC());
await ValkarnTask.WhenAll(taskList); // IEnumerable<ValkarnTask>
```

### WhenAny

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(DownloadFromA(), DownloadFromB());
```

Gibt das erste abgeschlossene Ergebnis zurück. Verlierende Tasks laufen weiterhin normal.

### Fire-and-forget

```csharp
SendAnalytics("event").Forget();

[FireAndForget]
async ValkarnTask SendAnalytics(string eventName) { ... }
// Aufrufer brauchen kein .Forget() — keine Warnung wird generiert
```

`.Forget()` leitet Fehler an `ValkarnTask.UnobservedException` weiter. Niemals still verschluckt.

### AsNonGeneric

```csharp
ValkarnTask voidTask = typedTask.AsNonGeneric();
```

---

## Zeit und Verzögerung

```csharp
await ValkarnTask.Delay(1000);                                    // Millisekunden
await ValkarnTask.Delay(TimeSpan.FromSeconds(2));                  // TimeSpan
await ValkarnTask.Delay(1000, DelayType.UnscaledDeltaTime);       // Timescale ignorieren
await ValkarnTask.Delay(1000, DelayType.Realtime);                 // Stopwatch-basiert

await ValkarnTask.Yield();                                         // nächster PlayerLoop-Tick
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);             // spezifisches Timing
await ValkarnTask.NextFrame();                                      // garantiert nächstes Frame
await ValkarnTask.DelayFrame(5);                                    // N Frames

await ValkarnTask.WaitUntil(() => player.IsReady);
await ValkarnTask.WaitWhile(() => isLoading);
```

---

## Thread-Wechsel

```csharp
async ValkarnTask ProcessData()
{
    var raw = await DownloadData();

    await ValkarnTask.SwitchToThreadPool();
    var processed = HeavyComputation(raw);  // Hintergrund-Thread

    await ValkarnTask.SwitchToMainThread();
    ApplyToGameObject(processed);           // Haupt-Thread
}
```

---

## 16 PlayerLoop-Timings

| Gruppe | Timings |
|-------|---------|
| Initialization | `Initialization`, `LastInitialization` |
| EarlyUpdate | `EarlyUpdate`, `LastEarlyUpdate` |
| FixedUpdate | `FixedUpdate`, `LastFixedUpdate` |
| PreUpdate | `PreUpdate`, `LastPreUpdate` |
| Update | `Update`, `LastUpdate` |
| PreLateUpdate | `PreLateUpdate`, `LastPreLateUpdate` |
| PostLateUpdate | `PostLateUpdate`, `LastPostLateUpdate` |
| TimeUpdate | `TimeUpdate`, `LastTimeUpdate` |

Alle Operationen verwenden standardmäßig `PlayerLoopTiming.Update`, sofern nicht anders angegeben.

---

## Kanäle

```csharp
// Unbegrenzt
var channel = ValkarnTask.Channel.CreateUnbounded<EnemySpawnRequest>();

// Begrenzt — Gegendruck wenn voll
var channel = ValkarnTask.Channel.CreateBounded<LogEntry>(capacity: 100);

// Multi-Konsument
var channel = ValkarnTask.Channel.CreateUnbounded<WorkItem>(multiConsumer: true);

// Produzent
await channel.Writer.WriteAsync(entry);
bool accepted = channel.Writer.TryWrite(entry);

// Konsument
await foreach (var item in channel.Reader.ReadAllAsync())
    Process(item);

// Abschluss
channel.Writer.Complete();
await channel.Reader.Completion;
```

---

## Deterministische Tests (TestClock)

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

Alle zeitabhängigen Operationen lesen von `TimeProvider.Current`. Ersetzen Sie es in Tests durch `TestClock`. `AdvanceFrame()` simuliert einen einzelnen Player-Loop-Tick.

---

## Job-System-Brücke

```csharp
var job = new PathfindingJob { start = a, end = b, results = results };
await job.ScheduleAsync();
// das NativeArray mit Ergebnissen ist unmittelbar nach dem Await lesbar

await job.ScheduleParallelAsync(dataCount, batchSize); // IJobParallelFor

// Stornierung — Job Handle abschließen, bevor Stornierung gemeldet wird (kein Job-Leak)
await job.ScheduleAsync(cancellationToken);
```

---

## Compile-Zeit-Diagnosen

| Code | Schweregrad | Beschreibung |
|------|----------|-------------|
| TT001 | Warnung | Doppeltes Await / Use-after-free auf einem `ValkarnTask` |
| TT002 | Fehler | `async ValkarnTask`-Ergebnis als Ausdrucksanweisung verwendet — muss awaitet oder mit `.Forget()` verwendet werden |
| TT010 | Info | Async-Methode in MonoBehaviour wird bei Destroy automatisch storniert |
| TT011 | Warnung | `WhenAll` enthält Tasks mit unterschiedlichen Lebensdauern |
| TT012 | Warnung | Async-Schleife ohne Stornierungsprüfung (potenzielle Zombie-Schleife) |
| TT013 | Warnung | `ValkarnTask` zurückgegeben, aber nie awaitet und nicht explizit verworfen |
| TT014 | Warnung | `[NoAutoCancel]` ohne manuellen `CancellationToken`-Parameter |
| TT015 | Info | Await von `Awaitable` innerhalb von `ValkarnTask` — Brückenadapter generiert |
| TT016 | Warnung | Async-Methode ohne Await-Ausdruck |
| TT017 | Warnung | `[FireAndForget]` auf `ValkarnTask<T>` — Rückgabewert wird verworfen |

---

## Pool-Verwaltung

Jeder async-Methoden-Runner wird über `ValkarnPool<T>` gepooled:

- **Haupt-Thread** — lock-freier Stack, null Atomics
- **Hintergrund-Threads** — Treiber lock-freier Stack mit CAS-Operationen
- **Frame-basiertes Trimmen** — alle 300 Frames (~5s bei 60fps) werden überschüssige Objekte schrittweise freigegeben
- **Schrumpft nie** unter das konfigurierbare Minimum (Standard: 8)

Zur Laufzeit überwachen:

```csharp
foreach (var (type, size, maxSize) in ValkarnTask.GetPoolInfo())
    Debug.Log($"{type}: {size}/{maxSize}");
```

---

## ValkarnTaskSettings

Konfigurieren Sie über `ScriptableObject` (`Assets > Create > Valkarn > Tasks > Task Settings`, in `Resources/` ablegen):

| Einstellung | Standard | Beschreibung |
|---------|---------|-------------|
| `DefaultMaxPoolSize` | 256 | Maximale Elemente pro Pool-Typ |
| `MinPoolSize` | 8 | Niemals unter diesen Wert trimmen |
| `TrimCheckInterval` | 300 | Frames zwischen Trim-Prüfungen |
| `TrimHysteresisCount` | 2 | Aufeinanderfolgende Prüfungen vor dem Trimmen |
| `TrimReleaseRatio` | 0,25 | Anteil des Überschusses, der pro Zyklus freigegeben wird |
| `EnableAutoCancel` | true | MonoBehaviour-Tasks automatisch an destroyCancellationToken binden |
| `LogUnobservedCancellations` | false | Nicht beobachtete Stornierungen als Warnungen protokollieren |
| `MaxExceptionLogsPerFrame` | 10 | Ausnahmen-Log-Limit pro Frame |

---

## Fehlerbehandlung

```csharp
// Nicht beobachtete Ausnahmen — deterministisch beim Pool-Rückgabe ausgelöst
ValkarnTask.UnobservedException += ex =>
    Debug.LogError($"Unobserved: {ex}");
```

- `WhenAll` — wirft die erste Ausnahme, leitet zusätzliche an `UnobservedException` weiter
- `WhenAny` — die Ausnahme des Gewinners wird geworfen; Fehler der Verlierer gehen an `UnobservedException`
- Lifecycle-Stornierung — `OperationCanceledException` standardmäßig unterdrückt (konfigurierbar)

### Factory-Methoden

```csharp
ValkarnTask.CompletedTask          // void, ohne Allokation
ValkarnTask.FromResult<T>(value)   // typisiert, ohne Allokation
ValkarnTask.FromException(ex)      // fehlerhaft
ValkarnTask.FromException<T>(ex)
ValkarnTask.FromCanceled()         // storniert
ValkarnTask.FromCanceled<T>()
ValkarnTask.Never                  // schließt niemals ab (Sentinel für WhenAny)
```
