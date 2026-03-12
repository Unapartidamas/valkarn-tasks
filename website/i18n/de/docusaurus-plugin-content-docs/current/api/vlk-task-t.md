---
sidebar_position: 2
title: ValkarnTask<T>
---

# `ValkarnTask<T>`

`ValkarnTask<T>` ist der wertliefernde asynchrone Task-Typ in Valkarn Tasks. Es ist ein `readonly struct`, das entweder ein inline-Ergebnis (bei synchronem Abschluss) oder eine Referenz auf ein gepooltes Quellobjekt (bei asynchronem Abschluss) trägt.

**Namespace:** `UnaPartidaMas.Valkarn.Tasks`

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncValkarnTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
```

Es gibt keine generischen Einschränkungen für `T`. Jeder Typ — Werttyp, Referenztyp, Struct oder Klasse — ist gültig.

---

## Instanzen erstellen

### Synchron abgeschlossene Tasks

Diese Factory-Methoden geben ein `ValkarnTask<T>` ohne Backing-Quellobjekt zurück. Null Allokation.

#### `ValkarnTask.FromResult<T>(T value)`

Gibt ein abgeschlossenes `ValkarnTask<T>` zurück, das `value` inline trägt. Als statische Methode auf dem nicht-generischen `ValkarnTask`-Typ deklariert.

```csharp
public static ValkarnTask<T> FromResult<T>(T value)
```

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(42);
ValkarnTask<string> name = ValkarnTask.FromResult("Valkarn");
ValkarnTask<Vector3> pos = ValkarnTask.FromResult(transform.position);
```

Das zurückgegebene Struct hat `source == null`. Das Abwarten verursacht keine Fortsetzungs-Allokation — der Compiler sieht sofort `IsCompleted == true`.

#### `ValkarnTask.FromException<T>(Exception exception)`

Gibt ein gefaultetes `ValkarnTask<T>` zurück. Das Abwarten wirft die Ausnahme erneut, wobei der ursprüngliche Stack-Trace über `ExceptionDispatchInfo` erhalten bleibt.

```csharp
public static ValkarnTask<T> FromException<T>(Exception exception)
```

```csharp
ValkarnTask<Texture2D> LoadTexture(string path)
{
    if (string.IsNullOrEmpty(path))
        return ValkarnTask.FromException<Texture2D>(
            new ArgumentException("Pfad darf nicht leer sein.", nameof(path)));

    return LoadTextureAsync(path);
}
```

#### `ValkarnTask.FromCanceled<T>(CancellationToken ct = default)`

Gibt ein abgebrochenes `ValkarnTask<T>` zurück. Das Abwarten wirft `OperationCanceledException`.

```csharp
public static ValkarnTask<T> FromCanceled<T>(CancellationToken ct = default)
```

```csharp
ValkarnTask<byte[]> Download(string url, CancellationToken ct)
{
    if (ct.IsCancellationRequested)
        return ValkarnTask.FromCanceled<byte[]>(ct);

    return DownloadAsync(url, ct);
}
```

### Über `async`-Methoden

Jede `async`-Methode, die `ValkarnTask<T>` zurückgibt, verwendet automatisch `AsyncValkarnTaskMethodBuilder<TResult>`:

```csharp
async ValkarnTask<int> ComputeAsync()
{
    await ValkarnTask.Yield();
    return 42;
}
```

Der Compiler generiert eine Zustandsmaschine. Wenn die Methode synchron abschließt (nie suspendiert), gibt `AsyncValkarnTaskMethodBuilder<T>.Task` `new ValkarnTask<T>(result)` mit `source == null` zurück — null Allokation.

---

## Arbeit auf dem Thread-Pool ausführen

Diese Methoden führen ein Delegate auf dem .NET Thread-Pool aus und geben das Ergebnis auf dem Haupt-Thread (zum angegebenen `PlayerLoopTiming`) zurück. Sie sind Convenience-Wrapper über die länger benannten `RunOnThreadPool`-Varianten.

#### `ValkarnTask.Run<T>(Func<T> func, PlayerLoopTiming timing, CancellationToken ct)`

Führt ein synchrones `Func<T>` auf dem Thread-Pool aus.

```csharp
public static ValkarnTask<T> Run<T>(
    Func<T> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
// Auf Thread-Pool berechnen, Ergebnis kommt beim nächsten Update auf dem Haupt-Thread an
int hash = await ValkarnTask.Run(() => ComputeExpensiveHash(data));
```

#### `ValkarnTask.Run<T>(Func<ValkarnTask<T>> func, PlayerLoopTiming timing, CancellationToken ct)`

Führt ein asynchrones `Func<ValkarnTask<T>>` auf dem Thread-Pool aus. Verwenden Sie dies, wenn die Arbeit selbst asynchron ist (z. B. Datei-I/O).

```csharp
public static ValkarnTask<T> Run<T>(
    Func<ValkarnTask<T>> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
string json = await ValkarnTask.Run(async () =>
{
    using var reader = File.OpenText("data.json");
    return await reader.ReadToEndAsync();
});
```

Beide `Run`-Überladungen brechen frühzeitig ab, wenn das Token bereits abgebrochen ist, und wechseln nach Abschluss der Arbeit bei `timing` zurück auf den Haupt-Thread.

---

## Instanzmember

### `IsCompleted`

```csharp
public bool IsCompleted { get; }
```

Gibt `true` zurück, wenn die Task in einem beliebigen Endzustand abgeschlossen ist (Erfolgreich, Gefaultet oder Abgebrochen). Bei synchron abgeschlossenen Tasks (`source == null`) gibt es immer `true` ohne Interface-Dispatch zurück.

```csharp
var task = SomeLongOperation();
if (task.IsCompleted)
{
    int result = task.GetAwaiter().GetResult();
    Use(result);
}
```

### `GetStatus()`

```csharp
public ValkarnTask.Status GetStatus()
```

Gibt den aktuellen `ValkarnTask.Status` zurück. Mögliche Werte: `Pending`, `Succeeded`, `Faulted`, `Canceled`. Bei `source == null` gibt es immer `Succeeded` zurück.

### `GetAwaiter()`

```csharp
public Awaiter GetAwaiter()
```

Gibt ein `Awaiter`-Struct zurück. Wird vom Compiler verwendet, um `await` zu implementieren. Sie können es auch direkt aufrufen, um das Ergebnis synchron zu erhalten (nur sicher, wenn `IsCompleted` wahr ist).

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(10);
int value = task.GetAwaiter().GetResult(); // sicher — synchron abgeschlossen
```

Der Aufruf von `GetResult()` auf einer ausstehenden Task wirft eine `InvalidOperationException`.

### `AsNonGeneric()`

```csharp
public ValkarnTask AsNonGeneric()
```

Konvertiert dieses `ValkarnTask<T>` in ein nicht-generisches `ValkarnTask` und verwirft den Ergebnistyp. Die resultierende Task teilt dieselbe zugrunde liegende Quelle und dasselbe Token, schließt also gleichzeitig ab.

```csharp
ValkarnTask<int> typedTask = ComputeAsync();
ValkarnTask voidTask = typedTask.AsNonGeneric();
await voidTask;   // wartet auf Abschluss, ignoriert Ergebnis
```

Dies ist nützlich, wenn gemischte Typen-Tasks an Kombinatoren übergeben werden oder wenn nur das Abschluss-Timing interessiert, nicht der Wert.

---

## Kombinatoren, die `ValkarnTask<T>` zurückgeben

### `WhenAll` — typisierte Zwei-Task-Überladung

```csharp
public static ValkarnTask<(T1, T2)> WhenAll<T1, T2>(
    ValkarnTask<T1> task1, ValkarnTask<T2> task2)
```

Führt beide Tasks gleichzeitig aus und gibt ein Tupel mit Ergebnissen zurück. Wenn eine Task einen Fehler hat oder abgebrochen wird, gewinnt die erste Ausnahme und der Fehler der anderen wird über `ValkarnTask.UnobservedException` gemeldet.

**Tupel-Destructuring** funktioniert natürlich mit C#-Dekonstruktion:

```csharp
var (profile, inventory) = await ValkarnTask.WhenAll(
    FetchProfileAsync(userId),
    FetchInventoryAsync(userId)
);
```

**Null-Allokations-Schnellpfad:** Wenn beide Tasks am Aufrufpunkt synchron abgeschlossen sind, wird kein gepooltes Objekt erstellt und das Ergebnis-Tupel wird inline zurückgegeben.

### `WhenAll` — typisierte Sammlungsüberladung

```csharp
public static ValkarnTask<T[]> WhenAll<T>(IEnumerable<ValkarnTask<T>> tasks)
```

Wartet alle Tasks in der Sammlung gleichzeitig ab und gibt ein `T[]` in Indexreihenfolge zurück.

```csharp
var urls = new[] { "https://a.com", "https://b.com", "https://c.com" };
ValkarnTask<string>[] downloads = urls.Select(u => DownloadAsync(u)).ToArray();
string[] results = await ValkarnTask.WhenAll(downloads);
```

Wenn die Sammlung leer ist, gibt es `ValkarnTask.FromResult(Array.Empty<T>())` zurück — null Allokation. Wenn alle Tasks bereits synchron abgeschlossen sind, wird ein Ergebnis-Array inline ohne Erstellen eines Kombinator-Promises erstellt.

### `WhenAny` — typisierte Zwei-Task-Überladung

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    ValkarnTask<T> task1, ValkarnTask<T> task2)
```

Gibt zurück, sobald eine der Tasks abschließt. Das Ergebnis-Tupel enthält den 0-basierten Index des Gewinners und seinen Wert. Verlierer-Tasks laufen weiter; ihre Fehler (falls vorhanden) werden über `ValkarnTask.UnobservedException` gemeldet. Abbrüche von Verlierern werden absichtlich nicht gemeldet.

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(
    FetchFromCacheAsync(key),
    FetchFromNetworkAsync(key)
);

if (winnerIndex == 0)
    Debug.Log("Cache hat gewonnen");
```

### `WhenAny` — typisierte Sammlungsüberladung

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    IEnumerable<ValkarnTask<T>> tasks)
```

Dieselbe Semantik wie die Zwei-Task-Überladung, auf eine beliebige Anzahl von Tasks erweitert. Erfordert mindestens eine Task; wirft `ArgumentException` bei leeren Sammlungen.

```csharp
var tasks = servers.Select(s => s.FetchAsync(query)).ToArray();
var (winnerIndex, data) = await ValkarnTask.WhenAny(tasks);
Debug.Log($"Server {winnerIndex} hat zuerst geantwortet");
```

---

## Factory-Convenience-Methoden

### `ValkarnTask.Create<T>(Func<ValkarnTask<T>> factory)`

Ruft das Factory-Delegate auf und wartet die resultierende Task ab. Nützlich, wenn Sie die Konstruktion einer asynchronen Operation verzögern möchten.

```csharp
public static async ValkarnTask<T> Create<T>(Func<ValkarnTask<T>> factory)
```

```csharp
var result = await ValkarnTask.Create(() => LoadLevelDataAsync(levelId));
```

---

## `Awaiter`-Struct (verschachtelt)

`ValkarnTask<T>.Awaiter` ist der compilerseitige Awaiter. Es ist ein `readonly struct`, das `ICriticalNotifyCompletion` implementiert. Normalerweise interagieren Sie nicht direkt damit.

```csharp
public readonly struct Awaiter : ICriticalNotifyCompletion
{
    public bool IsCompleted { get; }
    public T GetResult();
    public void OnCompleted(Action continuation);
    public void UnsafeOnCompleted(Action continuation);
}
```

`UnsafeOnCompleted` ist der Pfad, der von `AsyncValkarnTaskMethodBuilder<T>` verwendet wird. Die Bezeichnung „unsafe" bedeutet, dass `ExecutionContext` nicht erfasst wird — dies ist für Unity beabsichtigt, wo kein `SynchronizationContext` in Kraft ist.

Wenn `IsCompleted` wahr ist, liest der Aufruf von `GetResult()` das inline gespeicherte `result`-Feld (bei synchronen Tasks) oder ruft es über das Source-Interface auf (bei asynchronen Tasks). Beide Pfade sind `[MethodImpl(MethodImplOptions.AggressiveInlining)]`.

---

## Erweiterungsmethoden auf `ValkarnTask<T>`

### `AsResult<T>()`

```csharp
public static ValkarnTask<Result<T>> AsResult<T>(this ValkarnTask<T> task)
```

Umhüllt ein `ValkarnTask<T>` in ein `ValkarnTask<Result<T>>`, fängt jede Ausnahme oder jeden Abbruch ab und kodiert es in den `Result<T>`-Wert. Dies vermeidet try/catch an der Aufrufstelle.

```csharp
Result<string> result = await FetchDataAsync(url).AsResult();

if (result.IsSuccess)
    Process(result.Value);
else if (result.IsCanceled)
    Debug.Log("Abgebrochen");
else
    Debug.LogError(result.Exception);
```

**Synchroner Schnellpfad:** Wenn die Quell-Task bereits synchron abgeschlossen ist, gibt `AsResult` synchron zurück ohne asynchrone Maschinerie.

---

## `Promise<T>` — manueller Abschluss-Source

`ValkarnTask.Promise<T>` ist ein heap-allozierter manueller Abschluss-Source für Fälle, in denen Sie steuern müssen, wann ein `ValkarnTask<T>` abschließt, und die Lebensdauer nicht durch eine einzelne asynchrone Operation begrenzt ist.

```csharp
public class Promise<T>
{
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

```csharp
// Eine callback-basierte API umhüllen
var promise = new ValkarnTask.Promise<string>();

SomeCallbackApi.OnComplete += value => promise.TrySetResult(value);
SomeCallbackApi.OnError   += ex    => promise.TrySetException(ex);

string result = await promise.Task;
```

Im Gegensatz zu `PooledPromise<T>` wird `Promise<T>` nicht gepooolt. Es verwendet einen Finalizer, um nicht beobachtete Ausnahmen zu erkennen und zu melden, wenn die Task einen Fehler hat und der Aufrufer sie nie abwartet.

Für häufige Muster (Producer/Consumer-Schleifen, pro-Frame-Operationen) bevorzugen Sie `ValkarnTask.PooledPromise<T>`, das nach dem Aufruf von `GetResult` automatisch in den Pool zurückkehrt.

---

## `PooledPromise<T>` — gepoolter manueller Abschluss-Source

```csharp
public sealed class PooledPromise<T> : ValkarnTask.ISource<T>, IPoolNode<PooledPromise<T>>
{
    public static PooledPromise<T> Create(out uint token);
    public static PooledPromise<T> CreateCompleted(T result, out uint token);
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

Nachdem `GetResult` auf der Backing-Task aufgerufen wurde, setzt das Promise seinen `ValkarnTaskCompletionCore<T>` zurück und gibt sich selbst in den Pool zurück. Ein Doppelrückgabe-Schutz stellt sicher, dass dies höchstens einmal passiert, auch wenn `GetResult` gleichzeitig aufgerufen wird.

```csharp
// Muster: ein ValkarnTask<T> produzieren, das bei Bereitschaft abschließt
var promise = ValkarnTask.PooledPromise<int>.Create(out uint token);
ValkarnTask<int> task = promise.Task;

// Arbeit asynchron versenden
ThreadPool.QueueUserWorkItem(_ =>
{
    int result = DoWork();
    promise.TrySetResult(result);
});

// Konsument wartet; bei Abschluss kehrt Promise automatisch in den Pool zurück
int value = await task;
```

---

## Zusammenfassung der Wege, ein `ValkarnTask<T>` zu erhalten

| Methode | Wann zu verwenden |
|--------|------------------|
| `return value` innerhalb von `async ValkarnTask<T>` | Normale asynchrone Methoden |
| `ValkarnTask.FromResult(value)` | Synchrone schnelle Rückgaben |
| `ValkarnTask.FromException<T>(ex)` | Vorab gefaultete Tasks |
| `ValkarnTask.FromCanceled<T>(ct)` | Vorab abgebrochene Tasks |
| `ValkarnTask.Run<T>(Func<T>, ...)` | Thread-Pool-Auslagerung |
| `ValkarnTask.Run<T>(Func<ValkarnTask<T>>, ...)` | Asynchrone Thread-Pool-Arbeit |
| `ValkarnTask.WhenAll<T1,T2>(t1, t2)` | Auf zwei typisierte Tasks warten, Tupel erhalten |
| `ValkarnTask.WhenAll<T>(IEnumerable<...>)` | Auf N typisierte Tasks warten, Array erhalten |
| `ValkarnTask.WhenAny<T>(t1, t2)` | Erste von zwei typisierten Tasks |
| `ValkarnTask.WhenAny<T>(IEnumerable<...>)` | Erste von N typisierten Tasks |
| `task.AsResult<T>()` | Ausnahme-sicheres Umhüllen |
| `new ValkarnTask.Promise<T>()` → `.Task` | Langlebiger manueller Abschluss |
| `ValkarnTask.PooledPromise<T>.Create(...)` → `.Task` | Häufiger manueller Abschluss |
