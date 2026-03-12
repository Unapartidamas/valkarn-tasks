---
sidebar_position: 1
title: تكامل Burst وECS
---

# تكامل Burst وECS

تتضمن Valkarn Tasks تكاملاً اختياريًا مع مُجمِّع Burst في Unity وUnity Collections وحزمة Entities (ECS). جميع هذه الوظائف تُجمَّع بشكل مشروط — لا تكون نشطة إلا عند وجود الحزم المطلوبة وضبط رموز تعريف البرمجة المقابلة.

## المتطلبات

| الميزة | الحزمة المطلوبة | رمز التعريف البرمجي |
|---|---|---|
| `NativeTimerHeap`، `NativeScheduler`، `BurstSchedulerRunner` | Unity Burst 1.8+، Unity Collections 2.0+ | `VTASKS_HAS_BURST` و`VTASKS_HAS_COLLECTIONS` |
| `AsyncSystemUtilities` | Unity Entities 1.0+ | `VTASKS_HAS_ENTITIES` |

جميع ملفات مصدر Burst/ECS مغلَّفة في حارسات `#if` تطابق هذه التعريفات. لا يُجمَّع أي شيء في هذه الملفات أو يُربط ما لم تكن التعريفات موجودة.

### الإعداد

1. ثبِّت الحزم المطلوبة عبر Unity Package Manager:
   - `com.unity.burst` 1.8 أو أحدث
   - `com.unity.collections` 2.0 أو أحدث
   - `com.unity.entities` 1.0 أو أحدث (لأدوات ECS فقط)

2. أضف رموز تعريف البرمجة إلى **Project Settings > Player > Scripting Define Symbols**:
   ```
   VTASKS_HAS_BURST;VTASKS_HAS_COLLECTIONS;VTASKS_HAS_ENTITIES
   ```
   تحتاج فقط إلى إضافة التعريفات الخاصة بالحزم التي ثبَّتّها.

---

## NativeTimerHeap

**مساحة الاسم:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**الحارس:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeTimerHeap` هو كومة min-heap ثنائية متوافقة مع Burst لجدولة المؤقتات. تخزّن قيم `TimerEntry` مرتَّبة حسب الموعد النهائي، مما يمنح إدخالاً بتعقيد O(log n) وإزالة O(log n) لكل مؤقت منتهٍ.

### الأنواع الرئيسية

```csharp
// الإدخال المخزَّن في الكومة. مرتَّب حسب Deadline.
public struct TimerEntry : IComparable<TimerEntry>
{
    public long Deadline;
    public int  Id;
}

public struct NativeTimerHeap : IDisposable
{
    public bool IsCreated { get; }
    public int  Count     { get; }

    // ينشئ الكومة. استخدم Allocator.Persistent للكوم طويلة الأجل.
    public NativeTimerHeap(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // يُدرج مؤقتًا جديدًا. يُرجع معرّف المؤقت (يُستخدم لتحديد الاستدعاء).
    // يجب أن يستخدم الموعد النهائي والقيمة الممررة إلى DrainExpired نفس الوحدة
    // (يستخدم BurstSchedulerRunner قراد DateTime عبر Time.realtimeSinceStartupAsDouble).
    [BurstCompile]
    public int Schedule(long deadline);

    // يُزيل ويُلحق معرّفات جميع المؤقتات التي يكون Deadline الخاص بها <= currentTimestamp.
    // يُرجع عدد المؤقتات المُستنزَفة.
    [BurstCompile]
    public int DrainExpired(long currentTimestamp, NativeList<int> expiredIds);

    public void Dispose();
}
```

`NativeTimerHeap` هو بنية غير مُدارة. لا يمكنها تخزين المندوبات المُدارة — يتم مطابقة المعرّفات مع الاستدعاءات المُدارة في قاموس `BurstSchedulerRunner` على الخيط الرئيسي.

---

## NativeScheduler

**مساحة الاسم:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**الحارس:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeScheduler` هو قائمة انتظار عمل متوافقة مع Burst مدعومة بـ`NativeQueue<ScheduledWork>`. تُضاف عناصر العمل من الوظائف المُجمَّعة بـBurst؛ يستنزف الخيط الرئيسي قائمة الانتظار في كل إطار.

### الأنواع الرئيسية

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

    // يُضيف عنصر عمل إلى قائمة الانتظار من وظيفة مُجمَّعة بـBurst.
    [BurstCompile]
    public void Enqueue(ScheduledWork work);

    // يستنزف جميع العمل المعلَّق في `results`. استدعِ من الخيط الرئيسي فقط.
    // يُرجع عدد العناصر المُستنزَفة.
    public int Drain(NativeList<ScheduledWork> results);

    // يُرجع كاتبًا متوازيًا مناسبًا للاستخدام في وظائف IJobParallelFor.
    public NativeQueue<ScheduledWork>.ParallelWriter AsParallelWriter();

    public void Dispose();
}
```

قائمة الانتظار هي نقطة العبور بين عالم Burst وعالم المُدار. `Enqueue` قابلة للاستدعاء من Burst؛ `Drain` للخيط الرئيسي فقط.

---

## BurstSchedulerRunner

**مساحة الاسم:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**الحارس:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`BurstSchedulerRunner` هو الجسر المُدار بين المُجدوِل الأصلي/كومة المؤقتات وبقية لعبتك. ينفِّذ `IPlayerLoopItem`، لذا تستدعي Unity الدالة `MoveNext()` مرة واحدة في الإطار عند التوقيت المسجَّل. في كل إطار:

1. يستنزف `NativeScheduler` ويُطلق أي استدعاءات مُدارة مسجَّلة مطابَقة بمعرّف العمل.
2. يستنزف `NativeTimerHeap` للمؤقتات المنتهية ويُطلق استدعاءاتها المُدارة.

الاستثناءات التي تُطرح من الاستدعاءات تُحال إلى `VlkTask.PublishUnobservedException` بدلاً من الانتشار عبر PlayerLoop.

### واجهة برمجة التطبيقات

```csharp
public sealed class BurstSchedulerRunner : IPlayerLoopItem, IDisposable
{
    // ينشئ مشغِّلاً ويسجِّله في PlayerLoop. يُرجع النسخة.
    // تخلَّص من المشغِّل المُرجَع عند عدم الحاجة إليه.
    public static BurstSchedulerRunner Create(
        PlayerLoopTiming timing       = PlayerLoopTiming.Update,
        int initialCapacity           = 64);

    // وصول مباشر إلى NativeScheduler لإضافة العمل من وظائف Burst.
    public NativeScheduler Scheduler { get; }

    // يجدول استدعاء مؤقت مُدار. يجب استدعاؤه من الخيط الرئيسي.
    // يُرجع معرّف المؤقت (للتعريف فقط؛ لا توجد واجهة برمجة للإلغاء).
    public int ScheduleTimer(TimeSpan delay, Action callback);

    // يربط استدعاءً مُدارًا بمعرّف عمل أضافه وظيفة Burst.
    // يجب استدعاؤه من الخيط الرئيسي، قبل أو أثناء الإطار الذي تكتمل فيه الوظيفة.
    public void RegisterCallback(int workId, Action callback);

    // يتخلَّص من جميع الحاويات الأصلية ويلغي تسجيله من PlayerLoop.
    public void Dispose();
}
```

### كيف يختلف BurstSchedulerRunner عن المُجدوِل الافتراضي

يتكامل المُجدوِل الافتراضي لـValkarn Tasks مباشرةً مع آلات حالة `async/await` ويدير إرسال الاستمرارات عبر `PlayerLoopHelper`. يضيف `BurstSchedulerRunner` مسارًا منفصلاً مخصصًا للإشارة من الوظائف المُجمَّعة بـBurst:

| | المُجدوِل الافتراضي | BurstSchedulerRunner |
|---|---|---|
| نوع الاستمرار | `Action` مُدار عبر `ISource` | `Action` مُدار مسجَّل بالمعرّف |
| مصدر الإشارة | نمط C# awaiter | `NativeScheduler.Enqueue` غير مُدار |
| مصدر المؤقت | `VlkTask.Delay` (مُدار) | `NativeTimerHeap` (غير مُدار) |
| أمان الخيوط | استمرارات الخيط الرئيسي | Enqueue آمنة لـBurst؛ Drain للخيط الرئيسي فقط |

### نمط الاستخدام

```csharp
// 1. أنشئ المشغِّل مرة واحدة (مثلاً في MonoBehaviour للتشغيل أو ISystem.OnCreate).
var runner = BurstSchedulerRunner.Create(PlayerLoopTiming.Update, initialCapacity: 128);

// 2. جدول استدعاء مؤقت (الخيط الرئيسي).
runner.ScheduleTimer(TimeSpan.FromSeconds(2.0), () =>
{
    Debug.Log("مضت ثانيتان (مؤقت غير مُدار).");
});

// 3. من وظيفة Burst، أضف عنصر عمل إلى قائمة الانتظار.
//    NativeScheduler.ParallelWriter آمن للاستخدام من IJobParallelFor.
var writer = runner.Scheduler.AsParallelWriter();
// داخل Execute(int index):
writer.Enqueue(new ScheduledWork { Id = myWorkId, Type = WorkType.Custom });

// 4. سجِّل الاستدعاء المُدار على الخيط الرئيسي قبل اكتمال الوظيفة.
runner.RegisterCallback(myWorkId, () =>
{
    Debug.Log($"استُقبلت إشارة اكتمال الوظيفة للعمل {myWorkId}.");
});

// 5. تخلَّص من المشغِّل عند الانتهاء (مثلاً OnDestroy أو إعادة تحميل النطاق).
runner.Dispose();
```

---

## AsyncSystemUtilities

**مساحة الاسم:** `UnaPartidaMas.Valkarn.Tasks.ECS`
**الحارس:** `#if VTASKS_HAS_ENTITIES`

يوفر `AsyncSystemUtilities` مساعدَين بامتداد لكتابة أنظمة ECS غير متزامنة.

### GetWorldCancellationToken

```csharp
public static CancellationToken GetWorldCancellationToken(
    this World world,
    PlayerLoopTiming timing = PlayerLoopTiming.Update)
```

يُرجع `CancellationToken` يُلغى تلقائيًا عند تدمير `World` المعطى. داخليًا يبدأ `VlkTask` بنمط fire-and-forget يستدعي `VlkTask.Yield(timing)` في كل إطار ما دام `world.IsCreated` صحيحًا، ثم يُلغي `CancellationTokenSource` عند خروج الحلقة.

إذا كان العالم محطوطًا بالفعل عند استدعاء هذه الدالة، فإنها تُرجع رمزًا في الحالة الملغاة بالفعل.

مرِّر هذا الرمز إلى كل دالة غير متزامنة تُشغِّلها من نظام حتى يتوقف العمل الجاري تلقائيًا عند اختفاء العالم.

### SafeEntityExists

```csharp
public static bool SafeEntityExists(this EntityManager entityManager, Entity entity)
```

يستدعي `entityManager.Exists(entity)` ويُرجع `false` إذا أُلقي `ObjectDisposedException`. يمكن أن يحدث هذا عند الوصول إلى `EntityManager` بعد التخلُّص من العالم، وهو حالة تسابق حقيقية في الكود غير المتزامن الذي يبقى عبر حدود الإطارات.

استخدم هذه الدالة بعد كل نقطة `await` قبل الكتابة مجددًا إلى كيان.

---

## مثال عملي: نظام ECS غير متزامن

المثال التالي مأخوذ من `Samples~/ECS/AsyncLoadSystem.cs`. يوضح النمط القانوني للتهيئة غير المتزامنة لمرة واحدة من `ISystem`.

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
        // احصل على رمز إلغاء مرتبط بعمر هذا العالم.
        // إذا دُمِّر العالم، ستُلغى تلقائيًا جميع الأعمال غير المتزامنة المُشغَّلة بهذا الرمز.
        var worldCt = state.World.GetWorldCancellationToken();

        // شغِّل التهيئة غير المتزامنة وتجاهل المهمة.
        // Forget() يُحيل أي استثناء غير مُلاحَظ إلى VlkTask.PublishUnobservedException.
        InitializeAsync(state.WorldUnmanaged, worldCt).Forget();
    }

    public void OnUpdate(ref SystemState state) { }
    public void OnDestroy(ref SystemState state) { }

    static async VlkTask InitializeAsync(WorldUnmanaged world, CancellationToken ct)
    {
        // المرحلة 1: تحميل البيانات على خيط خلفي.
        // RunOnThreadPool ينتقل إلى خيط عامل، ينفِّذ المندوب،
        // ويعود إلى الخيط الرئيسي تلقائيًا.
        var configData = await VlkTask.RunOnThreadPool(
            () => LoadFromDisk(),
            cancellationToken: ct);

        // المرحلة 2: تطبيق النتائج على الخيط الرئيسي.
        // تحقق من الإلغاء في حالة تدمير العالم أثناء التحميل.
        ct.ThrowIfCancellationRequested();
        ApplyConfiguration(world, configData);
    }

    static ConfigData LoadFromDisk()
    {
        // C# خالص فقط — لا استدعاءات لـUnity أو ECS هنا.
        return new ConfigData { MaxEnemies = 100, SpawnRadius = 50f };
    }

    static void ApplyConfiguration(WorldUnmanaged world, ConfigData data)
    {
        // آمن: نحن على الخيط الرئيسي.
        UnityEngine.Debug.Log($"تم تحميل الإعداد: MaxEnemies={data.MaxEnemies}");
    }

    struct ConfigData
    {
        public int   MaxEnemies;
        public float SpawnRadius;
    }
}
#endif
```

مثال تقنين الذكاء الاصطناعي (`Samples~/ECS/AISystemExample.cs`) يبني على هذا النمط ويضيف `AsyncThrottle` للحد من عدد المهام غير المتزامنة المتزامنة. راجع توثيق التقنين للحصول على تفاصيل حول هذا النمط.

---

## القيود

تنطبق القيود التالية على جميع أكواد Burst/ECS غير المتزامنة. انتهاك هذه القيود ينتج أخطاء في المحرر، أو انتهاكات أمان الوظائف، أو تلفًا صامتًا للبيانات.

### داخل الكود المُجمَّع بـBurst

- **لا أنواع مُدارة.** لا يمكن لـBurst تجميع الكود الذي يخصِّص أو يصل إلى أو يشير إلى الكائنات المُدارة (الفئات، المندوبات، المصفوفات، النصوص، `List<T>`، إلخ). البنى الblittable والحاويات الأصلية فقط مسموح بها.
- **لا استثناءات.** لا يدعم Burst `try`/`catch`/`throw`. استخدم رموز الإرجاع أو الأعلام للتواصل بشأن الأخطاء.
- **لا `async`/`await`.** آلات حالة C# الغير متزامنة مُدارة ولا يمكن لـBurst تجميعها. يوفر `NativeScheduler` و`NativeTimerHeap` قناة جانبية للإشارة إلى الاستمرارات المُدارة، لكن الاستمرارات نفسها تعمل على الخيط الرئيسي.
- **لا حالة مُدارة ثابتة قابلة للتغيير.** يمكن لوظائف Burst قراءة الحقول الثابتة للقراءة فقط لكن يجب ألا تكتب على الستاتيكات المُدارة.

### عبر نقاط الانتظار في أنظمة ECS

- **عمر الكيان.** يمكن تدمير الكيانات أثناء تعليق دالة غير متزامنة. استدعِ دائمًا `entityManager.SafeEntityExists(entity)` بعد كل `await` قبل الكتابة مجددًا.
- **قِدَم ComponentLookup.** تصبح `ComponentLookup` و`RefRW` وأنواع مؤشرات القطع الأخرى غير صالحة بعد التغييرات الهيكلية التي يمكن أن تحدث في أي إطار. لا تخزِّن هذه عبر نقاط `await`. أعد الحصول عليها من `SystemState` بعد الاستئناف، أو استخدم `EntityManager` مباشرةً.
- **المعاملات `ref`.** لا يمكن أن تحتوي الدوال غير المتزامنة على معاملات `ref` أو `in` أو `out` (خطأ C# CS1988). استخرج جميع بيانات ECS بشكل متزامن في دالة `OnUpdate` المتزامنة ومرِّرها إلى الدالة غير المتزامنة بالقيمة.
- **`SystemAPI` في الدوال غير المتزامنة.** `SystemAPI` مُولَّد بالمصدر ويعمل فقط داخل دوال `ISystem` الجزئية. غير متاح في الدوال `async`. نفِّذ جميع استعلامات `SystemAPI` قبل أول `await`.
- **أمان الخيوط.** `EntityManager` و`ComponentLookup` والتغييرات الهيكلية للخيط الرئيسي فقط. استخدم `VlkTask.RunOnThreadPool` فقط للحسابات C# الخالصة بدون استدعاءات Unity أو ECS.
