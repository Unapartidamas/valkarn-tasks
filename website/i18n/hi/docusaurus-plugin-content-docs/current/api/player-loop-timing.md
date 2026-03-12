---
sidebar_position: 2
title: PlayerLoopTiming
---

# PlayerLoopTiming

```csharp
public enum PlayerLoopTiming : byte
```

Namespace: `UnaPartidaMas.Valkarn.Tasks`

Specify करता है कि कौन सा Unity PlayerLoop phase एक ValkarnTasks operation suspension के बाद resume करने के लिए उपयोग करती है। `PlayerLoopTiming` value pass करना control करता है कि आपकी `await` continuation **frame में कब** run होती है, और **कब** delays और wait conditions जैसे recurring items check होते हैं।

Enum values UniTask के `PlayerLoopTiming` enum से exactly match करते हैं, जो UniTask से migration simplify करता है।

---

## Default

`PlayerLoopTiming.Update` (value `8`) सभी operations के लिए default है। यदि आप कोई timing argument pass नहीं करते, `Update` उपयोग होता है।

---

## Values

### Initialization

```
Value: 0
Parent phase: UnityEngine.PlayerLoop.Initialization
Position: phase में पहला sub-system
```

Initialization phase की बहुत शुरुआत में run होता है, उस phase में Unity के own initialization sub-systems से पहले। यह `EarlyUpdate` से पहले, per frame एक बार fire होता है। Game code के लिए rarely useful है लेकिन उन systems के लिए relevant हो सकता है जिन्हें frame में कुछ और touch करने से पहले state read या set up करनी हो।

---

### LastInitialization

```
Value: 1
Parent phase: UnityEngine.PlayerLoop.Initialization
Position: phase में अंतिम sub-system
```

Unity के built-in initialization sub-systems के बाद, Initialization phase के अंत में run होता है। `Initialization` के बजाय इसे prefer करें यदि आपको initialization-phase timing चाहिए लेकिन आप Unity के own systems को पहले run होने देना चाहते हैं।

---

### EarlyUpdate

```
Value: 2
Parent phase: UnityEngine.PlayerLoop.EarlyUpdate
Position: phase में पहला sub-system
```

EarlyUpdate की शुरुआत में run होता है, Unity input events process करने से पहले और physics simulation step से पहले। यह frame का earliest point है जहाँ previous frame से input data available है। उन systems के लिए useful जिन्हें कोई gameplay code run होने से पहले input sample करना हो।

---

### LastEarlyUpdate

```
Value: 3
Parent phase: UnityEngine.PlayerLoop.EarlyUpdate
Position: phase में अंतिम sub-system
```

Unity के own early-update sub-systems complete होने के बाद, EarlyUpdate के अंत में run होता है।

---

### FixedUpdate

```
Value: 4
Parent phase: UnityEngine.PlayerLoop.FixedUpdate
Position: phase में पहला sub-system
```

प्रत्येक FixedUpdate step की शुरुआत में run होता है। यह `MonoBehaviour.FixedUpdate()` की timing correspond करता है। Physics timestep frame delta time के साथ कैसे align होता है इस पर depend करते हुए, Unity per rendered frame zero या more FixedUpdate steps run कर सकता है।

Physics simulation के साथ synchronized रहने वाली किसी भी चीज़ के लिए इस timing का उपयोग करें: `Rigidbody` velocities read करना, forces apply करना, या physics-determined number of steps के लिए wait करना।

```csharp
// 10 physics steps wait करें
await ValkarnTask.DelayFrame(10, PlayerLoopTiming.FixedUpdate, ct);

// Physics condition true होने तक wait करें, प्रत्येक physics step पर checked
await ValkarnTask.WaitUntil(() => rb.velocity.magnitude < 0.1f,
    PlayerLoopTiming.FixedUpdate, ct);
```

---

### LastFixedUpdate

```
Value: 5
Parent phase: UnityEngine.PlayerLoop.FixedUpdate
Position: phase में अंतिम sub-system
```

Unity के physics sub-systems (`Physics.Simulate` सहित) complete होने के बाद, FixedUpdate phase के अंत में run होता है। Simulation advance होने के बाद physics results read करने के लिए इसका उपयोग करें।

---

### PreUpdate

```
Value: 6
Parent phase: UnityEngine.PlayerLoop.PreUpdate
Position: phase में पहला sub-system
```

PreUpdate phase की शुरुआत में run होता है, जो FixedUpdate के बाद और Update से पहले run होता है। Unity wind zone updates और network event processing जैसे tasks के लिए PreUpdate उपयोग करता है।

---

### LastPreUpdate

```
Value: 7
Parent phase: UnityEngine.PlayerLoop.PreUpdate
Position: phase में अंतिम sub-system
```

PreUpdate के अंत में run होता है।

---

### Update

```
Value: 8  (default)
Parent phase: UnityEngine.PlayerLoop.Update
Position: phase में पहला sub-system
```

सभी ValkarnTasks operations के लिए **default timing**। Update phase की शुरुआत में run होता है, जहाँ `MonoBehaviour.Update()` fire होता है। Gameplay code की vast majority के लिए यह सही choice है।

```csharp
// ये सभी default रूप से Update उपयोग करते हैं
await ValkarnTask.Yield();
await ValkarnTask.Delay(1000, ct);
await ValkarnTask.WaitUntil(() => _ready, ct: ct);
await ValkarnTask.NextFrame(ct: ct);
await ValkarnTask.DelayFrame(3, ct: ct);

// Explicit — ऊपर के identical
await ValkarnTask.Yield(PlayerLoopTiming.Update);
```

---

### LastUpdate

```
Value: 9
Parent phase: UnityEngine.PlayerLoop.Update
Position: phase में अंतिम sub-system
```

सभी `MonoBehaviour.Update()` calls और अन्य Update sub-systems complete होने के बाद, Update phase के अंत में run होता है। इसका उपयोग तब करें जब आपकी continuation को अन्य scripts के `Update()` methods के results observe करने चाहिए — उदाहरण के लिए, एक value polling जिसे दूसरा script `Update` के दौरान write करता है।

```csharp
// इस frame में सभी MonoBehaviour.Update() calls के बाद resume करें
await ValkarnTask.Yield(PlayerLoopTiming.LastUpdate);
```

---

### PreLateUpdate

```
Value: 10
Parent phase: UnityEngine.PlayerLoop.PreLateUpdate
Position: phase में पहला sub-system
```

PreLateUpdate phase की शुरुआत में run होता है, जहाँ `MonoBehaviour.LateUpdate()` fire होता है। Camera following, transform adjustments, और `Update` के दौरान set final positions पर react करने वाली किसी भी चीज़ के लिए इसका उपयोग करें।

```csharp
// MonoBehaviour.LateUpdate() के same point पर resume करें
await ValkarnTask.Yield(PlayerLoopTiming.PreLateUpdate);
```

---

### LastPreLateUpdate

```
Value: 11
Parent phase: UnityEngine.PlayerLoop.PreLateUpdate
Position: phase में अंतिम sub-system
```

सभी `MonoBehaviour.LateUpdate()` calls के बाद, PreLateUpdate phase के अंत में run होता है। इसका उपयोग तब करें जब आपको values read करनी हों जो scripts अपने `LateUpdate` के दौरान set करते हैं।

---

### PostLateUpdate

```
Value: 12
Parent phase: UnityEngine.PlayerLoop.PostLateUpdate
Position: phase में पहला sub-system
```

PostLateUpdate की शुरुआत में run होता है। यह phase frame के लिए rendering submit होने के बाद fire होता है। Unity इसे frame buffer present करने और render state cleanup जैसे tasks के लिए उपयोग करता है। यहीं `Camera.onPostRender` callbacks भी fire होते हैं।

End-of-frame operations के लिए इस timing का उपयोग करें: screenshot capture, streaming level unloads, या कोई भी काम जो scene fully rendered होने के बाद लेकिन अगला frame शुरू होने से पहले होना चाहिए।

---

### LastPostLateUpdate

```
Value: 13
Parent phase: UnityEngine.PlayerLoop.PostLateUpdate
Position: phase में अंतिम sub-system
```

Unity frame-local state reset करने और अगला frame शुरू करने से पहले standard frame loop का अंतिम point। Timing purposes के लिए यह frame का absolute end है।

---

### TimeUpdate

```
Value: 14
Parent phase: UnityEngine.PlayerLoop.TimeUpdate
Position: phase में पहला sub-system
```

TimeUpdate phase की शुरुआत में run होता है। Unity इस phase को time-related state (जैसे `Time.time`) advance करने के लिए उपयोग करता है। यह timing newer Unity versions में `EarlyUpdate` से पहले fire होती है।

Game code में यह timing rarely needed है। यह primarily उन systems के लिए exist करती है जिन्हें किसी अन्य system के नए time values read करने से पहले time advancement intercept या observe करना हो।

---

### LastTimeUpdate

```
Value: 15
Parent phase: UnityEngine.PlayerLoop.TimeUpdate
Position: phase में अंतिम sub-system
```

Unity के time-related sub-systems complete होने के बाद, TimeUpdate phase के अंत में run होता है।

---

## Summary Table

| Value | Name | Unity Phase | Position | Comparable MonoBehaviour callback |
|---|---|---|---|---|
| 0 | `Initialization` | `Initialization` | First | — |
| 1 | `LastInitialization` | `Initialization` | Last | — |
| 2 | `EarlyUpdate` | `EarlyUpdate` | First | — |
| 3 | `LastEarlyUpdate` | `EarlyUpdate` | Last | — |
| 4 | `FixedUpdate` | `FixedUpdate` | First | `FixedUpdate()` |
| 5 | `LastFixedUpdate` | `FixedUpdate` | Last | `FixedUpdate()` के बाद |
| 6 | `PreUpdate` | `PreUpdate` | First | — |
| 7 | `LastPreUpdate` | `PreUpdate` | Last | — |
| **8** | **`Update`** (default) | **`Update`** | **First** | **`Update()`** |
| 9 | `LastUpdate` | `Update` | Last | सभी `Update()` के बाद |
| 10 | `PreLateUpdate` | `PreLateUpdate` | First | `LateUpdate()` |
| 11 | `LastPreLateUpdate` | `PreLateUpdate` | Last | सभी `LateUpdate()` के बाद |
| 12 | `PostLateUpdate` | `PostLateUpdate` | First | rendering के बाद |
| 13 | `LastPostLateUpdate` | `PostLateUpdate` | Last | frame का अंत |
| 14 | `TimeUpdate` | `TimeUpdate` | First | — |
| 15 | `LastTimeUpdate` | `TimeUpdate` | Last | — |

---

## कौन से APIs PlayerLoopTiming Accept करते हैं

सभी time-based और condition-based ValkarnTasks APIs एक optional `PlayerLoopTiming` parameter accept करते हैं। Default हमेशा `Update` है।

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

// अगले Update tick पर Yield (default)
await ValkarnTask.Yield();

// अगले FixedUpdate tick पर Yield — physics-driven code के अंदर उपयोग करें
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// Scaled deltaTime का उपयोग करके 500 ms wait करें, प्रत्येक Update पर checked
await ValkarnTask.Delay(500, ct: destroyCancellationToken);

// Unscaled deltaTime का उपयोग करके 500 ms wait करें — Time.timeScale से unaffected
await ValkarnTask.Delay(
    500,
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// Real wall-clock time का उपयोग करके 500 ms wait करें (Stopwatch-based)
await ValkarnTask.Delay(
    500,
    DelayType.Realtime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// Flag set होने तक wait करें, LateUpdate में checked
bool _isReady;
await ValkarnTask.WaitUntil(() => _isReady, PlayerLoopTiming.PreLateUpdate, destroyCancellationToken);

// Loading के दौरान wait करें, frame के बिल्कुल अंत में checked
await ValkarnTask.WaitWhile(() => _loading, PlayerLoopTiming.LastPostLateUpdate, destroyCancellationToken);

// Exactly 5 physics steps skip करें
await ValkarnTask.DelayFrame(5, PlayerLoopTiming.FixedUpdate, destroyCancellationToken);

// एक full rendered frame advance करें
await ValkarnTask.NextFrame(PlayerLoopTiming.Update, destroyCancellationToken);
```
