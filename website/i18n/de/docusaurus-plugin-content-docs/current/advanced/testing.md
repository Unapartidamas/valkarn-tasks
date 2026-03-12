---
sidebar_position: 3
title: Testen
---

# Testen

Valkarn Tasks wird mit einer dedizierten Test-Infrastruktur geliefert, mit der Sie schnelle, deterministische Unit-Tests für asynchronen Code schreiben können — keine echten Timer, keine Frame-Wartezeiten, kein Unity Editor für die Kern-Test-Suite erforderlich.

---

## Überblick

Die Test-Unterstützung befindet sich in der `Testing/`-Assembly (`UnaPartidaMas.Valkarn.Tasks.Testing`), die über `InternalsVisibleTo` für die Laufzeit sichtbar ist. Sie stellt zwei öffentliche Typen bereit:

| Typ | Zweck |
|-----|-------|
| `VlkTaskTestHelper` | Valkarn Tasks-Laufzeit für eine Test-Session initialisieren und beenden |
| `TestClock` | Deterministischer Zeit- und Frame-Anbieter; ersetzt den Unity-`TimeProvider` während Tests |

---

## VlkTaskTestHelper

`VlkTaskTestHelper` ist eine statische Hilfsklasse im Namespace `UnaPartidaMas.Valkarn.Tasks.Testing`.

### Was es tut

`Setup()` führt drei Dinge durch:
1. Erstellt eine `TestClock` und installiert sie als `TimeProvider.Current`, wobei jeder echte Unity-Zeitanbieter ersetzt wird.
2. Ruft `PlayerLoopHelper.InitializeForTest()` auf, das die `ContinuationQueue`- und `PlayerLoopRunner`-Arrays für alle 16 PlayerLoop-Timings alloziert und die Laufzeit als initialisiert markiert.
3. Gibt die `TestClock` zurück, damit Ihr Test die Zeit steuern kann.

`Teardown()` kehrt das Setup um:
1. Installiert eine neue, keine-Operation-`TestClock` als `TimeProvider.Current`, um zu verhindern, dass veraltete Zeit zwischen Test-Fixtures durchsickert.
2. Ruft `PlayerLoopHelper.ShutdownForTest()` auf, das die Warteschlangen und Runner abbaut.

### Verwendungsmuster

```csharp
using NUnit.Framework;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Testing;

[TestFixture]
public class MyAsyncTests
{
    TestClock clock;

    [SetUp]
    public void SetUp()
    {
        clock = VlkTaskTestHelper.Setup();
    }

    [TearDown]
    public void TearDown()
    {
        VlkTaskTestHelper.Teardown();
    }

    // Tests kommen hierher
}
```

Rufen Sie `Setup` einmal pro Test in `[SetUp]` und `Teardown` einmal pro Test in `[TearDown]` auf. Das Muster stellt sicher, dass jeder Test mit einem sauberen Laufzeitzustand beginnt und nicht in den nächsten Test durchsickert.

### Standard-Delta-Zeit

`Setup` akzeptiert einen optionalen `defaultDeltaTime`-Parameter (Standard: `1/60` Sekunden, d. h. 60 fps):

```csharp
clock = VlkTaskTestHelper.Setup(defaultDeltaTime: 1f / 30f);  // 30-fps-Simulation
```

Dieser Wert wird von `TestClock.AdvanceFrame()` und `TestClock.AdvanceFrames(n)` verwendet.

---

## TestClock

`TestClock` implementiert `ITimeProvider` und gibt Tests volle Kontrolle über:

- Simulierte Zeit (via `GetTimestamp()`, durch `Stopwatch.Frequency`-Ticks unterstützt)
- `DeltaTime` und `UnscaledDeltaTime` für den aktuellen Frame
- `FrameCount`

### Zeit voranbringen

#### `Advance(TimeSpan duration)`

Bringt die Zeit um die angegebene Dauer in einem einzigen Schritt voran. Die gesamte Dauer wird als `DeltaTime` für diesen Frame angewendet, `FrameCount` wird um 1 erhöht und alle PlayerLoop-Timings werden verarbeitet. Nach der Verarbeitung wird `DeltaTime` auf den Wert zurückgesetzt, den es vor dem Aufruf hatte.

```csharp
var task = VlkTask.Delay(3000);  // 3-Sekunden-Verzögerung

clock.Advance(TimeSpan.FromSeconds(2));
Assert.IsFalse(task.IsCompleted);   // noch nicht

clock.Advance(TimeSpan.FromSeconds(1));
Assert.IsTrue(task.IsCompleted);    // genau bei 3 Sekunden
```

Verwenden Sie dies, wenn Sie zu einem bestimmten Zeitpunkt springen möchten, ohne einzelne Frames zu simulieren.

#### `AdvanceFrame()`

Bringt einen Frame mit der aktuellen `DeltaTime` voran. `FrameCount` wird um 1 erhöht, alle PlayerLoop-Timings werden verarbeitet, und ein 1-ms-`Thread.Sleep` wird vor der Verarbeitung eingefügt. Der Sleep existiert, weil Hintergrund-Worker-Threads (verwendet von `RunOnThreadPool` und Unity Job System-Integration) ein kleines Zeitfenster benötigen, um zwischen Frame-Ticks abzuschließen — ohne ihn laufen Tests Frames ohne Lücke hintereinander, was Race Conditions verursacht, die in echten Unity-Frames nicht auftreten.

```csharp
clock.AdvanceFrame();
```

#### `AdvanceFrames(int count)`

Ruft `AdvanceFrame()` `count` Mal auf.

```csharp
clock.AdvanceFrames(10);  // 10 Frames simulieren
```

#### `ProcessTick(PlayerLoopTiming timing)`

Verarbeitet eine einzelne PlayerLoop-Timing-Phase ohne Zeitfortschritt oder `FrameCount`-Erhöhung. Nützlich beim Testen von Code, der für ein bestimmtes Timing geplant ist (z. B. `PlayerLoopTiming.FixedUpdate`), und Sie nicht alle anderen Timings verarbeiten möchten.

```csharp
clock.ProcessTick(PlayerLoopTiming.FixedUpdate);
```

### Delta-Zeit steuern

#### `SetDeltaTime(float deltaTime)`

Setzt sowohl `DeltaTime` als auch `UnscaledDeltaTime` auf denselben Wert für alle nachfolgenden Frames.

#### `SetDeltaTime(float deltaTime, float unscaledDeltaTime)`

Setzt sie unabhängig voneinander, um eine Nicht-Einheits-`Time.timeScale` zu simulieren. Zum Beispiel, um Code zu testen, der `DelayType.UnscaledDeltaTime` verwendet, während die Zeit pausiert ist:

```csharp
clock.SetDeltaTime(deltaTime: 0f, unscaledDeltaTime: 0.05f);

var scaledTask = VlkTask.Delay(500, DelayType.DeltaTime);
var unscaledTask = VlkTask.Delay(500, DelayType.UnscaledDeltaTime);

clock.AdvanceFrames(9);  // 450 ms unskaliert, 0 ms skaliert
Assert.IsFalse(scaledTask.IsCompleted);    // pausierte Spielzeit erreicht niemals 500ms
Assert.IsFalse(unscaledTask.IsCompleted); // 450ms < 500ms

clock.AdvanceFrame();  // 500 ms unskaliert
Assert.IsTrue(unscaledTask.IsCompleted);  // unskalierte Verzögerung fertig
Assert.IsFalse(scaledTask.IsCompleted);  // skaliert bewegt sich immer noch nie
```

---

## Unit-Tests für async VlkTask-Methoden schreiben

### Tests, die synchron abschließen

Einige `VlkTask`-Operationen schließen ohne Suspendierung ab — zum Beispiel das Abwarten von `VlkTask.CompletedTask`, `VlkTask.FromResult(value)` oder einer `VlkTaskCompletionSource`, die vor dem Abwarten abgeschlossen wurde. Diese benötigen überhaupt keine Uhr und können die interne `TestHelper`-Klasse direkt verwenden (intern für die Test-Assembly):

```csharp
[TestFixture]
public class SyncTests
{
    [SetUp]
    public void SetUp()
    {
        // Setzt nur die Haupt-Thread-ID — keine Uhr oder PlayerLoop benötigt
        TestHelper.EnsureInitialized();
    }

    [Test]
    public void FromResult_ReturnsValue()
    {
        var task = VlkTask.FromResult(42);
        Assert.IsTrue(task.IsCompleted);
        Assert.AreEqual(42, task.GetAwaiter().GetResult());
    }
}
```

`TestHelper` (in `Tests/Editor/TestHelper.cs`) ist ein interner Helfer, der von der Test-Suite selbst verwendet wird:

- `TestHelper.EnsureInitialized()` setzt `VlkTaskPoolShared.MainThreadId` auf den aktuellen Thread. Dies ist erforderlich, damit Pool-Operationen zum korrekten (nicht-atomaren) Schnellpfad geleitet werden.
- `TestHelper.RunSync(VlkTask task)` stellt sicher, dass die Task bereits abgeschlossen ist und ruft `GetResult()` auf — nützlich zum Testen von Code-Pfaden, die synchron abschließen sollen.
- `TestHelper.RunSync<T>(VlkTask<T> task)` tut dasselbe und gibt den Ergebniswert zurück.
- `TestHelper.UnobservedExceptionCollector` abonniert `VlkTask.UnobservedException` für die Dauer seines Geltungsbereichs und sammelt nicht beobachtete Ausnahmen zur Prüfung.

### Tests, die Zeit erfordern

Jeder Test, der `VlkTask.Delay`, `VlkTask.Yield` oder bei einem PlayerLoop-Timing geplanten Code enthält, erfordert `VlkTaskTestHelper.Setup()` und die zurückgegebene `TestClock`.

**Beispiel: Test einer verzögerungsbasierten Methode**

Angenommen, Sie haben:

```csharp
public static async VlkTask WaitAndLog(int ms)
{
    await VlkTask.Delay(ms);
    Log("done");
}
```

Testen Sie es ohne echtes Warten:

```csharp
[TestFixture]
public class DelayTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = VlkTaskTestHelper.Setup();

    [TearDown]
    public void TearDown() => VlkTaskTestHelper.Teardown();

    [Test]
    public void WaitAndLog_CompletesAfterDelay()
    {
        var task = WaitAndLog(3000);
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(task.IsCompleted);

        // Ergebnis abrufen (wirft, wenn fehlgeschlagen)
        task.GetAwaiter().GetResult();
    }

    [Test]
    public void WaitAndLog_ZeroDelay_CompletesImmediately()
    {
        var task = WaitAndLog(0);
        // VlkTask.Delay(0) gibt sofort CompletedTask zurück
        Assert.IsTrue(task.IsCompleted);
    }
}
```

### Abbruch testen

```csharp
[Test]
public void Delay_CancelledMidway_ThrowsOCE()
{
    var cts = new CancellationTokenSource();
    var task = VlkTask.Delay(5000, cts.Token);
    Assert.IsFalse(task.IsCompleted);

    cts.Cancel();
    // Abbruch propagiert beim nächsten Tick
    clock.AdvanceFrame();

    Assert.IsTrue(task.IsCompleted);
    Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
}
```

### Nicht beobachtete Ausnahmen sammeln

```csharp
[Test]
public void FaultedTask_PublishesUnobservedException()
{
    using var collector = new TestHelper.UnobservedExceptionCollector();

    var tcs = new VlkTaskCompletionSource();
    var task = tcs.Task;

    // Nicht abwarten — fire and forget
    task.Forget();

    // Mit Ausnahme abschließen — löst nicht-beobachteten Pfad aus
    tcs.TrySetException(new InvalidOperationException("oops"));

    Assert.AreEqual(1, collector.Exceptions.Count);
    Assert.IsInstanceOf<InvalidOperationException>(collector.Exceptions[0]);
}
```

---

## Beispiel: Test einer Channel-Produzenten/Konsumenten-Pipeline

```csharp
[TestFixture]
public class ChannelPipelineTests
{
    [SetUp]
    public void SetUp() => TestHelper.EnsureInitialized();

    [Test]
    public void Producer_WritesItems_ConsumerReadsInOrder()
    {
        var channel = VlkTask.Channel.CreateUnbounded<int>();

        // Produzent: synchron schreiben
        channel.Writer.TryWrite(1);
        channel.Writer.TryWrite(2);
        channel.Writer.TryWrite(3);
        channel.Writer.Complete();

        // Konsument: ReadAsync auf einem Channel mit Elementen schließt synchron ab
        Assert.AreEqual(1, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(2, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(3, TestHelper.RunSync(channel.Reader.ReadAsync()));
    }

    [Test]
    public void ReadAsync_BeforeWrite_PendsThenCompletesOnWrite()
    {
        var channel = VlkTask.Channel.CreateUnbounded<string>();

        // Lesen vor dem Schreiben
        var readTask = channel.Reader.ReadAsync();
        Assert.IsFalse(readTask.IsCompleted);

        // Schreiben schließt das ausstehende Lesen synchron ab
        channel.Writer.TryWrite("hello");
        Assert.IsTrue(readTask.IsCompleted);
        Assert.AreEqual("hello", readTask.GetAwaiter().GetResult());
    }

    [Test]
    public void Complete_DrainedChannel_CompletionTaskCompletes()
    {
        var channel = VlkTask.Channel.CreateUnbounded<int>();
        channel.Writer.TryWrite(42);

        var completion = channel.Reader.Completion;
        Assert.IsFalse(completion.IsCompleted);

        channel.Writer.Complete();
        Assert.IsFalse(completion.IsCompleted);  // Element noch ungelesen

        channel.Reader.TryRead(out _);
        Assert.IsTrue(completion.IsCompleted);   // geleert — Completion wird ausgelöst
    }

    [Test]
    public void ClosedChannel_Read_ThrowsChannelClosedException()
    {
        var channel = VlkTask.Channel.CreateUnbounded<int>();
        channel.Writer.Complete();

        var readTask = channel.Reader.ReadAsync();
        Assert.IsTrue(readTask.IsCompleted);
        Assert.Throws<ChannelClosedException>(() => readTask.GetAwaiter().GetResult());
    }
}
```

---

## Beispiel: Test von VlkTask.Delay mit Frame-Fortschritt

Dieses Beispiel demonstriert den Test einer Methode, die eine konfigurierbare Verzögerung wartet, und überprüft, dass ein teilweiser Fortschritt die Task nicht vorzeitig abschließt.

```csharp
[TestFixture]
public class DelayFrameTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = VlkTaskTestHelper.Setup(defaultDeltaTime: 1f / 60f);

    [TearDown]
    public void TearDown() => VlkTaskTestHelper.Teardown();

    [Test]
    public void Delay_500ms_RequiresTenFramesAt50ms()
    {
        clock.SetDeltaTime(0.05f);  // 50 ms pro Frame
        var task = VlkTask.Delay(500);

        clock.AdvanceFrames(9);
        Assert.IsFalse(task.IsCompleted, "450 ms vergangen — sollte noch nicht fertig sein");

        clock.AdvanceFrame();
        Assert.IsTrue(task.IsCompleted, "500 ms vergangen — sollte fertig sein");
    }

    [Test]
    public void Delay_Realtime_UsesTimestampNotDeltaTime()
    {
        // Realtime-Verzögerung ignoriert DeltaTime; sie verwendet den Stopwatch-Zeitstempel
        clock.SetDeltaTime(0f);  // DeltaTime eingefroren
        var task = VlkTask.Delay(200, DelayType.Realtime);

        // Advance bewegt nur den Zeitstempel via TimeSpan
        clock.Advance(TimeSpan.FromMilliseconds(199));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(task.IsCompleted);
    }
}
```

---

## NUnit-Integration

Die Test-Suite verwendet **NUnit 3.x** (auf 3.x in `TestRunner.csproj` festgelegt). NUnit 4 hat die klassische `Assert.IsTrue` / `Assert.AreEqual`-API entfernt, die die Tests verwenden; aktualisieren Sie nicht auf NUnit 4, ohne die Assertions auf die Constraint-basierte API zu aktualisieren.

### Unity Test Framework

Im Unity Editor werden Tests in `Tests/Editor/` vom **Unity Test Framework** (UTF) entdeckt und ausgeführt, das auf NUnit basiert. Die Test-Runner-Integration funktioniert wie folgt:

- UTF führt jeden `[Test]` auf dem Haupt-Thread aus.
- `VlkTaskTestHelper.Setup()` installiert die `TestClock` vor jedem Test; UFTs `[SetUp]`-Attribut ruft es auf.
- `VlkTaskTestHelper.Teardown()` entfernt die Uhr in `[TearDown]`.
- Keine Coroutine oder `[UnityTest]` ist erforderlich: alle Valkarn Tasks-Tests verwenden synchrone NUnit-`[Test]`-Methoden. Die `TestClock` treibt die Zeit manuell voran, sodass kein echter Frame-Fortschritt benötigt wird.

---

## Das _TestRunner~-Projekt

Das `_TestRunner~/`-Verzeichnis enthält ein eigenständiges .NET 8-Projekt, das die gesamte Test-Suite **außerhalb von Unity** kompiliert und ausführt, mit dem .NET SDK und `dotnet test`. Dies wird für CI und die lokale Entwicklung von Mitwirkenden verwendet.

### Struktur

```
_TestRunner~/
  TestRunner.csproj   — .NET 8-Projektdatei
  bin~/               — Build-Ausgabe (gitignored)
  obj~/               — Zwischenausgabe (gitignored)
```

### Wie es funktioniert

`TestRunner.csproj` kompiliert vier Quellsätze in eine einzige Assembly:

| Quellsatz   | Pfad | Hinweise |
|-------------|------|----------|
| Runtime | `../Runtime/**/*.cs` | Alle Runtime-Dateien |
| Testing | `../Testing/**/*.cs` | `VlkTaskTestHelper`, `TestClock` |
| Tests | `../Tests/Editor/**/*.cs` | Alle `.cs`-Testdateien |
| Ausschlüsse | `UnityTimeProvider.cs`, `Bridge/`, `Burst/`, `ECS/` | Erfordert echte Unity-APIs — ausgeschlossen |

Das Projekt definiert **nicht** `UNITY_5_3_OR_NEWER`, `VTASKS_HAS_BURST`, `VTASKS_HAS_COLLECTIONS` oder `VTASKS_HAS_ENTITIES`, sodass alle Unity-spezifischen `#if`-Zweige herausgekompiliert werden. Das bedeutet, die Tests üben die reinen C#-Code-Pfade aus.

Die Analyzer-DLLs werden als `<Analyzer>`-Einträge geladen, sodass dieselben Regeln, die in der IDE ausgelöst werden, auch während `dotnet build` des Testprojekts ausgelöst werden.

### Tests ausführen

```bash
cd _TestRunner~
dotnet test
```

Oder vom Repository-Stamm:

```bash
dotnet test _TestRunner~/TestRunner.csproj
```

### Ausgeschlossene Test-Dateien

Drei Test-Unterverzeichnisse sind vom `_TestRunner~`-Projekt ausgeschlossen, weil sie echte Unity-Laufzeit-APIs erfordern:

| Verzeichnis | Grund |
|-------------|-------|
| `Tests/Editor/Bridge/` | Tests für die Job System-Bridge; erfordert `Unity.Jobs` |
| `Tests/Editor/Burst/` | Tests für den Burst-Scheduler; erfordert `Unity.Burst` |
| `Tests/Editor/ECS/` | Tests für ECS-Utilities; erfordert `Unity.Entities` |

Diese Tests laufen nur innerhalb des Unity Editors über das Unity Test Framework.

### Einen neuen Test hinzufügen

1. Fügen Sie eine neue `.cs`-Datei unter `Tests/Editor/` hinzu (nicht in einem Unity-API-Unterverzeichnis).
2. Dekorieren Sie die Klasse mit `[TestFixture]` und die Methoden mit `[Test]`.
3. Wenn der Test Zeitsteuerung benötigt, verwenden Sie `VlkTaskTestHelper.Setup()` / `Teardown()` in `[SetUp]` / `[TearDown]`.
4. Wenn der Test nur synchrone Task-Assertions benötigt (keine Verzögerung), verwenden Sie `TestHelper.EnsureInitialized()` in `[SetUp]` — kein Teardown erforderlich.
5. Führen Sie `dotnet test _TestRunner~/TestRunner.csproj` aus, um außerhalb von Unity zu überprüfen.
6. Führen Sie das Unity Test Framework aus, um innerhalb von Unity zu überprüfen.
