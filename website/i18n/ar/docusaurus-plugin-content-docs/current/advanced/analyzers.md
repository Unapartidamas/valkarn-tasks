---
sidebar_position: 1
title: قواعد المحلِّل
---

# قواعد المحلِّل

تشحن Valkarn Tasks حزمتَي محلِّل Roslyn اللتين تعملان تلقائيًا عند استيراد الحزمة:

- **`UnaPartidaMas.Valkarn.Tasks.SourceGen.dll`** — قواعد خاصة بصحة ValkarnTask ودورة حياة Unity. معرِّفات القواعد تبدأ بـ`TT`.
- **`UnaPartidaMas.Valkarn.Tasks.Analyzer.dll`** — قواعد الترحيل لقواعد الأكواد التي تنتقل من UniTask. معرِّفات القواعد تبدأ بـ`MIG`. هذه القواعد تُطلق **فقط عند وجود `Cysharp.Threading.Tasks.UniTask`** في التجميع، لذا فهي صامتة في المشاريع الجديدة.

كلتا الحزمتَين ملفات DLL مُجمَّعة مسبقًا تقع في مجلد `Analyzers/` في الحزمة. تحمِّلها Unity كمحلِّلات Roslyn عبر مرجع `.asmdef`؛ يحمِّلهما مشروع `_TestRunner~` عبر عناصر `<Analyzer>` في `TestRunner.csproj`.

---

## قواعد الصحة (TT)

هذه القواعد تكتشف الأخطاء المتعلقة بطبيعة الاستهلاك الأحادي لـ`ValkarnTask` وسوء استخدام fire-and-forget.

---

### TT001 — ValkarnTask جرى انتظاره بالفعل

| الخاصية | القيمة |
|--------|--------|
| الخطورة | تحذير |
| الفئة | ValkarnTask |
| إصلاح كود | لا |

`ValkarnTask` أحادي الاستهلاك: بمجرد الانتظار، يصبح الرمز الداخلي قديمًا. `await` ثانٍ على نفس المتغير سيواجه هذا الرمز القديم ويُطرح. يكتشف المحلِّل متى يُنتظَر نفس المتغير المحلي أو المعامل أو الحقل من النوع `ValkarnTask`/`ValkarnTask<T>` أكثر من مرة داخل نفس الدالة.

**يُطلق على:**
```csharp
async ValkarnTask Bad()
{
    ValkarnTask work = DoWorkAsync();
    await work;   // الانتظار الأول — صحيح
    await work;   // TT001: جرى انتظاره بالفعل
}
```

**الإصلاح:** إذا احتجت للتفرع بناءً على النتيجة، التقطها بـ`.AsResult()` قبل الانتظار الأول، أو أعد الهيكلة بحيث يُنتظَر Task مرة واحدة بالضبط.
```csharp
async ValkarnTask Good()
{
    var result = await DoWorkAsync().AsResult();
    // استخدم result في كلا الفرعين
}
```

---

### TT002 — ValkarnTask لم يُنتظَر ولم يُتجاهَل

| الخاصية | القيمة |
|--------|--------|
| الخطورة | **خطأ** |
| الفئة | ValkarnTask |
| إصلاح كود | لا |

استدعاء دالة تُرجع `ValkarnTask` مستخدَم كعبارة تعبير — لم يُنتظَر، ولم يُخصَّص، ولم يُتبَع بـ`.Forget()` — هو خطأ صامت. الاستثناءات المُطرحة داخل Task لا تُلاحَظ أبدًا، وآلة الحالة المُجمَّعة في التاسك لا تُعاد أبدًا إلى المجموعة.

يتحقق المحلِّل من عبارات التعبير حيث يُحلِّل نوع التعبير إلى `ValkarnTask` أو `ValkarnTask<T>`. يتخطى:
- تعبيرات الإسناد (`tasks[i] = DoWork()`)
- السلاسل المنتهية بـ`.Forget()` (fire-and-forget مقصود)

**يُطلق على:**
```csharp
void Bad()
{
    LoadDataAsync();      // TT002: لم يُنتظَر ولم يُتجاهَل
    ProcessItemAsync();   // TT002
}
```

**الإصلاح — انتظره:**
```csharp
async ValkarnTask Good()
{
    await LoadDataAsync();
    await ProcessItemAsync();
}
```

**الإصلاح — fire-and-forget صريح:**
```csharp
void GoodFireAndForget()
{
    LoadDataAsync().Forget();
}
```

---

### TT013 — ValkarnTask أُرجع لكن لم يُستهلَك

| الخاصية | القيمة |
|--------|--------|
| الخطورة | تحذير |
| الفئة | ValkarnTask |
| إصلاح كود | لا |

معرّف هذه القاعدة **محجوز لتحليل تدفق البيانات المستقبلي**. يُقصد بها اكتشاف نمط الإسناد دون الانتظار — تخزين `ValkarnTask` في متغير ثم عدم انتظاره أو تجاهله أبدًا — وهو ما لا تغطيه TT002 لأن TT002 تفحص فقط عبارات التعبير المجردة.

التنفيذ الحالي لا يسجِّل أي إجراءات تركيبية. عند تنفيذ تحليل تدفق البيانات، ستُكمِّل TT013 TT002 بتغطية:
```csharp
async ValkarnTask Bad()
{
    ValkarnTask task = DoWorkAsync();  // مُخصَّص لكن لم يُنتظَر أبدًا
    DoOtherThing();
    // task متروكة — TT013 (مستقبلي)
}
```

الدوال المزيَّنة بـ`[FireAndForget]` ستكون معفاة.

---

## قواعد دورة الحياة (TT)

هذه القواعد تتعلق بإدارة عمر MonoBehaviour والإلغاء.

---

### TT010 — الإلغاء التلقائي نشط لدالة async في MonoBehaviour

| الخاصية | القيمة |
|--------|--------|
| الخطورة | معلومة |
| الفئة | ValkarnTask |
| إصلاح كود | لا |

تلميح إعلامي. أي دالة `async ValkarnTask` (أو `async ValkarnTask<T>`) داخل فئة ترث من `UnityEngine.MonoBehaviour` ستُلغى تلقائيًا عند تدمير الكائن، **ما لم** تُزيَّن الدالة بـ`[NoAutoCancel]`. يجعل هذا التشخيص هذا السلوك مرئيًا في بيئة التطوير المتكاملة دون الحاجة لتذكره.

**يُطلق على:**
```csharp
public class MyBehaviour : MonoBehaviour
{
    async ValkarnTask LoadLevel()  // TT010: الإلغاء التلقائي نشط
    {
        await ValkarnTask.Delay(2000);
    }
}
```

**للإلغاء الاشتراك** (وقمع TT010 لتلك الدالة)، أضف `[NoAutoCancel]`:
```csharp
[NoAutoCancel]
async ValkarnTask LoadLevel(CancellationToken ct)
{
    await ValkarnTask.Delay(2000, ct);
}
```

---

### TT011 — WhenAll يخلط نطاقات عمر مختلفة

| الخاصية | القيمة |
|--------|--------|
| الخطورة | تحذير |
| الفئة | ValkarnTask |
| إصلاح كود | لا |

استدعاء `ValkarnTask.WhenAll` بمهام من نطاقات عمر مختلفة يمكن أن يُنتج سلوك إلغاء جزئي مفاجئًا. إذا كانت إحدى المهام مرتبطة بـ`MonoBehaviour` (مُرجَعة من دالة نسخة لفئة ترث `MonoBehaviour`) وأخرى غير مرتبطة (مهمة ثابتة، أو من فئة غير MonoBehaviour)، فإن تدمير الكائن سيُلغي إحدى المهمتين لكن ليس الأخرى، مما يترك المُدمِج في حالة غير محددة.

**يُطلق على:**
```csharp
// بافتراض أن EnemyAI : MonoBehaviour
await ValkarnTask.WhenAll(
    enemyAI.PatrolAsync(),   // مرتبطة: تُلغى تلقائيًا عند Destroy
    GlobalMusic.FadeAsync()  // غير مرتبطة: تعيش إلى أجل غير مسمى
);
// TT011: WhenAll يخلط أعماراً — PatrolAsync() مقابل GlobalMusic.FadeAsync()
```

**الإصلاح — أعطِ كلتا المهمتين رمز إلغاء مشتركًا:**
```csharp
using var cts = new CancellationTokenSource();
await ValkarnTask.WhenAll(
    enemyAI.PatrolAsync(cts.Token),
    GlobalMusic.FadeAsync(cts.Token)
);
```

---

### TT012 — حلقة async بدون تحقق من الإلغاء

| الخاصية | القيمة |
|--------|--------|
| الخطورة | تحذير |
| الفئة | ValkarnTask |
| إصلاح كود | لا |

دالة `async ValkarnTask` تحتوي على حلقة `for` أو `while` أو `foreach` أو `do`-`while` لا يحتوي جسمها على تحقق من الإلغاء هي "حلقة زومبي": إذا أُطلق إلغاء دورة الحياة (مثلاً تدمير MonoBehaviour)، لا يمكن لإشارة الإلغاء التلقائي كسر الحلقة. الحلقة تستمر في العمل رغم اختفاء الكائن المالك.

يعتبر المحلِّل أن جسم الحلقة يحتوي على تحقق من الإلغاء إذا كان يحتوي على أي مما يلي:
- تعبير `await` (العملية المنتظَرة يمكنها ملاحظة الرمز وإطراح `OperationCanceledException`)
- `ThrowIfCancellationRequested` (كمعرِّف أو وصول عضو)
- `IsCancellationRequested` (كمعرِّف أو وصول عضو)

التحقق **لا** ينزل إلى الدوال المجهولة أو الدوال المحلية المتداخلة.

**يُطلق على:**
```csharp
async ValkarnTask PollForever(CancellationToken ct)
{
    while (true)                     // TT012: لا تحقق من الإلغاء في الجسم
    {
        ProcessNextItem();
        Thread.Sleep(16);            // متزامن — ليس await
    }
}
```

**الإصلاح — أضف await أو تحققًا صريحًا:**
```csharp
async ValkarnTask PollForever(CancellationToken ct)
{
    while (true)
    {
        ct.ThrowIfCancellationRequested();
        ProcessNextItem();
        await ValkarnTask.Yield();       // يُحقق أيضًا الشرط
    }
}
```

---

### TT014 — [NoAutoCancel] بدون معامل CancellationToken

| الخاصية | القيمة |
|--------|--------|
| الخطورة | تحذير |
| الفئة | ValkarnTask |
| إصلاح كود | لا |

`[NoAutoCancel]` على دالة `async ValkarnTask` في `MonoBehaviour` يُلغي اشتراك الدالة في إلغاء دورة الحياة التلقائي. لكن إذا لم تحتوِ الدالة على معامل `CancellationToken`، فلا توجد لديها آلية لملاحظة الإلغاء على الإطلاق، مما يجعل السمة بلا معنى ويشير على الأرجح إلى معامل منسي.

**يُطلق على:**
```csharp
public class Enemy : MonoBehaviour
{
    [NoAutoCancel]
    async ValkarnTask Chase()      // TT014: [NoAutoCancel] لكن لا معامل CancellationToken
    {
        await ValkarnTask.Delay(1000);
    }
}
```

**الإصلاح — أضف معامل `CancellationToken`:**
```csharp
[NoAutoCancel]
async ValkarnTask Chase(CancellationToken ct)
{
    await ValkarnTask.Delay(1000, ct);
}
```

---

## القواعد الإعلامية (TT)

---

### TT015 — محوِّل جسر Awaitable مُولَّد

| الخاصية | القيمة |
|--------|--------|
| الخطورة | معلومة |
| الفئة | ValkarnTask |
| إصلاح كود | لا |

عند انتظار `Awaitable` من Unity (من `UnityEngine`) داخل دالة `async ValkarnTask`، تُولِّد Valkarn Tasks محوِّل جسر تلقائيًا عبر `AwaitableBridge`. هذا التشخيص إعلامي: يؤكد أن الجسر مفعَّل. لا يلزم اتخاذ أي إجراء.

**يُطلق على:**
```csharp
async ValkarnTask LoadScene()
{
    await SceneManager.LoadSceneAsync("Main");  // TT015: محوِّل جسر مُولَّد
}
```

لا يلزم تغيير. يتعامل الجسر مع التحويل بشكل شفاف.

---

## قواعد جودة الكود (TT)

---

### TT016 — دالة async بدون await

| الخاصية | القيمة |
|--------|--------|
| الخطورة | تحذير |
| الفئة | ValkarnTask |
| إصلاح كود | لا |

دالة `async ValkarnTask` (أو `async ValkarnTask<T>`) لا تحتوي على تعبيرات `await` — بما في ذلك `await foreach` و`await using` و`await` داخل إعلانات `using` — تتحمَّل عبء تخصيص آلة الحالة الكامل دون فائدة. لا يزال المُجمِّع يُولِّد آلة حالة، لكن نظرًا لعدم وجود نقطة تعليق، تكتمل الدالة دائمًا بشكل متزامن.

يتحقق المحلِّل من: `AwaitExpressionSyntax`، و`foreach` مع كلمة مفتاحية `await`، وإعلانات `using` مع كلمة مفتاحية `await`، وعبارات `using` مع كلمة مفتاحية `await`.

**يُطلق على:**
```csharp
async ValkarnTask<int> ComputeTotal()  // TT016: لا await في الجسم
{
    return items.Sum(x => x.Value);
}
```

**الإصلاح — أزل `async` وأرجع مهمة مكتملة:**
```csharp
ValkarnTask<int> ComputeTotal()
{
    return ValkarnTask.FromResult(items.Sum(x => x.Value));
}
```

أو لإرجاع void:
```csharp
ValkarnTask DoSetup()
{
    Initialize();
    return ValkarnTask.CompletedTask;
}
```

---

### TT017 — [FireAndForget] على ValkarnTask\<T\>

| الخاصية | القيمة |
|--------|--------|
| الخطورة | تحذير |
| الفئة | ValkarnTask |
| إصلاح كود | لا |

`[FireAndForget]` يُشير إلى أن الدالة مُشغَّلة عمدًا دون انتظار نتيجتها. تطبيقه على دالة تُرجع `ValkarnTask<T>` (نتيجة محدَّدة النوع) يتجاهل دائمًا `T`، مما يجعل الإرجاع المحدَّد النوع بلا معنى. يجب أن تُرجع الدالة `ValkarnTask` (void) بدلاً من ذلك.

**يُطلق على:**
```csharp
[FireAndForget]
async ValkarnTask<int> SendReport()   // TT017: قيمة الإرجاع تُتجاهَل
{
    await UploadAsync();
    return 42;                     // هذا 42 لن يُرى أبدًا
}
```

**الإصلاح — غيِّر نوع الإرجاع إلى `ValkarnTask`:**
```csharp
[FireAndForget]
async ValkarnTask SendReport()
{
    await UploadAsync();
}
```

---

## قواعد الترحيل (MIG)

يُفعَّل محلِّل الترحيل **فقط عند وجود النوع `Cysharp.Threading.Tasks.UniTask`** في التجميع. القواعد إعلامية أو تحذيرات لتوجيه الانتقال من UniTask إلى Valkarn Tasks. لا توجد لأي من هذه القواعد إصلاحات تلقائية؛ الأوصاف أدناه تشرح التغيير اليدوي.

---

### MIG001 — نوع UniTask مُكتشَف

| الخاصية | القيمة |
|--------|--------|
| الخطورة | معلومة |
| الفئة | ValkarnTaskMigration |

اكتُشف استخدام نوع `UniTask` (البنية نفسها أو `UniTask<T>`). استبدله بما يعادله `ValkarnTask` أو `ValkarnTask<T>`.

```csharp
// قبل
UniTask<Sprite> LoadSprite(string path) { ... }

// بعد
ValkarnTask<Sprite> LoadSprite(string path) { ... }
```

---

### MIG002 — معامل cancelImmediately غير ضروري

| الخاصية | القيمة |
|--------|--------|
| الخطورة | معلومة |
| الفئة | ValkarnTaskMigration |

تقبل دوال `Delay` وغيرها من الدوال المبنية على الوقت في UniTask معامل `cancelImmediately` للاشتراك في الإلغاء السريع. تُلغي Valkarn Tasks فورًا بشكل افتراضي — لا يوجد معامل `cancelImmediately`. أزل الوسيط.

```csharp
// قبل
await UniTask.Delay(1000, cancelImmediately: true, cancellationToken: ct);

// بعد
await ValkarnTask.Delay(1000, ct);
```

---

### MIG003 — SuppressCancellationThrow() مُكتشَف

| الخاصية | القيمة |
|--------|--------|
| الخطورة | تحذير |
| الفئة | ValkarnTaskMigration |

`.SuppressCancellationThrow()` في UniTask يحوِّل مهمة مُلغاة إلى صف `(bool isCancelled, T result)` دون إطراح. تستخدم Valkarn Tasks `.AsResult()` لنفس الغرض، والتي تُرجع بنية `Result<T>`.

```csharp
// قبل
var (isCancelled, value) = await myUniTask.SuppressCancellationThrow();

// بعد
var result = await myValkarnTask.AsResult();
if (result.IsCanceled) { ... }
T value = result.Value;
```

---

### MIG004 — نوع إرجاع Awaitable مُكتشَف

| الخاصية | القيمة |
|--------|--------|
| الخطورة | معلومة |
| الفئة | ValkarnTaskMigration |

دالة تُرجع نوع `Awaitable` من Unity. فكِّر في استبداله بـ`ValkarnTask`، الذي يتكامل بشكل أصلي مع بيئة تشغيل Valkarn Tasks ويدعم الإلغاء التلقائي والتجميع ومجموعة المُدمِجات الكاملة.

---

### MIG005 — قناة SingleConsumerUnbounded مُكتشَفة

| الخاصية | القيمة |
|--------|--------|
| الخطورة | تحذير |
| الفئة | ValkarnTaskMigration |

`Channel.CreateSingleConsumerUnbounded<T>()` من UniTask تنشئ قناة بدون ضغط خلفي. فكِّر في استبداله بـ`ValkarnTask.Channel.CreateBounded<T>(capacity)` لإضافة ضغط خلفي ومنع نمو الذاكرة غير المحدود تحت الحمل.

```csharp
// قبل
var ch = Channel.CreateSingleConsumerUnbounded<Event>();

// بعد (مع ضغط خلفي)
var ch = ValkarnTask.Channel.CreateBounded<Event>(capacity: 256);

// أو إذا كان غير المحدود مقصودًا
var ch = ValkarnTask.Channel.CreateUnbounded<Event>();
```

---

### MIG006 — PlayerLoopTiming من UniTask مُكتشَف

| الخاصية | القيمة |
|--------|--------|
| الخطورة | معلومة |
| الفئة | ValkarnTaskMigration |

اكتُشفت قيمة enum `PlayerLoopTiming` من مساحة الاسم `Cysharp.Threading.Tasks`. غيِّر توجيه `using` إلى `UnaPartidaMas.Valkarn.Tasks`؛ أسماء قيم enum متطابقة.

```csharp
// قبل
using Cysharp.Threading.Tasks;
timing = PlayerLoopTiming.Update;

// بعد
using UnaPartidaMas.Valkarn.Tasks;
timing = PlayerLoopTiming.Update;  // نفس الاسم، مساحة اسم مختلفة
```

---

### MIG007 — async UniTaskVoid مُكتشَف

| الخاصية | القيمة |
|--------|--------|
| الخطورة | تحذير |
| الفئة | ValkarnTaskMigration |

`async UniTaskVoid` كان نمط الدالة fire-and-forget في UniTask. تستبدله Valkarn Tasks بخيارَين:

```csharp
// قبل
async UniTaskVoid StartLoadAsync() { ... }

// بعد — الخيار 1: سمة [FireAndForget]
[FireAndForget]
async ValkarnTask StartLoadAsync() { ... }

// بعد — الخيار 2: ValkarnTask قياسية + .Forget() في موقع الاستدعاء
async ValkarnTask StartLoadAsync() { ... }
// يُستدعى على النحو:
StartLoadAsync().Forget();
```

---

### MIG008 — Awaitable.MainThreadAsync() مُكتشَف

| الخاصية | القيمة |
|--------|--------|
| الخطورة | معلومة |
| الفئة | ValkarnTaskMigration |

استُخدم `Awaitable.MainThreadAsync()` في واجهة برمجة `Awaitable` في Unity للعودة إلى الخيط الرئيسي. تُشغِّل Valkarn Tasks الاستمرارات على الخيط الرئيسي بشكل افتراضي عبر تكامل PlayerLoop، لذا فإن استدعاءات `MainThreadAsync()` الصريحة عادةً غير ضرورية ويمكن حذفها.

---

### MIG009 — UniTask.RunOnThreadPool مُكتشَف

| الخاصية | القيمة |
|--------|--------|
| الخطورة | تحذير |
| الفئة | ValkarnTaskMigration |

استبدل بـ`ValkarnTask.RunOnThreadPool`. واجهة برمجة التطبيقات متطابقة.

```csharp
// قبل
await UniTask.RunOnThreadPool(() => HeavyWork());

// بعد
await ValkarnTask.RunOnThreadPool(() => HeavyWork());
```

---

### MIG010 — .ToCoroutine() مُكتشَف

| الخاصية | القيمة |
|--------|--------|
| الخطورة | تحذير |
| الفئة | ValkarnTaskMigration |

`.ToCoroutine()` كان جسر UniTask للمستدعين القديمين للكوروتين. أعد كتابة الكود المستهلِك كدالة `async ValkarnTask` بدلاً من ذلك.

```csharp
// قبل
IEnumerator LegacyCaller() { yield return MyUniTask().ToCoroutine(); }

// بعد
async ValkarnTask ModernCaller() { await MyValkarnTask(); }
```

---

### MIG011 — UniTask.Create() مُكتشَف

| الخاصية | القيمة |
|--------|--------|
| الخطورة | تحذير |
| الفئة | ValkarnTaskMigration |

`UniTask.Create(Func<UniTask>)` يغلِّف مندوب مصنع. استبدل بنمط `ValkarnTask.Promise<T>` للاكتمال اليدوي المتحكَّم.

```csharp
// قبل
var task = UniTask.Create(async () => { await DoWork(); return 42; });

// بعد
var promise = new ValkarnTaskCompletionSource<int>();
DoWorkThenComplete(promise);
var task = promise.Task;
```

---

### MIG012 — UniTask.Lazy/Defer مُكتشَف

| الخاصية | القيمة |
|--------|--------|
| الخطورة | معلومة |
| الفئة | ValkarnTaskMigration |

كانت `UniTask.Lazy<T>` و`UniTask.Defer` موجودتَين لتجنب التخصيص عندما قد تكتمل مهمة بشكل متزامن. تمتلك Valkarn Tasks مسارًا سريعًا متزامن صفر التخصيص مدمجًا: إرجاع `ValkarnTask.CompletedTask` أو `ValkarnTask.FromResult(value)` لا يُخصِّص أبدًا. أزل أغلفة `Lazy`/`Defer`.

---

### MIG013 — .ToUniTask()/.AsUniTask() مُكتشَف

| الخاصية | القيمة |
|--------|--------|
| الخطورة | معلومة |
| الفئة | ValkarnTaskMigration |

استدعاءات التحويل من Unity `Awaitable` إلى UniTask. أزلها؛ تجسِّر Valkarn Tasks `Awaitable` بشكل أصلي (انظر TT015).

---

### MIG014 — UniTaskAsyncEnumerable مُكتشَف

| الخاصية | القيمة |
|--------|--------|
| الخطورة | تحذير |
| الفئة | ValkarnTaskMigration |

شحنت UniTask `IUniTaskAsyncEnumerable<T>` الخاصة بها وأدوات `UniTaskAsyncEnumerable`. استخدم `IAsyncEnumerable<T>` من BCL مع `System.Linq.Async` بدلاً من ذلك. `IAsyncEnumerable<T>` مدعوم بشكل أصلي من `await foreach` في C#.

```csharp
// قبل
IUniTaskAsyncEnumerable<int> GetItems() { ... }

// بعد
IAsyncEnumerable<int> GetItems() { ... }
```

---

### MIG015 — TimeoutController مُكتشَف

| الخاصية | القيمة |
|--------|--------|
| الخطورة | معلومة |
| الفئة | ValkarnTaskMigration |

`TimeoutController` في UniTask كان مساعدًا لمهلات قابلة لإعادة الاستخدام. استبدله بـ`CancellationTokenSource` قياسي مُنشأ بـ`TimeSpan`، وهو ما يدعمه BCL مباشرةً.

```csharp
// قبل
var controller = new TimeoutController();
var ct = controller.Timeout(TimeSpan.FromSeconds(5));

// بعد
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var ct = cts.Token;
```

---

## قمع القواعد

آليات قمع Roslyn القياسية تعمل لجميع القواعد:

```csharp
// قمع حدوث واحد بشكل مضمَّن
#pragma warning disable TT012
while (true) { DoSomething(); }
#pragma warning restore TT012
```

أو عبر `.editorconfig` لقمع القاعدة على مستوى المشروع:
```ini
[*.cs]
dotnet_diagnostic.TT012.severity = none
```

يمكن قمع قواعد الترحيل (`MIG*`) بنفس الطريقة، أو تعطيلها بشكل عام عند اكتمال الترحيل بضبط الخطورة على `none` في `.editorconfig`.
