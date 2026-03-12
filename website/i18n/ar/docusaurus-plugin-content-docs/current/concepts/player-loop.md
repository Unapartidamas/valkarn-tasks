---
sidebar_position: 1
title: تكامل Unity PlayerLoop
---

# تكامل Unity PlayerLoop

لا يستخدم ValkarnTasks خيوطًا أو مجدولات خلفية لاستئناف طرقك `async`. كل شيء يعمل على الخيط الرئيسي لـ Unity، مُدارًا بنظام PlayerLoop الخاص بـ Unity. فهم كيفية عمل هذا سيساعدك على اختيار التوقيت المناسب لحالتك الاستخدامية والاستنتاج بدقة متى يستأنف كودك بعد `await`.

## ما هو Unity PlayerLoop

PlayerLoop في Unity هو حلقة المحرك الداخلية التي تُحرّك كل إطار. إنها ليست استدعاء `Update()` واحدًا — بل هي تسلسل هرمي من المراحل التي تعمل بترتيب محدد في كل إطار:

```
Initialization
EarlyUpdate
FixedUpdate      (مكررة إذا كانت الفيزياء تتقدم)
PreUpdate
Update
PreLateUpdate
PostLateUpdate
TimeUpdate
```

كل واحدة من هذه المراحل العليا تحتوي على أنظمة فرعية تُدرجها Unity (وحزم الطرف الثالث) لتشغيل منطقها في نقاط محددة. مثلًا، `MonoBehaviour.Update()` تعمل داخل مرحلة `Update`. `MonoBehaviour.LateUpdate()` تعمل داخل `PreLateUpdate`.

نظرًا لأن ValkarnTasks تتصل مباشرةً بهذه الحلقة، فإن `await ValkarnTask.Yield()` لا يُعطّل خيطًا — بل يُسجّل استدعاءً يستدعيه Unity عند التكتة التالية للمرحلة المختارة، ثم يعود فورًا.

## التوقيتات الـ 16 لـ PlayerLoop

تُحقن ValkarnTasks في 16 نقطة: واحدة في **البداية** وواحدة في **النهاية** لكل من مراحل PlayerLoop الـ 8 في Unity. نوعيات `Last` تُلحق في نهاية قائمة الأنظمة الفرعية للمرحلة الأصلية؛ الأنواع العادية تُدرج في البداية.

| القيمة | عدد صحيح الـ enum | المرحلة الأصلية | الموضع في الأصل |
|---|---|---|---|
| `Initialization` | 0 | `UnityEngine.PlayerLoop.Initialization` | الأول |
| `LastInitialization` | 1 | `UnityEngine.PlayerLoop.Initialization` | الأخير |
| `EarlyUpdate` | 2 | `UnityEngine.PlayerLoop.EarlyUpdate` | الأول |
| `LastEarlyUpdate` | 3 | `UnityEngine.PlayerLoop.EarlyUpdate` | الأخير |
| `FixedUpdate` | 4 | `UnityEngine.PlayerLoop.FixedUpdate` | الأول |
| `LastFixedUpdate` | 5 | `UnityEngine.PlayerLoop.FixedUpdate` | الأخير |
| `PreUpdate` | 6 | `UnityEngine.PlayerLoop.PreUpdate` | الأول |
| `LastPreUpdate` | 7 | `UnityEngine.PlayerLoop.PreUpdate` | الأخير |
| `Update` | 8 | `UnityEngine.PlayerLoop.Update` | الأول |
| `LastUpdate` | 9 | `UnityEngine.PlayerLoop.Update` | الأخير |
| `PreLateUpdate` | 10 | `UnityEngine.PlayerLoop.PreLateUpdate` | الأول |
| `LastPreLateUpdate` | 11 | `UnityEngine.PlayerLoop.PreLateUpdate` | الأخير |
| `PostLateUpdate` | 12 | `UnityEngine.PlayerLoop.PostLateUpdate` | الأول |
| `LastPostLateUpdate` | 13 | `UnityEngine.PlayerLoop.PostLateUpdate` | الأخير |
| `TimeUpdate` | 14 | `UnityEngine.PlayerLoop.TimeUpdate` | الأول |
| `LastTimeUpdate` | 15 | `UnityEngine.PlayerLoop.TimeUpdate` | الأخير |

`Update` (القيمة 8) هو **الافتراضي** لجميع عمليات ValkarnTasks — `Yield()` و`Delay()` و`WaitUntil()` و`WaitWhile()` و`NextFrame()` و`DelayFrame()`.

## بنى العلامات والحقن الفائق الأثر

للحقن في PlayerLoop لـ Unity، تنشئ ValkarnTasks نظام `PlayerLoopSystem` واحد لكل توقيت. كل نظام يُعرَّف ببنية علامة فريدة مُعرَّفة داخل `PlayerLoopHelper`:

```csharp
struct ValkarnTaskInitialization     { }
struct ValkarnTaskLastInitialization { }
struct ValkarnTaskEarlyUpdate        { }
struct ValkarnTaskLastEarlyUpdate    { }
struct ValkarnTaskFixedUpdate        { }
struct ValkarnTaskLastFixedUpdate    { }
struct ValkarnTaskPreUpdate          { }
struct ValkarnTaskLastPreUpdate      { }
struct ValkarnTaskUpdate             { }
struct ValkarnTaskLastUpdate         { }
struct ValkarnTaskPreLateUpdate      { }
struct ValkarnTaskLastPreLateUpdate  { }
struct ValkarnTaskPostLateUpdate     { }
struct ValkarnTaskLastPostLateUpdate { }
struct ValkarnTaskTimeUpdate         { }
struct ValkarnTaskLastTimeUpdate     { }
```

قبل الحقن، يفحص `PlayerLoopHelper` ما إذا كان `ValkarnTaskUpdate` يظهر بالفعل في أي مكان في شجرة PlayerLoop الحالية. إذا ظهر، يُتخطى الحقن. هذا يجعل الحقن **فائق الأثر** — استدعاء `Init()` عدة مرات (قد يحدث في المحرر) لا يؤدي أبدًا إلى تسجيل أنظمة مكررة.

يقرأ الحقن دائمًا PlayerLoop **الحالي** بـ`PlayerLoop.GetCurrentPlayerLoop()`، وليس `GetDefaultPlayerLoop()`. هذا يعني حفظ أي أنظمة مثبَّتة مسبقًا بواسطة حزم أخرى.

## ContinuationQueue — استدعاءات أحادية الاستخدام

عندما تكتب `await ValkarnTask.Yield()` (أو أي شيء يتوقف لتكتة واحدة بالضبط)، تستدعي آلة الحالة المُولَّدة من المُترجم `OnCompleted` على المُنتظِر. يستدعي المُنتظِر `PlayerLoopHelper.AddContinuation(timing, action, state)`، مما يُضيف الاستدعاء في طابور `ContinuationQueue` لذلك التوقيت.

يستخدم `ContinuationQueue` تصميم **المخزن المؤقت المزدوج**:

1. **المخزن المؤقت النشط** (`actionList`): يحمل الاستمرارات المُضافة قبل وأثناء التصريف الحالي.
2. **مخزن الانتظار** (`waitingList`): يلتقط الاستمرارات المُضافة _أثناء_ تصريف المخزن النشط (أي الإضافات المتداخلة من داخل استمرار).
3. **مكدس Treiber عبر الخيوط** (`crossThreadHead`): الاستمرارات المُنشرة من خيوط الخلفية تهبط هنا عبر مقارنة-وتبادل خالية من الأقفال. في بداية كل تصريف، يُطالَب بالمكدس بالكامل ذريًا ويُنقل إلى المخزن النشط.

في كل تكتة PlayerLoop، تُنفّذ `ContinuationQueue.Run()` هذا التسلسل:

```
1. تصريف crossThreadHead → actionList     (أخذ ذري خالٍ من الأقفال، ثم نسخ تحت SpinLock)
2. لقطة العدد، ضبط isDraining = true     (تحت SpinLock)
3. تنفيذ جميع الاستدعاءات               (خارج القفل، مراجع ممسوحة لـ GC)
4. تبادل actionList ↔ waitingList        (تحت SpinLock، ضبط isDraining = false)
```

بعد إحماء يدوم إطارًا أو إطارين تقريبًا، تستقر المصفوفات الداخلية عند علامة أعلى مستوى لها وتعمل الطابور بـ **صفر تخصيصات**.

العقد عبر الخيوط مُجمَّعة في مجموعة موارد محدودة خالية من الأقفال (بحد أقصى 1024 عقدة) لتجنب التخصيص عند كل إضافة من خيط الخلفية.

## PlayerLoopRunner — العناصر المتكررة

بعض العمليات يجب فحصها في كل تكتة حتى تكتمل: `Delay()` و`DelayFrame()` و`WaitUntil()` و`WaitWhile()` و`NextFrame()`. هذه تُطبّق واجهة `IPlayerLoopItem`:

```csharp
internal interface IPlayerLoopItem
{
    // أرجع true للاستمرار في التشغيل؛ أرجع false للإزالة من المُشغّل.
    bool MoveNext();
}
```

تُضاف العناصر إلى `PlayerLoopRunner` عبر `PlayerLoopHelper.AddAction(timing, item)`. في كل تكتة، يُكرر `PlayerLoopRunner.Run()` على جميع العناصر المسجلة ويستدعي `MoveNext()` على كل منها. العناصر التي تُرجع `false` تُزال عبر **ضغط في المكان** — المصفوفة تُضغط في مرور واحد، مع حفظ ترتيب الإدراج. العناصر المُضافة أثناء استدعاء `Run()` تُلحق بأمان وستُعالج في التكتة التالية.

```
التكتة N:
  ContinuationQueue.Run()   → استئناف جميع المنتظِرين أحاديي الاستخدام
  PlayerLoopRunner.Run()    → تكتيك جميع العناصر المتكررة (Delay، WaitUntil، ...)
```

يعمل كلاهما لكل واحد من التوقيتات الـ 16، بالترتيب الذي يستدعيها المحرك.

توقيت `Update` يتتبع أيضًا عدد الإطارات العالمي ويُطلق دوريًا تقليص مجموعة الكائنات (كل `ValkarnTask.TrimCheckInterval` إطار).

## التهيئة — RuntimeInitializeOnLoadMethod

تتهيأ ValkarnTasks في `RuntimeInitializeLoadType.SubsystemRegistration`، أبكر خطاف يوفره Unity، الذي يُطلق قبل `Awake()` على أي كائن في المشهد:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void Init()
{
    // 1. التقاط معرف الخيط الرئيسي لفحوصات أمان الخيوط
    // 2. إنشاء ContinuationQueue وPlayerLoopRunner لجميع التوقيتات الـ 16
    // 3. الحقن في PlayerLoop (فائق الأثر)
    // 4. تسجيل استدعاء تغيير حالة وضع التشغيل (المحرر فقط)
}
```

يُلتقط معرف الخيط الرئيسي في هذه النقطة ويُشارَك مع جميع أنظمة مجموعة الموارد والطابور حتى تتمكن من التمييز بين إضافات الخيط الرئيسي (مسار SpinLock) وإضافات الخيوط الأخرى (مسار مكدس Treiber).

## معالجة إعادة تحميل النطاق (المحرر)

في محرر Unity، يؤدي الدخول إلى وضع التشغيل أو الخروج منه إلى إعادة تحميل النطاق. الحالة الثابتة من جلسة التشغيل السابقة ستبقى وتسبب مراجع قديمة.

تتعامل ValkarnTasks مع هذا باستدعاء `EditorApplication.playModeStateChanged` مسجَّل أثناء `Init()`. عندما تنتقل الحالة إلى `ExitingPlayMode` أو `EnteredEditMode`، يتم تنفيذ التنظيف التالي:

```csharp
// إعادة تعيين جميع الطوابير والمُشغّلات
for (int i = 0; i < 16; i++)
{
    s_continuationQueues[i] = null;
    s_playerLoopRunners[i] = null;
}

// إعادة تعيين الأنظمة الفرعية الأخرى
ValkarnTask.ResetStatics();
TimeProvider.ResetToDefault();   // يعود إلى UnityTimeProvider
PoolRegistry.Clear();
ContinuationQueue.ContinuationNode.ResetPool();
```

عند الدخول إلى وضع التشغيل مجددًا، تُطلق `Init()` عبر `RuntimeInitializeOnLoadMethod` وتُخصص طوابير ومُشغّلات جديدة. فحص حقن PlayerLoop (`HasValkarnTaskSystems`) يمنع إدراج أنظمة العلامات مرة ثانية إذا لم يُفرَّغ النطاق بالكامل.

## اختيار التوقيت المناسب

معظم الكود يجب استخدام توقيت `Update` الافتراضي. الوصول للآخرين فقط عند وجود سبب محدد.

| التوقيت | متى تستخدمه |
|---|---|
| `Initialization` / `LastInitialization` | إعداد مبكر جدًا للإطار؛ نادرًا مطلوب في كود اللعبة |
| `EarlyUpdate` / `LastEarlyUpdate` | أخذ عيّنات المدخلات؛ يعمل قبل الفيزياء وقبل `Update` |
| `FixedUpdate` / `LastFixedUpdate` | منطق متزامن مع الفيزياء؛ يطابق إيقاع `MonoBehaviour.FixedUpdate` |
| `PreUpdate` / `LastPreUpdate` | قبل إرسال التحديث الرئيسي لـ Unity؛ مفيد للتمرير المسبق للمجدول المخصص |
| **`Update`** (الافتراضي) | **منطق اللعبة القياسي؛ يطابق `MonoBehaviour.Update`** |
| `LastUpdate` | المنطق الذي يجب تشغيله بعد جميع استدعاءات `MonoBehaviour.Update` |
| `PreLateUpdate` / `LastPreLateUpdate` | يطابق `MonoBehaviour.LateUpdate`؛ متابعة الكاميرا والتحويل |
| `PostLateUpdate` / `LastPostLateUpdate` | بعد تقديم الرسم؛ نهاية واجهة المستخدم، التقاط لقطات الشاشة |
| `TimeUpdate` / `LastTimeUpdate` | مزامنة قيم الوقت؛ نادرًا مطلوب |

```csharp
// الاستئناف في تكتة Update التالية (الافتراضي)
await ValkarnTask.Yield();

// الاستئناف في بداية FixedUpdate التالي (آمن للفيزياء)
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// انتظار ثانيتين، مع تقدم بالوقت غير المقيّس، محسوب في LateUpdate
await ValkarnTask.Delay(
    TimeSpan.FromSeconds(2),
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.PreLateUpdate);

// انتظار حتى يصبح الشرط صحيحًا، محسوب بعد جميع استدعاءات LateUpdate
await ValkarnTask.WaitUntil(() => _ready, PlayerLoopTiming.LastPreLateUpdate);
```
