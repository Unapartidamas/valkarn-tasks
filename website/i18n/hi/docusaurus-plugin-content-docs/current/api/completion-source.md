---
sidebar_position: 3
title: Completion Sources
---

# ValkarnTaskCompletionSource

`ValkarnTaskCompletionSource<T>` और non-generic `ValkarnTaskCompletionSource` आपको एक `ValkarnTask` पर manual control देते हैं। ये async equivalent हैं result खुद लिखने के — आप एक source object hold करते हैं, callers को उसका `.Task` देते हैं, और फिर एक separate call site से resolve, fault, या cancel करते हैं।

यह BCL से `TaskCompletionSource<T>` का Valkarn Tasks equivalent है, लेकिन library के बाकी हिस्से के साथ allocation model consistent रखने के लिए `ValkarnTask.Promise<T>` द्वारा backed है।

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

वह task जिसे awaiting code observe करेगा। इसे उन सभी callers को distribute करें जिन्हें result के लिए wait करना है। Multiple callers same task को concurrently `await` कर सकते हैं।

### TrySetResult

```csharp
public bool TrySetResult(T result);
```

Task को दिए गए value के साथ successfully complete करता है। सभी awaiting continuations resume होती हैं। `true` return करता है यदि completion accept हुआ; `false` return करता है यदि task पहले से complete था (किसी prior `TrySet*` call द्वारा)। कभी throw नहीं करता।

### TrySetException

```csharp
public bool TrySetException(Exception ex);
```

Task को provided exception के साथ fault करता है। Awaiting code exception receive करता है जब यह `await` call करता है। `ex` `null` होने पर `ArgumentNullException` throw करता है। `true` return करता है यदि accept हुआ, `false` यदि पहले से complete था।

### TrySetCanceled

```csharp
public bool TrySetCanceled();
```

Task cancel करता है। Awaiting code `OperationCanceledException` receive करता है। `true` return करता है यदि accept हुआ, `false` यदि पहले से complete था।

---

## Non-generic ValkarnTaskCompletionSource

Public API में कोई separate non-generic `ValkarnTaskCompletionSource` class नहीं है। Void-returning manual tasks के लिए, `ValkarnTask.Promise` directly उपयोग करें:

```csharp
var promise = new ValkarnTask.Promise();
ValkarnTask task = promise.Task;

promise.TrySetResult();    // complete
promise.TrySetException(ex);
promise.TrySetCanceled();
```

`ValkarnTask.Promise` same `TrySet*` surface और same double-completion protection expose करता है, लेकिन `ValkarnTask<T>` के बजाय non-generic `ValkarnTask` produce करता है।

---

## Double-Completion Protection

सभी `TrySet*` methods किसी भी thread से किसी भी समय, concurrently सहित, safely call की जा सकती हैं। पहला call जो internal state machine पर compare-and-swap जीतता है succeed करता है; हर subsequent call `false` return करता है और कोई effect नहीं होता। इसका मतलब है:

- `TrySetResult` दो बार call करने पर दूसरे call पर कुछ नहीं होता।
- `TrySetException` के बाद `TrySetResult` call करने पर कुछ नहीं होता।
- दो threads एक ही source को simultaneously complete करने की race करना safe है — एक जीतता है, एक silently ignored होता है।

यदि आपको जानना है कि कौन सा caller "जीता", return value check करें। यदि आप care नहीं करते (fire-and-forget signal), आप इसे safely ignore कर सकते हैं।

```csharp
// Safe race — इनमें से केवल एक actually task complete करेगा
_ = source.TrySetResult(value);
_ = source.TrySetCanceled();
```

---

## Common Patterns

### Callback API bridge करना

कई Unity और platform APIs async/await के बजाय callbacks के माध्यम से results deliver करते हैं। `ValkarnTaskCompletionSource<T>` उन्हें cleanly wrap करने देता है।

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

Callers अब simply returned task `await` कर सकते हैं:

```csharp
Texture2D tex = await LoadTextureAsync("https://example.com/image.png");
```

### One-shot signal (async gate)

`ValkarnTask.Promise` का उपयोग तब करें जब आपको एक signal की आवश्यकता हो जो एक बार fire हो और किसी भी number of waiters को unblock करे। यह `ManualResetEventSlim` के समान है लेकिन async-native है।

```csharp
public class AsyncGate
{
    readonly ValkarnTask.Promise _promise = new();

    // कोई भी number of callers इसे await कर सकते हैं
    public ValkarnTask WaitAsync() => _promise.Task;

    // सबको unblock करने के लिए एक बार call करें
    public void Open() => _promise.TrySetResult();
}

// Usage
var gate = new AsyncGate();

// कई systems independently gate await करते हैं
async ValkarnTask SystemAAsync()
{
    await gate.WaitAsync();
    // gate खुलने के बाद proceed
}

async ValkarnTask SystemBAsync()
{
    await gate.WaitAsync();
    // SystemA के साथ same समय पर proceed होता है
}

// किसी और जगह — gate खोलें
gate.Open();
```

एक बार `TrySetResult()` call होने के बाद, task पर सभी current और future `await` calls immediately complete होती हैं (synchronously यदि task पहले से done है जब वे run होती हैं)।

### Cancellation support के साथ third-party async operation wrap करना

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

### Deferred initialization gate

एक common Unity pattern है एक "ready" task expose करना जिसे components await कर सकते हैं चाहे initialization शुरू हुई हो या नहीं।

```csharp
public class ServiceBus : MonoBehaviour
{
    readonly ValkarnTask.Promise _readyPromise = new();

    // कोई भी किसी भी point पर इसे await कर सकता है — Initialize() से पहले या बाद में
    public ValkarnTask Ready => _readyPromise.Task;

    async void Start()
    {
        await LoadConfigAsync();
        await ConnectAsync();
        _readyPromise.TrySetResult();  // सभी waiters unblock
    }
}

// किसी अन्य component में
async ValkarnTask OnEnableAsync()
{
    await ServiceBus.Instance.Ready;  // ready नहीं है तो wait करता है, पहले से ready है तो instantly return
    DoWork();
}
```

---

## ValkarnTask.Promise के साथ संबंध

`ValkarnTaskCompletionSource<T>` `ValkarnTask.Promise<T>` के आसपास एक thin public wrapper है। दोनों same functionality प्रदान करते हैं। अंतर naming convention का है:

| Type | Returns | Common use |
|------|---------|------------|
| `ValkarnTaskCompletionSource<T>` | `ValkarnTask<T>` | Public API, BCL `TaskCompletionSource<T>` style mirror करता है |
| `ValkarnTask.Promise<T>` | `ValkarnTask<T>` | Internal use, थोड़ा अधिक direct |
| `ValkarnTask.Promise` | `ValkarnTask` | Void signals (gates, events) |

तीनों अपना task `ValkarnTaskCompletionCore<T>` के साथ back करते हैं, internal struct-based state machine जो CAS-based two-phase protocol का उपयोग करके thread safety और continuation/result race handle करता है।
