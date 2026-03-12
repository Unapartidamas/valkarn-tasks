---
sidebar_position: 2
title: Job & Awaitable Bridges
---

# Job & Awaitable Bridges

Valkarn Tasks bridge types का एक set प्रदान करता है जो Unity के Job System और `Awaitable` API को `VlkTask` pipeline से connect करते हैं। प्रत्येक bridge एक thin, allocation-minimizing layer है; कोई hidden magic नहीं है।

यहाँ described सभी types namespace `UnaPartidaMas.Valkarn.Tasks.Bridge` में हैं और `#if UNITY_5_3_OR_NEWER` (या Awaitable support के लिए `#if UNITY_2023_1_OR_NEWER`) द्वारा guarded हैं।

---

## JobHandleExtensions — single JobHandle await करना

सबसे simple bridge। किसी भी `JobHandle` पर `.ToVlkTask()` call करें job finish होने पर complete होने वाला `VlkTask` वापस पाने के लिए।

```csharp
public static VlkTask ToVlkTask(
    this JobHandle handle,
    PlayerLoopTiming timing          = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

### यह कैसे काम करता है

1. **Fast path.** यदि `handle.IsCompleted` already true है, `handle.Complete()` immediately call होता है और `VlkTask.CompletedTask` return होता है — शून्य allocation, कोई PlayerLoop registration नहीं।
2. **Normal path.** एक pooled `JobHandlePromise` rent होता है, दिए गए `timing` पर PlayerLoop पर register होता है, और `VlkTask` में wrapped return होता है। प्रत्येक frame `MoveNext()` `JobHandle.ScheduleBatchedJobs()` call करता है (edit mode और batch mode में job queue flush करने के लिए) और फिर `handle.IsCompleted` check करता है। Handle done होने पर, promise task complete करता है और खुद pool में return करता है।
3. **Cancellation.** यदि `CancellationToken` fire करता है, handle forcibly completed होता है (`handle.Complete()` हमेशा job system leaks prevent करने के लिए call होता है) और task canceled state में transition करता है।

### Basic usage

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Jobs;

// Job schedule करें और immediately await करें।
var handle = myJob.Schedule();
await handle.ToVlkTask();

// Non-default timing और cancellation के साथ।
await handle.ToVlkTask(PlayerLoopTiming.PreUpdate, destroyCancellationToken);
```

---

## JobHandleWhenAll — parallel में multiple JobHandles await करना

जब आपको कई independent jobs schedule करने और उन सभी के complete होने के बाद ही resume करने की आवश्यकता हो, `JobHandleExtensions.WhenAll` उपयोग करें।

```csharp
// Simplest overload: Update timing पर सभी handles के लिए wait करता है।
public static VlkTask WhenAll(params JobHandle[] handles)

// Full overload: configurable timing और cancellation।
public static VlkTask WhenAll(
    JobHandle[]       handles,
    PlayerLoopTiming  timing,
    CancellationToken cancellationToken = default)

// Full overload के लिए Extension method alias।
public static VlkTask ToVlkTask(
    this JobHandle[]  handles,
    PlayerLoopTiming  timing              = PlayerLoopTiming.Update,
    CancellationToken cancellationToken   = default)
```

### यह कैसे काम करता है

- **Fast path.** यदि array में हर handle already completed है, सभी handles finalize होते हैं और `VlkTask.CompletedTask` immediately return होता है।
- **Empty array.** `VlkTask.CompletedTask` return करता है।
- **Normal path.** एक pooled `JobHandleArrayPromise` create होता है। Internally यह `ArrayPool<JobHandle>.Shared` से `JobHandle[]` rent करता है (per-call heap allocation avoid करते हुए), input handles copy करता है, और PlayerLoop पर register होता है। प्रत्येक frame यह compact swap-and-shrink loop का उपयोग करके केवल still-pending handles iterate करता है, और workers running रखने के लिए `JobHandle.ScheduleBatchedJobs()` call करता है।
- **Cancellation.** सभी remaining handles force-completed होते हैं और task canceled होता है।

### Usage

```csharp
var handleA = jobA.Schedule();
var handleB = jobB.Schedule();
var handleC = jobC.Schedule();

// तीनों के लिए wait करें।
await JobHandleExtensions.WhenAll(handleA, handleB, handleC);

// या array पर extension method उपयोग करके।
JobHandle[] handles = { handleA, handleB, handleC };
await handles.ToVlkTask();
```

---

## TempNativeArrayScope — await points के पार NativeArray lifetime

### समस्या

`Allocator.TempJob` के साथ allocated `NativeArray<T>` का short lifetime है। यदि आप एक allocate करते हैं, job schedule करते हैं, job handle `await` करते हैं, और array dispose करना भूल जाते हैं, Unity का safety system एक memory leak report करेगा। Plain `try`/`finally` उपयोग करना काम करता है लेकिन long async method में गलत करना आसान है।

`TempNativeArrayScope<T>` एक struct है जो `NativeArray<T>` wrap करता है और scope end होने पर इसे dispose करता है, `using` statement का उपयोग करते हुए — native memory पर applied RAII pattern।

### API

```csharp
public struct TempNativeArrayScope<T> : IDisposable where T : struct
{
    // Wrapped array access करता है। पहले से disposed होने पर ObjectDisposedException throw करता है।
    public NativeArray<T> Array { get; }

    // True यदि scope dispose नहीं हुआ और array created है।
    public bool IsCreated { get; }

    // Allocator.TempJob के साथ नया NativeArray<T> allocate करता है और ownership लेता है।
    public static TempNativeArrayScope<T> Create(int length);

    // Already-allocated NativeArray<T> की ownership लेता है।
    public static TempNativeArrayScope<T> Wrap(NativeArray<T> existing);

    // Array dispose करता है। Idempotent: कई बार call करना safe है।
    public void Dispose();
}

// Non-generic helper (type inference convenience)।
public static class TempNativeArrayScope
{
    public static TempNativeArrayScope<T> Create<T>(int length) where T : struct;
    public static TempNativeArrayScope<T> Wrap<T>(NativeArray<T> existing) where T : struct;
}
```

`Dispose` plain `int` flag उपयोग करता है `Interlocked` के बजाय क्योंकि scope `using var` के माध्यम से मुख्य thread पर single-threaded उपयोग के लिए designed है।

### Usage

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Collections;
using Unity.Jobs;

async VlkTask ProcessDataAsync(int count, CancellationToken ct)
{
    // using statement guarantee करता है कि Dispose() scope exit होने पर call होगा,
    // normal completion, exception, या cancellation द्वारा।
    using var inputScope  = TempNativeArrayScope.Create<float>(count);
    using var outputScope = TempNativeArrayScope.Create<float>(count);

    NativeArray<float> input  = inputScope.Array;
    NativeArray<float> output = outputScope.Array;

    // Input populate करें, job schedule करें।
    var job = new MyProcessingJob { Input = input, Output = output };
    var handle = job.Schedule(count, 64);

    // मुख्य thread block किए बिना await करें।
    // NativeArrays valid रहते हैं — job अभी running है।
    await handle.ToVlkTask(cancellationToken: ct);

    // Job done है। यहाँ results read करें।
    float total = 0f;
    for (int i = 0; i < count; i++)
        total += output[i];

    UnityEngine.Debug.Log($"Sum: {total}");

    // inputScope.Dispose() और outputScope.Dispose() यहाँ automatically run होते हैं।
}
```

आप already allocated array भी wrap कर सकते हैं:

```csharp
var existing = new NativeArray<int>(1024, Allocator.TempJob);
using var scope = TempNativeArrayScope.Wrap(existing);
// scope existing का owner है और इसे dispose करेगा।
```

### Common pitfall: Scope के बिना NativeArray lifetime

यह pattern broken है और safety system error cause करेगा:

```csharp
// WRONG: array job को outlive कर सकता है या exception होने पर leak हो सकता है।
var array = new NativeArray<float>(1024, Allocator.TempJob);
var handle = job.Schedule(1024, 64);
await handle.ToVlkTask(); // suspension point — array alive रहना चाहिए
array.Dispose();          // ऊपर exception होने पर कभी reached नहीं
```

सभी code paths में disposal guarantee करने के लिए `TempNativeArrayScope` या `try`/`finally` उपयोग करें।

---

## AwaitableBridge — Unity Awaitable को VlkTask में convert करना

`AwaitableBridge` Unity के `Awaitable` और `Awaitable<T>` types (Unity 2023.1 से available) को `VlkTask`-compatible awaiters में convert करने के लिए extension methods प्रदान करता है।

**Note:** `Awaitable` का अपना `GetAwaiter()` भी है। क्योंकि C# overload resolution हमेशा instance methods को extension methods पर prefer करती है, `async VlkTask` method के अंदर `await myAwaitable` लिखना already correctly काम करता है — Unity का awaiter `ICriticalNotifyCompletion` implement करता है और `VlkTask` builder इसे accept करता है। `.AsVlkTask()` extension methods केवल तभी आवश्यक हैं जब आप `Awaitable` को combinator (`VlkTask.WhenAll`, `VlkTask.WhenAny`) को pass करना चाहते हों या इसे `VlkTask` variable के रूप में store करना चाहते हों।

यह file `#if UNITY_2023_1_OR_NEWER` द्वारा guarded है।

### API

```csharp
// Awaitable को VlkTask-compatible awaiter में convert करें।
public static AwaitableVlkTaskAwaiter   AsVlkTask(this Awaitable awaitable)

// Awaitable<T> को VlkTask-compatible awaiter में convert करें।
public static AwaitableVlkTaskAwaiter<T> AsVlkTask<T>(this Awaitable<T> awaitable)
```

दोनों awaiters `ICriticalNotifyCompletion` implement करते हैं, जो `ExecutionContext` capture skip करता है। वे `IsCompleted`, `GetResult`, और `OnCompleted` directly wrapped Unity awaiter को delegate करते हैं।

### Usage

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnityEngine;

// Direct await — async VlkTask method में conversion के बिना काम करता है।
async VlkTask DirectExample()
{
    await Awaitable.NextFrameAsync();       // कोई conversion आवश्यक नहीं।
    await Awaitable.WaitForSecondsAsync(1f);
}

// Explicit conversion — combinators और storage के लिए आवश्यक।
async VlkTask CombinatorExample()
{
    VlkTask a = Awaitable.NextFrameAsync().AsVlkTask();
    VlkTask b = Awaitable.WaitForSecondsAsync(2f).AsVlkTask();
    await VlkTask.WhenAll(a, b);
}

// Result type के साथ generic version।
async VlkTask<Texture2D> LoadTextureExample(string path)
{
    Awaitable<Texture2D> loadOp = LoadTextureAsync(path);
    return await loadOp.AsVlkTask();
}
```

---

## JobBridge — source-generated wrapper

`JobBridge.cs` `JobPromise<TJob>` define करता है, जो source generator द्वारा उपयोग किया जाने वाला generic pooled promise type है। यह implementation detail है; आप normally इसे खुद instantiate नहीं करेंगे।

```csharp
// प्रत्येक frame एक JobHandle poll करता है। Source-generated ScheduleAsync methods द्वारा उपयोग।
public sealed class JobPromise<TJob> : VlkTask.ISource, IPlayerLoopItem, IPoolNode<JobPromise<TJob>>
    where TJob : struct
{
    public static JobPromise<TJob> Create(JobHandle handle, CancellationToken ct, out uint token);
}
```

Behaviour `JobHandlePromise` (`JobHandleExtensions` देखें) के identical है, सिवाय इसके कि यह pool isolation के लिए job type के ऊपर generic है — प्रत्येक job type को अपना pool मिलता है।

---

## Source generator: JobBridgeGenerator

`JobBridgeGenerator` एक Roslyn incremental source generator है (class `UnaPartidaMas.Valkarn.Tasks.SourceGen.Generators.JobBridgeGenerator`) जो automatically आपके job types के लिए `ScheduleAsync` extension methods produce करता है।

### यह क्या detect करता है

Generator compilation में सभी **public** structs scan करता है जो इनमें से एक implement करते हैं:
- `Unity.Jobs.IJob`
- `Unity.Jobs.IJobParallelFor`
- `Unity.Jobs.IJobFor`

Private और internal job structs skip होते हैं। यदि struct non-public type के अंदर nested है, यह भी skip होता है।

Generator कुछ नहीं करता यदि `UnaPartidaMas.Valkarn.Tasks.ValkarnTask` compilation में नहीं मिलता, इसलिए यह उन assemblies में safe है जो Valkarn Tasks reference नहीं करती।

### यह क्या generate करता है

Output file `ValkarnTask.JobBridge.Generated.g.cs` है। प्रत्येक detected job type के लिए यह एक `public static class __<TypeName>_AsyncExt` emit करता है जिसमें:

| Job interface | Generated method signature |
|---|---|
| `IJob` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, CancellationToken ct = default)` |
| `IJobParallelFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleParallelAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |

प्रत्येक generated method standard Unity extension methods का उपयोग करके job schedule करता है, फिर resulting `JobHandle` को `JobPromise<TJob>` में wrap करता है और `ValkarnTask` return करता है।

Nested types के लिए (जैसे outer class के अंदर job struct), generated class name underscores उपयोग करता है: `__Outer_Inner_AsyncExt`।

### Generated methods का usage

```csharp
// IJob example
public struct MyCalculationJob : IJob
{
    public NativeArray<float> Data;
    public void Execute() { /* ... */ }
}

// Generator produce करता है:
//   public static ValkarnTask ScheduleAsync(this ref MyCalculationJob job, CancellationToken ct = default)

async VlkTask RunCalculation(CancellationToken ct)
{
    using var scope = TempNativeArrayScope.Create<float>(1024);
    var job = new MyCalculationJob { Data = scope.Array };
    await job.ScheduleAsync(ct);  // generated extension method
    // यहाँ scope.Array से results read करें।
}

// IJobParallelFor example
public struct MyParallelJob : IJobParallelFor
{
    public NativeArray<float> Input;
    public NativeArray<float> Output;
    public void Execute(int index) { Output[index] = Input[index] * 2f; }
}

async VlkTask RunParallel(int length, CancellationToken ct)
{
    using var inputScope  = TempNativeArrayScope.Create<float>(length);
    using var outputScope = TempNativeArrayScope.Create<float>(length);
    var job = new MyParallelJob { Input = inputScope.Array, Output = outputScope.Array };
    await job.ScheduleAsync(length, innerLoopBatchCount: 64, ct);
}
```

---

## Source generator: AwaitableBridgeGenerator

`AwaitableBridgeGenerator` detect करता है कि `UnityEngine.Awaitable` और `UnityEngine.Awaitable<T>` compilation में present हैं या नहीं और जब होते हैं `AsValkarnTask()` extension methods emit करता है।

Output file `ValkarnTask.AwaitableBridge.Generated.g.cs` है। Generated code `namespace UnaPartidaMas.Valkarn.Tasks.Bridge` में `AwaitableBridgeExtensions` class के तहत रहता है।

Generated methods:

```csharp
// जब UnityEngine.Awaitable मिलता है emit होता है:
public static async ValkarnTask AsValkarnTask(this Awaitable awaitable)
{
    await awaitable;
}

// जब UnityEngine.Awaitable<T> मिलता है emit होता है:
public static async ValkarnTask<T> AsValkarnTask<T>(this Awaitable<T> awaitable)
{
    return await awaitable;
}
```

ये `async ValkarnTask` methods हैं, इसलिए ये Valkarn Tasks के pooled async builder से गुज़रते हैं — warm pool पर ये zero-allocation हैं।

Generator guarded है: यदि `ValkarnTask` compilation में नहीं है, कोई code emit नहीं होता। यह उन assemblies में CS0246 errors prevent करता है जो Unity reference करती हैं लेकिन Valkarn Tasks नहीं।

---

## Full working example: ECS system में Job bridge

निम्नलिखित `Samples~/ECS/JobBridgeExample.cs` से है। यह `ISystem` से Burst parallel job schedule करने, उसे बिना blocking के await करने, और results write back करने का complete pattern दिखाता है।

```csharp
#if UNITY_5_3_OR_NEWER && VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnaPartidaMas.Valkarn.Tasks.ECS;

public partial struct JobBridgeExample : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<HealthData>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var worldCt = state.World.GetWorldCancellationToken();

        // OnUpdate के अंदर synchronously सभी entity data extract करें।
        // Async methods में ref parameters नहीं हो सकते (CS1988), इसलिए data
        // यहाँ copy होनी चाहिए और async method को value द्वारा pass होनी चाहिए।
        var query       = SystemAPI.QueryBuilder().WithAll<HealthData>().Build();
        var entityCount = query.CalculateEntityCount();
        if (entityCount == 0) return;

        var entities    = query.ToEntityArray(Allocator.TempJob);
        var healthArray = query.ToComponentDataArray<HealthData>(Allocator.TempJob);
        var results     = new NativeArray<float>(entityCount, Allocator.TempJob);

        // Async method NativeArrays की ownership लेता है और उन्हें dispose करता है।
        ProcessHealthAsync(state.EntityManager, entities, healthArray, results, worldCt).Forget();
        state.Enabled = false;
    }

    public void OnDestroy(ref SystemState state) { }

    static async VlkTask ProcessHealthAsync(
        EntityManager entityManager,
        NativeArray<Entity> entities,
        NativeArray<HealthData> healthArray,
        NativeArray<float> results,
        CancellationToken ct)
    {
        try
        {
            // Phase 1: Burst job schedule करें।
            var job = new HealthProcessingJob
            {
                HealthInputs     = healthArray,
                ProcessedOutputs = results,
            };
            var handle = job.Schedule(entities.Length, batchSize: 64);

            // Phase 2: मुख्य thread block किए बिना completion await करें।
            await handle.ToVlkTask(cancellationToken: ct);

            // Phase 3: Results apply करें। हम वापस मुख्य thread पर हैं।
            ct.ThrowIfCancellationRequested();

            for (int i = 0; i < entities.Length; i++)
            {
                // Job running होते entity destroy हो सकती है।
                if (!entityManager.SafeEntityExists(entities[i]))
                    continue;

                entityManager.SetComponentData(entities[i], new HealthData
                {
                    CurrentHealth = results[i],
                });
            }
        }
        finally
        {
            // NativeArrays हमेशा dispose करें — success, exception, या cancellation पर runs।
            if (entities.IsCreated)    entities.Dispose();
            if (healthArray.IsCreated) healthArray.Dispose();
            if (results.IsCreated)     results.Dispose();
        }
    }

    [BurstCompile]
    struct HealthProcessingJob : IJobParallelFor
    {
        [ReadOnly]  public NativeArray<HealthData> HealthInputs;
        [WriteOnly] public NativeArray<float>      ProcessedOutputs;

        public void Execute(int index)
        {
            var h = HealthInputs[index];
            var newHealth = h.CurrentHealth + h.RegenRate;
            if (newHealth > h.MaxHealth) newHealth = h.MaxHealth;
            ProcessedOutputs[index] = newHealth;
        }
    }

    struct HealthData : IComponentData
    {
        public float CurrentHealth;
        public float MaxHealth;
        public float RegenRate;
    }
}
#endif
```

---

## Bridge types का सारांश

| Type | Purpose | Allocation |
|---|---|---|
| `JobHandleExtensions.ToVlkTask()` | Single `JobHandle` await करें | Fast path पर शून्य; अन्यथा pooled promise |
| `JobHandleExtensions.WhenAll()` | Multiple `JobHandle` parallel में await करें | Fast path पर शून्य; अन्यथा pooled promise + `ArrayPool` rent |
| `TempNativeArrayScope<T>` | `NativeArray` के लिए RAII lifetime management | कोई नहीं (struct) |
| `AwaitableBridge.AsVlkTask()` | `Awaitable`/`Awaitable<T>` को VlkTask में convert करें | कोई नहीं (struct awaiter) |
| Generated `ScheduleAsync()` | Typed job directly await करें | Pooled `JobPromise<TJob>` |
