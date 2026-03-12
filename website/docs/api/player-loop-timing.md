---
sidebar_position: 2
title: PlayerLoopTiming
---

# PlayerLoopTiming

```csharp
public enum PlayerLoopTiming : byte
```

Namespace: `UnaPartidaMas.Valkarn.Tasks`

Specifies which Unity PlayerLoop phase a ValkarnTasks operation uses to resume after suspension. Passing a `PlayerLoopTiming` value controls **when in the frame** your `await` continuation runs, and **when** recurring items such as delays and wait conditions are checked.

The enum values match UniTask's `PlayerLoopTiming` enum exactly, which simplifies migration from UniTask.

---

## Default

`PlayerLoopTiming.Update` (value `8`) is the default for all operations. If you do not pass a timing argument, `Update` is used.

---

## Values

### Initialization

```
Value: 0
Parent phase: UnityEngine.PlayerLoop.Initialization
Position: first sub-system in the phase
```

Runs at the very beginning of the Initialization phase, before Unity's own initialization sub-systems in that phase. This fires once per frame, before `EarlyUpdate`. It is rarely useful for gameplay code but can be relevant for systems that must read or set up state before anything else in the frame touches it.

---

### LastInitialization

```
Value: 1
Parent phase: UnityEngine.PlayerLoop.Initialization
Position: last sub-system in the phase
```

Runs at the end of the Initialization phase, after Unity's built-in initialization sub-systems. Prefer this over `Initialization` if you need initialization-phase timing but want to let Unity's own systems run first.

---

### EarlyUpdate

```
Value: 2
Parent phase: UnityEngine.PlayerLoop.EarlyUpdate
Position: first sub-system in the phase
```

Runs at the start of EarlyUpdate, before Unity processes input events and before the physics simulation step. This is the earliest point in the frame where input data from the previous frame is available. Useful for systems that must sample input before any gameplay code runs.

---

### LastEarlyUpdate

```
Value: 3
Parent phase: UnityEngine.PlayerLoop.EarlyUpdate
Position: last sub-system in the phase
```

Runs at the end of EarlyUpdate, after Unity's own early-update sub-systems have completed.

---

### FixedUpdate

```
Value: 4
Parent phase: UnityEngine.PlayerLoop.FixedUpdate
Position: first sub-system in the phase
```

Runs at the start of each FixedUpdate step. This corresponds to the timing of `MonoBehaviour.FixedUpdate()`. Unity may run zero or more FixedUpdate steps per rendered frame depending on how the physics timestep aligns with the frame delta time.

Use this timing for anything that must stay synchronized with the physics simulation: reading `Rigidbody` velocities, applying forces, or waiting for a physics-determined number of steps.

```csharp
// Wait 10 physics steps
await ValkarnTask.DelayFrame(10, PlayerLoopTiming.FixedUpdate, ct);

// Wait until a physics condition is true, checked each physics step
await ValkarnTask.WaitUntil(() => rb.velocity.magnitude < 0.1f,
    PlayerLoopTiming.FixedUpdate, ct);
```

---

### LastFixedUpdate

```
Value: 5
Parent phase: UnityEngine.PlayerLoop.FixedUpdate
Position: last sub-system in the phase
```

Runs at the end of the FixedUpdate phase, after Unity's physics sub-systems (including `Physics.Simulate`) have completed. Use this to read physics results after the simulation has advanced.

---

### PreUpdate

```
Value: 6
Parent phase: UnityEngine.PlayerLoop.PreUpdate
Position: first sub-system in the phase
```

Runs at the start of the PreUpdate phase, which runs after FixedUpdate and before Update. Unity uses PreUpdate for tasks such as wind zone updates and network event processing.

---

### LastPreUpdate

```
Value: 7
Parent phase: UnityEngine.PlayerLoop.PreUpdate
Position: last sub-system in the phase
```

Runs at the end of PreUpdate.

---

### Update

```
Value: 8  (default)
Parent phase: UnityEngine.PlayerLoop.Update
Position: first sub-system in the phase
```

The **default timing** for all ValkarnTasks operations. Runs at the start of the Update phase, which is where `MonoBehaviour.Update()` fires. This is the right choice for the vast majority of gameplay code.

```csharp
// All of these use Update by default
await ValkarnTask.Yield();
await ValkarnTask.Delay(1000, ct);
await ValkarnTask.WaitUntil(() => _ready, ct: ct);
await ValkarnTask.NextFrame(ct: ct);
await ValkarnTask.DelayFrame(3, ct: ct);

// Explicit — identical to the above
await ValkarnTask.Yield(PlayerLoopTiming.Update);
```

---

### LastUpdate

```
Value: 9
Parent phase: UnityEngine.PlayerLoop.Update
Position: last sub-system in the phase
```

Runs at the end of the Update phase, after all `MonoBehaviour.Update()` calls and any other Update sub-systems have completed. Use this when your continuation must observe the results of other scripts' `Update()` methods — for example, polling a value that another script writes during `Update`.

```csharp
// Resume after all MonoBehaviour.Update() calls in this frame
await ValkarnTask.Yield(PlayerLoopTiming.LastUpdate);
```

---

### PreLateUpdate

```
Value: 10
Parent phase: UnityEngine.PlayerLoop.PreLateUpdate
Position: first sub-system in the phase
```

Runs at the start of the PreLateUpdate phase, which is where `MonoBehaviour.LateUpdate()` fires. Use this for camera following, transform adjustments, and anything that should react to the final positions set during `Update`.

```csharp
// Resume at the same point as MonoBehaviour.LateUpdate()
await ValkarnTask.Yield(PlayerLoopTiming.PreLateUpdate);
```

---

### LastPreLateUpdate

```
Value: 11
Parent phase: UnityEngine.PlayerLoop.PreLateUpdate
Position: last sub-system in the phase
```

Runs at the end of the PreLateUpdate phase, after all `MonoBehaviour.LateUpdate()` calls. Use this when you need to read values that scripts set during their `LateUpdate`.

---

### PostLateUpdate

```
Value: 12
Parent phase: UnityEngine.PlayerLoop.PostLateUpdate
Position: first sub-system in the phase
```

Runs at the start of PostLateUpdate. This phase fires after rendering has been submitted for the frame. Unity uses it for tasks such as presenting the frame buffer and cleaning up render state. It is also where `Camera.onPostRender` callbacks fire.

Use this timing for end-of-frame operations: screenshot capture, streaming level unloads, or any work that must happen after the scene has been fully rendered but before the next frame begins.

---

### LastPostLateUpdate

```
Value: 13
Parent phase: UnityEngine.PlayerLoop.PostLateUpdate
Position: last sub-system in the phase
```

The last point in the standard frame loop before Unity resets frame-local state and begins the next frame. This is the absolute end of a frame for timing purposes.

---

### TimeUpdate

```
Value: 14
Parent phase: UnityEngine.PlayerLoop.TimeUpdate
Position: first sub-system in the phase
```

Runs at the start of the TimeUpdate phase. Unity uses this phase to advance time-related state (such as `Time.time`). This timing fires before `EarlyUpdate` in newer Unity versions.

This timing is rarely needed in game code. It exists primarily for systems that must intercept or observe time advancement before any other system reads the new time values.

---

### LastTimeUpdate

```
Value: 15
Parent phase: UnityEngine.PlayerLoop.TimeUpdate
Position: last sub-system in the phase
```

Runs at the end of the TimeUpdate phase, after Unity's time-related sub-systems have completed.

---

## Summary Table

| Value | Name | Unity Phase | Position | Comparable MonoBehaviour callback |
|---|---|---|---|---|
| 0 | `Initialization` | `Initialization` | First | — |
| 1 | `LastInitialization` | `Initialization` | Last | — |
| 2 | `EarlyUpdate` | `EarlyUpdate` | First | — |
| 3 | `LastEarlyUpdate` | `EarlyUpdate` | Last | — |
| 4 | `FixedUpdate` | `FixedUpdate` | First | `FixedUpdate()` |
| 5 | `LastFixedUpdate` | `FixedUpdate` | Last | after `FixedUpdate()` |
| 6 | `PreUpdate` | `PreUpdate` | First | — |
| 7 | `LastPreUpdate` | `PreUpdate` | Last | — |
| **8** | **`Update`** (default) | **`Update`** | **First** | **`Update()`** |
| 9 | `LastUpdate` | `Update` | Last | after all `Update()` |
| 10 | `PreLateUpdate` | `PreLateUpdate` | First | `LateUpdate()` |
| 11 | `LastPreLateUpdate` | `PreLateUpdate` | Last | after all `LateUpdate()` |
| 12 | `PostLateUpdate` | `PostLateUpdate` | First | after rendering |
| 13 | `LastPostLateUpdate` | `PostLateUpdate` | Last | end of frame |
| 14 | `TimeUpdate` | `TimeUpdate` | First | — |
| 15 | `LastTimeUpdate` | `TimeUpdate` | Last | — |

---

## Which APIs Accept PlayerLoopTiming

All time-based and condition-based ValkarnTasks APIs accept an optional `PlayerLoopTiming` parameter. The default is always `Update`.

| Method | Signature |
|---|---|
| `ValkarnTask.Yield` | `Yield(PlayerLoopTiming timing = Update)` |
| `ValkarnTask.NextFrame` | `NextFrame(PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.DelayFrame` | `DelayFrame(int frameCount, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.Delay(int)` | `Delay(int ms, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.Delay(TimeSpan)` | `Delay(TimeSpan delay, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.WaitUntil` | `WaitUntil(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.WaitWhile` | `WaitWhile(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |

---

## Code Examples

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using System.Threading;

// Yield to next Update tick (default)
await ValkarnTask.Yield();

// Yield to next FixedUpdate tick — use inside physics-driven code
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// Wait 500 ms using scaled deltaTime, checked each Update
await ValkarnTask.Delay(500, ct: destroyCancellationToken);

// Wait 500 ms using unscaled deltaTime — unaffected by Time.timeScale
await ValkarnTask.Delay(
    500,
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// Wait 500 ms using real wall-clock time (Stopwatch-based)
await ValkarnTask.Delay(
    500,
    DelayType.Realtime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// Wait until a flag is set, checked in LateUpdate
bool _isReady;
await ValkarnTask.WaitUntil(() => _isReady, PlayerLoopTiming.PreLateUpdate, destroyCancellationToken);

// Wait while loading, checked at the very end of the frame
await ValkarnTask.WaitWhile(() => _loading, PlayerLoopTiming.LastPostLateUpdate, destroyCancellationToken);

// Skip exactly 5 physics steps
await ValkarnTask.DelayFrame(5, PlayerLoopTiming.FixedUpdate, destroyCancellationToken);

// Advance one full rendered frame
await ValkarnTask.NextFrame(PlayerLoopTiming.Update, destroyCancellationToken);
```
