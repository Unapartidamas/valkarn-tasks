---
sidebar_position: 2
title: ValkarnTask<T>
---

# `ValkarnTask<T>`

`ValkarnTask<T>` Valkarn Tasks में value-returning async task type है। यह एक `readonly struct` है, जो synchronously complete होने पर inline result carry करता है, या asynchronously complete होने पर pooled source object का reference carry करता है।

**Namespace:** `UnaPartidaMas.Valkarn.Tasks`

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncValkarnTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
```

`T` पर कोई generic constraints नहीं हैं। कोई भी type — value type, reference type, struct, या class — valid है।

---

## Instances बनाना

### Synchronously completed tasks

ये factory methods बिना किसी backing source object के `ValkarnTask<T>` return करती हैं। शून्य आवंटन।

#### `ValkarnTask.FromResult<T>(T value)`

`value` inline carry करने वाला एक completed `ValkarnTask<T>` return करता है। Non-generic `ValkarnTask` type पर static method के रूप में declare किया गया।

```csharp
public static ValkarnTask<T> FromResult<T>(T value)
```

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(42);
ValkarnTask<string> name = ValkarnTask.FromResult("Valkarn");
ValkarnTask<Vector3> pos = ValkarnTask.FromResult(transform.position);
```

Returned struct का `source == null` है। इसे await करने पर कोई continuation allocation नहीं होती — compiler तुरंत `IsCompleted == true` देखता है।

#### `ValkarnTask.FromException<T>(Exception exception)`

एक faulted `ValkarnTask<T>` return करता है। इसे await करने पर exception `ExceptionDispatchInfo` के माध्यम से original stack trace preserve करते हुए re-throw होती है।

```csharp
public static ValkarnTask<T> FromException<T>(Exception exception)
```

```csharp
ValkarnTask<Texture2D> LoadTexture(string path)
{
    if (string.IsNullOrEmpty(path))
        return ValkarnTask.FromException<Texture2D>(
            new ArgumentException("Path must not be empty.", nameof(path)));

    return LoadTextureAsync(path);
}
```

#### `ValkarnTask.FromCanceled<T>(CancellationToken ct = default)`

एक canceled `ValkarnTask<T>` return करता है। इसे await करने पर `OperationCanceledException` throw होती है।

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

### `async` methods के माध्यम से

`ValkarnTask<T>` return करने के लिए declare कोई भी `async` method automatically `AsyncValkarnTaskMethodBuilder<TResult>` उपयोग करती है:

```csharp
async ValkarnTask<int> ComputeAsync()
{
    await ValkarnTask.Yield();
    return 42;
}
```

Compiler एक state machine generate करता है। यदि method synchronously complete होती है (कभी suspend नहीं होती), `AsyncValkarnTaskMethodBuilder<T>.Task` `source == null` के साथ `new ValkarnTask<T>(result)` return करता है — शून्य allocation।

---

## Thread pool पर काम run करना

ये methods .NET thread pool पर एक delegate run करती हैं और मुख्य thread पर result return करती हैं (specified `PlayerLoopTiming` पर)। ये longer-named `RunOnThreadPool` variants पर convenience wrappers हैं।

#### `ValkarnTask.Run<T>(Func<T> func, PlayerLoopTiming timing, CancellationToken ct)`

Thread pool पर synchronous `Func<T>` run करता है।

```csharp
public static ValkarnTask<T> Run<T>(
    Func<T> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
// Thread pool पर compute करें, result अगले Update पर मुख्य thread पर वापस आता है
int hash = await ValkarnTask.Run(() => ComputeExpensiveHash(data));
```

#### `ValkarnTask.Run<T>(Func<ValkarnTask<T>> func, PlayerLoopTiming timing, CancellationToken ct)`

Thread pool पर async `Func<ValkarnTask<T>>` run करता है। इसका उपयोग करें जब काम खुद async हो (जैसे file I/O)।

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
public ValkarnTask.Status GetStatus()
```

Current `ValkarnTask.Status` return करता है। Possible values: `Pending`, `Succeeded`, `Faulted`, `Canceled`। `source == null` के लिए, हमेशा `Succeeded` return करता है।

### `GetAwaiter()`

```csharp
public Awaiter GetAwaiter()
```

एक `Awaiter` struct return करता है। Compiler द्वारा `await` implement करने के लिए उपयोग किया जाता है। आप इसे synchronously result obtain करने के लिए directly भी call कर सकते हैं (केवल तभी safe जब `IsCompleted` true हो)।

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(10);
int value = task.GetAwaiter().GetResult(); // safe — sync-completed
```

Pending task पर `GetResult()` call करने पर `InvalidOperationException` throw होती है।

### `AsNonGeneric()`

```csharp
public ValkarnTask AsNonGeneric()
```

इस `ValkarnTask<T>` को non-generic `ValkarnTask` में convert करता है, result type discard करता है। Resulting task same underlying source और token share करता है, इसलिए same समय पर complete होता है।

```csharp
ValkarnTask<int> typedTask = ComputeAsync();
ValkarnTask voidTask = typedTask.AsNonGeneric();
await voidTask;   // completion के लिए wait करता है, result ignore करता है
```

यह तब उपयोगी है जब mixed-type tasks को combinators में pass करना हो या जब आप केवल completion timing care करते हों, value नहीं।

---

## `ValkarnTask<T>` return करने वाले Combinators

### `WhenAll` — typed two-task overload

```csharp
public static ValkarnTask<(T1, T2)> WhenAll<T1, T2>(
    ValkarnTask<T1> task1, ValkarnTask<T2> task2)
```

दोनों tasks concurrently run करता है और results का tuple return करता है। यदि कोई task fault या cancel होता है, पहली exception जीतती है और दूसरे की error `ValkarnTask.UnobservedException` के माध्यम से reported होती है।

**Tuple destructuring** C# deconstruction के साथ naturally काम करती है:

```csharp
var (profile, inventory) = await ValkarnTask.WhenAll(
    FetchProfileAsync(userId),
    FetchInventoryAsync(userId)
);
```

**Zero-alloc fast path:** यदि call site पर दोनों tasks synchronously completed हैं, कोई pooled object create नहीं होता और result tuple inline return होता है।

### `WhenAll` — typed collection overload

```csharp
public static ValkarnTask<T[]> WhenAll<T>(IEnumerable<ValkarnTask<T>> tasks)
```

Collection में सभी tasks concurrently await करता है और index order में `T[]` return करता है।

```csharp
var urls = new[] { "https://a.com", "https://b.com", "https://c.com" };
ValkarnTask<string>[] downloads = urls.Select(u => DownloadAsync(u)).ToArray();
string[] results = await ValkarnTask.WhenAll(downloads);
```

यदि collection empty है, `ValkarnTask.FromResult(Array.Empty<T>())` return करता है — शून्य allocation। यदि सभी tasks पहले से synchronously completed हैं, combinator promise create किए बिना result array inline बनाया जाता है।

### `WhenAny` — typed two-task overload

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    ValkarnTask<T> task1, ValkarnTask<T> task2)
```

जैसे ही कोई भी task complete होता है return करता है। Result tuple में winner का 0-based index और उसका value होता है। Losing tasks run करती रहती हैं; उनकी errors (यदि कोई हो) `ValkarnTask.UnobservedException` के माध्यम से reported होती हैं। Losing cancellations intentionally report नहीं की जाती।

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(
    FetchFromCacheAsync(key),
    FetchFromNetworkAsync(key)
);

if (winnerIndex == 0)
    Debug.Log("Cache जीता");
```

### `WhenAny` — typed collection overload

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    IEnumerable<ValkarnTask<T>> tasks)
```

Two-task overload के same semantics, किसी भी number of tasks तक extended। कम से कम एक task आवश्यक है; empty collections के लिए `ArgumentException` throw होती है।

```csharp
var tasks = servers.Select(s => s.FetchAsync(query)).ToArray();
var (winnerIndex, data) = await ValkarnTask.WhenAny(tasks);
Debug.Log($"Server {winnerIndex} ने पहले respond किया");
```

---

## Factory convenience methods

### `ValkarnTask.Create<T>(Func<ValkarnTask<T>> factory)`

Factory delegate invoke करता है और resulting task await करता है। तब उपयोगी जब आप async operation के construction को defer करना चाहते हों।

```csharp
public static async ValkarnTask<T> Create<T>(Func<ValkarnTask<T>> factory)
```

```csharp
var result = await ValkarnTask.Create(() => LoadLevelDataAsync(levelId));
```

---

## `Awaiter` struct (nested)

`ValkarnTask<T>.Awaiter` compiler-facing awaiter है। यह `ICriticalNotifyCompletion` implement करने वाला `readonly struct` है। आप normally इससे directly interact नहीं करते।

```csharp
public readonly struct Awaiter : ICriticalNotifyCompletion
{
    public bool IsCompleted { get; }
    public T GetResult();
    public void OnCompleted(Action continuation);
    public void UnsafeOnCompleted(Action continuation);
}
```

`UnsafeOnCompleted` वह path है जो `AsyncValkarnTaskMethodBuilder<T>` द्वारा उपयोग की जाती है। "unsafe" label का अर्थ है `ExecutionContext` capture नहीं होता — यह Unity के लिए intentional है जहाँ कोई `SynchronizationContext` effect में नहीं है।

---

## `ValkarnTask<T>` पर Extension methods

### `AsResult<T>()`

```csharp
public static ValkarnTask<Result<T>> AsResult<T>(this ValkarnTask<T> task)
```

`ValkarnTask<T>` को `ValkarnTask<Result<T>>` में wrap करता है, किसी भी exception या cancellation को catch करके `Result<T>` value में encode करता है। यह call site पर try/catch से बचाता है।

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

`ValkarnTask.Promise<T>` उन cases के लिए heap-allocated manual completion source है जहाँ आपको control करना है कि `ValkarnTask<T>` कब complete होता है, और lifetime एक single async operation से bounded नहीं है।

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
// Callback-based API wrap करना
var promise = new ValkarnTask.Promise<string>();

SomeCallbackApi.OnComplete += value => promise.TrySetResult(value);
SomeCallbackApi.OnError   += ex    => promise.TrySetException(ex);

string result = await promise.Task;
```

`PooledPromise<T>` के विपरीत, `Promise<T>` pooled नहीं है। यह task fault होने और caller इसे कभी await न करने पर unobserved exceptions detect और report करने के लिए finalizer का उपयोग करता है।

High-frequency patterns (producer/consumer loops, per-frame operations) के लिए, `ValkarnTask.PooledPromise<T>` prefer करें, जो `GetResult` call होने के बाद automatically pool में return होता है।

---

## `PooledPromise<T>` — pooled manual completion source

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

Backing task पर `GetResult` call होने के बाद, promise अपना `ValkarnTaskCompletionCore<T>` reset करता है और खुद को pool में return करता है। एक double-return guard सुनिश्चित करता है कि यह अधिकतम एक बार हो चाहे `GetResult` concurrently call हो।

```csharp
// Pattern: ValkarnTask<T> produce करें जो ready होने पर complete हो
var promise = ValkarnTask.PooledPromise<int>.Create(out uint token);
ValkarnTask<int> task = promise.Task;

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

## `ValkarnTask<T>` प्राप्त करने के तरीकों का सारांश

| Method | कब उपयोग करें |
|--------|------------|
| `async ValkarnTask<T>` के अंदर `return value` | Normal async methods |
| `ValkarnTask.FromResult(value)` | Synchronous fast returns |
| `ValkarnTask.FromException<T>(ex)` | Pre-faulted tasks |
| `ValkarnTask.FromCanceled<T>(ct)` | Pre-canceled tasks |
| `ValkarnTask.Run<T>(Func<T>, ...)` | Thread-pool offloading |
| `ValkarnTask.Run<T>(Func<ValkarnTask<T>>, ...)` | Async thread-pool काम |
| `ValkarnTask.WhenAll<T1,T2>(t1, t2)` | दो typed tasks के लिए wait करें, tuple प्राप्त करें |
| `ValkarnTask.WhenAll<T>(IEnumerable<...>)` | N typed tasks के लिए wait करें, array प्राप्त करें |
| `ValkarnTask.WhenAny<T>(t1, t2)` | दो typed tasks में से पहला |
| `ValkarnTask.WhenAny<T>(IEnumerable<...>)` | N typed tasks में से पहला |
| `task.AsResult<T>()` | Exception-safe wrapping |
| `new ValkarnTask.Promise<T>()` → `.Task` | Long-lived manual completion |
| `ValkarnTask.PooledPromise<T>.Create(...)` → `.Task` | High-frequency manual completion |
