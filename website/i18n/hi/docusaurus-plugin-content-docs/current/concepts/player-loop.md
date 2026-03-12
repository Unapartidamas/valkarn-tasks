---
sidebar_position: 1
title: Unity PlayerLoop Integration
---

# Unity PlayerLoop Integration

ValkarnTasks आपके `async` methods resume करने के लिए threads या background schedulers का उपयोग नहीं करता। सब कुछ Unity के मुख्य thread पर चलता है, Unity के PlayerLoop system द्वारा driven। यह समझना कि यह कैसे काम करता है, आपको अपने use case के लिए सही timing चुनने और यह reason करने में मदद करेगा कि `await` के बाद आपका code exactly कब resume होता है।

## Unity PlayerLoop क्या है

Unity का PlayerLoop वह internal engine loop है जो हर frame drive करता है। यह एक single `Update()` call नहीं है — यह phases का एक hierarchical sequence है जो हर frame एक defined order में run करता है:

```
Initialization
EarlyUpdate
FixedUpdate      (physics stepping होने पर repeated)
PreUpdate
Update
PreLateUpdate
PostLateUpdate
TimeUpdate
```

इनमें से प्रत्येक top-level phase में sub-systems होते हैं जिन्हें Unity (और third-party packages) अपना logic specific points पर run करने के लिए insert करते हैं। उदाहरण के लिए `MonoBehaviour.Update()`, `Update` phase के अंदर run होता है। `MonoBehaviour.LateUpdate()` `PreLateUpdate` के अंदर run होता है।

क्योंकि ValkarnTasks directly इस loop में hook होता है, `await VlkTask.Yield()` एक thread park नहीं करता — यह एक callback register करता है जिसे Unity chosen phase के next tick पर call करता है, फिर immediately return करता है।

## 16 PlayerLoop Timings

ValkarnTasks 16 points पर inject करता है: 8 Unity PlayerLoop phases में से प्रत्येक के **शुरुआत** और **अंत** में एक-एक। `Last` variants अपने parent phase की sub-system list के tail पर append होते हैं; plain variants head पर prepend होते हैं।

| Value | Enum integer | Parent phase | Parent में Position |
|---|---|---|---|
| `Initialization` | 0 | `UnityEngine.PlayerLoop.Initialization` | पहला |
| `LastInitialization` | 1 | `UnityEngine.PlayerLoop.Initialization` | अंतिम |
| `EarlyUpdate` | 2 | `UnityEngine.PlayerLoop.EarlyUpdate` | पहला |
| `LastEarlyUpdate` | 3 | `UnityEngine.PlayerLoop.EarlyUpdate` | अंतिम |
| `FixedUpdate` | 4 | `UnityEngine.PlayerLoop.FixedUpdate` | पहला |
| `LastFixedUpdate` | 5 | `UnityEngine.PlayerLoop.FixedUpdate` | अंतिम |
| `PreUpdate` | 6 | `UnityEngine.PlayerLoop.PreUpdate` | पहला |
| `LastPreUpdate` | 7 | `UnityEngine.PlayerLoop.PreUpdate` | अंतिम |
| `Update` | 8 | `UnityEngine.PlayerLoop.Update` | पहला |
| `LastUpdate` | 9 | `UnityEngine.PlayerLoop.Update` | अंतिम |
| `PreLateUpdate` | 10 | `UnityEngine.PlayerLoop.PreLateUpdate` | पहला |
| `LastPreLateUpdate` | 11 | `UnityEngine.PlayerLoop.PreLateUpdate` | अंतिम |
| `PostLateUpdate` | 12 | `UnityEngine.PlayerLoop.PostLateUpdate` | पहला |
| `LastPostLateUpdate` | 13 | `UnityEngine.PlayerLoop.PostLateUpdate` | अंतिम |
| `TimeUpdate` | 14 | `UnityEngine.PlayerLoop.TimeUpdate` | पहला |
| `LastTimeUpdate` | 15 | `UnityEngine.PlayerLoop.TimeUpdate` | अंतिम |

`Update` (value 8) सभी ValkarnTasks operations के लिए **default** है — `Yield()`, `Delay()`, `WaitUntil()`, `WaitWhile()`, `NextFrame()`, और `DelayFrame()`।

## Marker Structs और Idempotent Injection

Unity के PlayerLoop में inject करने के लिए, ValkarnTasks प्रति timing एक `PlayerLoopSystem` create करता है। प्रत्येक system को `PlayerLoopHelper` के अंदर define किए गए एक unique marker struct द्वारा identify किया जाता है:

```csharp
struct VlkTaskInitialization     { }
struct VlkTaskLastInitialization { }
struct VlkTaskEarlyUpdate        { }
struct VlkTaskLastEarlyUpdate    { }
struct VlkTaskFixedUpdate        { }
struct VlkTaskLastFixedUpdate    { }
struct VlkTaskPreUpdate          { }
struct VlkTaskLastPreUpdate      { }
struct VlkTaskUpdate             { }
struct VlkTaskLastUpdate         { }
struct VlkTaskPreLateUpdate      { }
struct VlkTaskLastPreLateUpdate  { }
struct VlkTaskPostLateUpdate     { }
struct VlkTaskLastPostLateUpdate { }
struct VlkTaskTimeUpdate         { }
struct VlkTaskLastTimeUpdate     { }
```

Inject करने से पहले, `PlayerLoopHelper` check करता है कि `VlkTaskUpdate` current PlayerLoop tree में कहीं already appear होता है या नहीं। यदि करता है, injection skip हो जाता है। यह injection को **idempotent** बनाता है — `Init()` को कई बार call करने पर (जो editor में हो सकता है) duplicate systems कभी register नहीं होते।

Injection हमेशा `PlayerLoop.GetCurrentPlayerLoop()` के साथ **current** PlayerLoop read करता है, `GetDefaultPlayerLoop()` के साथ नहीं। इसका मतलब है कि अन्य packages द्वारा previously installed कोई भी system preserve होते हैं।

## ContinuationQueue — One-Shot Callbacks

जब आप `await VlkTask.Yield()` (या कुछ भी जो exactly एक tick के लिए suspend करता है), compiler-generated state machine awaiter पर `OnCompleted` call करता है। awaiter उस timing के लिए `ContinuationQueue` में callback enqueue करने के लिए `PlayerLoopHelper.AddContinuation(timing, action, state)` call करता है।

`ContinuationQueue` एक **double-buffer** design उपयोग करता है:

1. **Active buffer** (`actionList`): current drain से पहले और during queued continuations hold करता है।
2. **Waiting buffer** (`waitingList`): उन continuations को catches करता है जो active buffer drain होने के _दौरान_ enqueued होती हैं (यानी, किसी continuation के अंदर से re-entrant enqueues)।
3. **Cross-thread Treiber stack** (`crossThreadHead`): background threads से posted continuations lock-free compare-and-swap के माध्यम से यहाँ land होती हैं। प्रत्येक drain की शुरुआत में, पूरा stack atomically claimed होकर active buffer में moved होता है।

प्रत्येक PlayerLoop tick, `ContinuationQueue.Run()` यह sequence execute करता है:

```
1. crossThreadHead → actionList drain करें     (lock-free atomic take, फिर SpinLock के तहत copy)
2. count snapshot करें, isDraining = true set करें  (SpinLock के तहत)
3. सभी callbacks execute करें                  (lock के बाहर, refs GC के लिए cleared)
4. actionList ↔ waitingList swap करें          (SpinLock के तहत, isDraining = false set करें)
```

लगभग एक या दो frames के warmup के बाद, internal arrays अपने high-water mark पर stabilize हो जाते हैं और queue **zero allocations** के साथ run करता है।

Cross-thread nodes एक bounded lock-free pool (max 1,024 nodes) में pooled होते हैं ताकि हर background-thread enqueue पर allocation से बचा जा सके।

## PlayerLoopRunner — Recurring Items

कुछ operations को complete होने तक हर tick check करना होता है: `Delay()`, `DelayFrame()`, `WaitUntil()`, `WaitWhile()`, और `NextFrame()`। ये `IPlayerLoopItem` interface implement करते हैं:

```csharp
internal interface IPlayerLoopItem
{
    // true return करें चलते रहने के लिए; false return करें runner से remove करने के लिए।
    bool MoveNext();
}
```

Items `PlayerLoopHelper.AddAction(timing, item)` के माध्यम से `PlayerLoopRunner` में add होते हैं। प्रत्येक tick, `PlayerLoopRunner.Run()` सभी registered items iterate करता है और प्रत्येक पर `MoveNext()` call करता है। `false` return करने वाले items **in-place compaction** के माध्यम से remove होते हैं — array एक single pass में compacted होता है, insertion order preserve करता है। `Run()` call के दौरान add किए गए items safely append होते हैं और next tick पर picked up होंगे।

```
Tick N:
  ContinuationQueue.Run()   → सभी one-shot awaiters resume करें
  PlayerLoopRunner.Run()    → सभी recurring items tick करें (Delay, WaitUntil, ...)
```

दोनों 16 timings में से हर एक के लिए run होते हैं, उस order में जिसमें engine उन्हें call करता है।

`Update` timing global frame count भी track करता है और periodically object pool trimming trigger करता है (हर `VlkTask.TrimCheckInterval` frames)।

## Initialization — RuntimeInitializeOnLoadMethod

ValkarnTasks `RuntimeInitializeLoadType.SubsystemRegistration` पर initialize होता है, जो earliest hook है जो Unity प्रदान करता है, किसी भी scene object पर `Awake()` से पहले fires:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void Init()
{
    // 1. Thread-safety checks के लिए मुख्य thread ID capture करें
    // 2. सभी 16 timings के लिए ContinuationQueue और PlayerLoopRunner create करें
    // 3. PlayerLoop में inject करें (idempotent)
    // 4. play-mode-state-changed callback register करें (केवल editor)
}
```

मुख्य thread ID इस point पर captured होता है और सभी pool और queue subsystems के साथ shared होता है ताकि वे main-thread enqueues (SpinLock path) को cross-thread enqueues (Treiber stack path) से distinguish कर सकें।

## Domain Reload Handling (Editor)

Unity Editor में, Play Mode enter या exit करने पर domain reload trigger होता है। Previous play session की static state अन्यथा persist होती और stale references cause करती।

ValkarnTasks इसे `Init()` के दौरान registered `EditorApplication.playModeStateChanged` callback के साथ handle करता है। जब state `ExitingPlayMode` या `EnteredEditMode` में transition करती है, निम्न cleanup runs:

```csharp
// सभी queues और runners reset करें
for (int i = 0; i < 16; i++)
{
    s_continuationQueues[i] = null;
    s_playerLoopRunners[i] = null;
}

// अन्य subsystems reset करें
VlkTask.ResetStatics();
TimeProvider.ResetToDefault();   // UnityTimeProvider पर revert करता है
PoolRegistry.Clear();
ContinuationQueue.ContinuationNode.ResetPool();
```

जब Play Mode subsequently फिर से enter होता है, `Init()` `RuntimeInitializeOnLoadMethod` के माध्यम से fires और fresh queues और runners allocate होते हैं। PlayerLoop injection check (`HasVlkTaskSystems`) marker systems को दूसरी बार insert होने से रोकता है यदि domain fully unloaded नहीं था।

## सही Timing चुनना

अधिकांश code default `Update` timing का उपयोग करना चाहिए। केवल तभी दूसरों को reach करें जब आपके पास कोई specific reason हो।

| Timing | कब उपयोग करें |
|---|---|
| `Initialization` / `LastInitialization` | बहुत early frame setup; game code में शायद ही कभी आवश्यक |
| `EarlyUpdate` / `LastEarlyUpdate` | Input sampling; physics और `Update` से पहले run होता है |
| `FixedUpdate` / `LastFixedUpdate` | Physics-synchronized logic; `MonoBehaviour.FixedUpdate` cadence से match करता है |
| `PreUpdate` / `LastPreUpdate` | Unity के main update dispatch से पहले; custom scheduler pre-pass के लिए उपयोगी |
| **`Update`** (default) | **Standard game logic; `MonoBehaviour.Update` से match करता है** |
| `LastUpdate` | Logic जो सभी `MonoBehaviour.Update` calls के बाद run होनी चाहिए |
| `PreLateUpdate` / `LastPreLateUpdate` | `MonoBehaviour.LateUpdate` से match करता है; camera और transform follow |
| `PostLateUpdate` / `LastPostLateUpdate` | Rendering submission के बाद; UI finalization, screenshot capture |
| `TimeUpdate` / `LastTimeUpdate` | Time value synchronization; बहुत कम ही आवश्यक |

```csharp
// अगले Update tick पर resume करें (default)
await VlkTask.Yield();

// अगले FixedUpdate की शुरुआत पर resume करें (physics-safe)
await VlkTask.Yield(PlayerLoopTiming.FixedUpdate);

// 2 सेकंड wait करें, unscaled time के साथ advancing, LateUpdate में checked
await VlkTask.Delay(
    TimeSpan.FromSeconds(2),
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.PreLateUpdate);

// एक condition true होने तक wait करें, सभी LateUpdate calls के बाद checked
await VlkTask.WaitUntil(() => _ready, PlayerLoopTiming.LastPreLateUpdate);
```
