---
sidebar_position: 5
title: Architecture
---

# Architecture

Technical overview of Valkarn Tasks internals.

---

## High-level structure

```
┌─────────────────────────────────────────────────────────────────┐
│                    COMPILE TIME                                   │
│                                                                   │
│  ┌──────────────┐  ┌──────────────────┐  ┌───────────────────┐  │
│  │  Lifecycle    │  │  Awaitable       │  │  Diagnostics      │  │
│  │  Analyzer     │  │  Bridge Gen      │  │  (TT001–TT017)    │  │
│  └──────────────┘  └──────────────────┘  └───────────────────┘  │
│                                                                   │
│  ┌──────────────┐  ┌──────────────────┐                          │
│  │  Job Bridge   │  │  Combinator      │                          │
│  │  Gen          │  │  Gen             │                          │
│  └──────────────┘  └──────────────────┘                          │
├─────────────────────────────────────────────────────────────────┤
│                    RUNTIME                                        │
│                                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐  │
│  │ ValkarnTask │  │ Result<T>  │  │ ValkarnPool│  │ Completion│  │
│  │  struct    │  │  struct    │  │  bounded   │  │  Core<T>  │  │
│  └────────────┘  └────────────┘  └────────────┘  └───────────┘  │
│                                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐  │
│  │ PlayerLoop │  │Continuation│  │  Channels  │  │ TestClock │  │
│  │  Helper    │  │   Queue    │  │            │  │           │  │
│  └────────────┘  └────────────┘  └────────────┘  └───────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### Assembly layout

```
ValkarnTask.Runtime      — ships with the game
ValkarnTask.SourceGen    — compile-time only (source generator)
ValkarnTask.Analyzer     — compile-time only (diagnostics + code fixes)
ValkarnTask.Testing      — TestClock + test utilities
```

---

## ValkarnTask struct

```csharp
[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask
{
    internal readonly ValkarnTask.ISource source;
    internal readonly ulong token; // packed: high 32 bits = generation, low 32 = slot index
}

[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
{
    internal readonly ValkarnTask.ISource<T> source;
    internal readonly T result;  // inline on sync fast path
    internal readonly ulong token;
}
```

**Key invariant:** `source == null` means the task completed synchronously with no error — zero heap object involved. `ValkarnTask.CompletedTask` is `default(ValkarnTask)`; `ValkarnTask.FromResult(value)` stores the result inline.

### Generational token

```csharp
// Pack
ulong token = ((ulong)generation << 32) | slotIndex;

// Unpack
uint slotIndex  = (uint)(token & 0xFFFFFFFF);
uint generation = (uint)(token >> 32);
```

Validated on every ISource call: `slots[slotIndex].generation == expectedGeneration`. A stale reference to a recycled pool slot immediately throws `InvalidOperationException`. 4 billion generations per slot — collision is impossible in practice. (UniTask uses `short` — collision after ~18 minutes.)

---

## ISource contract

```csharp
public interface ISource
{
    Status GetStatus(ulong token);
    void   GetResult(ulong token);
    void   OnCompleted(Action<object> continuation, object state, ulong token);
    Status UnsafeGetStatus();
}

public interface ISource<out T> : ISource
{
    new T GetResult(ulong token);
}
```

Any object implementing `ISource` can back a `ValkarnTask`. Built-in implementations:

| Type | Purpose |
|------|---------|
| `AsyncValkarnRunner<TStateMachine>` | Backs every `async ValkarnTask` method |
| `AsyncValkarnRunner<TStateMachine, T>` | Backs every `async ValkarnTask<T>` method |
| `ValkarnTask.PooledPromise[<T>]` | Manual completion, auto pool return |
| `ValkarnTask.Promise[<T>]` | Manual completion, not pooled (long-lived) |
| `ExceptionSource` | Backs `FromException` |
| `CanceledSource` | Backs `FromCanceled` |
| `NeverSource` | Singleton — never transitions from Pending |

---

## Async method builder

The C# compiler drives the builder protocol for custom async return types:

```
Create()
  └─ returns struct builder (zero alloc)

Start(ref stateMachine)
  └─ runs state machine synchronously
     ├─ completes without suspending → SetResult(), runner stays null
     │  └─ Task returns default(ValkarnTask)  ← zero allocation
     └─ hits incomplete await → AwaitUnsafeOnCompleted()
           └─ rents AsyncValkarnRunner from pool
              copies state machine into runner (value, no boxing)
              registers continuation on awaitable
              └─ Task wraps runner as ISource  ← async path
```

The builder itself is a `struct` — no allocation just for the builder. `runner` is allocated **lazily**: if a method completes synchronously, no pool rent ever occurs.

---

## State machine runner and pool

`AsyncValkarnRunner<TStateMachine>` holds the compiler-generated state machine **by value** (no boxing) and acts as the `ISource`. It is rented from `ValkarnPool<T>` on first suspension and returned on `GetResult`.

Because `TStateMachine` is a unique type per async method (per closed generic instantiation), each async method gets its own pool automatically via C# generic specialization.

### ValkarnPool\<T\>

| Context | Structure | Reason |
|---------|-----------|--------|
| Unity main thread | Single-threaded stack | No synchronization — fastest possible |
| Background threads | Treiber lock-free stack | CAS operations, no locks |

Thread context detected via `Thread.CurrentThread.IsBackground`. Pool shape (capacity, trim rate) configured via `ValkarnTaskSettings`.

---

## ValkarnCompletionCore\<T\>

Shared state inside every `ISource` implementation:

- Current `Status` (Pending / Succeeded / Faulted / Canceled)
- Result value (generic sources)
- Exception or `OperationCanceledException` (error paths)
- Registered continuation delegate + state

Status transitions use `Interlocked.CompareExchange` — lock-free, thread-safe. A **double-complete guard** ensures only the first `TrySet*` call wins; subsequent calls are silent no-ops.

---

## PlayerLoop integration

`PlayerLoopHelper` inserts lightweight runner callbacks into Unity's PlayerLoop at startup (`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]`).

Each `PlayerLoopTiming` value corresponds to a phase. When `await ValkarnTask.Yield(timing)` is called, the continuation is queued into that phase's runner and dispatched the next time Unity reaches it.

```
Initialization  →  EarlyUpdate  →  FixedUpdate  →  PreUpdate
→  Update  →  PreLateUpdate  →  PostLateUpdate  →  TimeUpdate
  (+ Last* variants for each phase)
```

---

## Source generator

The Roslyn source generator runs at compile time. For each `partial` class extending `MonoBehaviour` with `async ValkarnTask` methods, it generates a partial class file that:

1. Declares `_valkarnCancelToken` field
2. Assigns it from `destroyCancellationToken` in `Awake`
3. Wraps each async method to thread the token through automatically

The generated file is never shown in the debugger and never modifies user source.

The generator also produces:

- **Awaitable bridge adapters** — when `Awaitable` is awaited inside `async ValkarnTask`
- **Job async wrappers** — when `IJob` / `IJobParallelFor` types are detected
- **Combinator pools** — typed `WhenAll`/`WhenAny` sources for 2–8 arity tuples

---

## Roslyn analyzers

17 `DiagnosticAnalyzer` rules ship in `Analyzers/netstandard2.0/`. They run during the C# compiler pass in the Unity Editor and on CI:

- All use `SemanticModel` for type resolution (not string matching)
- `ValkarnTypeHelper` shared utility detects any `ValkarnTask` variant
- Zombie-loop analyzer correctly skips nested local functions and lambdas
- Migration analyzers (MIG001–MIG015) auto-activate only when UniTask / Awaitable are referenced — inert otherwise

---

## Burst & ECS layer

Three optional modules, each guarded by `#if` define checks:

| Module | Requires | Purpose |
|--------|----------|---------|
| `JobBridge` | `Unity.Jobs` | Wraps `JobHandle` as awaitable; polls `handle.IsCompleted` each PlayerLoop tick |
| `AsyncSystemBase` | `Unity.Entities` | ECS system base class with async support |
| `BurstScheduler` | `Unity.Burst` + `Unity.Collections` | Schedules Burst jobs from async context; manages `NativeTimerHeap` |

`NativeTimerHeap` is a Burst-compatible min-heap for high-precision timers that avoids managed heap allocation entirely.

---

## Editor integration

The **Valkarn Hub** (`Tools → Valkarn → Hub`) uses `TypeCache.GetTypesDerivedFrom<IValkarnEditorPanel>()` to discover all installed Valkarn packages automatically. No manual registration required.

The `TasksTrackerPanel` subscribes to `EditorApplication.update` to refresh pool diagnostics every 0.5 s (configurable) and surfaces the `ValkarnTaskSettings` asset reference for quick access.

---

## IL2CPP considerations

- State machines are stored **by value** inside runners — no boxing, IL2CPP handles correctly
- Each async method's runner is a separate generic specialization — type-safe, no cross-contamination
- Awaiter structs implement `ICriticalNotifyCompletion` — compiler calls `UnsafeOnCompleted`, skipping `ExecutionContext` capture (no overhead in Unity's default config)
- If aggressive stripping is enabled, preserve the runtime assembly:

```xml
<!-- link.xml -->
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```
