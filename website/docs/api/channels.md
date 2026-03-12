---
sidebar_position: 2
title: Channels
---

# Channels

Channels provide a thread-safe, async-capable pipeline for passing data between producers and consumers. They are particularly suited to game systems where work is generated on one side (background threads, event callbacks, job completions) and needs to be consumed on another (main thread, worker pools).

## Creating a Channel

Channels are created through the `ValkarnTask.Channel` static factory class. There is no public constructor.

### Unbounded Channel

```csharp
Channel<T> channel = ValkarnTask.Channel.CreateUnbounded<T>(bool multiConsumer = false);
```

An unbounded channel has no capacity limit. `WriteAsync` and `TryWrite` always succeed immediately as long as the channel has not been completed. Items accumulate in an internal `Queue<T>` until a consumer reads them.

**Parameters**

| Parameter | Default | Description |
|-----------|---------|-------------|
| `multiConsumer` | `false` | When `true`, multiple concurrent `ReadAsync` calls are supported (competing consumers). When `false`, only one reader may await `ReadAsync` at a time. |

### Bounded Channel

```csharp
Channel<T> channel = ValkarnTask.Channel.CreateBounded<T>(int capacity, bool multiConsumer = false);
```

A bounded channel holds at most `capacity` items in a fixed-size ring buffer. When the buffer is full, `WriteAsync` suspends the calling code until space becomes available (backpressure). `TryWrite` returns `false` immediately when full rather than waiting.

**Parameters**

| Parameter | Default | Description |
|-----------|---------|-------------|
| `capacity` | required | Maximum number of items the buffer can hold. Must be greater than zero. |
| `multiConsumer` | `false` | Same as unbounded — enables multiple concurrent readers. |

**Choosing between bounded and unbounded**

Use `CreateBounded` when you need to apply backpressure — that is, when you want producers to slow down automatically if consumers fall behind. Use `CreateUnbounded` when the producer rate is naturally bounded (such as input events) and unbounded buffering is acceptable, or when you have already accounted for memory growth.

---

## Channel&lt;T&gt;

`Channel<T>` is a container that exposes two sides of the pipeline as separate objects.

```csharp
public sealed class Channel<T>
{
    public ChannelReader<T> Reader { get; }
    public ChannelWriter<T> Writer { get; }
}
```

Keep the `Writer` reference on the producer side and the `Reader` reference on the consumer side. There is no requirement for them to be on the same thread.

---

## ChannelWriter&lt;T&gt;

`ChannelWriter<T>` is the write side of the channel. Obtain it from `channel.Writer`.

### TryWrite

```csharp
public abstract bool TryWrite(T item);
```

Attempts to write an item without suspending. Returns `true` if the item was accepted; returns `false` if the channel is full (bounded) or has been completed.

Use `TryWrite` in hot paths where you can afford to discard items, or when you poll in a loop and want to avoid allocation from async state machines.

```csharp
if (!channel.Writer.TryWrite(item))
{
    // channel is full or closed — handle accordingly
}
```

### WriteAsync

```csharp
public abstract ValkarnTask WriteAsync(T item);
```

Writes an item to the channel, suspending the caller asynchronously if necessary.

- **Unbounded channels**: always completes synchronously (zero-allocation fast path) as long as the channel is open.
- **Bounded channels**: completes synchronously when there is space in the buffer; suspends the caller and queues a pending-writer record when the buffer is full. The caller resumes as soon as a consumer reads an item and frees a slot.

Throws `ChannelClosedException` if the channel has been completed via `Complete()` before or during the write.

```csharp
await channel.Writer.WriteAsync(item);
```

Multiple producers may call `WriteAsync` concurrently on a bounded channel. Each suspended writer is queued and unblocked in FIFO order as space becomes available.

### Complete

```csharp
public abstract void Complete();
```

Signals that no more items will be written. After `Complete()` is called:

- Any items already written remain in the buffer and can still be consumed.
- New calls to `WriteAsync` or `TryWrite` will fail with `ChannelClosedException`.
- Once the buffer is fully drained, `ChannelReader<T>.Completion` completes and any pending or future `ReadAsync` calls throw `ChannelClosedException`.

`Complete()` is idempotent on a single call — calling it more than once is safe (subsequent calls are ignored).

```csharp
// Signal end of work
channel.Writer.Complete();
```

---

## ChannelReader&lt;T&gt;

`ChannelReader<T>` is the read side of the channel. Obtain it from `channel.Reader`.

### ReadAsync

```csharp
public abstract ValkarnTask<T> ReadAsync();
```

Reads the next item from the channel. If no item is currently available, the caller suspends asynchronously until one arrives. When the channel is both completed and fully drained, throws `ChannelClosedException`.

```csharp
T item = await channel.Reader.ReadAsync();
```

**Single-consumer mode** (default): only one `ReadAsync` may be in flight at a time. Attempting to start a second concurrent `ReadAsync` throws immediately. This restriction enables an internal zero-allocation optimization — the reader core is embedded directly in the channel implementation rather than being allocated from a pool per call.

**Multi-consumer mode** (`multiConsumer: true`): any number of `ReadAsync` calls may be pending simultaneously. Each pending caller is queued and resolved in FIFO order as items become available.

### TryRead

```csharp
public abstract bool TryRead(out T item);
```

Attempts to read an item without suspending. Returns `true` and populates `item` if an item was available; returns `false` if the channel is empty (sets `item` to `default`).

`TryRead` does not distinguish between an empty-but-open channel and an empty-and-closed channel. Use `Completion` to detect the closed state when using `TryRead` in a polling loop.

### ReadAllAsync

```csharp
public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default);
```

Returns an `IAsyncEnumerable<T>` that iterates over all items until the channel is completed and drained. The enumeration ends cleanly without propagating `ChannelClosedException` — it simply stops.

```csharp
await foreach (var item in channel.Reader.ReadAllAsync(ct))
{
    Process(item);
}
// Reached here when channel is complete and empty
```

The cancellation token passed to `ReadAllAsync` is used as a fallback. If `GetAsyncEnumerator` is also passed a token (as `await foreach` does via `WithCancellation`), the `foreach`-side token takes precedence.

### Completion

```csharp
public abstract ValkarnTask Completion { get; }
```

A `ValkarnTask` that completes when the channel is fully drained after `Complete()` has been called. Specifically:

- If `Complete()` is called on an already-empty channel, `Completion` resolves immediately.
- If `Complete()` is called while items remain in the buffer, `Completion` resolves only after the last item is consumed.

Awaiting `Completion` is the canonical way to wait for a pipeline to finish.

```csharp
channel.Writer.Complete();
await channel.Reader.Completion;
// All items have been consumed
```

---

## ChannelClosedException

```csharp
public sealed class ChannelClosedException : InvalidOperationException
```

Thrown in two circumstances:

1. **Reading from a completed, drained channel** — `ReadAsync()` throws when the channel has been marked complete and no items remain.
2. **Writing to a completed channel** — `WriteAsync()` throws when `Complete()` was called before the write.

`ChannelClosedException` inherits from `InvalidOperationException`. It is not thrown by `TryRead` or `TryWrite`, which return `false` instead.

Constructors:

```csharp
new ChannelClosedException()
new ChannelClosedException(string message)
new ChannelClosedException(Exception innerException)
```

---

## Bounded Channel: Backpressure in Detail

When a bounded channel's buffer is full and `WriteAsync` is called, the writer is suspended and a pending-writer record is enqueued internally. The writer holds onto its item. When a consumer calls `ReadAsync` or `TryRead` and dequeues an item:

1. The freed slot is immediately claimed by the oldest pending writer.
2. That writer's item is placed into the buffer.
3. The writer's awaiting code resumes.

This means a full bounded channel never loses items and never wastes buffer capacity — there is always a one-to-one correspondence between a slot becoming free and a blocked writer resuming. In cases where a reader arrives while pending writers are waiting but the buffer is empty, the item is handed off directly without touching the buffer at all.

---

## Patterns

### Basic producer/consumer

```csharp
var channel = ValkarnTask.Channel.CreateUnbounded<WorkItem>();

// Producer (e.g., runs on a background thread or from callbacks)
async ValkarnTask ProduceAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var work = await FetchNextWorkItemAsync(ct);
        await channel.Writer.WriteAsync(work);
    }
    channel.Writer.Complete();
}

// Consumer (runs wherever you choose to call it)
async ValkarnTask ConsumeAsync()
{
    await foreach (var item in channel.Reader.ReadAllAsync())
    {
        await ProcessAsync(item);
    }
}
```

### Multiple producers, single consumer

Multiple producers can each hold a reference to `channel.Writer` and call `WriteAsync` concurrently. All channel operations are protected by an internal lock, so this is safe.

```csharp
var channel = ValkarnTask.Channel.CreateBounded<Event>(capacity: 64);

// Several producers writing concurrently
ValkarnTask ProducerA() => ProduceFrom(sourceA, channel.Writer);
ValkarnTask ProducerB() => ProduceFrom(sourceB, channel.Writer);
ValkarnTask ProducerC() => ProduceFrom(sourceC, channel.Writer);

// Single consumer (default — no multiConsumer flag needed)
async ValkarnTask ConsumerAsync()
{
    await foreach (var ev in channel.Reader.ReadAllAsync())
        HandleEvent(ev);
}
```

When using multiple producers with a bounded channel, coordinate `Complete()` carefully — only call it after all producers have finished writing, otherwise some writers may receive `ChannelClosedException`.

### Multiple producers, multiple consumers

```csharp
// multiConsumer: true enables concurrent ReadAsync from several consumers
var channel = ValkarnTask.Channel.CreateUnbounded<Job>(multiConsumer: true);

async ValkarnTask WorkerAsync(int id, CancellationToken ct)
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
        // Channel is done — exit gracefully
    }
}
```

Each item is delivered to exactly one consumer. Consumers compete for items in FIFO order (the consumer that has been waiting longest gets the next available item).

### Graceful shutdown

The recommended shutdown sequence is:

1. Signal all producers to stop (e.g., cancel their `CancellationToken`).
2. Call `channel.Writer.Complete()` after all producers have stopped writing.
3. Await `channel.Reader.Completion` to confirm all items have been consumed.

```csharp
cts.Cancel();                        // stop producers
await allProducersTask;              // wait for them to exit
channel.Writer.Complete();           // seal the channel
await channel.Reader.Completion;     // drain remaining items
```

If you are using `ReadAllAsync`, step 3 happens automatically — the `await foreach` loop exits when the channel is complete and empty.

---

## Comparison with System.Threading.Channels

| Feature | Valkarn Channels | System.Threading.Channels |
|---------|-----------------|---------------------------|
| Return type | `ValkarnTask` / `ValkarnTask<T>` | `ValueTask` / `ValueTask<T>` |
| Allocation (hot path) | Zero (single-consumer unbounded) | Near-zero |
| `WaitToReadAsync` | Not present — use `ReadAsync` or `ReadAllAsync` | Present |
| `TryComplete(Exception)` | Not present — use `Complete()` | Present |
| `Count` / `CanCount` | Not exposed | Present on some channel types |
| Drop policy when full | Not supported — `WriteAsync` blocks | `DropWrite`, `DropNewest`, `DropOldest`, `Wait` |
| Async enumerable | `ReadAllAsync()` | `ReadAllAsync()` |
| Thread safety | Full (lock-based) | Full (lock-based) |

The primary difference is that Valkarn Channels integrate natively with `ValkarnTask` for zero-overhead awaiting in Unity builds, and the single-consumer path avoids a pool rent/return on every `ReadAsync` call by embedding the completion core directly in the channel.
