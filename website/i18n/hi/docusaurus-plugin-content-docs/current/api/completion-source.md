---
sidebar_position: 3
title: Completion Sources
---

# VlkTaskCompletionSource

`VlkTaskCompletionSource<T>` और non-generic `VlkTaskCompletionSource` आपको एक `VlkTask` पर manual control देते हैं। ये async equivalent हैं result खुद लिखने के — आप एक source object hold करते हैं, callers को उसका `.Task` देते हैं, और फिर एक separate call site से resolve, fault, या cancel करते हैं।

यह BCL से `TaskCompletionSource<T>` का Valkarn Tasks equivalent है, लेकिन library के बाकी हिस्से के साथ allocation model consistent रखने के लिए `VlkTask.Promise<T>` द्वारा backed है।

---

## VlkTaskCompletionSource&lt;T&gt;

```csharp
public class VlkTaskCompletionSource<T>
{
    public VlkTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled();
}
```

### Task

```csharp
public VlkTask<T> Task { get; }
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

## Non-generic VlkTaskCompletionSource

Public API में कोई separate non-generic `VlkTaskCompletionSource` class नहीं है। Void-returning manual tasks के लिए, `VlkTask.Promise` directly उपयोग करें:

```csharp
var promise = new VlkTask.Promise();
VlkTask task = promise.Task;

promise.TrySetResult();    // complete
promise.TrySetException(ex);
promise.TrySetCanceled();
```

`VlkTask.Promise` same `TrySet*` surface और same double-completion protection expose करता है, लेकिन `VlkTask<T>` के बजाय non-generic `VlkTask` produce करता है।

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

कई Unity और platform APIs async/await के बजाय callbacks के माध्यम से results deliver करते हैं। `VlkTaskCompletionSource<T>` उन्हें cleanly wrap करने देता है।

```csharp
public VlkTask<Texture2D> LoadTextureAsync(string url)
{
    var tcs = new VlkTaskCompletionSource<Texture2D>();

    StartCoroutine(LoadCoroutine(url, tcs));

    return tcs.Task;
}

IEnumerator LoadCoroutine(string url, VlkTaskCompletionSource<Texture2D> tcs)
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

`VlkTask.Promise` का उपयोग तब करें जब आपको एक signal की आवश्यकता हो जो एक बार fire हो और किसी भी number of waiters को unblock करे। यह `ManualResetEventSlim` के समान है लेकिन async-native है।

```csharp
public class AsyncGate
{
    readonly VlkTask.Promise _promise = new();

    // कोई भी number of callers इसे await कर सकते हैं
    public VlkTask WaitAsync() => _promise.Task;

    // सबको unblock करने के लिए एक बार call करें
    public void Open() => _promise.TrySetResult();
}

// Usage
var gate = new AsyncGate();

// कई systems independently gate await करते हैं
async VlkTask SystemAAsync()
{
    await gate.WaitAsync();
    // gate खुलने के बाद proceed
}

async VlkTask SystemBAsync()
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
public VlkTask<Result> RunWithTimeoutAsync(
    Func<VlkTask<Result>> operation,
    float timeoutSeconds,
    CancellationToken ct)
{
    var tcs = new VlkTaskCompletionSource<Result>();

    RunCoreAsync(tcs, operation, timeoutSeconds, ct).Forget();

    return tcs.Task;
}

async VlkTask RunCoreAsync(
    VlkTaskCompletionSource<Result> tcs,
    Func<VlkTask<Result>> operation,
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
    readonly VlkTask.Promise _readyPromise = new();

    // कोई भी किसी भी point पर इसे await कर सकता है — Initialize() से पहले या बाद में
    public VlkTask Ready => _readyPromise.Task;

    async void Start()
    {
        await LoadConfigAsync();
        await ConnectAsync();
        _readyPromise.TrySetResult();  // सभी waiters unblock
    }
}

// किसी अन्य component में
async VlkTask OnEnableAsync()
{
    await ServiceBus.Instance.Ready;  // ready नहीं है तो wait करता है, पहले से ready है तो instantly return
    DoWork();
}
```

---

## VlkTask.Promise के साथ संबंध

`VlkTaskCompletionSource<T>` `VlkTask.Promise<T>` के आसपास एक thin public wrapper है। दोनों same functionality प्रदान करते हैं। अंतर naming convention का है:

| Type | Returns | Common use |
|------|---------|------------|
| `VlkTaskCompletionSource<T>` | `VlkTask<T>` | Public API, BCL `TaskCompletionSource<T>` style mirror करता है |
| `VlkTask.Promise<T>` | `VlkTask<T>` | Internal use, थोड़ा अधिक direct |
| `VlkTask.Promise` | `VlkTask` | Void signals (gates, events) |

तीनों अपना task `VlkTaskCompletionCore<T>` के साथ back करते हैं, internal struct-based state machine जो CAS-based two-phase protocol का उपयोग करके thread safety और continuation/result race handle करता है।
