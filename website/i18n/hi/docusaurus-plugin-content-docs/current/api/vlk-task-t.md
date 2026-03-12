---
sidebar_position: 2
title: VlkTask<T>
---

# `VlkTask<T>`

`VlkTask<T>` Valkarn Tasks में value-returning async task type है। यह एक `readonly struct` है, जो synchronously complete होने पर inline result carry करता है, या asynchronously complete होने पर pooled source object का reference carry करता है।

**Namespace:** `UnaPartidaMas.Valkarn.Tasks`

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncVlkTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct VlkTask<T>
```

`T` पर कोई generic constraints नहीं हैं। कोई भी type — value type, reference type, struct, या class — valid है।

---

## Instances बनाना

### Synchronously completed tasks

ये factory methods बिना किसी backing source object के `VlkTask<T>` return करती हैं। शून्य आवंटन।

#### `VlkTask.FromResult<T>(T value)`

`value` inline carry करने वाला एक completed `VlkTask<T>` return करता है। Non-generic `VlkTask` type पर static method के रूप में declare किया गया।

```csharp
public static VlkTask<T> FromResult<T>(T value)
```

```csharp
VlkTask<int> task = VlkTask.FromResult(42);
VlkTask<string> name = VlkTask.FromResult("Valkarn");
VlkTask<Vector3> pos = VlkTask.FromResult(transform.position);
```

Returned struct का `source == null` है। इसे await करने पर कोई continuation allocation नहीं होती — compiler तुरंत `IsCompleted == true` देखता है।

#### `VlkTask.FromException<T>(Exception exception)`

एक faulted `VlkTask<T>` return करता है। इसे await करने पर exception `ExceptionDispatchInfo` के माध्यम से original stack trace preserve करते हुए re-throw होती है।

```csharp
public static VlkTask<T> FromException<T>(Exception exception)
```

```csharp
VlkTask<Texture2D> LoadTexture(string path)
{
    if (string.IsNullOrEmpty(path))
        return VlkTask.FromException<Texture2D>(
            new ArgumentException("Path must not be empty.", nameof(path)));

    return LoadTextureAsync(path);
}
```

#### `VlkTask.FromCanceled<T>(CancellationToken ct = default)`

एक canceled `VlkTask<T>` return करता है। इसे await करने पर `OperationCanceledException` throw होती है।

```csharp
public static VlkTask<T> FromCanceled<T>(CancellationToken ct = default)
```

```csharp
VlkTask<byte[]> Download(string url, CancellationToken ct)
{
    if (ct.IsCancellationRequested)
        return VlkTask.FromCanceled<byte[]>(ct);

    return DownloadAsync(url, ct);
}
```

### `async` methods के माध्यम से

`VlkTask<T>` return करने के लिए declare कोई भी `async` method automatically `AsyncVlkTaskMethodBuilder<TResult>` उपयोग करती है:

```csharp
async VlkTask<int> ComputeAsync()
{
    await VlkTask.Yield();
    return 42;
}
```

Compiler एक state machine generate करता है। यदि method synchronously complete होती है (कभी suspend नहीं होती), `AsyncVlkTaskMethodBuilder<T>.Task` `source == null` के साथ `new VlkTask<T>(result)` return करता है — शून्य allocation।

---

## Thread pool पर काम run करना

ये methods .NET thread pool पर एक delegate run करती हैं और मुख्य thread पर result return करती हैं (specified `PlayerLoopTiming` पर)। ये longer-named `RunOnThreadPool` variants पर convenience wrappers हैं।

#### `VlkTask.Run<T>(Func<T> func, PlayerLoopTiming timing, CancellationToken ct)`

Thread pool पर synchronous `Func<T>` run करता है।

```csharp
public static VlkTask<T> Run<T>(
    Func<T> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
// Thread pool पर compute करें, result अगले Update पर मुख्य thread पर वापस आता है
int hash = await VlkTask.Run(() => ComputeExpensiveHash(data));
```

#### `VlkTask.Run<T>(Func<VlkTask<T>> func, PlayerLoopTiming timing, CancellationToken ct)`

Thread pool पर async `Func<VlkTask<T>>` run करता है। इसका उपयोग करें जब काम खुद async हो (जैसे file I/O)।

```csharp
public static VlkTask<T> Run<T>(
    Func<VlkTask<T>> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
string json = await VlkTask.Run(async () =>
{
    using var reader = File.OpenText("data.json");
    return await reader.ReadToEndAsync();
});
```

दोनों `Run` overloads token पहले से cancelled होने पर early cancel करते हैं, और काम complete होने के बाद `timing` पर मुख्य thread पर switch वापस करते हैं।

---

## Instance members

### `IsCompleted`

```csharp
public bool IsCompleted { get; }
```

`true` return करता है यदि task किसी भी terminal state में complete हो गया है (Succeeded, Faulted, या Canceled)। Synchronously completed tasks (`source == null`) के लिए, बिना किसी interface dispatch के हमेशा `true` return करता है।

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
public VlkTask.Status GetStatus()
```

Current `VlkTask.Status` return करता है। Possible values: `Pending`, `Succeeded`, `Faulted`, `Canceled`। `source == null` के लिए, हमेशा `Succeeded` return करता है।

### `GetAwaiter()`

```csharp
public Awaiter GetAwaiter()
```

एक `Awaiter` struct return करता है। Compiler द्वारा `await` implement करने के लिए उपयोग किया जाता है। आप इसे synchronously result obtain करने के लिए directly भी call कर सकते हैं (केवल तभी safe जब `IsCompleted` true हो)।

```csharp
VlkTask<int> task = VlkTask.FromResult(10);
int value = task.GetAwaiter().GetResult(); // safe — sync-completed
```

Pending task पर `GetResult()` call करने पर `InvalidOperationException` throw होती है।

### `AsNonGeneric()`

```csharp
public VlkTask AsNonGeneric()
```

इस `VlkTask<T>` को non-generic `VlkTask` में convert करता है, result type discard करता है। Resulting task same underlying source और token share करता है, इसलिए same समय पर complete होता है।

```csharp
VlkTask<int> typedTask = ComputeAsync();
VlkTask voidTask = typedTask.AsNonGeneric();
await voidTask;   // completion के लिए wait करता है, result ignore करता है
```

यह तब उपयोगी है जब mixed-type tasks को combinators में pass करना हो या जब आप केवल completion timing care करते हों, value नहीं।

---

## `VlkTask<T>` return करने वाले Combinators

### `WhenAll` — typed two-task overload

```csharp
public static VlkTask<(T1, T2)> WhenAll<T1, T2>(
    VlkTask<T1> task1, VlkTask<T2> task2)
```

दोनों tasks concurrently run करता है और results का tuple return करता है। यदि कोई task fault या cancel होता है, पहली exception जीतती है और दूसरे की error `VlkTask.UnobservedException` के माध्यम से reported होती है।

**Tuple destructuring** C# deconstruction के साथ naturally काम करती है:

```csharp
var (profile, inventory) = await VlkTask.WhenAll(
    FetchProfileAsync(userId),
    FetchInventoryAsync(userId)
);
```

**Zero-alloc fast path:** यदि call site पर दोनों tasks synchronously completed हैं, कोई pooled object create नहीं होता और result tuple inline return होता है।

### `WhenAll` — typed collection overload

```csharp
public static VlkTask<T[]> WhenAll<T>(IEnumerable<VlkTask<T>> tasks)
```

Collection में सभी tasks concurrently await करता है और index order में `T[]` return करता है।

```csharp
var urls = new[] { "https://a.com", "https://b.com", "https://c.com" };
VlkTask<string>[] downloads = urls.Select(u => DownloadAsync(u)).ToArray();
string[] results = await VlkTask.WhenAll(downloads);
```

यदि collection empty है, `VlkTask.FromResult(Array.Empty<T>())` return करता है — शून्य allocation। यदि सभी tasks पहले से synchronously completed हैं, combinator promise create किए बिना result array inline बनाया जाता है।

### `WhenAny` — typed two-task overload

```csharp
public static VlkTask<(int winnerIndex, T result)> WhenAny<T>(
    VlkTask<T> task1, VlkTask<T> task2)
```

जैसे ही कोई भी task complete होता है return करता है। Result tuple में winner का 0-based index और उसका value होता है। Losing tasks run करती रहती हैं; उनकी errors (यदि कोई हो) `VlkTask.UnobservedException` के माध्यम से reported होती हैं। Losing cancellations intentionally report नहीं की जाती।

```csharp
var (winnerIndex, result) = await VlkTask.WhenAny(
    FetchFromCacheAsync(key),
    FetchFromNetworkAsync(key)
);

if (winnerIndex == 0)
    Debug.Log("Cache जीता");
```

### `WhenAny` — typed collection overload

```csharp
public static VlkTask<(int winnerIndex, T result)> WhenAny<T>(
    IEnumerable<VlkTask<T>> tasks)
```

Two-task overload के same semantics, किसी भी number of tasks तक extended। कम से कम एक task आवश्यक है; empty collections के लिए `ArgumentException` throw होती है।

```csharp
var tasks = servers.Select(s => s.FetchAsync(query)).ToArray();
var (winnerIndex, data) = await VlkTask.WhenAny(tasks);
Debug.Log($"Server {winnerIndex} ने पहले respond किया");
```

---

## Factory convenience methods

### `VlkTask.Create<T>(Func<VlkTask<T>> factory)`

Factory delegate invoke करता है और resulting task await करता है। तब उपयोगी जब आप async operation के construction को defer करना चाहते हों।

```csharp
public static async VlkTask<T> Create<T>(Func<VlkTask<T>> factory)
```

```csharp
var result = await VlkTask.Create(() => LoadLevelDataAsync(levelId));
```

---

## `Awaiter` struct (nested)

`VlkTask<T>.Awaiter` compiler-facing awaiter है। यह `ICriticalNotifyCompletion` implement करने वाला `readonly struct` है। आप normally इससे directly interact नहीं करते।

```csharp
public readonly struct Awaiter : ICriticalNotifyCompletion
{
    public bool IsCompleted { get; }
    public T GetResult();
    public void OnCompleted(Action continuation);
    public void UnsafeOnCompleted(Action continuation);
}
```

`UnsafeOnCompleted` वह path है जो `AsyncVlkTaskMethodBuilder<T>` द्वारा उपयोग की जाती है। "unsafe" label का अर्थ है `ExecutionContext` capture नहीं होता — यह Unity के लिए intentional है जहाँ कोई `SynchronizationContext` effect में नहीं है।

---

## `VlkTask<T>` पर Extension methods

### `AsResult<T>()`

```csharp
public static VlkTask<Result<T>> AsResult<T>(this VlkTask<T> task)
```

`VlkTask<T>` को `VlkTask<Result<T>>` में wrap करता है, किसी भी exception या cancellation को catch करके `Result<T>` value में encode करता है। यह call site पर try/catch से बचाता है।

```csharp
Result<string> result = await FetchDataAsync(url).AsResult();

if (result.IsSuccess)
    Process(result.Value);
else if (result.IsCanceled)
    Debug.Log("रद्द किया गया");
else
    Debug.LogError(result.Exception);
```

**Sync fast path:** यदि source task पहले से synchronously completed है, `AsResult` बिना किसी async machinery के synchronously return करता है।

---

## `Promise<T>` — manual completion source

`VlkTask.Promise<T>` उन cases के लिए heap-allocated manual completion source है जहाँ आपको control करना है कि `VlkTask<T>` कब complete होता है, और lifetime एक single async operation से bounded नहीं है।

```csharp
public class Promise<T>
{
    public VlkTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

```csharp
// Callback-based API wrap करना
var promise = new VlkTask.Promise<string>();

SomeCallbackApi.OnComplete += value => promise.TrySetResult(value);
SomeCallbackApi.OnError   += ex    => promise.TrySetException(ex);

string result = await promise.Task;
```

`PooledPromise<T>` के विपरीत, `Promise<T>` pooled नहीं है। यह task fault होने और caller इसे कभी await न करने पर unobserved exceptions detect और report करने के लिए finalizer का उपयोग करता है।

High-frequency patterns (producer/consumer loops, per-frame operations) के लिए, `VlkTask.PooledPromise<T>` prefer करें, जो `GetResult` call होने के बाद automatically pool में return होता है।

---

## `PooledPromise<T>` — pooled manual completion source

```csharp
public sealed class PooledPromise<T> : VlkTask.ISource<T>, IPoolNode<PooledPromise<T>>
{
    public static PooledPromise<T> Create(out uint token);
    public static PooledPromise<T> CreateCompleted(T result, out uint token);
    public VlkTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

Backing task पर `GetResult` call होने के बाद, promise अपना `VlkTaskCompletionCore<T>` reset करता है और खुद को pool में return करता है। एक double-return guard सुनिश्चित करता है कि यह अधिकतम एक बार हो चाहे `GetResult` concurrently call हो।

```csharp
// Pattern: VlkTask<T> produce करें जो ready होने पर complete हो
var promise = VlkTask.PooledPromise<int>.Create(out uint token);
VlkTask<int> task = promise.Task;

// काम asynchronously dispatch करें
ThreadPool.QueueUserWorkItem(_ =>
{
    int result = DoWork();
    promise.TrySetResult(result);
});

// Consumer await करता है; completion पर promise automatically pool में return होता है
int value = await task;
```

---

## `VlkTask<T>` प्राप्त करने के तरीकों का सारांश

| Method | कब उपयोग करें |
|--------|------------|
| `async VlkTask<T>` के अंदर `return value` | Normal async methods |
| `VlkTask.FromResult(value)` | Synchronous fast returns |
| `VlkTask.FromException<T>(ex)` | Pre-faulted tasks |
| `VlkTask.FromCanceled<T>(ct)` | Pre-canceled tasks |
| `VlkTask.Run<T>(Func<T>, ...)` | Thread-pool offloading |
| `VlkTask.Run<T>(Func<VlkTask<T>>, ...)` | Async thread-pool काम |
| `VlkTask.WhenAll<T1,T2>(t1, t2)` | दो typed tasks के लिए wait करें, tuple प्राप्त करें |
| `VlkTask.WhenAll<T>(IEnumerable<...>)` | N typed tasks के लिए wait करें, array प्राप्त करें |
| `VlkTask.WhenAny<T>(t1, t2)` | दो typed tasks में से पहला |
| `VlkTask.WhenAny<T>(IEnumerable<...>)` | N typed tasks में से पहला |
| `task.AsResult<T>()` | Exception-safe wrapping |
| `new VlkTask.Promise<T>()` → `.Task` | Long-lived manual completion |
| `VlkTask.PooledPromise<T>.Create(...)` → `.Task` | High-frequency manual completion |
