---
sidebar_position: 3
title: Abschluss-Sources
---

# ValkarnTaskCompletionSource

`ValkarnTaskCompletionSource<T>` und das nicht-generische `ValkarnTaskCompletionSource` geben Ihnen manuelle Kontrolle über ein `ValkarnTask`. Sie sind das asynchrone Äquivalent dazu, das Ergebnis selbst zu schreiben — Sie halten ein Source-Objekt, reichen dessen `.Task` an Aufrufer weiter und lösen es dann auf, verursachen einen Fehler oder brechen es von einer separaten Aufrufstelle aus ab.

Dies ist das Valkarn Tasks-Äquivalent von `TaskCompletionSource<T>` aus der BCL, aber gestützt durch `ValkarnTask.Promise<T>`, um das Allokationsmodell konsistent mit dem Rest der Bibliothek zu halten.

---

## ValkarnTaskCompletionSource&lt;T&gt;

```csharp
public class ValkarnTaskCompletionSource<T>
{
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled();
}
```

### Task

```csharp
public ValkarnTask<T> Task { get; }
```

Die Task, die awaiting-Code beobachten wird. Verteilen Sie diese an alle Aufrufer, die auf das Ergebnis warten müssen. Mehrere Aufrufer können dieselbe Task gleichzeitig `await`en.

### TrySetResult

```csharp
public bool TrySetResult(T result);
```

Schließt die Task erfolgreich mit dem angegebenen Wert ab. Alle wartenden Fortsetzungen werden fortgesetzt. Gibt `true` zurück, wenn der Abschluss akzeptiert wurde; gibt `false` zurück, wenn die Task bereits abgeschlossen war (durch einen vorherigen `TrySet*`-Aufruf). Wirft nie.

### TrySetException

```csharp
public bool TrySetException(Exception ex);
```

Verursacht einen Fehler in der Task mit der angegebenen Ausnahme. Awaiting-Code erhält die Ausnahme, wenn er `await` aufruft. Wirft `ArgumentNullException`, wenn `ex` `null` ist. Gibt `true` zurück, wenn akzeptiert, `false` wenn bereits abgeschlossen.

### TrySetCanceled

```csharp
public bool TrySetCanceled();
```

Bricht die Task ab. Awaiting-Code erhält eine `OperationCanceledException`. Gibt `true` zurück, wenn akzeptiert, `false` wenn bereits abgeschlossen.

---

## Nicht-generisches ValkarnTaskCompletionSource

Es gibt keine separate nicht-generische `ValkarnTaskCompletionSource`-Klasse in der öffentlichen API. Für manuell gesteuerte void-Tasks verwenden Sie `ValkarnTask.Promise` direkt:

```csharp
var promise = new ValkarnTask.Promise();
ValkarnTask task = promise.Task;

promise.TrySetResult();    // abschließen
promise.TrySetException(ex);
promise.TrySetCanceled();
```

`ValkarnTask.Promise` bietet dieselbe `TrySet*`-Oberfläche und denselben Doppelabschluss-Schutz, erzeugt aber ein nicht-generisches `ValkarnTask` statt eines `ValkarnTask<T>`.

---

## Doppelabschluss-Schutz

Alle `TrySet*`-Methoden können jederzeit von einem beliebigen Thread sicher aufgerufen werden, einschließlich gleichzeitig. Der erste Aufruf, der ein Compare-and-Swap auf der internen Zustandsmaschine gewinnt, gelingt; jeder nachfolgende Aufruf gibt `false` zurück und hat keine Wirkung. Das bedeutet:

- `TrySetResult` zweimal aufzurufen hat beim zweiten Aufruf keine Wirkung.
- `TrySetResult` nach `TrySetException` aufzurufen hat keine Wirkung.
- Zwei Threads, die gleichzeitig um den Abschluss derselben Source wetteifern, sind sicher — einer gewinnt, einer wird lautlos ignoriert.

Wenn Sie wissen möchten, welcher Aufrufer „gewonnen" hat, prüfen Sie den Rückgabewert. Wenn Sie sich nicht darum kümmern (Fire-and-Forget-Signal), können Sie ihn sicher ignorieren.

```csharp
// Sicheres Rennen — nur eines davon wird die Task tatsächlich abschließen
_ = source.TrySetResult(value);
_ = source.TrySetCanceled();
```

---

## Häufige Muster

### Eine callback-basierte API überbrücken

Viele Unity- und Plattform-APIs liefern Ergebnisse über Callbacks statt async/await. `ValkarnTaskCompletionSource<T>` lässt Sie diese sauber umhüllen.

```csharp
public ValkarnTask<Texture2D> LoadTextureAsync(string url)
{
    var tcs = new ValkarnTaskCompletionSource<Texture2D>();

    StartCoroutine(LoadCoroutine(url, tcs));

    return tcs.Task;
}

IEnumerator LoadCoroutine(string url, ValkarnTaskCompletionSource<Texture2D> tcs)
{
    var request = UnityWebRequestTexture.GetTexture(url);
    yield return request.SendWebRequest();

    if (request.result == UnityWebRequest.Result.Success)
        tcs.TrySetResult(DownloadHandlerTexture.GetContent(request));
    else
        tcs.TrySetException(new Exception(request.error));
}
```

Aufrufer können nun einfach die zurückgegebene Task `await`en:

```csharp
Texture2D tex = await LoadTextureAsync("https://example.com/image.png");
```

### Einmaliges Signal (asynchrones Gate)

Verwenden Sie `ValkarnTask.Promise`, wenn Sie ein Signal benötigen, das einmal ausgelöst wird und eine beliebige Anzahl von Wartenden entsperrt. Dies ähnelt einem `ManualResetEventSlim`, ist aber nativ asynchron.

```csharp
public class AsyncGate
{
    readonly ValkarnTask.Promise _promise = new();

    // Eine beliebige Anzahl von Aufrufern kann dies abwarten
    public ValkarnTask WaitAsync() => _promise.Task;

    // Einmal aufrufen, um alle zu entsperren
    public void Open() => _promise.TrySetResult();
}

// Verwendung
var gate = new AsyncGate();

// Mehrere Systeme warten unabhängig auf das Gate
async ValkarnTask SystemAAsync()
{
    await gate.WaitAsync();
    // nach dem Öffnen des Gates fortfahren
}

async ValkarnTask SystemBAsync()
{
    await gate.WaitAsync();
    // zur selben Zeit wie SystemA fortfahren
}

// Irgendwo anders — Gate öffnen
gate.Open();
```

Sobald `TrySetResult()` aufgerufen wird, schließen alle aktuellen und zukünftigen `await`-Aufrufe auf der Task sofort ab (synchron, wenn die Task bereits abgeschlossen ist, wenn sie laufen).

### Eine Drittanbieter-asynchrone Operation mit Abbruchunterstützung umhüllen

```csharp
public ValkarnTask<Result> RunWithTimeoutAsync(
    Func<ValkarnTask<Result>> operation,
    float timeoutSeconds,
    CancellationToken ct)
{
    var tcs = new ValkarnTaskCompletionSource<Result>();

    RunCoreAsync(tcs, operation, timeoutSeconds, ct).Forget();

    return tcs.Task;
}

async ValkarnTask RunCoreAsync(
    ValkarnTaskCompletionSource<Result> tcs,
    Func<ValkarnTask<Result>> operation,
    float timeout,
    CancellationToken ct)
{
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
    linked.CancelAfter(TimeSpan.FromSeconds(timeout));

    try
    {
        var result = await operation();
        tcs.TrySetResult(result);
    }
    catch (OperationCanceledException)
    {
        tcs.TrySetCanceled();
    }
    catch (Exception ex)
    {
        tcs.TrySetException(ex);
    }
}
```

### Verzögertes Initialisierungsgate

Ein häufiges Unity-Muster ist es, eine „bereit"-Task verfügbar zu machen, auf die Komponenten warten können, unabhängig davon, ob die Initialisierung bereits begonnen hat.

```csharp
public class ServiceBus : MonoBehaviour
{
    readonly ValkarnTask.Promise _readyPromise = new();

    // Jeder kann dies zu jedem Zeitpunkt abwarten — vor oder nach Initialize()
    public ValkarnTask Ready => _readyPromise.Task;

    async void Start()
    {
        await LoadConfigAsync();
        await ConnectAsync();
        _readyPromise.TrySetResult();  // entsperrt alle Wartenden
    }
}

// In einer anderen Komponente
async ValkarnTask OnEnableAsync()
{
    await ServiceBus.Instance.Ready;  // wartet, wenn noch nicht bereit, gibt sofort zurück, wenn bereits bereit
    DoWork();
}
```

---

## Beziehung zu ValkarnTask.Promise

`ValkarnTaskCompletionSource<T>` ist ein dünner öffentlicher Wrapper um `ValkarnTask.Promise<T>`. Beide bieten dieselbe Funktionalität. Der Unterschied liegt in der Namensgebungskonvention:

| Typ | Gibt zurück | Häufige Verwendung |
|-----|------------|-------------------|
| `ValkarnTaskCompletionSource<T>` | `ValkarnTask<T>` | Öffentliche API, spiegelt BCL `TaskCompletionSource<T>`-Stil |
| `ValkarnTask.Promise<T>` | `ValkarnTask<T>` | Interne Verwendung, etwas direkter |
| `ValkarnTask.Promise` | `ValkarnTask` | Void-Signale (Gates, Events) |

Alle drei stützen ihre Task auf einen `ValkarnTaskCompletionCore<T>`, die interne struct-basierte Zustandsmaschine, die Thread-Sicherheit und das Fortsetzungs-/Ergebnis-Rennen mithilfe eines CAS-basierten zweiphasigen Protokolls handhabt.
