---
sidebar_position: 6
title: Why Valkarn Tasks
---

# Why Valkarn Tasks

> Zero-allocation async/await for Unity. Faster than UniTask. Smarter than Awaitable.

---

## The problem: async in Unity is broken

Unity developers need asynchronous operations everywhere: loading scenes, downloading assets, waiting for animations, delaying spawns, communicating with servers. Today, the options are:

| Option | Problem |
|--------|---------|
| **Coroutines** | No return values, no error handling, no cancellation, no composition |
| **System.Threading.Tasks** | Allocates on every `async` call (~144–232 bytes), triggers GC, no Unity lifecycle awareness |
| **UniTask** | Good — but: token collision after 18 minutes, no compile-time diagnostics, finalizer-dependent error reporting |
| **Unity Awaitable** (2023+) | Class-based (allocates), no combinators, no channels, no testing support |

Valkarn Tasks solves all of these in a single, source-generated, zero-allocation package.

---

## Zero allocations — why every byte counts

### What allocation means for games

Every `new object()`, `new List<T>()`, or `async Task` call allocates on the **managed heap**, tracked by the Garbage Collector. Unity uses the Boehm GC which has two critical problems:

1. **Stop-the-world pauses** — when the GC runs, your game freezes. A 2 ms pause at 60 fps costs 12% of your frame budget; at 120 fps (VR) it costs 24%.
2. **Unpredictable timing** — GC can trigger during a boss fight, a cutscene, or a competitive match.

### What Valkarn Tasks does differently

| Scenario | `System.Task` | UniTask | **Valkarn Tasks** |
|----------|--------------|---------|------------------|
| `async Method()` — completes synchronously | 144 bytes | **0 bytes** | **0 bytes** |
| `async Method()` — suspends once | 232+ bytes | 0 bytes (pooled) | **0 bytes (pooled)** |
| `WhenAll(a, b)` | 232 bytes | 0 bytes (pooled) | **0 bytes (source-gen pooled)** |
| `WhenAny(a, b)` | 144 bytes | 72 bytes | **0 bytes** |
| Promise (manual completion) | 144 bytes | 104 bytes | **88 bytes** |
| Pooled Promise (reusable) | 144 bytes | 0 bytes | **0 bytes** |

In a typical game frame with 50–100 async operations, `System.Task` generates 7–23 KB of garbage. Valkarn Tasks generates **zero**.

### What this means for your game

- **No GC stuttering** — locked framerate without hitches from async operations
- **VR-safe** — 90/120 fps targets without GC spikes
- **Mobile-friendly** — less memory pressure on limited RAM devices
- **Console-certified** — predictable memory behaviour helps pass certification requirements

---

## Performance — benchmarked against the best

Benchmarks: BenchmarkDotNet v0.14.0, .NET 9.0, Intel Core i7-10875H.

### Core async/await — 2× faster than ValueTask

| Benchmark | ValueTask | UniTask | **Valkarn Tasks** | vs ValueTask |
|-----------|-----------|---------|------------------|--------------|
| Bulk 100 tasks | 956 ns | 508 ns | **489 ns** | **1.95×** |
| Bulk 1,000 tasks | 9,697 ns | 5,016 ns | **4,728 ns** | **2.05×** |
| With CancellationToken | 38.8 ns | 36.2 ns | **29.6 ns** | **1.31×** |
| Exception handling | 10,399 ns | 8,247 ns | **9,248 ns** | **1.12×** |

All paths: **0 bytes allocated**.

### Combinators — up to 9.6× faster than Task

| Benchmark | Task | UniTask | **Valkarn Tasks** | vs Task |
|-----------|------|---------|------------------|---------|
| WhenAll (2 tasks) | 117 ns / 232B | 13.6 ns / 0B | **12.1 ns / 0B** | **9.6×** |
| WhenAll (5 tasks) | 156 ns / 272B | 25.1 ns / 0B | **25.3 ns / 0B** | **6.2×** |
| WhenAny (2 tasks) | 39.0 ns / 144B | 60.0 ns / 72B | **11.6 ns / 0B** | **3.4×** |
| Pooled Promise | 59.5 ns / 144B | 53.6 ns / 0B | **38.3 ns / 0B** | **1.55×** |

WhenAny is **5.2× faster than UniTask** and allocates zero bytes.

### Object pool — 4.3 nanoseconds

| Operation | Time | Allocation |
|-----------|------|------------|
| Main-thread fast-slot rent + return | **4.3 ns** | 0 bytes |
| Cross-thread Treiber stack | ~15 ns | 0 bytes |

Zero atomic operations on the main thread — critical for IL2CPP where `Volatile.Read` is 9.2× slower.

### Real-world impact (50 async ops/frame at 60 fps)

| Library | Time/frame | Frame budget | GC/second |
|---------|-----------|-------------|-----------|
| System.Task | ~48 µs | 0.29% | ~430 KB/s |
| UniTask | ~25 µs | 0.15% | ~3.6 KB/s |
| **Valkarn Tasks** | **~24 µs** | **0.14%** | **0 KB/s** |

Over 10 minutes, `System.Task` generates **~258 MB** of async garbage. Valkarn Tasks generates **zero**.

---

## Features no other library has

### Automatic lifecycle cancellation

```csharp
public partial class EnemySpawner : MonoBehaviour
{
    async ValkarnTask Start()
    {
        while (true)
        {
            await ValkarnTask.Delay(3000);
            SpawnWave();
        }
        // Cancelled automatically when this GameObject is destroyed.
        // No CancellationToken. No memory leaks. No zombie tasks.
    }
}
```

No more `MissingReferenceException` from async methods running after destroy. No manual `OnDestroy` cleanup. No forgotten `CancellationTokenSource` disposal.

### Critical sections

```csharp
async ValkarnTask SaveProgress()
{
    var data = await LoadPlayerData();       // cancellable

    await using (ValkarnTask.Critical())
    {
        await db.Insert(data);              // completes even if GO is destroyed
        await db.Commit();
    }                                       // pending cancellation applies now

    await SendNotification();               // cancellable again
}
```

Database writes, network requests, and file saves complete even when the player quits or a scene unloads. No corrupted saves. No half-written analytics. No lost receipts.

### Compile-time diagnostics

| Diagnostic | What it catches |
|------------|----------------|
| TT001 | Double-awaiting a ValkarnTask (use-after-free bug) |
| TT002 | Forgetting to await a task (silent failure) |
| TT012 | Async loops without cancellation checks (zombie loops) |
| TT013 | Returned but not awaited task (fire-and-forget bug) |
| TT016 | Async method with no await (unnecessary overhead) |
| TT017 | `[FireAndForget]` on `ValkarnTask<T>` (discarding a result) |

Bugs caught in the IDE as red squiggles — not runtime crashes 20 minutes into testing.

### Result\<T\> — error handling without try/catch

```csharp
var result = await loadTask.AsResult();

if (result.Succeeded)       Use(result.Value);
else if (result.IsCanceled) ShowRetryDialog();
else                         Debug.LogError(result.Error);
```

Every error path is explicit. No swallowed exceptions. No missing handlers.

### Channels with backpressure

```csharp
var channel = ValkarnTask.Channel.CreateBounded<EnemySpawnRequest>(capacity: 10);

// Producer (game logic)
await channel.Writer.WriteAsync(new EnemySpawnRequest { type = EnemyType.Dragon });

// Consumer (spawn system)
await foreach (var request in channel.Reader.ReadAllAsync())
    await SpawnEnemy(request);
```

Decouple systems cleanly. Rate-limit spawning. Queue network messages. Buffer input events. The producer slows down when the consumer can't keep up — preventing memory spikes.

### Deterministic testing with TestClock

```csharp
[Test]
public void Respawn_WaitsThreeSeconds()
{
    var clock = new TestClock();
    var task = spawner.RespawnEnemy();

    clock.Advance(TimeSpan.FromSeconds(2));
    Assert.IsFalse(task.IsCompleted);

    clock.Advance(TimeSpan.FromSeconds(1));
    Assert.IsTrue(task.IsCompleted);
}
```

Test time-dependent logic instantly. No `yield return new WaitForSeconds(3)` in tests. No flaky CI timing.

### Generational token safety

UniTask uses a `short` token (16-bit). After 65,536 pool cycles (~18 minutes of active async work), a stale reference silently reads another task's result — a use-after-free bug virtually impossible to reproduce.

Valkarn Tasks uses a `uint` generation counter per pool slot: **4,294,967,296 cycles per slot** before collision. Impossible in any realistic scenario.

---

## Migration in minutes, not weeks

### From UniTask

```
Step 1: Install Valkarn Tasks
Step 2: Yellow lightbulbs appear on UniTask usages in the IDE
Step 3: Right-click → "Fix all occurrences in Solution" (Ctrl+.)
Step 4: Remove UniTask package reference
```

**15 migration diagnostics** (MIG001–MIG015) cover every UniTask API automatically. A typical project with 500–2,000 async methods migrates in under 5 minutes. 95%+ fully automated.

### From Unity Awaitable

Same one-click migration:
- `async Awaitable` → `async ValkarnTask`
- `Awaitable.NextFrameAsync()` → `ValkarnTask.NextFrame()`
- `Awaitable.BackgroundThreadAsync()` → `ValkarnTask.SwitchToThreadPool()`
- `Awaitable.MainThreadAsync()` → removed (Valkarn runs on main thread by default)

---

## Comparison matrix

| Feature | System.Task | UniTask | Awaitable | **Valkarn Tasks** |
|---------|------------|---------|-----------|------------------|
| Zero-alloc sync path | No | Yes | No | **Yes** |
| Zero-alloc combinators | No | No | No | **Yes (source gen)** |
| Struct-based | No | Yes | No | **Yes** |
| Lifecycle auto-cancel | No | Manual | Partial | **Automatic** |
| No sibling cancellation | No | No | No | **Yes** |
| Critical sections | No | No | No | **Yes** |
| Result\<T\> (no throw) | No | Partial | No | **Yes** |
| TestClock | No | No | No | **Yes** |
| Job System bridge | No | No | No | **Yes** |
| Compile-time diagnostics | No | No | No | **Yes (17 rules)** |
| Bounded pools + trim | No | No | N/A | **Yes** |
| Deterministic error report | No | No (finalizer) | Partial | **Yes (pool return)** |
| Complete channels | Yes (.NET) | Minimal | No | **Yes** |
| Awaitable bridge | N/A | Minimal | Native | **Transparent** |
| IL2CPP-optimized pooling | No | No (Volatile every op) | N/A | **Yes (zero atomics)** |
| Token collision safety | N/A | 18 min (short) | N/A | **Never (uint gen)** |
| Auto-migration from UniTask | N/A | N/A | N/A | **Yes (15 fixes)** |
| Auto-migration from Awaitable | N/A | N/A | N/A | **Yes (8 fixes)** |

---

**Your game deserves async that doesn't stutter.**
