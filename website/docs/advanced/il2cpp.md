---
sidebar_position: 2
title: IL2CPP Compatibility
---

# IL2CPP Compatibility

Valkarn Tasks is designed to work correctly under IL2CPP from the ground up. This page describes every measure taken and what you need to do — or not do — to ship safely on IL2CPP platforms such as iOS, WebGL, and consoles.

---

## Why IL2CPP Needs Special Care

IL2CPP converts C# IL to C++ source code and then compiles it with a native compiler. Two features of the pipeline are relevant to an async library:

1. **Code stripping.** Unity's managed code stripper (using IL2CPP's linker) removes types, methods, and fields that are not referenced by a statically-analysable call graph. Types accessed only through interface dispatch, generic sharing, or reflection — which includes pooled promise classes and `ISource` implementations — can be silently stripped.

2. **Generic sharing.** IL2CPP does not generate a separate native binary for every generic instantiation. Instead, it shares code across reference types and uses specific instantiations for value types. This can hide bugs in development (Mono) that only surface in IL2CPP builds.

---

## The link.xml File

The primary defence against stripping is the `link.xml` file, located at:

```
Runtime/link.xml
```

Its contents:

```xml
<linker>
  <!-- Preserve all types in the Valkarn.Tasks runtime assembly.
       IL2CPP code stripping can remove internal types accessed only via
       interface dispatch, generic sharing, or reflection (e.g. promise
       classes, pooled runners, ISource implementations). -->
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks.Burst" preserve="all"/>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks.ECS" preserve="all"/>
</linker>
```

`preserve="all"` instructs the linker to keep every type, method, field, and constructor in those assemblies regardless of what the static analysis finds. This is the safest setting for a library whose internal types are accessed through generic parameters that the stripper cannot trace.

Unity discovers and applies `link.xml` files automatically when they are placed inside a package folder that is imported into the project. No manual step is required.

If you are **forking** or **embedding** the source rather than using the package, copy `link.xml` to a `Resources`-adjacent folder or place it anywhere Unity will find it per the [Unity Managed Code Stripping documentation](https://docs.unity3d.com/Manual/ManagedCodeStripping.html).

---

## Internal Types That Would Be Stripped Without link.xml

The following categories of internal types are the primary stripping risk:

### Pooled Promise Classes (ISource Implementations)

Every combinator and delay type creates a pooled promise class that implements `ValkarnTask.ISource` or `ValkarnTask.ISource<T>`:

- `AsyncValkarnTaskRunner<TStateMachine>` — the pooled state machine runner, one per async method (specialised on `TStateMachine`)
- `WhenAllPromise<T1, T2>`, `WhenAllArrayPromise<T>`, `WhenAllVoidPromise2`, `WhenAllVoidPromise3`, `WhenAllVoidArrayPromise`
- `DeltaTimeDelayPromise`, `UnscaledDeltaTimeDelayPromise`, `RealtimeDelayPromise`
- `CanceledSource`, `CanceledSource<T>`, `ExceptionSource`, `ExceptionSource<T>`, `NeverSource`
- Channel internals: `BoundedChannel<T>`, `UnboundedChannel<T>`, and their reader/writer types

These types are instantiated via generic factory methods (`ValkarnTaskPool<T>.GetOrCreate`). The static analysis call graph starts at a generic method call and cannot reliably trace all concrete `T` instantiations, so without `link.xml` any of these types can be stripped.

### ValkarnTaskPool\<T\>

```csharp
internal sealed class ValkarnTaskPool<T> : IPoolInfo where T : class, IPoolNode<T>
```

The pool is generic over its element type. Each promise class has its own static pool field. If a given promise type is unused in a scene, the pool and the promise class may both be stripped together.

### ValkarnTaskCompletionCore\<T\>

The internal completion core is a value type used inside every promise. It holds the continuation callback, the token, and completion state. It is never referenced by name from outside the library.

---

## Generic Type Constraints and IL2CPP

The custom async method builder is generic over the state machine type:

```csharp
public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
    ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : ICriticalNotifyCompletion
    where TStateMachine : IAsyncStateMachine
```

And the runner is generic over the state machine:

```csharp
internal sealed class AsyncValkarnTaskRunner<TStateMachine>
    : IStateMachineRunnerPromise, IPoolNode<AsyncValkarnTaskRunner<TStateMachine>>
    where TStateMachine : IAsyncStateMachine
```

Under IL2CPP, a new concrete type is generated for each distinct `TStateMachine` (because state machines are structs, which require full generic specialisation). This means:

- Each `async ValkarnTask` method in your project produces a separate `AsyncValkarnTaskRunner<TYourStateMachine>` native type.
- If the stripper removes `AsyncValkarnTaskRunner<T>` before seeing all instantiations, some async methods may crash at runtime.
- `preserve="all"` in `link.xml` prevents this.

---

## Managed Code Stripping Level

Unity's stripping level is set in **Player Settings → Other Settings → Managed Stripping Level**.

| Level       | Status with Valkarn Tasks                                                        |
|-------------|----------------------------------------------------------------------------------|
| Disabled    | Safe. No stripping occurs.                                                       |
| Low         | Safe. Only removes clearly unused assemblies.                                    |
| Medium      | Safe **with `link.xml`**. The included `link.xml` preserves all runtime types. |
| High        | Safe **with `link.xml`**. Same coverage; `link.xml` is the protection layer.  |

All stripping levels up to and including **High** are safe as long as the `link.xml` file is present and applied. Do not remove or modify `link.xml` without understanding which internal types you are exposing to the stripper.

---

## [Preserve] Attribute Usage

A search of the Runtime source finds no `[UnityEngine.Scripting.Preserve]` attribute applied to individual members. The chosen approach is assembly-level preservation via `link.xml` rather than per-member attributes. This is intentional:

- Per-member `[Preserve]` requires annotating every pooled class individually, including all future additions.
- Assembly-level `preserve="all"` in `link.xml` is simpler, less error-prone, and guaranteed to cover types added in future versions.

If you need to integrate Valkarn Tasks into a larger `link.xml` file rather than using the package-bundled one, the equivalent directive is:

```xml
<assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
```

---

## IL2CPP-First Pool Design

The object pool (`ValkarnTaskPool<T>`) contains an explicit note in its documentation comment:

> IL2CPP-first: main thread operations use zero atomics.

The pool uses two access paths:

- **Fast path (main thread):** A single `fastItem` field is read/written with plain field access — no `Volatile` or `Interlocked` operations. This avoids the overhead of atomic operations on the main thread, where IL2CPP cannot always optimise them away.
- **Overflow path (any thread):** A Treiber lock-free stack with `Interlocked.CompareExchange` for correctness under concurrent access from background threads (e.g., `ValkarnTask.RunOnThreadPool`).

The main thread identity is stored in a `volatile int` field in `ValkarnTaskPoolShared.MainThreadId`, published once at startup via `RuntimeInitializeOnLoadMethod`. IL2CPP correctly handles `volatile` fields.

---

## Console Platform Considerations

Console platforms (PlayStation, Xbox, Nintendo Switch) use IL2CPP exclusively. The following apply:

- The `link.xml` coverage is the same as for other IL2CPP targets. No console-specific additional preservation is needed.
- `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` fires correctly on all platforms Unity supports for console certification.
- `Interlocked` and `Volatile` operations are supported on all console IL2CPP targets. The pool's Treiber stack is safe.
- Generic sharing for struct `TStateMachine` instantiations applies on consoles. Each `async ValkarnTask` method generates its own native type — this is expected behaviour and is handled correctly.
- If a console platform enforces additional AOT requirements, confirm that your `link.xml` is picked up correctly by checking the build log for "Stripping assembly: UnaPartidaMas.Valkarn.Tasks". If the assembly is stripped, the `link.xml` was not discovered.

---

## Verifying Nothing Was Stripped

After building for an IL2CPP target:

1. **Check the build log.** Unity prints stripping decisions. Search for `UnaPartidaMas.Valkarn.Tasks`. If you see `Stripping class` messages for internal Valkarn types, the `link.xml` was not applied.

2. **Run a smoke test.** A minimal test that exercises `ValkarnTask.Delay`, `WhenAll`, and a custom `ValkarnTaskCompletionSource` will instantiate the most frequently stripped types. If those three scenarios survive the build, the core is intact.

3. **Enable "Strip Engine Code" selectively.** If you must use High stripping and cannot use the bundled `link.xml`, enable stripping incrementally and run tests after each increase in stripping level.

4. **IL2CPP build with Development Build enabled.** Development builds include additional diagnostics. If a type is missing, the runtime will report `TypeInitializationException` or a null reference at the call site of the missing type. Match the stack trace to the types listed in the "Internal Types" section above.

---

## Summary Checklist

- `Runtime/link.xml` is present in the package — verify it was not accidentally deleted.
- Managed stripping level can be set to any level; High is supported.
- No `[Preserve]` attributes need to be added to application code.
- No AOT hint attributes or code registration calls are needed.
- Console builds use the same `link.xml`; no platform-specific additions are required.
- If you fork the source, carry `link.xml` with it.
