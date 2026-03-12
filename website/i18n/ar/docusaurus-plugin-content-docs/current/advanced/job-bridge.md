---
sidebar_position: 2
title: جسور الوظائف والـAwaitable
---

# جسور الوظائف والـAwaitable

توفر Valkarn Tasks مجموعة من أنواع الجسور التي تربط نظام الوظائف في Unity وواجهة برمجة `Awaitable` بمسار `VlkTask`. كل جسر طبقة رفيعة تُقلِّل التخصيص؛ لا توجد أي حيل خفية.

جميع الأنواع الموصوفة هنا في مساحة الاسم `UnaPartidaMas.Valkarn.Tasks.Bridge` ومحمية بـ`#if UNITY_5_3_OR_NEWER` (أو `#if UNITY_2023_1_OR_NEWER` لدعم Awaitable).

---

## JobHandleExtensions — انتظار JobHandle واحد

الجسر الأبسط. استدعِ `.ToVlkTask()` على أي `JobHandle` للحصول على `VlkTask` يكتمل عند انتهاء الوظيفة.

```csharp
public static VlkTask ToVlkTask(
    this JobHandle handle,
    PlayerLoopTiming timing          = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

### كيف يعمل

1. **المسار السريع.** إذا كان `handle.IsCompleted` صحيحًا بالفعل، يُستدعى `handle.Complete()` فورًا ويُرجَع `VlkTask.CompletedTask` — صفر تخصيص، لا تسجيل في PlayerLoop.
2. **المسار العادي.** يُستعار `JobHandlePromise` من المجموعة، يُسجَّل على PlayerLoop عند `timing` المعطى، ويُرجَع مغلَّفًا في `VlkTask`. في كل إطار تستدعي `MoveNext()` الدالة `JobHandle.ScheduleBatchedJobs()` (لتدفق قائمة الوظائف في وضع التحرير وضع الدُفعة) ثم تتحقق من `handle.IsCompleted`. عند اكتمال المقبض، تكتمل الوعدة المهمةَ وتُعيد نفسها إلى المجموعة.
3. **الإلغاء.** إذا أُطلق `CancellationToken`، يُكمَل المقبض بالإجبار (`handle.Complete()` يُستدعى دائمًا لمنع تسرب نظام الوظائف) وتنتقل المهمة إلى الحالة الملغاة.

### الاستخدام الأساسي

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Jobs;

// جدوِل وظيفة وانتظرها فورًا.
var handle = myJob.Schedule();
await handle.ToVlkTask();

// مع توقيت غير افتراضي وإلغاء.
await handle.ToVlkTask(PlayerLoopTiming.PreUpdate, destroyCancellationToken);
```

---

## JobHandleWhenAll — انتظار عدة JobHandle بالتوازي

عندما تحتاج إلى جدولة عدة وظائف مستقلة والاستئناف بعد اكتمالها جميعًا، استخدم `JobHandleExtensions.WhenAll`.

```csharp
// أبسط تحميل زائد: ينتظر جميع المقابض عند توقيت Update.
public static VlkTask WhenAll(params JobHandle[] handles)

// التحميل الزائد الكامل: توقيت وإلغاء قابلان للتهيئة.
public static VlkTask WhenAll(
    JobHandle[]       handles,
    PlayerLoopTiming  timing,
    CancellationToken cancellationToken = default)

// طريقة امتداد مستعارة للتحميل الزائد الكامل.
public static VlkTask ToVlkTask(
    this JobHandle[]  handles,
    PlayerLoopTiming  timing              = PlayerLoopTiming.Update,
    CancellationToken cancellationToken   = default)
```

### كيف يعمل

- **المسار السريع.** إذا كانت كل مقاضٍ في المصفوفة مكتملة بالفعل، تُنهَى جميع المقابض ويُرجَع `VlkTask.CompletedTask` فورًا.
- **المصفوفة الفارغة.** تُرجع `VlkTask.CompletedTask`.
- **المسار العادي.** يُنشأ `JobHandleArrayPromise` من المجموعة. داخليًا يستعير `JobHandle[]` من `ArrayPool<JobHandle>.Shared` (تجنبًا لتخصيص الكومة لكل استدعاء)، ينسخ المقابض المدخلة فيه، ويسجِّل على PlayerLoop. في كل إطار يُكرِّر فقط على المقابض التي لم تكتمل بعد باستخدام حلقة ضغط بالمبادلة والتقليص، ويستدعي `JobHandle.ScheduleBatchedJobs()` لإبقاء العمال يعملون.
- **الإلغاء.** تُكمَل جميع المقابض المتبقية بالإجبار وتُلغى المهمة.

### الاستخدام

```csharp
var handleA = jobA.Schedule();
var handleB = jobB.Schedule();
var handleC = jobC.Schedule();

// انتظر الثلاثة.
await JobHandleExtensions.WhenAll(handleA, handleB, handleC);

// أو باستخدام طريقة الامتداد على مصفوفة.
JobHandle[] handles = { handleA, handleB, handleC };
await handles.ToVlkTask();
```

---

## TempNativeArrayScope — عمر NativeArray عبر نقاط الانتظار

### المشكلة

`NativeArray<T>` المُخصَّص بـ`Allocator.TempJob` له عمر قصير. إذا خصَّصت واحدة، وجدولت وظيفة، وانتظرت مقبض الوظيفة، ثم نسيت التخلُّص من المصفوفة، سيُبلِّغ نظام أمان Unity عن تسرب ذاكرة. استخدام `try`/`finally` بسيط يعمل لكنه سهل الخطأ في الدوال غير المتزامنة الطويلة.

`TempNativeArrayScope<T>` هو بنية تغلِّف `NativeArray<T>` وتتخلَّص منها عند انتهاء النطاق، باستخدام عبارة `using` — نمط RAII مطبَّق على الذاكرة الأصلية.

### واجهة برمجة التطبيقات

```csharp
public struct TempNativeArrayScope<T> : IDisposable where T : struct
{
    // الوصول إلى المصفوفة المُغلَّفة. يُطرح ObjectDisposedException إذا جرى التخلُّص منها بالفعل.
    public NativeArray<T> Array { get; }

    // صحيح إذا لم يُتخلَّص من النطاق وكانت المصفوفة منشأةً.
    public bool IsCreated { get; }

    // يُخصِّص NativeArray<T> جديدة بـAllocator.TempJob ويتولى ملكيتها.
    public static TempNativeArrayScope<T> Create(int length);

    // يتولى ملكية NativeArray<T> مُخصَّصة بالفعل.
    public static TempNativeArrayScope<T> Wrap(NativeArray<T> existing);

    // يتخلَّص من المصفوفة. آمن للاستدعاء عدة مرات.
    public void Dispose();
}

// مساعد غير جنيريكي (لسهولة استنتاج النوع).
public static class TempNativeArrayScope
{
    public static TempNativeArrayScope<T> Create<T>(int length) where T : struct;
    public static TempNativeArrayScope<T> Wrap<T>(NativeArray<T> existing) where T : struct;
}
```

تستخدم `Dispose` علَمًا `int` بسيطًا بدلاً من `Interlocked` لأن النطاق مصمَّم للاستخدام أحادي الخيط على الخيط الرئيسي عبر `using var`.

### الاستخدام

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Collections;
using Unity.Jobs;

async VlkTask ProcessDataAsync(int count, CancellationToken ct)
{
    // تضمن عبارة using استدعاء Dispose() عند خروج النطاق،
    // سواء بالاكتمال العادي أو الاستثناء أو الإلغاء.
    using var inputScope  = TempNativeArrayScope.Create<float>(count);
    using var outputScope = TempNativeArrayScope.Create<float>(count);

    NativeArray<float> input  = inputScope.Array;
    NativeArray<float> output = outputScope.Array;

    // ملء المدخلات، جدولة الوظيفة.
    var job = new MyProcessingJob { Input = input, Output = output };
    var handle = job.Schedule(count, 64);

    // انتظر دون حجب الخيط الرئيسي.
    // تبقى NativeArrays صالحة — الوظيفة لا تزال تعمل.
    await handle.ToVlkTask(cancellationToken: ct);

    // الوظيفة منتهية. اقرأ النتائج هنا.
    float total = 0f;
    for (int i = 0; i < count; i++)
        total += output[i];

    UnityEngine.Debug.Log($"المجموع: {total}");

    // inputScope.Dispose() وoutputScope.Dispose() تعمل تلقائيًا هنا.
}
```

يمكنك أيضًا تغليف مصفوفة خصَّصتها بالفعل:

```csharp
var existing = new NativeArray<int>(1024, Allocator.TempJob);
using var scope = TempNativeArrayScope.Wrap(existing);
// scope تمتلك existing وستتخلَّص منها.
```

### الخطأ الشائع: عمر NativeArray بدون نطاق

هذا النمط معطوب وسيسبب خطأ في نظام الأمان:

```csharp
// خطأ: قد تعيش المصفوفة أطول من الوظيفة أو تتسرب إذا حدث استثناء.
var array = new NativeArray<float>(1024, Allocator.TempJob);
var handle = job.Schedule(1024, 64);
await handle.ToVlkTask(); // نقطة تعليق — يجب أن تبقى المصفوفة حيّة
array.Dispose();          // لا يُصل إليها إذا أُطلق استثناء أعلاه
```

استخدم `TempNativeArrayScope` أو `try`/`finally` لضمان التخلُّص في جميع مسارات الكود.

---

## AwaitableBridge — تحويل Unity Awaitable إلى VlkTask

يوفر `AwaitableBridge` طرق امتداد لتحويل أنواع `Awaitable` و`Awaitable<T>` في Unity (المتوفرة منذ Unity 2023.1) إلى مُنتظِرين متوافقين مع `VlkTask`.

**ملاحظة:** لدى `Awaitable` أيضًا `GetAwaiter()` الخاص بها. نظرًا لأن دقة التحميل الزائد في C# تُفضِّل دائمًا دوال النسخة على دوال الامتداد، فإن كتابة `await myAwaitable` داخل دالة `async VlkTask` تعمل بالفعل بشكل صحيح — مُنتظِر Unity ينفِّذ `ICriticalNotifyCompletion` ومُنشئ `VlkTask` يقبله. طرق الامتداد `.AsVlkTask()` مطلوبة فقط عندما تريد تمرير `Awaitable` إلى مُدمِج (`VlkTask.WhenAll`، `VlkTask.WhenAny`) أو تخزينه كمتغير `VlkTask`.

هذا الملف محمي بـ`#if UNITY_2023_1_OR_NEWER`.

### واجهة برمجة التطبيقات

```csharp
// تحويل Awaitable إلى مُنتظِر متوافق مع VlkTask.
public static AwaitableVlkTaskAwaiter   AsVlkTask(this Awaitable awaitable)

// تحويل Awaitable<T> إلى مُنتظِر متوافق مع VlkTask.
public static AwaitableVlkTaskAwaiter<T> AsVlkTask<T>(this Awaitable<T> awaitable)
```

كلا المُنتظِرَين ينفِّذان `ICriticalNotifyCompletion`، مما يتجنب التقاط `ExecutionContext`. يُفوِّضان `IsCompleted` و`GetResult` و`OnCompleted` مباشرةً إلى المُنتظِر الأصلي لـUnity.

### الاستخدام

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnityEngine;

// انتظار مباشر — يعمل بدون تحويل في دالة async VlkTask.
async VlkTask DirectExample()
{
    await Awaitable.NextFrameAsync();       // لا حاجة للتحويل.
    await Awaitable.WaitForSecondsAsync(1f);
}

// تحويل صريح — مطلوب للمُدمِجات والتخزين.
async VlkTask CombinatorExample()
{
    VlkTask a = Awaitable.NextFrameAsync().AsVlkTask();
    VlkTask b = Awaitable.WaitForSecondsAsync(2f).AsVlkTask();
    await VlkTask.WhenAll(a, b);
}

// الإصدار الجنيريكي مع نوع نتيجة.
async VlkTask<Texture2D> LoadTextureExample(string path)
{
    Awaitable<Texture2D> loadOp = LoadTextureAsync(path);
    return await loadOp.AsVlkTask();
}
```

---

## JobBridge — الغلاف المُولَّد بالمصدر

يعرِّف `JobBridge.cs` الفئة `JobPromise<TJob>`، نوع الوعدة المُجمَّعة الجنيرية المستخدمة من قِبَل مولِّد المصدر. هو تفصيل تنفيذي؛ عادةً لن تُنشئه بنفسك.

```csharp
// يستطلع JobHandle في كل إطار. تستخدمه دوال ScheduleAsync المُولَّدة بالمصدر.
public sealed class JobPromise<TJob> : VlkTask.ISource, IPlayerLoopItem, IPoolNode<JobPromise<TJob>>
    where TJob : struct
{
    public static JobPromise<TJob> Create(JobHandle handle, CancellationToken ct, out uint token);
}
```

السلوك مطابق لـ`JobHandlePromise` (انظر `JobHandleExtensions`)، إلا أنه جنيريكي على نوع الوظيفة لعزل المجموعة — كل نوع وظيفة يحصل على مجموعته الخاصة.

---

## مولِّد المصدر: JobBridgeGenerator

`JobBridgeGenerator` هو مولِّد مصدر تدريجي بـRoslyn (الفئة `UnaPartidaMas.Valkarn.Tasks.SourceGen.Generators.JobBridgeGenerator`) يُنتج تلقائيًا دوال الامتداد `ScheduleAsync` لأنواع وظائفك.

### ما يكتشفه

يفحص المولِّد جميع البنى **العامة** في التجميع التي تنفِّذ أحد:
- `Unity.Jobs.IJob`
- `Unity.Jobs.IJobParallelFor`
- `Unity.Jobs.IJobFor`

يتم تخطي البنى الخاصة والداخلية. إذا كانت البنية متداخلة داخل نوع غير عام، يُتخطى أيضًا.

لا يفعل المولِّد شيئًا إذا لم يُعثر على `UnaPartidaMas.Valkarn.Tasks.ValkarnTask` في التجميع، لذا فهو آمن في التجميعات التي لا تشير إلى Valkarn Tasks.

### ما يُولِّده

ملف الإخراج هو `ValkarnTask.JobBridge.Generated.g.cs`. لكل نوع وظيفة مُكتشَف يُصدر `public static class __<TypeName>_AsyncExt` يحتوي على:

| واجهة الوظيفة | توقيع الدالة المُولَّدة |
|---|---|
| `IJob` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, CancellationToken ct = default)` |
| `IJobParallelFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleParallelAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |

كل دالة مُولَّدة تجدول الوظيفة باستخدام دوال الامتداد القياسية لـUnity، ثم تغلِّف `JobHandle` الناتجة في `JobPromise<TJob>` وتُرجع `ValkarnTask`.

بالنسبة للأنواع المتداخلة (مثل بنية وظيفة داخل فئة خارجية)، يستخدم اسم الفئة المُولَّدة شرطات سفلية: `__Outer_Inner_AsyncExt`.

### استخدام الدوال المُولَّدة

```csharp
// مثال IJob
public struct MyCalculationJob : IJob
{
    public NativeArray<float> Data;
    public void Execute() { /* ... */ }
}

// يُنتج المولِّد:
//   public static ValkarnTask ScheduleAsync(this ref MyCalculationJob job, CancellationToken ct = default)

async VlkTask RunCalculation(CancellationToken ct)
{
    using var scope = TempNativeArrayScope.Create<float>(1024);
    var job = new MyCalculationJob { Data = scope.Array };
    await job.ScheduleAsync(ct);  // دالة امتداد مُولَّدة
    // اقرأ النتائج من scope.Array هنا.
}

// مثال IJobParallelFor
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

## مولِّد المصدر: AwaitableBridgeGenerator

يكتشف `AwaitableBridgeGenerator` ما إذا كانت `UnityEngine.Awaitable` و`UnityEngine.Awaitable<T>` موجودتَين في التجميع ويُصدر دوال الامتداد `AsValkarnTask()` عند وجودهما.

ملف الإخراج هو `ValkarnTask.AwaitableBridge.Generated.g.cs`. الكود المُولَّد يقع في `namespace UnaPartidaMas.Valkarn.Tasks.Bridge` تحت الفئة `AwaitableBridgeExtensions`.

الدوال المُولَّدة:

```csharp
// يُصدَر عند وجود UnityEngine.Awaitable:
public static async ValkarnTask AsValkarnTask(this Awaitable awaitable)
{
    await awaitable;
}

// يُصدَر عند وجود UnityEngine.Awaitable<T>:
public static async ValkarnTask<T> AsValkarnTask<T>(this Awaitable<T> awaitable)
{
    return await awaitable;
}
```

هذه دوال `async ValkarnTask`، لذا تمر عبر مُنشئ async المُجمَّع لـValkarn Tasks — على مجموعة دافئة تكون صفر تخصيص.

المولِّد محمي: إذا لم يكن `ValkarnTask` في التجميع، لا يُصدَر أي كود. هذا يمنع أخطاء CS0246 في التجميعات التي تشير إلى Unity لكن ليس إلى Valkarn Tasks.

---

## مثال عملي كامل: جسر الوظائف في نظام ECS

ما يلي مأخوذ من `Samples~/ECS/JobBridgeExample.cs`. يوضح النمط الكامل لجدولة وظيفة متوازية مُجمَّعة بـBurst من `ISystem`، وانتظارها دون حجب، وكتابة النتائج مجددًا.

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

        // استخرج جميع بيانات الكيانات بشكل متزامن داخل OnUpdate.
        // لا يمكن أن تحتوي الدوال غير المتزامنة على معاملات ref (CS1988)، لذا
        // يجب نسخ البيانات هنا وتمريرها بالقيمة إلى الدالة غير المتزامنة.
        var query       = SystemAPI.QueryBuilder().WithAll<HealthData>().Build();
        var entityCount = query.CalculateEntityCount();
        if (entityCount == 0) return;

        var entities    = query.ToEntityArray(Allocator.TempJob);
        var healthArray = query.ToComponentDataArray<HealthData>(Allocator.TempJob);
        var results     = new NativeArray<float>(entityCount, Allocator.TempJob);

        // الدالة غير المتزامنة تتولى ملكية NativeArrays وتتخلَّص منها.
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
            // المرحلة 1: جدولة وظيفة Burst.
            var job = new HealthProcessingJob
            {
                HealthInputs     = healthArray,
                ProcessedOutputs = results,
            };
            var handle = job.Schedule(entities.Length, batchSize: 64);

            // المرحلة 2: انتظر الاكتمال دون حجب الخيط الرئيسي.
            await handle.ToVlkTask(cancellationToken: ct);

            // المرحلة 3: طبِّق النتائج. نحن عدنا إلى الخيط الرئيسي.
            ct.ThrowIfCancellationRequested();

            for (int i = 0; i < entities.Length; i++)
            {
                // ربما دُمِّر الكيان أثناء تشغيل الوظيفة.
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
            // تخلَّص دائمًا من NativeArrays — يعمل عند النجاح والاستثناء والإلغاء.
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

## ملخص أنواع الجسور

| النوع | الغرض | التخصيص |
|---|---|---|
| `JobHandleExtensions.ToVlkTask()` | انتظار `JobHandle` واحد | صفر في المسار السريع؛ وعدة مُجمَّعة خلافًا لذلك |
| `JobHandleExtensions.WhenAll()` | انتظار عدة `JobHandle` بالتوازي | صفر في المسار السريع؛ وعدة مُجمَّعة + استعارة `ArrayPool` خلافًا لذلك |
| `TempNativeArrayScope<T>` | إدارة عمر RAII لـ`NativeArray` | لا شيء (بنية) |
| `AwaitableBridge.AsVlkTask()` | تحويل `Awaitable`/`Awaitable<T>` إلى VlkTask | لا شيء (مُنتظِر بنية) |
| `ScheduleAsync()` المُولَّد | انتظار وظيفة محدَّدة النوع مباشرةً | `JobPromise<TJob>` مُجمَّع |
