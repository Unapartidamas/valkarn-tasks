---
sidebar_position: 2
title: Channels
---

# Channels

Channels producers और consumers के बीच data pass करने के लिए thread-safe, async-capable pipeline प्रदान करते हैं। ये विशेष रूप से game systems के लिए suited हैं जहाँ काम एक side पर generate होता है (background threads, event callbacks, job completions) और दूसरे पर consume किया जाना है (मुख्य thread, worker pools)।

## Channel बनाना

Channels `VlkTask.Channel` static factory class के माध्यम से बनाए जाते हैं। कोई public constructor नहीं है।

### Unbounded Channel

```csharp
Channel<T> channel = VlkTask.Channel.CreateUnbounded<T>(bool multiConsumer = false);
```

Unbounded channel की कोई capacity limit नहीं है। `WriteAsync` और `TryWrite` हमेशा immediately succeed होते हैं जब तक channel complete नहीं हुआ। Items internal `Queue<T>` में accumulate होते हैं जब तक consumer उन्हें read नहीं करता।

**Parameters**

| Parameter | Default | विवरण |
|-----------|---------|-------------|
| `multiConsumer` | `false` | `true` होने पर, multiple concurrent `ReadAsync` calls supported हैं (competing consumers)। `false` होने पर, एक समय में केवल एक reader `ReadAsync` await कर सकता है। |

### Bounded Channel

```csharp
Channel<T> channel = VlkTask.Channel.CreateBounded<T>(int capacity, bool multiConsumer = false);
```

Bounded channel fixed-size ring buffer में अधिकतम `capacity` items hold करता है। Buffer full होने पर, `WriteAsync` calling code को asynchronously suspend करता है जब तक space available न हो (backpressure)। `TryWrite` wait करने के बजाय full होने पर immediately `false` return करता है।

**Parameters**

| Parameter | Default | विवरण |
|-----------|---------|-------------|
| `capacity` | आवश्यक | Buffer hold कर सकने वाले maximum items की संख्या। शून्य से अधिक होनी चाहिए। |
| `multiConsumer` | `false` | Unbounded के same — multiple concurrent readers enable करता है। |

**Bounded और unbounded के बीच चुनना**

`CreateBounded` का उपयोग करें जब आपको backpressure apply करनी हो — यानी जब आप चाहते हों कि producers automatically slow down करें यदि consumers पीछे रह जाएँ। `CreateUnbounded` का उपयोग करें जब producer rate naturally bounded हो (जैसे input events) और unbounded buffering acceptable हो, या जब आपने पहले से memory growth account किया हो।

---

## Channel&lt;T&gt;

`Channel<T>` एक container है जो pipeline के दो sides को separate objects के रूप में expose करता है।

```csharp
public sealed class Channel<T>
{
    public ChannelReader<T> Reader { get; }
    public ChannelWriter<T> Writer { get; }
}
```

Producer side पर `Writer` reference और consumer side पर `Reader` reference रखें। उनके same thread पर होने की कोई आवश्यकता नहीं है।

---

## ChannelWriter&lt;T&gt;

`ChannelWriter<T>` channel का write side है। `channel.Writer` से प्राप्त करें।

### TryWrite

```csharp
public abstract bool TryWrite(T item);
```

Suspending के बिना item write करने का attempt करता है। `true` return करता है यदि item accept हुआ; `false` return करता है यदि channel full (bounded) या complete हो चुका है।

`TryWrite` का उपयोग hot paths में करें जहाँ आप items discard करने का afford कर सकते हैं, या जब आप loop में poll करते हैं और async state machines से allocation avoid करना चाहते हैं।

```csharp
if (!channel.Writer.TryWrite(item))
{
    // channel full या closed है — accordingly handle करें
}
```

### WriteAsync

```csharp
public abstract VlkTask WriteAsync(T item);
```

Channel में item write करता है, आवश्यक होने पर caller को asynchronously suspend करता है।

- **Unbounded channels**: channel open होने पर हमेशा synchronously complete होता है (zero-allocation fast path)।
- **Bounded channels**: buffer में space होने पर synchronously complete होता है; buffer full होने पर caller suspend और pending-writer record queue होता है। Consumer item read करने और slot free करते ही caller resume होता है।

Write से पहले या दौरान `Complete()` के माध्यम से channel complete होने पर `ChannelClosedException` throw करता है।

```csharp
await channel.Writer.WriteAsync(item);
```

Multiple producers bounded channel पर concurrently `WriteAsync` call कर सकते हैं। प्रत्येक suspended writer queued होता है और FIFO order में space available होते ही unblocked होता है।

### Complete

```csharp
public abstract void Complete();
```

Signal करता है कि अधिक items नहीं लिखे जाएँगे। `Complete()` call होने के बाद:

- पहले से written items buffer में रहते हैं और अभी भी consume किए जा सकते हैं।
- `WriteAsync` या `TryWrite` के नए calls `ChannelClosedException` के साथ fail होंगे।
- Buffer fully drained होने के बाद, `ChannelReader<T>.Completion` complete होता है और कोई pending या future `ReadAsync` calls `ChannelClosedException` throw करती हैं।

`Complete()` single call पर idempotent है — इसे एक से अधिक बार call करना safe है (subsequent calls ignored हैं)।

```csharp
// काम का अंत signal करें
channel.Writer.Complete();
```

---

## ChannelReader&lt;T&gt;

`ChannelReader<T>` channel का read side है। `channel.Reader` से प्राप्त करें।

### ReadAsync

```csharp
public abstract VlkTask<T> ReadAsync();
```

Channel से अगला item read करता है। यदि कोई item currently available नहीं है, caller asynchronously suspend होता है जब तक एक arrive न हो। Channel complete और fully drained होने पर `ChannelClosedException` throw करता है।

```csharp
T item = await channel.Reader.ReadAsync();
```

**Single-consumer mode** (default): एक समय में केवल एक `ReadAsync` in-flight हो सकता है। दूसरी concurrent `ReadAsync` शुरू करने पर immediately throw होता है। यह restriction एक internal zero-allocation optimization enable करती है — reader core channel implementation में directly embedded होता है बजाय per call pool से allocate होने के।

**Multi-consumer mode** (`multiConsumer: true`): कोई भी number of `ReadAsync` calls simultaneously pending हो सकती हैं। प्रत्येक pending caller queued होता है और items available होने पर FIFO order में resolve होता है।

### TryRead

```csharp
public abstract bool TryRead(out T item);
```

Suspending के बिना item read करने का attempt करता है। `true` return करता है और `item` populate करता है यदि item available था; `false` return करता है यदि channel empty था (`item` `default` पर set)।

`TryRead` empty-but-open channel और empty-and-closed channel के बीच distinguish नहीं करता। `TryRead` को polling loop में उपयोग करते समय closed state detect करने के लिए `Completion` का उपयोग करें।

### ReadAllAsync

```csharp
public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default);
```

एक `IAsyncEnumerable<T>` return करता है जो सभी items iterate करता है जब तक channel complete और drained न हो। Enumeration `ChannelClosedException` propagate किए बिना cleanly end होती है — यह simply stop हो जाती है।

```csharp
await foreach (var item in channel.Reader.ReadAllAsync(ct))
{
    Process(item);
}
// यहाँ पहुँचा जब channel complete और empty हो
```

`ReadAllAsync` को pass किया गया cancellation token fallback के रूप में उपयोग होता है। यदि `GetAsyncEnumerator` को भी token pass किया गया है (जैसा `await foreach` `WithCancellation` के माध्यम से करता है), foreach-side token precedence लेता है।

### Completion

```csharp
public abstract VlkTask Completion { get; }
```

एक `VlkTask` जो `Complete()` call होने के बाद channel fully drained होने पर complete होता है। Specifically:

- यदि already-empty channel पर `Complete()` call होता है, `Completion` immediately resolve होता है।
- यदि items buffer में रहते हुए `Complete()` call होता है, `Completion` केवल आखिरी item consume होने के बाद resolve होता है।

Pipeline finish होने के लिए wait करने का canonical तरीका `Completion` await करना है।

```csharp
channel.Writer.Complete();
await channel.Reader.Completion;
// सभी items consume हो गए
```

---

## ChannelClosedException

```csharp
public sealed class ChannelClosedException : InvalidOperationException
```

दो circumstances में throw होता है:

1. **Complete, drained channel से read करना** — `ReadAsync()` throw करता है जब channel complete marked और कोई items नहीं बचे।
2. **Complete channel पर write करना** — `WriteAsync()` throw करता है जब `Complete()` write से पहले call हुआ था।

`ChannelClosedException` `InvalidOperationException` से inherit करता है। `TryRead` या `TryWrite` द्वारा throw नहीं किया जाता, जो `false` return करते हैं।

Constructors:

```csharp
new ChannelClosedException()
new ChannelClosedException(string message)
new ChannelClosedException(Exception innerException)
```

---

## Bounded Channel: Backpressure विस्तार में

जब bounded channel का buffer full हो और `WriteAsync` call हो, writer suspend होता है और एक pending-writer record internally enqueue होता है। Writer अपना item hold करता है। जब consumer `ReadAsync` या `TryRead` call करता है और item dequeue करता है:

1. Freed slot immediately oldest pending writer द्वारा claim होता है।
2. उस writer का item buffer में place होता है।
3. Writer का awaiting code resume होता है।

इसका मतलब है कि full bounded channel items कभी नहीं खोता और buffer capacity कभी waste नहीं होती — slot free होने और blocked writer resume होने के बीच हमेशा one-to-one correspondence है। उन cases में जहाँ reader arrive होता है जब pending writers wait कर रहे हैं लेकिन buffer empty है, item buffer को touch किए बिना directly hand-off होता है।

---

## Patterns

### Basic producer/consumer

```csharp
var channel = VlkTask.Channel.CreateUnbounded<WorkItem>();

// Producer (जैसे background thread या callbacks से run होता है)
async VlkTask ProduceAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var work = await FetchNextWorkItemAsync(ct);
        await channel.Writer.WriteAsync(work);
    }
    channel.Writer.Complete();
}

// Consumer (जहाँ भी आप इसे call करें वहाँ run होता है)
async VlkTask ConsumeAsync()
{
    await foreach (var item in channel.Reader.ReadAllAsync())
    {
        await ProcessAsync(item);
    }
}
```

### Multiple producers, single consumer

Multiple producers प्रत्येक `channel.Writer` का reference hold कर सकते हैं और concurrently `WriteAsync` call कर सकते हैं। सभी channel operations internal lock द्वारा protected हैं, इसलिए यह safe है।

```csharp
var channel = VlkTask.Channel.CreateBounded<Event>(capacity: 64);

// कई producers concurrently write करते हैं
VlkTask ProducerA() => ProduceFrom(sourceA, channel.Writer);
VlkTask ProducerB() => ProduceFrom(sourceB, channel.Writer);
VlkTask ProducerC() => ProduceFrom(sourceC, channel.Writer);

// Single consumer (default — कोई multiConsumer flag आवश्यक नहीं)
async VlkTask ConsumerAsync()
{
    await foreach (var ev in channel.Reader.ReadAllAsync())
        HandleEvent(ev);
}
```

Multiple producers के साथ bounded channel उपयोग करते समय, `Complete()` carefully coordinate करें — इसे केवल तभी call करें जब सभी producers writing finish कर लें, अन्यथा कुछ writers `ChannelClosedException` receive कर सकते हैं।

### Multiple producers, multiple consumers

```csharp
// multiConsumer: true कई consumers से concurrent ReadAsync enable करता है
var channel = VlkTask.Channel.CreateUnbounded<Job>(multiConsumer: true);

async VlkTask WorkerAsync(int id, CancellationToken ct)
{
    try
    {
        while (true)
        {
            var job = await channel.Reader.ReadAsync();
            await ExecuteJobAsync(job, ct);
        }
    }
    catch (ChannelClosedException)
    {
        // Channel done — gracefully exit
    }
}
```

प्रत्येक item exactly एक consumer को delivered होता है। Consumers FIFO order में items के लिए compete करते हैं (जो consumer सबसे लंबे समय से wait कर रहा है उसे अगला available item मिलता है)।

### Graceful shutdown

Recommended shutdown sequence है:

1. सभी producers को stop signal करें (जैसे उनका `CancellationToken` cancel करें)।
2. सभी producers के writing stop करने के बाद `channel.Writer.Complete()` call करें।
3. Confirm करने के लिए कि सभी items consume हो गए `channel.Reader.Completion` await करें।

```csharp
cts.Cancel();                        // producers stop करें
await allProducersTask;              // उनके exit के लिए wait करें
channel.Writer.Complete();           // channel seal करें
await channel.Reader.Completion;     // remaining items drain करें
```

यदि आप `ReadAllAsync` उपयोग कर रहे हैं, step 3 automatically होता है — `await foreach` loop तब exit होता है जब channel complete और empty हो।

---

## System.Threading.Channels से तुलना

| Feature | Valkarn Channels | System.Threading.Channels |
|---------|-----------------|---------------------------|
| Return type | `VlkTask` / `VlkTask<T>` | `ValueTask` / `ValueTask<T>` |
| Allocation (hot path) | शून्य (single-consumer unbounded) | Near-zero |
| `WaitToReadAsync` | Present नहीं — `ReadAsync` या `ReadAllAsync` उपयोग करें | Present |
| `TryComplete(Exception)` | Present नहीं — `Complete()` उपयोग करें | Present |
| `Count` / `CanCount` | Exposed नहीं | कुछ channel types पर present |
| Full होने पर Drop policy | Supported नहीं — `WriteAsync` blocks | `DropWrite`, `DropNewest`, `DropOldest`, `Wait` |
| Async enumerable | `ReadAllAsync()` | `ReadAllAsync()` |
| Thread safety | Full (lock-based) | Full (lock-based) |

मुख्य अंतर यह है कि Valkarn Channels नatively `VlkTask` के साथ integrate होते हैं Unity builds में zero-overhead awaiting के लिए, और single-consumer path हर `ReadAsync` call पर pool rent/return avoid करता है completion core को channel में directly embed करके।
