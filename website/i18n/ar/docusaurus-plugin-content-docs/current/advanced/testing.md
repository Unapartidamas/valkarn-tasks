---
sidebar_position: 3
title: الاختبار
---

# الاختبار

تشحن Valkarn Tasks بنية تحتية مخصصة للاختبار تتيح لك كتابة اختبارات وحدة سريعة وحتمية للكود غير المتزامن — بدون مؤقتات حقيقية، ولا انتظار إطارات، ولا حاجة لمحرر Unity لمجموعة الاختبارات الأساسية.

---

## نظرة عامة

دعم الاختبار موجود في تجميع `Testing/` (`UnaPartidaMas.Valkarn.Tasks.Testing`)، والذي يكون مرئيًا لبيئة التشغيل عبر `InternalsVisibleTo`. يعرض نوعَين عامَّين:

| النوع | الغرض |
|------|---------|
| `ValkarnTaskTestHelper` | تهيئة وإيقاف بيئة تشغيل Valkarn Tasks لجلسة اختبار |
| `TestClock` | مزوِّد وقت وإطارات حتمي؛ يستبدل Unity `TimeProvider` أثناء الاختبارات |

---

## ValkarnTaskTestHelper

`ValkarnTaskTestHelper` هو فئة أداة ثابتة في مساحة الاسم `UnaPartidaMas.Valkarn.Tasks.Testing`.

### ما تفعله

تُنجز `Setup()` ثلاثة أشياء:
1. تُنشئ `TestClock` وتثبِّته كـ`TimeProvider.Current`، مستبدِلةً أي مزوِّد وقت Unity حقيقي.
2. تستدعي `PlayerLoopHelper.InitializeForTest()`، الذي يُخصِّص مصفوفات `ContinuationQueue` و`PlayerLoopRunner` لجميع توقيتات PlayerLoop الـ16 ويضع علامة على بيئة التشغيل كمهيَّأة.
3. تُرجع `TestClock` حتى يتمكن اختبارك من التحكم في الوقت.

تعكس `Teardown()` الإعداد:
1. تثبِّت `TestClock` جديدًا عديم التأثير كـ`TimeProvider.Current` لمنع تسرب الوقت القديم بين مثبِّتات الاختبار.
2. تستدعي `PlayerLoopHelper.ShutdownForTest()`، الذي يُوقف قوائم الانتظار والمشغِّلات.

### نمط الاستخدام

```csharp
using NUnit.Framework;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Testing;

[TestFixture]
public class MyAsyncTests
{
    TestClock clock;

    [SetUp]
    public void SetUp()
    {
        clock = ValkarnTaskTestHelper.Setup();
    }

    [TearDown]
    public void TearDown()
    {
        ValkarnTaskTestHelper.Teardown();
    }

    // الاختبارات هنا
}
```

استدعِ `Setup` مرة واحدة لكل اختبار في `[SetUp]` و`Teardown` مرة واحدة لكل اختبار في `[TearDown]`. يضمن هذا النمط أن كل اختبار يبدأ بحالة بيئة تشغيل نظيفة ولا يتسرب إلى الاختبار التالي.

### وقت الدلتا الافتراضي

تقبل `Setup` معامل `defaultDeltaTime` اختياريًا (الافتراضي: `1/60` ثانية، أي 60 fps):

```csharp
clock = ValkarnTaskTestHelper.Setup(defaultDeltaTime: 1f / 30f);  // محاكاة 30 fps
```

تستخدم هذه القيمة `TestClock.AdvanceFrame()` و`TestClock.AdvanceFrames(n)`.

---

## TestClock

تنفِّذ `TestClock` الواجهة `ITimeProvider` وتمنح الاختبارات تحكمًا كاملاً في:

- الوقت المحاكى (عبر `GetTimestamp()`، مدعومًا بقراد `Stopwatch.Frequency`)
- `DeltaTime` و`UnscaledDeltaTime` للإطار الحالي
- `FrameCount`

### تقدم الوقت

#### `Advance(TimeSpan duration)`

يُقدِّم الوقت بالمدة المعطاة في خطوة واحدة. تُطبَّق المدة كاملةً كـ`DeltaTime` لذلك الإطار، يزداد `FrameCount` بمقدار 1، وتُعالَج جميع توقيتات PlayerLoop. بعد المعالجة، يُستعاد `DeltaTime` إلى القيمة التي كان عليها قبل الاستدعاء.

```csharp
var task = ValkarnTask.Delay(3000);  // تأخير 3 ثوانٍ

clock.Advance(TimeSpan.FromSeconds(2));
Assert.IsFalse(task.IsCompleted);   // لم يكتمل بعد

clock.Advance(TimeSpan.FromSeconds(1));
Assert.IsTrue(task.IsCompleted);    // عند 3 ثوانٍ بالضبط
```

استخدم هذا عندما تريد القفز إلى نقطة زمنية محددة دون محاكاة إطارات فردية.

#### `AdvanceFrame()`

يُقدِّم إطارًا واحدًا باستخدام `DeltaTime` الحالية. يزداد `FrameCount` بمقدار 1، وتُعالَج جميع توقيتات PlayerLoop، ويُدرج `Thread.Sleep` لمدة 1 ملي ثانية قبل المعالجة. يوجد النوم لأن خيوط العمال الخلفية (المستخدمة من قِبَل `RunOnThreadPool` وتكامل نظام وظائف Unity) تحتاج نافذة صغيرة للاكتمال بين دقات الإطارات — بدونها، تشغِّل الاختبارات الإطارات بشكل متتالٍ بدون فجوة، مما يسبب حالات تسابق لا تحدث في إطارات Unity الحقيقية.

```csharp
clock.AdvanceFrame();
```

#### `AdvanceFrames(int count)`

يستدعي `AdvanceFrame()` عدد `count` من المرات.

```csharp
clock.AdvanceFrames(10);  // محاكاة 10 إطارات
```

#### `ProcessTick(PlayerLoopTiming timing)`

يُعالج مرحلة توقيت PlayerLoop واحدة دون تقدم الوقت أو `FrameCount`. مفيد عند اختبار الكود المجدوَل عند توقيت محدد (مثل `PlayerLoopTiming.FixedUpdate`) ولا تريد معالجة جميع التوقيتات الأخرى.

```csharp
clock.ProcessTick(PlayerLoopTiming.FixedUpdate);
```

### التحكم في وقت الدلتا

#### `SetDeltaTime(float deltaTime)`

يضبط كلًا من `DeltaTime` و`UnscaledDeltaTime` على نفس القيمة لجميع الإطارات اللاحقة.

#### `SetDeltaTime(float deltaTime, float unscaledDeltaTime)`

يضبطهما بشكل مستقل لمحاكاة `Time.timeScale` غير الوحدوي. على سبيل المثال، لاختبار الكود الذي يستخدم `DelayType.UnscaledDeltaTime` أثناء إيقاف الوقت:

```csharp
clock.SetDeltaTime(deltaTime: 0f, unscaledDeltaTime: 0.05f);

var scaledTask = ValkarnTask.Delay(500, DelayType.DeltaTime);
var unscaledTask = ValkarnTask.Delay(500, DelayType.UnscaledDeltaTime);

clock.AdvanceFrames(9);  // 450 ملي ثانية غير مقيَّسة، 0 ملي ثانية مقيَّسة
Assert.IsFalse(scaledTask.IsCompleted);    // وقت اللعبة المقيَّسة لا يصل أبدًا إلى 500 ملي ثانية
Assert.IsFalse(unscaledTask.IsCompleted); // 450 ملي ثانية < 500 ملي ثانية

clock.AdvanceFrame();  // 500 ملي ثانية غير مقيَّسة
Assert.IsTrue(unscaledTask.IsCompleted);  // التأخير غير المقيَّس منتهٍ
Assert.IsFalse(scaledTask.IsCompleted);  // المقيَّس لا يتحرك أبدًا
```

---

## كتابة اختبارات الوحدة لدوال ValkarnTask غير المتزامنة

### الاختبارات التي تكتمل بشكل متزامن

بعض عمليات `ValkarnTask` تكتمل دون تعليق — على سبيل المثال، انتظار `ValkarnTask.CompletedTask` أو `ValkarnTask.FromResult(value)` أو `ValkarnTaskCompletionSource` اكتمل قبل انتظاره. هذه لا تتطلب ساعة على الإطلاق ويمكنها استخدام الفئة الداخلية `TestHelper` مباشرةً (داخلية لتجميع الاختبار):

```csharp
[TestFixture]
public class SyncTests
{
    [SetUp]
    public void SetUp()
    {
        // يضبط فقط معرّف الخيط الرئيسي — لا ساعة ولا PlayerLoop مطلوب
        TestHelper.EnsureInitialized();
    }

    [Test]
    public void FromResult_ReturnsValue()
    {
        var task = ValkarnTask.FromResult(42);
        Assert.IsTrue(task.IsCompleted);
        Assert.AreEqual(42, task.GetAwaiter().GetResult());
    }
}
```

`TestHelper` (في `Tests/Editor/TestHelper.cs`) هو مساعد داخلي تستخدمه مجموعة الاختبارات نفسها:

- `TestHelper.EnsureInitialized()` يضبط `ValkarnTaskPoolShared.MainThreadId` على الخيط الحالي. هذا مطلوب حتى تتوجه عمليات المجموعة إلى المسار السريع الصحيح (غير الذري).
- `TestHelper.RunSync(ValkarnTask task)` يؤكد أن المهمة مكتملة بالفعل ويستدعي `GetResult()` — مفيد لاختبار مسارات الكود التي يجب أن تكتمل بشكل متزامن.
- `TestHelper.RunSync<T>(ValkarnTask<T> task)` يفعل نفس الشيء ويُرجع قيمة النتيجة.
- `TestHelper.UnobservedExceptionCollector` يشترك في `ValkarnTask.UnobservedException` طوال مدة نطاقه، جامعًا أي استثناءات غير ملاحَظة للتأكيد عليها.

### الاختبارات التي تتطلب الوقت

أي اختبار يتضمن `ValkarnTask.Delay` أو `ValkarnTask.Yield` أو الكود المجدوَل عند توقيت PlayerLoop يتطلب `ValkarnTaskTestHelper.Setup()` و`TestClock` المُرجَع.

**مثال: اختبار دالة مبنية على التأخير**

لنفترض أن لديك:

```csharp
public static async ValkarnTask WaitAndLog(int ms)
{
    await ValkarnTask.Delay(ms);
    Log("done");
}
```

اختبرها بدون انتظار حقيقي:

```csharp
[TestFixture]
public class DelayTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = ValkarnTaskTestHelper.Setup();

    [TearDown]
    public void TearDown() => ValkarnTaskTestHelper.Teardown();

    [Test]
    public void WaitAndLog_CompletesAfterDelay()
    {
        var task = WaitAndLog(3000);
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(task.IsCompleted);

        // استرجع النتيجة (يُطرح إذا كانت خاطئة)
        task.GetAwaiter().GetResult();
    }

    [Test]
    public void WaitAndLog_ZeroDelay_CompletesImmediately()
    {
        var task = WaitAndLog(0);
        // ValkarnTask.Delay(0) يُرجع CompletedTask فورًا
        Assert.IsTrue(task.IsCompleted);
    }
}
```

### اختبار الإلغاء

```csharp
[Test]
public void Delay_CancelledMidway_ThrowsOCE()
{
    var cts = new CancellationTokenSource();
    var task = ValkarnTask.Delay(5000, cts.Token);
    Assert.IsFalse(task.IsCompleted);

    cts.Cancel();
    // الإلغاء يُطبَّق في الدقة التالية
    clock.AdvanceFrame();

    Assert.IsTrue(task.IsCompleted);
    Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
}
```

### جمع الاستثناءات غير الملاحَظة

```csharp
[Test]
public void FaultedTask_PublishesUnobservedException()
{
    using var collector = new TestHelper.UnobservedExceptionCollector();

    var tcs = new ValkarnTaskCompletionSource();
    var task = tcs.Task;

    // لا تنتظر — fire and forget
    task.Forget();

    // اكتمل بالاستثناء — يُطلق مسار غير ملاحَظ
    tcs.TrySetException(new InvalidOperationException("oops"));

    Assert.AreEqual(1, collector.Exceptions.Count);
    Assert.IsInstanceOf<InvalidOperationException>(collector.Exceptions[0]);
}
```

---

## مثال: اختبار مُنتِج/مُستهلِك قناة

```csharp
[TestFixture]
public class ChannelPipelineTests
{
    [SetUp]
    public void SetUp() => TestHelper.EnsureInitialized();

    [Test]
    public void Producer_WritesItems_ConsumerReadsInOrder()
    {
        var channel = ValkarnTask.Channel.CreateUnbounded<int>();

        // المُنتِج: الكتابة بشكل متزامن
        channel.Writer.TryWrite(1);
        channel.Writer.TryWrite(2);
        channel.Writer.TryWrite(3);
        channel.Writer.Complete();

        // المُستهلِك: ReadAsync على قناة بعناصر يكتمل بشكل متزامن
        Assert.AreEqual(1, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(2, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(3, TestHelper.RunSync(channel.Reader.ReadAsync()));
    }

    [Test]
    public void ReadAsync_BeforeWrite_PendsThenCompletesOnWrite()
    {
        var channel = ValkarnTask.Channel.CreateUnbounded<string>();

        // القراءة تُصدَر قبل أي كتابة
        var readTask = channel.Reader.ReadAsync();
        Assert.IsFalse(readTask.IsCompleted);

        // الكتابة تُكمل القراءة المعلَّقة بشكل متزامن
        channel.Writer.TryWrite("hello");
        Assert.IsTrue(readTask.IsCompleted);
        Assert.AreEqual("hello", readTask.GetAwaiter().GetResult());
    }

    [Test]
    public void Complete_DrainedChannel_CompletionTaskCompletes()
    {
        var channel = ValkarnTask.Channel.CreateUnbounded<int>();
        channel.Writer.TryWrite(42);

        var completion = channel.Reader.Completion;
        Assert.IsFalse(completion.IsCompleted);

        channel.Writer.Complete();
        Assert.IsFalse(completion.IsCompleted);  // العنصر لا يزال غير مقروء

        channel.Reader.TryRead(out _);
        Assert.IsTrue(completion.IsCompleted);   // مُستنزَف — الاكتمال يُطلق
    }

    [Test]
    public void ClosedChannel_Read_ThrowsChannelClosedException()
    {
        var channel = ValkarnTask.Channel.CreateUnbounded<int>();
        channel.Writer.Complete();

        var readTask = channel.Reader.ReadAsync();
        Assert.IsTrue(readTask.IsCompleted);
        Assert.Throws<ChannelClosedException>(() => readTask.GetAwaiter().GetResult());
    }
}
```

---

## مثال: اختبار ValkarnTask.Delay مع تقدم الإطارات

يوضح هذا المثال اختبار دالة تنتظر تأخيرًا قابلاً للتهيئة، مع التحقق من عدم اكتمال المهمة قبل الأوان عند التقدم الجزئي.

```csharp
[TestFixture]
public class DelayFrameTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = ValkarnTaskTestHelper.Setup(defaultDeltaTime: 1f / 60f);

    [TearDown]
    public void TearDown() => ValkarnTaskTestHelper.Teardown();

    [Test]
    public void Delay_500ms_RequiresTenFramesAt50ms()
    {
        clock.SetDeltaTime(0.05f);  // 50 ملي ثانية لكل إطار
        var task = ValkarnTask.Delay(500);

        clock.AdvanceFrames(9);
        Assert.IsFalse(task.IsCompleted, "مضت 450 ملي ثانية — يجب ألا يكون منتهيًا");

        clock.AdvanceFrame();
        Assert.IsTrue(task.IsCompleted, "مضت 500 ملي ثانية — يجب أن يكون منتهيًا");
    }

    [Test]
    public void Delay_Realtime_UsesTimestampNotDeltaTime()
    {
        // التأخير في الوقت الحقيقي يتجاهل DeltaTime؛ يستخدم الطابع الزمني لـStopwatch
        clock.SetDeltaTime(0f);  // DeltaTime مجمَّد
        var task = ValkarnTask.Delay(200, DelayType.Realtime);

        // Advance تُحرِّك فقط الطابع الزمني عبر TimeSpan
        clock.Advance(TimeSpan.FromMilliseconds(199));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(task.IsCompleted);
    }
}
```

---

## تكامل NUnit

تستخدم مجموعة الاختبارات **NUnit 3.x** (مُثبَّت على 3.x في `TestRunner.csproj`). أزال NUnit 4 واجهة برمجة `Assert.IsTrue` / `Assert.AreEqual` الكلاسيكية التي تستخدمها الاختبارات؛ لا ترقِّ إلى NUnit 4 ما لم تُحدَّث التأكيدات إلى واجهة برمجة مبنية على القيود.

### إطار اختبار Unity

في محرر Unity، تُكتشَف الاختبارات في `Tests/Editor/` وتُشغَّل بواسطة **Unity Test Framework** (UTF)، المبني على NUnit. يعمل تكامل مشغِّل الاختبارات على النحو التالي:

- يُشغِّل UTF كل `[Test]` على الخيط الرئيسي.
- `ValkarnTaskTestHelper.Setup()` يثبِّت `TestClock` قبل كل اختبار؛ سمة `[SetUp]` في UTF تستدعيه.
- `ValkarnTaskTestHelper.Teardown()` يُزيل الساعة في `[TearDown]`.
- لا يلزم كوروتين أو `[UnityTest]`: جميع اختبارات Valkarn Tasks تستخدم دوال NUnit `[Test]` المتزامنة. يُحرِّك `TestClock` الوقت يدويًا، لذا لا حاجة لتقدم الإطارات الحقيقية.

---

## مشروع _TestRunner~

يحتوي مجلد `_TestRunner~/` على مشروع .NET 8 مستقل يُجمِّع ويُشغِّل مجموعة الاختبارات الكاملة **خارج Unity**، باستخدام .NET SDK و`dotnet test`. يُستخدم للتكامل المستمر وللتطوير المحلي للمساهمين.

### الهيكل

```
_TestRunner~/
  TestRunner.csproj   — ملف مشروع .NET 8
  bin~/               — إخراج البناء (مُجاهَل بـgit)
  obj~/               — الإخراج الوسيط (مُجاهَل بـgit)
```

### كيف يعمل

يُجمِّع `TestRunner.csproj` أربع مجموعات مصادر في تجميع واحد:

| مجموعة المصادر | المسار | ملاحظات |
|------------|------|-------|
| بيئة التشغيل | `../Runtime/**/*.cs` | جميع ملفات بيئة التشغيل |
| الاختبار | `../Testing/**/*.cs` | `ValkarnTaskTestHelper`، `TestClock` |
| الاختبارات | `../Tests/Editor/**/*.cs` | جميع ملفات `.cs` للاختبار |
| الاستثناءات | `UnityTimeProvider.cs`، `Bridge/`، `Burst/`، `ECS/` | يتطلب واجهات برمجة Unity الحقيقية — مستثنى |

المشروع **لا** يعرِّف `UNITY_5_3_OR_NEWER` أو `VTASKS_HAS_BURST` أو `VTASKS_HAS_COLLECTIONS` أو `VTASKS_HAS_ENTITIES`، لذا تُجمَّع جميع فروع `#if` الخاصة بـUnity. هذا يعني أن الاختبارات تُمارس مسارات الكود الخالص C#.

يُحمَّل ملف DLL للمحلِّل كعناصر `<Analyzer>`، لذا تُطلَق نفس القواعد التي تُطلَق في بيئة التطوير المتكاملة أيضًا أثناء `dotnet build` لمشروع الاختبار.

### تشغيل الاختبارات

```bash
cd _TestRunner~
dotnet test
```

أو من جذر المستودع:

```bash
dotnet test _TestRunner~/TestRunner.csproj
```

### ملفات الاختبار المستثناة

ثلاثة مجلدات فرعية للاختبار مستثناة من مشروع `_TestRunner~` لأنها تتطلب واجهات برمجة بيئة تشغيل Unity الحقيقية:

| المجلد | السبب |
|-----------|--------|
| `Tests/Editor/Bridge/` | اختبارات جسر نظام الوظائف؛ تتطلب `Unity.Jobs` |
| `Tests/Editor/Burst/` | اختبارات مُجدوِل Burst؛ تتطلب `Unity.Burst` |
| `Tests/Editor/ECS/` | اختبارات أدوات ECS؛ تتطلب `Unity.Entities` |

تعمل هذه الاختبارات فقط داخل محرر Unity عبر Unity Test Framework.

### إضافة اختبار جديد

1. أضف ملف `.cs` جديدًا تحت `Tests/Editor/` (ليس في مجلد فرعي لواجهة برمجة Unity).
2. زيِّن الفئة بـ`[TestFixture]` والدوال بـ`[Test]`.
3. إذا احتاج الاختبار للتحكم في الوقت، استخدم `ValkarnTaskTestHelper.Setup()` / `Teardown()` في `[SetUp]` / `[TearDown]`.
4. إذا احتاج الاختبار فقط لتأكيدات المهام المتزامنة (بدون تأخير)، استخدم `TestHelper.EnsureInitialized()` في `[SetUp]` — لا يلزم TearDown.
5. شغِّل `dotnet test _TestRunner~/TestRunner.csproj` للتحقق خارج Unity.
6. شغِّل Unity Test Framework للتحقق داخل Unity.
