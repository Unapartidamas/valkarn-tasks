---
sidebar_position: 7
title: ValkarnSemaphore
---

# ValkarnSemaphore

`ValkarnSemaphore` is a zero-allocation async semaphore built on `ValkarnTask`. It works like `SemaphoreSlim` but integrates natively with the Valkarn Tasks pipeline — no `Task` allocations, no GC pressure in steady state.

```csharp
namespace UnaPartidaMas.Valkarn.Tasks
```

**Fast path (uncontended):** returns `ValkarnTask.CompletedTask` immediately — zero allocation.
**Slow path (contended):** waiter nodes are rented from a bounded pool and returned to it on completion — zero GC in steady state.

---

## Constructor

```csharp
public ValkarnSemaphore(int initialCount, int maxCount)
```

| Parameter | Description |
|-----------|-------------|
| `initialCount` | Number of slots available immediately. Must be in `[0, maxCount]`. |
| `maxCount` | Upper bound for the slot count. Must be > 0. |

---

## Properties

```csharp
public int CurrentCount { get; } // Available slots right now (thread-safe)
```

---

## Methods

### WaitAsync

```csharp
public ValkarnTask WaitAsync(CancellationToken ct = default)
```

Acquires one slot asynchronously. Returns a completed `ValkarnTask` immediately if a slot is available (zero allocation). Suspends the caller until `Release()` is called if no slot is available. Waiters are served in FIFO order.

Throws `OperationCanceledException` if `ct` fires while waiting.

### Wait

```csharp
public void Wait(CancellationToken ct = default)
```

Synchronous variant. Blocks the calling thread if no slot is available. Use only when you cannot use `await` (e.g. non-async entry points).

### Release

```csharp
public void Release(int releaseCount = 1)
```

Returns `releaseCount` slots. Pending waiters are completed in FIFO order, outside the internal lock — continuations never run with the lock held.

Throws `InvalidOperationException` if releasing would exceed `maxCount`.

### Dispose

```csharp
public void Dispose()
```

Cancels all pending waiters and releases resources. Idempotent.

---

## Usage

### Mutual exclusion (1 slot)

```csharp
var sem = new ValkarnSemaphore(initialCount: 1, maxCount: 1);

async ValkarnTask WriteFileAsync(string path, string content, CancellationToken ct)
{
    await sem.WaitAsync(ct);
    try
    {
        await File.WriteAllTextAsync(path, content, ct);
    }
    finally
    {
        sem.Release();
    }
}
```

### Bounded concurrency (N slots)

```csharp
// Allow up to 4 concurrent network requests.
var sem = new ValkarnSemaphore(initialCount: 4, maxCount: 4);

async ValkarnTask FetchAsync(string url, CancellationToken ct)
{
    await sem.WaitAsync(ct);
    try
    {
        await DownloadAsync(url, ct);
    }
    finally
    {
        sem.Release();
    }
}
```

### Producer / consumer gate

```csharp
// Start with 0 slots — consumer waits until producer signals.
var gate = new ValkarnSemaphore(initialCount: 0, maxCount: 1);

async ValkarnTask ProducerAsync(CancellationToken ct)
{
    await ProduceDataAsync(ct);
    gate.Release(); // signal consumer
}

async ValkarnTask ConsumerAsync(CancellationToken ct)
{
    await gate.WaitAsync(ct); // wait for signal
    await ConsumeDataAsync(ct);
}
```

---

## Thread safety

All operations are thread-safe. `Release()` may be called from any thread, including background threads. Completions are dispatched outside the internal lock — a continuation that re-enters the semaphore will not deadlock.

---

## Comparison with SemaphoreSlim

| | `ValkarnSemaphore` | `SemaphoreSlim` |
|---|---|---|
| Fast path allocation | Zero | Zero |
| Slow path allocation | Pooled node, zero GC | `Task` allocation |
| Integrates with ValkarnTask | Yes | Requires `.AsValkarnTask()` |
| `IDisposable` | Yes | Yes |
| FIFO ordering | Yes | Yes |
| Cancellation | Yes | Yes |
