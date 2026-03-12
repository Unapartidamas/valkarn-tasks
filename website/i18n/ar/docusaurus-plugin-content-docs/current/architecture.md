---
sidebar_position: 5
title: الهندسة المعمارية
---

# الهندسة المعمارية

نظرة عامة تقنية على الداخليات الخاصة بـ Valkarn Tasks.

---

## الهيكل العام

```
┌─────────────────────────────────────────────────────────────────┐
│                    وقت الترجمة                                    │
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
│                    وقت التشغيل                                    │
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

### تخطيط التجميعات Assemblies

```
ValkarnTask.Runtime      — يُشحن مع اللعبة
ValkarnTask.SourceGen    — وقت الترجمة فقط (المولّد المصدري)
ValkarnTask.Analyzer     — وقت الترجمة فقط (التشخيصات + إصلاحات الكود)
ValkarnTask.Testing      — TestClock + أدوات الاختبار
```

---

## بنية ValkarnTask

```csharp
[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask
{
    internal readonly ValkarnTask.ISource source;
    internal readonly ulong token; // مُعبَّأ: 32 بت عليا = الجيل، 32 سفلى = فهرس الفتحة
}

[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
{
    internal readonly ValkarnTask.ISource<T> source;
    internal readonly T result;  // inline في المسار السريع المتزامن
    internal readonly ulong token;
}
```

**الثابت الأساسي:** `source == null` يعني اكتمال المهمة بشكل متزامن بدون خطأ — لا كائن heap مُستخدَم. `ValkarnTask.CompletedTask` هو `default(ValkarnTask)`؛ `ValkarnTask.FromResult(value)` يخزن النتيجة inline.

### الرمز المميز الجيلي

```csharp
// تعبئة
ulong token = ((ulong)generation << 32) | slotIndex;

// فك تعبئة
uint slotIndex  = (uint)(token & 0xFFFFFFFF);
uint generation = (uint)(token >> 32);
```

يُتحقق منه عند كل استدعاء ISource: `slots[slotIndex].generation == expectedGeneration`. مرجع قديم لفتحة pool مُعاد تدويرها يرمي فوراً `InvalidOperationException`. 4 مليارات جيل لكل فتحة — التصادم مستحيل عملياً. (يستخدم UniTask `short` — تصادم بعد ~18 دقيقة.)

---

## عقد ISource

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

أي كائن يُنفّذ `ISource` يمكنه دعم `ValkarnTask`. التنفيذات المدمجة:

| النوع | الغرض |
|------|-------|
| `AsyncValkarnRunner<TStateMachine>` | يدعم كل طريقة `async ValkarnTask` |
| `AsyncValkarnRunner<TStateMachine, T>` | يدعم كل طريقة `async ValkarnTask<T>` |
| `ValkarnTask.PooledPromise[<T>]` | إكمال يدوي، إرجاع تلقائي للـ pool |
| `ValkarnTask.Promise[<T>]` | إكمال يدوي، غير مُجمَّع (طويل الأمد) |
| `ExceptionSource` | يدعم `FromException` |
| `CanceledSource` | يدعم `FromCanceled` |
| `NeverSource` | Singleton — لا ينتقل أبداً من Pending |

---

## منشئ الطريقة غير المتزامنة

يُشغّل مترجم C# بروتوكول المنشئ لأنواع الإرجاع async المخصصة:

```
Create()
  └─ يُرجع struct منشئ (بدون تخصيص)

Start(ref stateMachine)
  └─ يُشغّل آلة الحالة بشكل متزامن
     ├─ تكتمل بدون تعليق → SetResult()، runner يبقى null
     │  └─ Task تُرجع default(ValkarnTask)  ← بدون تخصيص
     └─ تصطدم بـ await غير مكتمل → AwaitUnsafeOnCompleted()
           └─ تستعير AsyncValkarnRunner من pool
              تنسخ آلة الحالة إلى runner (بالقيمة، بدون boxing)
              تُسجّل الاستمرارية على awaitable
              └─ Task تلف runner كـ ISource  ← المسار غير المتزامن
```

المنشئ نفسه هو `struct` — لا تخصيص فقط للمنشئ. `runner` مخصص **بشكل كسول**: إذا اكتملت طريقة بشكل متزامن، لا استعارة pool تحدث.

---

## Runner آلة الحالة والـ Pool

`AsyncValkarnRunner<TStateMachine>` يحتفظ بآلة الحالة المُولَّدة من المترجم **بالقيمة** (بدون boxing) ويعمل كـ `ISource`. يُستعار من `ValkarnPool<T>` عند التعليق الأول ويُرجَع عند `GetResult`.

نظراً لأن `TStateMachine` هو نوع فريد لكل طريقة async (لكل إنشاء generic مغلق)، تحصل كل طريقة async على pool خاص بها تلقائياً عبر التخصيص الجنيري لـ C#.

### ValkarnPool\<T\>

| السياق | الهيكل | السبب |
|--------|--------|-------|
| الخيط الرئيسي لـ Unity | مكدس أحادي الخيط | بدون مزامنة — أسرع ما يمكن |
| الخيوط الخلفية | Treiber lock-free stack | عمليات CAS، بدون أقفال |

يُكتشف سياق الخيط عبر `Thread.CurrentThread.IsBackground`. شكل Pool (السعة، معدل القص) مهيَّأ عبر `ValkarnTaskSettings`.

---

## ValkarnCompletionCore\<T\>

الحالة المشتركة داخل كل تنفيذ `ISource`:

- `Status` الحالي (Pending / Succeeded / Faulted / Canceled)
- قيمة النتيجة (المصادر الجنيرية)
- الاستثناء أو `OperationCanceledException` (مسارات الخطأ)
- delegate الاستمرارية المُسجَّل + الحالة

تستخدم انتقالات الحالة `Interlocked.CompareExchange` — خالية من القفل، آمنة للخيوط. **حارس الإكمال المزدوج** يضمن أن أول استدعاء `TrySet*` فقط ينجح؛ الاستدعاءات اللاحقة تُهمَل بصمت.

---

## تكامل PlayerLoop

يُدرج `PlayerLoopHelper` callbacks خفيفة لـ runner في PlayerLoop الخاص بـ Unity عند بدء التشغيل (`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]`).

كل قيمة `PlayerLoopTiming` تتوافق مع مرحلة. عند استدعاء `await ValkarnTask.Yield(timing)`، تُوضَع الاستمرارية في قائمة انتظار runner تلك المرحلة وتُرسَل في المرة التالية التي يصل فيها Unity إليها.

```
Initialization  →  EarlyUpdate  →  FixedUpdate  →  PreUpdate
→  Update  →  PreLateUpdate  →  PostLateUpdate  →  TimeUpdate
  (+ متغيرات Last* لكل مرحلة)
```

---

## المولّد المصدري

يعمل مولّد الكود المصدري Roslyn في وقت الترجمة. لكل كلاس `partial` يمتد `MonoBehaviour` مع طرق `async ValkarnTask`، يُولّد ملف كلاس جزئي:

1. يُعلن حقل `_valkarnCancelToken`
2. يعيّنه من `destroyCancellationToken` في `Awake`
3. يلف كل طريقة async لتمرير الرمز المميز تلقائياً

الملف المُولَّد لا يظهر أبداً في مُنقّح الأخطاء ولا يُعدّل كود المستخدم المصدري.

يُنتج المولّد أيضاً:

- **محوّلات bridge لـ Awaitable** — عند انتظار `Awaitable` داخل `async ValkarnTask`
- **غلافات async لـ Job** — عند اكتشاف أنواع `IJob` / `IJobParallelFor`
- **مجمعات المدمجات** — مصادر `WhenAll`/`WhenAny` المحددة النوع لتوبلات ثنائية إلى ثمانية

---

## محللات Roslyn

17 قاعدة `DiagnosticAnalyzer` مُضمَّنة في `Analyzers/netstandard2.0/`. تعمل أثناء مرحلة مترجم C# في Unity Editor وعلى CI:

- جميعها تستخدم `SemanticModel` لتحليل النوع (وليس مطابقة النصوص)
- الأداة المساعدة المشتركة `ValkarnTypeHelper` تكتشف أي متغير من `ValkarnTask`
- محلّل حلقات الزومبي يتخطى بشكل صحيح الدوال المحلية المتداخلة والـ lambdas
- محللات الترحيل (MIG001–MIG015) تنشط تلقائياً فقط عند الإشارة إلى UniTask / Awaitable — خاملة في غير ذلك

---

## طبقة Burst وECS

ثلاثة وحدات اختيارية، كل منها محمي بفحوصات define `#if`:

| الوحدة | تتطلب | الغرض |
|--------|--------|-------|
| `JobBridge` | `Unity.Jobs` | تلف `JobHandle` كـ awaitable؛ تستطلع `handle.IsCompleted` لكل علامة PlayerLoop |
| `AsyncSystemBase` | `Unity.Entities` | كلاس أساس لنظام ECS مع دعم async |
| `BurstScheduler` | `Unity.Burst` + `Unity.Collections` | يجدول مهام Burst من سياق async؛ يدير `NativeTimerHeap` |

`NativeTimerHeap` هو min-heap متوافق مع Burst لمؤقتات عالية الدقة يتجنب تخصيص الكومة المُدارة كلياً.

---

## تكامل المحرر

**Valkarn Hub** (`Tools → Valkarn → Hub`) يستخدم `TypeCache.GetTypesDerivedFrom<IValkarnEditorPanel>()` لاكتشاف جميع حزم Valkarn المثبتة تلقائياً. لا تسجيل يدوي مطلوب.

`TasksTrackerPanel` يشترك في `EditorApplication.update` لتحديث تشخيصات pool كل 0.5 ثانية (قابل للتهيئة) ويُظهر مرجع أصل `ValkarnTaskSettings` للوصول السريع.

---

## اعتبارات IL2CPP

- آلات الحالة مُخزَّنة **بالقيمة** داخل الـ runners — بدون boxing، يتعامل معها IL2CPP بشكل صحيح
- runner كل طريقة async هو تخصيص generic مستقل — محدد النوع، بدون تلوث متبادل
- بنيات Awaiter تُنفّذ `ICriticalNotifyCompletion` — يستدعي المترجم `UnsafeOnCompleted`، متخطياً التقاط `ExecutionContext` (بدون عبء في تهيئة Unity الافتراضية)
- إذا كان الـ stripping الجائر مُفعَّلاً، احفظ تجميع وقت التشغيل:

```xml
<!-- link.xml -->
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```
