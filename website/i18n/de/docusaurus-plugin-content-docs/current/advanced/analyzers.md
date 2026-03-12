---
sidebar_position: 1
title: Analyzer-Regeln
---

# Analyzer-Regeln

Valkarn Tasks wird mit zwei Roslyn-Analyzer-Paketen geliefert, die beim Importieren des Pakets automatisch aktiviert werden:

- **`UnaPartidaMas.Valkarn.Tasks.SourceGen.dll`** — Regeln spezifisch für die Korrektheit von VlkTask und den Unity-Lebenszyklus. Regel-IDs beginnen mit `TT`.
- **`UnaPartidaMas.Valkarn.Tasks.Analyzer.dll`** — Migrationsregeln für Codebasen, die von UniTask wechseln. Regel-IDs beginnen mit `MIG`. Diese Regeln werden **nur ausgelöst, wenn `Cysharp.Threading.Tasks.UniTask` in der Kompilierung vorhanden ist**, sodass sie in neuen Projekten still sind.

Beide Pakete sind vorkompilierte DLLs, die sich im `Analyzers/`-Ordner des Pakets befinden. Unity lädt sie als Roslyn-Analyzer über die `.asmdef`-Referenz; das `_TestRunner~`-Projekt lädt sie über `<Analyzer>`-Einträge in `TestRunner.csproj`.

---

## Korrektheitsregeln (TT)

Diese Regeln erkennen Fehler im Zusammenhang mit der Einzelverbrauchsnatur von `VlkTask` und dem Missbrauch von Fire-and-Forget.

---

### TT001 — ValkarnTask bereits abgewartet

| Eigenschaft | Wert          |
|-------------|---------------|
| Schwere     | Warnung       |
| Kategorie   | ValkarnTask   |
| Code-Fix    | Nein          |

`VlkTask` ist ein Einzelverbrauch: einmal abgewartet, wird das interne Token veraltet. Ein zweites `await` auf derselben Variable trifft auf dieses veraltete Token und wirft eine Ausnahme. Der Analyzer erkennt, wenn dieselbe lokale Variable, derselbe Parameter oder dasselbe Feld vom Typ `VlkTask`/`VlkTask<T>` mehr als einmal innerhalb derselben Methode abgewartet wird.

**Ausgelöst bei:**
```csharp
async VlkTask Bad()
{
    VlkTask work = DoWorkAsync();
    await work;   // erstes await — korrekt
    await work;   // TT001: bereits abgewartet
}
```

**Behebung:** Wenn Sie das Ergebnis verzweigen müssen, erfassen Sie es mit `.AsResult()` vor dem ersten await, oder strukturieren Sie den Code so um, dass die Task genau einmal abgewartet wird.
```csharp
async VlkTask Good()
{
    var result = await DoWorkAsync().AsResult();
    // result in beiden Verzweigungen verwenden
}
```

---

### TT002 — ValkarnTask nicht abgewartet oder verworfen

| Eigenschaft | Wert          |
|-------------|---------------|
| Schwere     | **Fehler**    |
| Kategorie   | ValkarnTask   |
| Code-Fix    | Nein          |

Ein Methodenaufruf, der `VlkTask` zurückgibt und als Ausdrucksanweisung verwendet wird — nicht abgewartet, nicht zugewiesen und nicht von `.Forget()` gefolgt — ist ein stiller Fehler. Im Innern der Task geworfene Ausnahmen werden niemals beobachtet, und die gepoolte Zustandsmaschine der Task wird niemals in den Pool zurückgegeben.

Der Analyzer prüft Ausdrucksanweisungen, bei denen der Ausdruckstyp zu `VlkTask` oder `VlkTask<T>` aufgelöst wird. Er überspringt:
- Zuweisungsausdrücke (`tasks[i] = DoWork()`)
- Ketten, die mit `.Forget()` enden (beabsichtigtes Fire-and-Forget)

**Ausgelöst bei:**
```csharp
void Bad()
{
    LoadDataAsync();      // TT002: nicht abgewartet oder verworfen
    ProcessItemAsync();   // TT002
}
```

**Behebung — abwarten:**
```csharp
async VlkTask Good()
{
    await LoadDataAsync();
    await ProcessItemAsync();
}
```

**Behebung — explizites Fire-and-Forget:**
```csharp
void GoodFireAndForget()
{
    LoadDataAsync().Forget();
}
```

---

### TT013 — ValkarnTask zurückgegeben, aber nie verbraucht

| Eigenschaft | Wert          |
|-------------|---------------|
| Schwere     | Warnung       |
| Kategorie   | ValkarnTask   |
| Code-Fix    | Nein          |

Diese Regel-ID ist **für zukünftige Datenflusanalyse reserviert**. Sie soll das Muster „zugewiesen, aber nie abgewartet" erkennen — eine `VlkTask` in eine Variable speichern und dann nie abwarten oder verwerfen — was TT002 nicht abdeckt, da TT002 nur nackte Ausdrucksanweisungen untersucht.

Die aktuelle Implementierung registriert keine Syntaxaktionen. Wenn die Datenflussanalyse implementiert ist, wird TT013 TT002 ergänzen und folgendes abdecken:
```csharp
async VlkTask Bad()
{
    VlkTask task = DoWorkAsync();  // zugewiesen, aber nie abgewartet
    DoOtherThing();
    // task wird aufgegeben — TT013 (zukünftig)
}
```

Mit `[FireAndForget]` dekorierte Methoden werden ausgenommen.

---

## Lebenszyklusregeln (TT)

Diese Regeln betreffen das Lebensdauermanagement von MonoBehaviour und Abbruchlogik.

---

### TT010 — Auto-Abbruch aktiv für MonoBehaviour-Async-Methode

| Eigenschaft | Wert          |
|-------------|---------------|
| Schwere     | Info          |
| Kategorie   | ValkarnTask   |
| Code-Fix    | Nein          |

Ein informativer Hinweis. Jede `async VlkTask`- (oder `async VlkTask<T>`-) Methode innerhalb einer Klasse, die von `UnityEngine.MonoBehaviour` erbt, wird automatisch abgebrochen, wenn das Objekt zerstört wird — **es sei denn**, die Methode ist mit `[NoAutoCancel]` dekoriert. Diese Diagnose macht dieses Verhalten in der IDE sichtbar, ohne dass sich der Entwickler daran erinnern muss.

**Ausgelöst bei:**
```csharp
public class MyBehaviour : MonoBehaviour
{
    async VlkTask LoadLevel()  // TT010: Auto-Abbruch aktiv
    {
        await VlkTask.Delay(2000);
    }
}
```

**Zum Deaktivieren** (und Unterdrücken von TT010 für diese Methode) fügen Sie `[NoAutoCancel]` hinzu:
```csharp
[NoAutoCancel]
async VlkTask LoadLevel(CancellationToken ct)
{
    await VlkTask.Delay(2000, ct);
}
```

---

### TT011 — WhenAll mischt verschiedene Lebensdauerbereiche

| Eigenschaft | Wert          |
|-------------|---------------|
| Schwere     | Warnung       |
| Kategorie   | ValkarnTask   |
| Code-Fix    | Nein          |

`VlkTask.WhenAll`, aufgerufen mit Tasks aus verschiedenen Lebensdauerbereichen, kann überraschendes Teilabbruch-Verhalten erzeugen. Wenn eine Task an ein `MonoBehaviour` gebunden ist (zurückgegeben von einer Instanzmethode einer Klasse, die `MonoBehaviour` erbt) und eine andere ungebunden ist (eine statische Task oder aus einer Nicht-MonoBehaviour-Klasse), wird das Zerstören des Objekts eine Task abbrechen, aber nicht die andere, und den Kombinator in einem unbestimmten Zustand hinterlassen.

**Ausgelöst bei:**
```csharp
// Annahme: EnemyAI : MonoBehaviour
await VlkTask.WhenAll(
    enemyAI.PatrolAsync(),   // gebunden: auto-abgebrochen bei Destroy
    GlobalMusic.FadeAsync()  // ungebunden: lebt unbegrenzt
);
// TT011: WhenAll mischt Lebensdauern — PatrolAsync() vs GlobalMusic.FadeAsync()
```

**Behebung — beiden Tasks ein gemeinsames Abbruch-Token geben:**
```csharp
using var cts = new CancellationTokenSource();
await VlkTask.WhenAll(
    enemyAI.PatrolAsync(cts.Token),
    GlobalMusic.FadeAsync(cts.Token)
);
```

---

### TT012 — Async-Schleife ohne Abbruchprüfung

| Eigenschaft | Wert          |
|-------------|---------------|
| Schwere     | Warnung       |
| Kategorie   | ValkarnTask   |
| Code-Fix    | Nein          |

Eine `async VlkTask`-Methode, die eine `for`-, `while`-, `foreach`- oder `do`-`while`-Schleife enthält, deren Körper keine Abbruchprüfung hat, ist eine „Zombie-Schleife": Wenn ein Lebenszyklusabbruch ausgelöst wird (z. B. ein MonoBehaviour wird zerstört), kann das Auto-Abbruch-Signal die Schleife nicht unterbrechen. Die Schleife läuft weiter, obwohl das besitzende Objekt verschwunden ist.

Der Analyzer betrachtet einen Schleifenkörper als abbruchgeprüft, wenn er eines der folgenden enthält:
- Einen `await`-Ausdruck (die abgewartete Operation kann das Token beobachten und `OperationCanceledException` werfen)
- `ThrowIfCancellationRequested` (als Bezeichner oder Member-Zugriff)
- `IsCancellationRequested` (als Bezeichner oder Member-Zugriff)

Die Prüfung steigt **nicht** in verschachtelte Lambdas oder lokale Funktionen ab.

**Ausgelöst bei:**
```csharp
async VlkTask PollForever(CancellationToken ct)
{
    while (true)                     // TT012: keine Abbruchprüfung im Körper
    {
        ProcessNextItem();
        Thread.Sleep(16);            // synchron — kein await
    }
}
```

**Behebung — ein await oder eine explizite Prüfung hinzufügen:**
```csharp
async VlkTask PollForever(CancellationToken ct)
{
    while (true)
    {
        ct.ThrowIfCancellationRequested();
        ProcessNextItem();
        await VlkTask.Yield();       // erfüllt ebenfalls die Prüfung
    }
}
```

---

### TT014 — [NoAutoCancel] ohne CancellationToken-Parameter

| Eigenschaft | Wert          |
|-------------|---------------|
| Schwere     | Warnung       |
| Kategorie   | ValkarnTask   |
| Code-Fix    | Nein          |

`[NoAutoCancel]` auf einer `async VlkTask`-Methode in einem `MonoBehaviour` deaktiviert den automatischen Lebenszyklusabbruch. Wenn die Methode jedoch keinen `CancellationToken`-Parameter hat, hat sie keinen Mechanismus, um Abbrüche überhaupt zu beobachten, was das Attribut sinnlos macht und fast sicher einen vergessenen Parameter anzeigt.

**Ausgelöst bei:**
```csharp
public class Enemy : MonoBehaviour
{
    [NoAutoCancel]
    async VlkTask Chase()      // TT014: [NoAutoCancel] ohne CancellationToken-Parameter
    {
        await VlkTask.Delay(1000);
    }
}
```

**Behebung — einen `CancellationToken`-Parameter hinzufügen:**
```csharp
[NoAutoCancel]
async VlkTask Chase(CancellationToken ct)
{
    await VlkTask.Delay(1000, ct);
}
```

---

## Informationsregeln (TT)

---

### TT015 — Awaitable-Bridge-Adapter generiert

| Eigenschaft | Wert          |
|-------------|---------------|
| Schwere     | Info          |
| Kategorie   | ValkarnTask   |
| Code-Fix    | Nein          |

Wenn Sie ein Unity-`Awaitable` (aus `UnityEngine`) innerhalb einer `async VlkTask`-Methode abwarten, generiert Valkarn Tasks automatisch einen Bridge-Adapter über `AwaitableBridge`. Diese Diagnose ist informativ: Sie bestätigt, dass die Bridge aktiv ist. Es ist keine Aktion erforderlich.

**Ausgelöst bei:**
```csharp
async VlkTask LoadScene()
{
    await SceneManager.LoadSceneAsync("Main");  // TT015: Bridge-Adapter generiert
}
```

Es ist keine Änderung nötig. Die Bridge übernimmt die Konvertierung transparent.

---

## Code-Qualitätsregeln (TT)

---

### TT016 — Async-Methode ohne await

| Eigenschaft | Wert          |
|-------------|---------------|
| Schwere     | Warnung       |
| Kategorie   | ValkarnTask   |
| Code-Fix    | Nein          |

Eine `async VlkTask`- (oder `async VlkTask<T>`-) Methode, die keine `await`-Ausdrücke enthält — einschließlich `await foreach`, `await using` und `await` in `using`-Deklarationen — verursacht den vollständigen Overhead der Zustandsmaschinen-Allokation ohne Nutzen. Der Compiler generiert trotzdem eine Zustandsmaschine, aber da es keinen Suspendierungspunkt gibt, wird die Methode immer synchron abgeschlossen.

Der Analyzer prüft auf: `AwaitExpressionSyntax`, `foreach` mit `await`-Schlüsselwort, `using`-Deklarationen mit `await`-Schlüsselwort und `using`-Anweisungen mit `await`-Schlüsselwort.

**Ausgelöst bei:**
```csharp
async VlkTask<int> ComputeTotal()  // TT016: kein await im Körper
{
    return items.Sum(x => x.Value);
}
```

**Behebung — `async` entfernen und eine abgeschlossene Task zurückgeben:**
```csharp
VlkTask<int> ComputeTotal()
{
    return VlkTask.FromResult(items.Sum(x => x.Value));
}
```

Oder für eine void-Rückgabe:
```csharp
VlkTask DoSetup()
{
    Initialize();
    return VlkTask.CompletedTask;
}
```

---

### TT017 — [FireAndForget] auf ValkarnTask\<T\>

| Eigenschaft | Wert          |
|-------------|---------------|
| Schwere     | Warnung       |
| Kategorie   | ValkarnTask   |
| Code-Fix    | Nein          |

`[FireAndForget]` signalisiert, dass eine Methode absichtlich ohne Abwarten ihres Ergebnisses ausgeführt wird. Das Anwenden auf eine Methode, die `VlkTask<T>` (typisiertes Ergebnis) zurückgibt, verwirft immer `T`, was die typisierte Rückgabe bedeutungslos macht. Die Methode sollte stattdessen `VlkTask` (void) zurückgeben.

**Ausgelöst bei:**
```csharp
[FireAndForget]
async VlkTask<int> SendReport()   // TT017: Rückgabewert wird verworfen
{
    await UploadAsync();
    return 42;                     // diese 42 wird niemals gesehen
}
```

**Behebung — den Rückgabetyp zu `VlkTask` ändern:**
```csharp
[FireAndForget]
async VlkTask SendReport()
{
    await UploadAsync();
}
```

---

## Migrationsregeln (MIG)

Der Migrations-Analyzer wird **nur aktiviert, wenn der Typ `Cysharp.Threading.Tasks.UniTask` in der Kompilierung vorhanden ist**. Die Regeln sind informativ oder Warnungen, um den Übergang von UniTask zu Valkarn Tasks zu leiten. Keine dieser Regeln hat Auto-Fixes; die nachstehenden Beschreibungen erklären die manuelle Änderung.

---

### MIG001 — UniTask-Typ erkannt

| Eigenschaft | Wert                  |
|-------------|----------------------|
| Schwere     | Info                  |
| Kategorie   | ValkarnTaskMigration  |

Eine Verwendung eines `UniTask`-Typs (das Struct selbst oder `UniTask<T>`) wurde erkannt. Ersetzen Sie ihn durch das `VlkTask`- oder `VlkTask<T>`-Äquivalent.

```csharp
// Vorher
UniTask<Sprite> LoadSprite(string path) { ... }

// Nachher
VlkTask<Sprite> LoadSprite(string path) { ... }
```

---

### MIG002 — cancelImmediately-Parameter ist unnötig

| Eigenschaft | Wert                  |
|-------------|----------------------|
| Schwere     | Info                  |
| Kategorie   | ValkarnTaskMigration  |

UniTasks `Delay` und andere zeitbasierte Methoden akzeptieren einen `cancelImmediately`-Parameter für schnellen Abbruch. Valkarn Tasks bricht standardmäßig sofort ab — es gibt keinen `cancelImmediately`-Parameter. Entfernen Sie das Argument.

```csharp
// Vorher
await UniTask.Delay(1000, cancelImmediately: true, cancellationToken: ct);

// Nachher
await VlkTask.Delay(1000, ct);
```

---

### MIG003 — SuppressCancellationThrow() erkannt

| Eigenschaft | Wert                  |
|-------------|----------------------|
| Schwere     | Warnung               |
| Kategorie   | ValkarnTaskMigration  |

UniTasks `.SuppressCancellationThrow()` konvertiert eine abgebrochene Task in ein `(bool isCancelled, T result)`-Tupel ohne zu werfen. Valkarn Tasks verwendet `.AsResult()` für denselben Zweck, das ein `Result<T>`-Struct zurückgibt.

```csharp
// Vorher
var (isCancelled, value) = await myUniTask.SuppressCancellationThrow();

// Nachher
var result = await myVlkTask.AsResult();
if (result.IsCanceled) { ... }
T value = result.Value;
```

---

### MIG004 — Awaitable-Rückgabetyp erkannt

| Eigenschaft | Wert                  |
|-------------|----------------------|
| Schwere     | Info                  |
| Kategorie   | ValkarnTaskMigration  |

Eine Methode gibt Unitys `Awaitable`-Typ zurück. Erwägen Sie, ihn durch `VlkTask` zu ersetzen, das nativ mit der Valkarn Tasks-Laufzeit integriert und Auto-Abbruch, Pooling und den vollständigen Kombinator-Satz unterstützt.

---

### MIG005 — SingleConsumerUnbounded-Channel erkannt

| Eigenschaft | Wert                  |
|-------------|----------------------|
| Schwere     | Warnung               |
| Kategorie   | ValkarnTaskMigration  |

`Channel.CreateSingleConsumerUnbounded<T>()` von UniTask erstellt einen Channel ohne Gegendruck. Erwägen Sie, ihn durch `VlkTask.Channel.CreateBounded<T>(capacity)` zu ersetzen, um Gegendruck hinzuzufügen und unbegrenztes Speicherwachstum unter Last zu verhindern.

```csharp
// Vorher
var ch = Channel.CreateSingleConsumerUnbounded<Event>();

// Nachher (mit Gegendruck)
var ch = VlkTask.Channel.CreateBounded<Event>(capacity: 256);

// Oder wenn unbegrenzt beabsichtigt ist
var ch = VlkTask.Channel.CreateUnbounded<Event>();
```

---

### MIG006 — UniTask PlayerLoopTiming erkannt

| Eigenschaft | Wert                  |
|-------------|----------------------|
| Schwere     | Info                  |
| Kategorie   | ValkarnTaskMigration  |

Ein `PlayerLoopTiming`-Enum-Wert aus dem `Cysharp.Threading.Tasks`-Namespace wurde erkannt. Ändern Sie die `using`-Direktive zu `UnaPartidaMas.Valkarn.Tasks`; die Enum-Wertnamen sind identisch.

```csharp
// Vorher
using Cysharp.Threading.Tasks;
timing = PlayerLoopTiming.Update;

// Nachher
using UnaPartidaMas.Valkarn.Tasks;
timing = PlayerLoopTiming.Update;  // gleicher Name, anderer Namespace
```

---

### MIG007 — async UniTaskVoid erkannt

| Eigenschaft | Wert                  |
|-------------|----------------------|
| Schwere     | Warnung               |
| Kategorie   | ValkarnTaskMigration  |

`async UniTaskVoid` war UniTasks Fire-and-Forget-Methodenmuster. Valkarn Tasks ersetzt es durch zwei Optionen:

```csharp
// Vorher
async UniTaskVoid StartLoadAsync() { ... }

// Nachher — Option 1: [FireAndForget]-Attribut
[FireAndForget]
async VlkTask StartLoadAsync() { ... }

// Nachher — Option 2: Standard-VlkTask + .Forget() an der Aufrufstelle
async VlkTask StartLoadAsync() { ... }
// Aufgerufen als:
StartLoadAsync().Forget();
```

---

### MIG008 — Awaitable MainThreadAsync() erkannt

| Eigenschaft | Wert                  |
|-------------|----------------------|
| Schwere     | Info                  |
| Kategorie   | ValkarnTaskMigration  |

`Awaitable.MainThreadAsync()` wurde in Unitys `Awaitable`-API verwendet, um zum Haupt-Thread zurückzuwechseln. Valkarn Tasks führt Fortsetzungen standardmäßig auf dem Haupt-Thread via PlayerLoop-Integration aus, sodass explizite `MainThreadAsync()`-Aufrufe meist unnötig sind und entfernt werden können.

---

### MIG009 — UniTask.RunOnThreadPool erkannt

| Eigenschaft | Wert                  |
|-------------|----------------------|
| Schwere     | Warnung               |
| Kategorie   | ValkarnTaskMigration  |

Ersetzen Sie durch `VlkTask.RunOnThreadPool`. Die API ist identisch.

```csharp
// Vorher
await UniTask.RunOnThreadPool(() => HeavyWork());

// Nachher
await VlkTask.RunOnThreadPool(() => HeavyWork());
```

---

### MIG010 — .ToCoroutine() erkannt

| Eigenschaft | Wert                  |
|-------------|----------------------|
| Schwere     | Warnung               |
| Kategorie   | ValkarnTaskMigration  |

`.ToCoroutine()` war eine UniTask-Bridge für Legacy-Coroutine-Aufrufer. Schreiben Sie den verbrauchenden Code stattdessen als `async VlkTask`-Methode um.

```csharp
// Vorher
IEnumerator LegacyCaller() { yield return MyUniTask().ToCoroutine(); }

// Nachher
async VlkTask ModernCaller() { await MyVlkTask(); }
```

---

### MIG011 — UniTask.Create() erkannt

| Eigenschaft | Wert                  |
|-------------|----------------------|
| Schwere     | Warnung               |
| Kategorie   | ValkarnTaskMigration  |

`UniTask.Create(Func<UniTask>)` umhüllt ein Factory-Delegate. Ersetzen Sie es durch das `VlkTask.Promise<T>`-Muster für manuell gesteuerten Abschluss.

```csharp
// Vorher
var task = UniTask.Create(async () => { await DoWork(); return 42; });

// Nachher
var promise = new VlkTaskCompletionSource<int>();
DoWorkThenComplete(promise);
var task = promise.Task;
```

---

### MIG012 — UniTask.Lazy/Defer erkannt

| Eigenschaft | Wert                  |
|-------------|----------------------|
| Schwere     | Info                  |
| Kategorie   | ValkarnTaskMigration  |

`UniTask.Lazy<T>` und `UniTask.Defer` existierten, um Allokationen zu vermeiden, wenn eine Task synchron abschließen könnte. Valkarn Tasks hat einen allokationsfreien synchronen Schnellpfad eingebaut: `VlkTask.CompletedTask` oder `VlkTask.FromResult(value)` zurückzugeben alloziert nie. Entfernen Sie die `Lazy`/`Defer`-Wrapper.

---

### MIG013 — .ToUniTask()/.AsUniTask() erkannt

| Eigenschaft | Wert                  |
|-------------|----------------------|
| Schwere     | Info                  |
| Kategorie   | ValkarnTaskMigration  |

Konvertierungsaufrufe von Unity `Awaitable` zu UniTask. Entfernen Sie sie; Valkarn Tasks überbrückt `Awaitable` nativ (siehe TT015).

---

### MIG014 — UniTaskAsyncEnumerable erkannt

| Eigenschaft | Wert                  |
|-------------|----------------------|
| Schwere     | Warnung               |
| Kategorie   | ValkarnTaskMigration  |

UniTask lieferte sein eigenes `IUniTaskAsyncEnumerable<T>` und `UniTaskAsyncEnumerable`-Utilities. Verwenden Sie stattdessen `IAsyncEnumerable<T>` aus der BCL mit `System.Linq.Async`. `IAsyncEnumerable<T>` wird nativ von C# `await foreach` unterstützt.

```csharp
// Vorher
IUniTaskAsyncEnumerable<int> GetItems() { ... }

// Nachher
IAsyncEnumerable<int> GetItems() { ... }
```

---

### MIG015 — TimeoutController erkannt

| Eigenschaft | Wert                  |
|-------------|----------------------|
| Schwere     | Info                  |
| Kategorie   | ValkarnTaskMigration  |

UniTasks `TimeoutController` war ein Helfer für wiederverwendbare Timeouts. Ersetzen Sie ihn durch eine Standard-`CancellationTokenSource`, die mit einem `TimeSpan` konstruiert wird, was die BCL direkt unterstützt.

```csharp
// Vorher
var controller = new TimeoutController();
var ct = controller.Timeout(TimeSpan.FromSeconds(5));

// Nachher
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var ct = cts.Token;
```

---

## Regeln unterdrücken

Standard-Roslyn-Unterdrückungsmechanismen funktionieren für alle Regeln:

```csharp
// Eine einzelne Vorkommen inline unterdrücken
#pragma warning disable TT012
while (true) { DoSomething(); }
#pragma warning restore TT012
```

Oder über `.editorconfig` für projektweite Unterdrückung:
```ini
[*.cs]
dotnet_diagnostic.TT012.severity = none
```

Migrationsregeln (`MIG*`) können auf die gleiche Weise unterdrückt werden oder global deaktiviert werden, sobald die Migration abgeschlossen ist, indem die Schwere in `.editorconfig` auf `none` gesetzt wird.
