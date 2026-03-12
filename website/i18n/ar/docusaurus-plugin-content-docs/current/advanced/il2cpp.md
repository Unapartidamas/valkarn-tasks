---
sidebar_position: 2
title: توافق IL2CPP
---

# توافق IL2CPP

صُمِّمت Valkarn Tasks للعمل بشكل صحيح تحت IL2CPP من الأساس. تصف هذه الصفحة كل إجراء تم اتخاذه وما تحتاج إلى فعله — أو عدم فعله — للشحن بأمان على منصات IL2CPP مثل iOS وWebGL وأجهزة الألعاب.

---

## لماذا يحتاج IL2CPP عناية خاصة

يحوِّل IL2CPP كود C# IL إلى كود مصدر C++ ثم يُجمِّعه بمُجمِّع أصلي. ميزتان من مسار التحويل ذواتا صلة بمكتبة async:

1. **تجريد الكود.** يُزيل مجرِّد الكود المُدار في Unity (باستخدام رابط IL2CPP) الأنواع والدوال والحقول التي لا يشير إليها مخطط استدعاء قابل للتحليل بشكل ثابت. يمكن تجريد الأنواع التي يُوصَل إليها فقط من خلال إرسال الواجهة أو المشاركة الجنيرية أو الانعكاس — والتي تشمل فئات الوعود المُجمَّعة وتنفيذات `ISource` — بشكل صامت.

2. **المشاركة الجنيرية.** لا يُولِّد IL2CPP نظيرًا ثنائيًا أصليًا منفصلاً لكل إنشاء جنيريكي. بدلاً من ذلك، يُشارك الكود عبر الأنواع المرجعية ويستخدم إنشاءات محددة للأنواع ذات القيمة. يمكن أن يُخفي هذا أخطاء في التطوير (Mono) تظهر فقط في بنيات IL2CPP.

---

## ملف link.xml

الدفاع الأساسي ضد التجريد هو ملف `link.xml`، الموجود في:

```
Runtime/link.xml
```

محتوياته:

```xml
<linker>
  <!-- الحفاظ على جميع الأنواع في تجميع بيئة تشغيل Valkarn.Tasks.
       يمكن لتجريد كود IL2CPP إزالة الأنواع الداخلية التي يُوصَل إليها فقط عبر
       إرسال الواجهة، أو المشاركة الجنيرية، أو الانعكاس (مثل فئات الوعود،
       المشغِّلات المُجمَّعة، تنفيذات ISource). -->
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks.Burst" preserve="all"/>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks.ECS" preserve="all"/>
</linker>
```

`preserve="all"` يوجِّه الرابط للاحتفاظ بكل نوع ودالة وحقل ومُنشئ في تلك التجميعات بغض النظر عما يجده التحليل الثابت. هذا هو الإعداد الأكثر أمانًا لمكتبة أنواعها الداخلية يُوصَل إليها من خلال معاملات جنيرية لا يستطيع المجرِّد تتبعها.

تكتشف Unity وتطبِّق ملفات `link.xml` تلقائيًا عند وضعها داخل مجلد حزمة مستورد في المشروع. لا يلزم اتخاذ أي خطوة يدوية.

إذا كنت تُفرِّع **الكود المصدري** أو **تُدمجه** بدلاً من استخدام الحزمة، انسخ `link.xml` إلى مجلد مجاور لـ`Resources` أو ضعه في أي مكان ستجده Unity وفقًا لـ[توثيق تجريد الكود المُدار في Unity](https://docs.unity3d.com/Manual/ManagedCodeStripping.html).

---

## الأنواع الداخلية التي ستُجرَّد بدون link.xml

الفئات التالية من الأنواع الداخلية هي المخاطر الرئيسية للتجريد:

### فئات الوعود المُجمَّعة (تنفيذات ISource)

كل مُدمِج ونوع تأخير ينشئ فئة وعود مُجمَّعة تنفِّذ `ValkarnTask.ISource` أو `ValkarnTask.ISource<T>`:

- `AsyncValkarnTaskRunner<TStateMachine>` — مشغِّل آلة الحالة المُجمَّعة، واحد لكل دالة async (متخصص على `TStateMachine`)
- `WhenAllPromise<T1, T2>`، `WhenAllArrayPromise<T>`، `WhenAllVoidPromise2`، `WhenAllVoidPromise3`، `WhenAllVoidArrayPromise`
- `DeltaTimeDelayPromise`، `UnscaledDeltaTimeDelayPromise`، `RealtimeDelayPromise`
- `CanceledSource`، `CanceledSource<T>`، `ExceptionSource`، `ExceptionSource<T>`، `NeverSource`
- مكوِّنات القناة الداخلية: `BoundedChannel<T>`، `UnboundedChannel<T>`، وأنواع القراءة/الكتابة الخاصة بها

هذه الأنواع تُنشأ عبر دوال مصانع جنيرية (`ValkarnTaskPool<T>.GetOrCreate`). يبدأ مخطط استدعاء التحليل الثابت من استدعاء دالة جنيريكية ولا يستطيع تتبع جميع إنشاءات `T` الملموسة بشكل موثوق، لذا بدون `link.xml` يمكن تجريد أي من هذه الأنواع.

### ValkarnTaskPool\<T\>

```csharp
internal sealed class ValkarnTaskPool<T> : IPoolInfo where T : class, IPoolNode<T>
```

المجموعة جنيرية على نوع عنصرها. كل فئة وعود لها حقل مجموعة ثابت خاص بها. إذا كان نوع وعود معين غير مستخدَم في مشهد، قد تُجرَّد المجموعة وفئة الوعود معًا.

### ValkarnTaskCompletionCore\<T\>

النواة الداخلية للاكتمال هي نوع قيمة مستخدَم داخل كل وعد. تحتفظ باستدعاء الاستمرار والرمز وحالة الاكتمال. لا تُشار إليها باسمها من خارج المكتبة.

---

## قيود النوع الجنيريكي وIL2CPP

مُنشئ الدالة غير المتزامنة المخصَّص جنيريكي على نوع آلة الحالة:

```csharp
public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
    ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : ICriticalNotifyCompletion
    where TStateMachine : IAsyncStateMachine
```

والمشغِّل جنيريكي على آلة الحالة:

```csharp
internal sealed class AsyncValkarnTaskRunner<TStateMachine>
    : IStateMachineRunnerPromise, IPoolNode<AsyncValkarnTaskRunner<TStateMachine>>
    where TStateMachine : IAsyncStateMachine
```

تحت IL2CPP، يُولَّد نوع أصلي منفصل لكل `TStateMachine` مميز (لأن آلات الحالة هي بنى، وتتطلب تخصصًا جنيريكيًا كاملاً). هذا يعني:

- كل دالة `async ValkarnTask` في مشروعك تُنتج نوع `AsyncValkarnTaskRunner<TYourStateMachine>` أصليًا منفصلاً.
- إذا أزال المجرِّد `AsyncValkarnTaskRunner<T>` قبل رؤية جميع الإنشاءات، قد تتعطل بعض الدوال غير المتزامنة في وقت التشغيل.
- `preserve="all"` في `link.xml` يمنع هذا.

---

## مستوى تجريد الكود المُدار

مستوى التجريد في Unity مضبوط في **Player Settings → Other Settings → Managed Stripping Level**.

| المستوى | الحالة مع Valkarn Tasks |
|---------|------------------------|
| معطَّل | آمن. لا يحدث تجريد. |
| منخفض | آمن. يُزيل فقط التجميعات الغير مستخدمة بوضوح. |
| متوسط | آمن **مع `link.xml`**. يحفظ `link.xml` المُضمَّن جميع أنواع بيئة التشغيل. |
| عالٍ | آمن **مع `link.xml`**. نفس التغطية؛ `link.xml` هو طبقة الحماية. |

جميع مستويات التجريد حتى **عالٍ** آمنة ما دام ملف `link.xml` موجودًا ومطبَّقًا. لا تُزيل أو تُعدِّل `link.xml` دون فهم الأنواع الداخلية التي تعرِّضها للمجرِّد.

---

## استخدام سمة [Preserve]

لا يعثر فحص مصدر Runtime على أي سمة `[UnityEngine.Scripting.Preserve]` مطبَّقة على أعضاء فردية. النهج المختار هو الحفاظ على مستوى التجميع عبر `link.xml` بدلاً من سمات لكل عضو. هذا مقصود:

- `[Preserve]` لكل عضو يتطلب تعليق كل فئة مُجمَّعة بشكل فردي، بما في ذلك جميع الإضافات المستقبلية.
- `preserve="all"` على مستوى التجميع في `link.xml` أبسط وأقل عرضة للخطأ ومضمون تغطية الأنواع المضافة في الإصدارات المستقبلية.

إذا احتجت إلى دمج Valkarn Tasks في ملف `link.xml` أكبر بدلاً من استخدام الملف المُضمَّن في الحزمة، التوجيه المكافئ هو:

```xml
<assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
```

---

## تصميم المجموعة المُحسَّن لـIL2CPP

تحتوي مجموعة الكائنات (`ValkarnTaskPool<T>`) على ملاحظة صريحة في تعليق توثيقها:

> IL2CPP-first: main thread operations use zero atomics.

تستخدم المجموعة مسارَي وصول:

- **المسار السريع (الخيط الرئيسي):** يُقرأ حقل `fastItem` الواحد ويُكتب بوصول حقل عادي — لا عمليات `Volatile` أو `Interlocked`. هذا يتجنب عبء العمليات الذرية على الخيط الرئيسي، حيث لا يستطيع IL2CPP دائمًا تحسينها.
- **مسار الفيض (أي خيط):** كومة Treiber خالية من القفل مع `Interlocked.CompareExchange` للصحة تحت الوصول المتزامن من الخيوط الخلفية (مثل `ValkarnTask.RunOnThreadPool`).

هوية الخيط الرئيسي مخزَّنة في حقل `volatile int` في `ValkarnTaskPoolShared.MainThreadId`، يُنشَر مرة واحدة عند بدء التشغيل عبر `RuntimeInitializeOnLoadMethod`. يتعامل IL2CPP بشكل صحيح مع الحقول `volatile`.

---

## اعتبارات منصة أجهزة الألعاب

تستخدم منصات أجهزة الألعاب (PlayStation، Xbox، Nintendo Switch) IL2CPP حصريًا. ينطبق التالي:

- تغطية `link.xml` هي نفسها كما هي للأهداف الأخرى لـIL2CPP. لا يلزم حفاظ إضافي خاص بالمنصة.
- `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` يعمل بشكل صحيح على جميع المنصات التي تدعمها Unity لشهادة أجهزة الألعاب.
- عمليات `Interlocked` و`Volatile` مدعومة على جميع أهداف IL2CPP لأجهزة الألعاب. كومة Treiber في المجموعة آمنة.
- المشاركة الجنيرية لإنشاءات `TStateMachine` ذات البنية تنطبق على أجهزة الألعاب. كل دالة `async ValkarnTask` تُولِّد نوعها الأصلي — هذا سلوك متوقَّع ويُعالَج بشكل صحيح.
- إذا فرضت منصة جهاز ألعاب متطلبات AOT إضافية، تأكد من أن `link.xml` الخاص بك مُكتشَف بشكل صحيح عبر التحقق من سجل البناء للبحث عن "Stripping assembly: UnaPartidaMas.Valkarn.Tasks". إذا جُرِّد التجميع، فإن `link.xml` لم يُكتشَف.

---

## التحقق من عدم تجريد أي شيء

بعد البناء لهدف IL2CPP:

1. **تحقق من سجل البناء.** تطبع Unity قرارات التجريد. ابحث عن `UnaPartidaMas.Valkarn.Tasks`. إذا رأيت رسائل `Stripping class` لأنواع Valkarn الداخلية، فإن `link.xml` لم يُطبَّق.

2. **شغِّل اختبار دخان.** اختبار بسيط يُمارس `ValkarnTask.Delay` و`WhenAll` ومخصص `ValkarnTaskCompletionSource` سيُنشئ أكثر الأنواع عرضة للتجريد. إذا نجت هذه الثلاثة سيناريوهات من البناء، فإن النواة سليمة.

3. **فعِّل "Strip Engine Code" بشكل انتقائي.** إذا اضطُررت لاستخدام التجريد العالي ولا تستطيع استخدام `link.xml` المُضمَّن، فعِّل التجريد بشكل تدريجي وشغِّل الاختبارات بعد كل زيادة في مستوى التجريد.

4. **بناء IL2CPP مع تمكين Development Build.** تتضمن بنيات التطوير تشخيصات إضافية. إذا كان أحد الأنواع مفقودًا، سيُبلِّغ وقت التشغيل عن `TypeInitializationException` أو مرجع null في موقع استدعاء النوع المفقود. طابق ترتيب الاستدعاء مع الأنواع المدرجة في قسم "الأنواع الداخلية" أعلاه.

---

## قائمة تحقق الملخص

- `Runtime/link.xml` موجود في الحزمة — تحقق من عدم حذفه عن طريق الخطأ.
- يمكن ضبط مستوى التجريد المُدار على أي مستوى؛ العالي مدعوم.
- لا يلزم إضافة سمات `[Preserve]` إلى كود التطبيق.
- لا يلزم سمات تلميح AOT أو استدعاءات تسجيل الكود.
- تستخدم بنيات أجهزة الألعاب نفس `link.xml`؛ لا يلزم أي إضافات خاصة بالمنصة.
- إذا قمت بتفريع الكود المصدري، احتفظ بـ`link.xml` معه.
