---
sidebar_position: 1
title: Burst & ECS Integration
---

# Burst & ECS Integration

Valkarn Tasks में Unity के Burst compiler, Unity Collections, और Entities (ECS) package के साथ optional integration शामिल है। यह सभी functionality conditionally compiled है — यह केवल तभी active है जब required packages present हों और corresponding scripting define symbols set हों।

## आवश्यकताएँ

| Feature | Required package | Scripting define |
|---|---|---|
| `NativeTimerHeap`, `NativeScheduler`, `BurstSchedulerRunner` | Unity Burst 1.8+, Unity Collections 2.0+ | `VTASKS_HAS_BURST` और `VTASKS_HAS_COLLECTIONS` |
| `AsyncSystemUtilities` | Unity Entities 1.0+ | `VTASKS_HAS_ENTITIES` |

सभी Burst/ECS source files इन defines से match करने वाले `#if` guards में wrapped हैं। इन files में कुछ भी compile या link नहीं होता जब तक defines present न हों।

### Setup

1. Unity Package Manager के माध्यम से required packages install करें:
   - `com.unity.burst` 1.8 या बाद में
   - `com.unity.collections` 2.0 या बाद में
   - `com.unity.entities` 1.0 या बाद में (केवल ECS utilities के लिए)

2. **Project Settings > Player > Scripting Define Symbols** में scripting define symbols add करें:
   ```
   VTASKS_HAS_BURST;VTASKS_HAS_COLLECTIONS;VTASKS_HAS_ENTITIES
   ```
   आपको केवल उन packages के लिए defines add करने की आवश्यकता है जो install हैं।

---

## NativeTimerHeap

**Namespace:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Guard:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeTimerHeap` timers schedule करने के लिए एक Burst-compatible binary min-heap है। यह `TimerEntry` values को deadline द्वारा ordered store करता है, प्रति expired timer O(log n) insert और O(log n) removal देता है।

### मुख्य types

```csharp
// Heap में stored entry। Deadline द्वारा ordered।
public struct TimerEntry : IComparable<TimerEntry>
{
    public long Deadline;
    public int  Id;
}

public struct NativeTimerHeap : IDisposable
{
    public bool IsCreated { get; }
    public int  Count     { get; }

    // Heap create करता है। Long-lived heaps के लिए Allocator.Persistent उपयोग करें।
    public NativeTimerHeap(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // एक नया timer insert करता है। Timer ID return करता है (callback identify करने के लिए उपयोग)।
    // Deadline और DrainExpired को pass किया गया value same unit उपयोग करना चाहिए
    // (BurstSchedulerRunner DateTime ticks via Time.realtimeSinceStartupAsDouble उपयोग करता है)।
    [BurstCompile]
    public int Schedule(long deadline);

    // Deadline <= currentTimestamp वाले सभी timers की IDs remove और append करता है।
    // Drained timers की संख्या return करता है।
    [BurstCompile]
    public int DrainExpired(long currentTimestamp, NativeList<int> expiredIds);

    public void Dispose();
}
```

`NativeTimerHeap` एक unmanaged struct है। यह managed delegates store नहीं कर सकता — IDs मुख्य thread पर `BurstSchedulerRunner` dictionary में managed callbacks से match होते हैं।

---

## NativeScheduler

**Namespace:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Guard:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeScheduler` `NativeQueue<ScheduledWork>` द्वारा backed एक Burst-compatible work queue है। Burst-compiled jobs work items enqueue करते हैं; मुख्य thread उन्हें प्रत्येक frame drain करता है।

### मुख्य types

```csharp
public enum WorkType : byte
{
    TimerExpired  = 0,
    JobCompleted  = 1,
    Custom        = 2
}

public struct ScheduledWork
{
    public int      Id;
    public WorkType Type;
    public long     Payload;
}

public struct NativeScheduler : IDisposable
{
    public bool IsCreated { get; }

    public NativeScheduler(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // Burst-compiled job से work item enqueue करता है।
    [BurstCompile]
    public void Enqueue(ScheduledWork work);

    // सभी pending work को `results` में drain करता है। केवल मुख्य thread से call करें।
    // Drained items की संख्या return करता है।
    public int Drain(NativeList<ScheduledWork> results);

    // IJobParallelFor jobs में उपयोग के लिए suitable parallel writer return करता है।
    public NativeQueue<ScheduledWork>.ParallelWriter AsParallelWriter();

    public void Dispose();
}
```

Queue Burst world और managed world के बीच crossing point है। `Enqueue` Burst-callable है; `Drain` केवल main-thread है।

---

## BurstSchedulerRunner

**Namespace:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Guard:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`BurstSchedulerRunner` native scheduler/timer heap और आपके बाकी game के बीच managed bridge है। यह `IPlayerLoopItem` implement करता है, इसलिए Unity registered timing पर per frame `MoveNext()` call करता है। प्रत्येक frame यह:

1. `NativeScheduler` drain करता है और work ID द्वारा match किए गए कोई registered managed callbacks fire करता है।
2. Expired timers के लिए `NativeTimerHeap` drain करता है और उनके managed callbacks fire करता है।

Callbacks द्वारा throw की गई exceptions `VlkTask.PublishUnobservedException` को forward होती हैं बजाय PlayerLoop के माध्यम से propagate होने के।

### API

```csharp
public sealed class BurstSchedulerRunner : IPlayerLoopItem, IDisposable
{
    // Runner create करता है और PlayerLoop पर register करता है। Instance return करता है।
    // जब longer needed न हो, returned runner Dispose करें।
    public static BurstSchedulerRunner Create(
        PlayerLoopTiming timing       = PlayerLoopTiming.Update,
        int initialCapacity           = 64);

    // Burst jobs से enqueueing के लिए NativeScheduler का direct access।
    public NativeScheduler Scheduler { get; }

    // Managed timer callback schedule करता है। मुख्य thread से call होना चाहिए।
    // एक timer ID return करता है (केवल identification के लिए; कोई cancel API नहीं है)।
    public int ScheduleTimer(TimeSpan delay, Action callback);

    // एक managed callback को Burst job द्वारा enqueued work ID के साथ associate करता है।
    // मुख्य thread से call होना चाहिए, job complete होने वाले frame से पहले या दौरान।
    public void RegisterCallback(int workId, Action callback);

    // सभी native containers dispose करता है और PlayerLoop से unregister करता है।
    public void Dispose();
}
```

### BurstSchedulerRunner default scheduler से कैसे differ करता है

Default Valkarn Tasks scheduler directly `async/await` state machines के साथ integrate होता है और `PlayerLoopHelper` के माध्यम से continuation dispatch manage करता है। `BurstSchedulerRunner` specifically Burst-compiled jobs से signalling के लिए एक separate lane add करता है:

| | Default scheduler | BurstSchedulerRunner |
|---|---|---|
| Continuation type | `ISource` के माध्यम से Managed `Action` | ID द्वारा registered Managed `Action` |
| Signal source | C# awaiter pattern | Unmanaged `NativeScheduler.Enqueue` |
| Timer source | `VlkTask.Delay` (managed) | `NativeTimerHeap` (unmanaged) |
| Thread safety | Main-thread continuations | Enqueue Burst-safe है; drain केवल main-thread |

### Usage pattern

```csharp
// 1. Runner एक बार create करें (जैसे bootstrap MonoBehaviour या ISystem.OnCreate में)।
var runner = BurstSchedulerRunner.Create(PlayerLoopTiming.Update, initialCapacity: 128);

// 2. Timer callback schedule करें (मुख्य thread)।
runner.ScheduleTimer(TimeSpan.FromSeconds(2.0), () =>
{
    Debug.Log("दो सेकंड elapsed (unmanaged timer)।");
});

// 3. Burst job से, work item enqueue करें।
//    NativeScheduler.ParallelWriter IJobParallelFor से उपयोग के लिए safe है।
var writer = runner.Scheduler.AsParallelWriter();
// Execute(int index) के अंदर:
writer.Enqueue(new ScheduledWork { Id = myWorkId, Type = WorkType.Custom });

// 4. Job complete होने से पहले मुख्य thread पर managed callback register करें।
runner.RegisterCallback(myWorkId, () =>
{
    Debug.Log($"Job completed signal received for work {myWorkId}।");
});

// 5. Done होने पर runner dispose करें (जैसे OnDestroy या domain reload)।
runner.Dispose();
```

---

## AsyncSystemUtilities

**Namespace:** `UnaPartidaMas.Valkarn.Tasks.ECS`
**Guard:** `#if VTASKS_HAS_ENTITIES`

`AsyncSystemUtilities` async ECS systems लिखने के लिए दो extension helpers प्रदान करता है।

### GetWorldCancellationToken

```csharp
public static CancellationToken GetWorldCancellationToken(
    this World world,
    PlayerLoopTiming timing = PlayerLoopTiming.Update)
```

एक `CancellationToken` return करता है जो दिए गए `World` के destroy होने पर automatically canceled होता है। Internally यह एक fire-and-forget `VlkTask` start करता है जो `world.IsCreated` true होते तक प्रत्येक frame `VlkTask.Yield(timing)` call करता है, फिर जब loop exit होता है एक `CancellationTokenSource` cancel करता है।

यदि call करते समय world पहले से destroyed है, यह already canceled state में एक token return करता है।

किसी system से launch होने वाले हर async method को यह token pass करें ताकि world जाने पर in-flight काम automatically stop हो।

### SafeEntityExists

```csharp
public static bool SafeEntityExists(this EntityManager entityManager, Entity entity)
```

`entityManager.Exists(entity)` call करता है और `ObjectDisposedException` throw होने पर `false` return करता है। यह तब हो सकता है जब `EntityManager` world dispose होने के बाद access होता है, जो async code में एक real race condition है जो frame boundaries पर survive करता है।

Entity पर write back करने से पहले हर `await` point के बाद इसका उपयोग करें।

---

## Working example: Async ECS system

निम्नलिखित उदाहरण `Samples~/ECS/AsyncLoadSystem.cs` से है। यह `ISystem` से one-shot async initialization का canonical pattern demonstrate करता है।

```csharp
#if VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Entities;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.ECS;

public partial struct AsyncLoadSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // इस World के lifetime से tied cancellation token obtain करें।
        // यदि World destroyed हो, इस token के साथ launched सभी async काम
        // automatically canceled हो जाएँगे।
        var worldCt = state.World.GetWorldCancellationToken();

        // Async initialization launch करें और task forget करें।
        // Forget() किसी unhandled exception को VlkTask.PublishUnobservedException पर route करता है।
        InitializeAsync(state.WorldUnmanaged, worldCt).Forget();
    }

    public void OnUpdate(ref SystemState state) { }
    public void OnDestroy(ref SystemState state) { }

    static async VlkTask InitializeAsync(WorldUnmanaged world, CancellationToken ct)
    {
        // Phase 1: Background thread पर data load करें।
        // RunOnThreadPool worker thread पर switch करता है, delegate run करता है,
        // और automatically मुख्य thread पर return करता है।
        var configData = await VlkTask.RunOnThreadPool(
            () => LoadFromDisk(),
            cancellationToken: ct);

        // Phase 2: मुख्य thread पर results apply करें।
        // Check करें cancellation in case World loading के दौरान destroyed हो गया।
        ct.ThrowIfCancellationRequested();
        ApplyConfiguration(world, configData);
    }

    static ConfigData LoadFromDisk()
    {
        // Pure C# only — यहाँ कोई Unity या ECS API calls allowed नहीं।
        return new ConfigData { MaxEnemies = 100, SpawnRadius = 50f };
    }

    static void ApplyConfiguration(WorldUnmanaged world, ConfigData data)
    {
        // Safe: हम मुख्य thread पर हैं।
        UnityEngine.Debug.Log($"Config loaded: MaxEnemies={data.MaxEnemies}");
    }

    struct ConfigData
    {
        public int   MaxEnemies;
        public float SpawnRadius;
    }
}
#endif
```

AI throttling उदाहरण (`Samples~/ECS/AISystemExample.cs`) इस pattern पर build करता है और concurrent async tasks की संख्या cap करने के लिए `AsyncThrottle` add करता है। उस pattern पर details के लिए Throttling documentation देखें।

---

## सीमाएँ

निम्नलिखित constraints सभी Burst/ECS async code पर apply होते हैं। इन्हें violate करने पर editor errors, job safety violations, या silent data corruption होगी।

### Burst-compiled code के अंदर

- **कोई managed types नहीं।** Burst ऐसे code compile नहीं कर सकता जो managed objects (classes, delegates, arrays, strings, `List<T>`, etc.) allocate, access, या reference करता है। केवल blittable structs और native containers allowed हैं।
- **कोई exceptions नहीं।** Burst `try`/`catch`/`throw` support नहीं करता। Errors communicate करने के लिए return codes या flags उपयोग करें।
- **कोई `async`/`await` नहीं।** C# async state machines managed हैं और Burst द्वारा compile नहीं किए जा सकते। `NativeScheduler` और `NativeTimerHeap` managed continuations के लिए signalling के लिए एक side-channel प्रदान करते हैं, लेकिन continuations खुद मुख्य thread पर run होती हैं।
- **कोई static mutable managed state नहीं।** Burst jobs static readonly fields read कर सकते हैं लेकिन managed statics में write नहीं करना चाहिए।

### ECS systems में await points के पार

- **Entity lifetime.** Async method suspended होते entities destroy हो सकती हैं। Write back करने से पहले हर `await` के बाद `entityManager.SafeEntityExists(entity)` call करें।
- **ComponentLookup staleness.** `ComponentLookup`, `RefRW`, और अन्य chunk-pointer types structural changes के बाद invalid हो जाते हैं, जो किसी भी frame पर हो सकते हैं। इन्हें `await` points के पार cache न करें। Resume के बाद `SystemState` से re-acquire करें, या directly `EntityManager` उपयोग करें।
- **`ref` parameters.** Async methods में `ref`, `in`, या `out` parameters नहीं हो सकते (C# error CS1988)। Synchronous `OnUpdate` method में सभी ECS data synchronously extract करें और async method को value द्वारा pass करें।
- **Async methods में `SystemAPI`.** `SystemAPI` source-generated है और केवल partial `ISystem` methods के अंदर काम करती है। यह `async` methods में available नहीं है। पहले `await` से पहले सभी `SystemAPI` queries perform करें।
- **Thread safety.** `EntityManager`, `ComponentLookup`, और structural changes केवल main-thread हैं। Pure C# computation के लिए ही `VlkTask.RunOnThreadPool` उपयोग करें बिना किसी Unity या ECS API calls के।
