---
sidebar_position: 2
title: تجميع الكائنات
---

# تجميع الكائنات

يُزيل Valkarn Tasks تخصيصات جامع النفايات على مسارات async الشائعة بتجميع الكائنات التي تدعم كل `VlkTask`. تشرح هذه الصفحة معمارية مجموعة الموارد — كيف تُخزَّن الكائنات، وكيف تُكتسب وتُرجَع، وما هي ضمانات دورة الحياة التي يوفرها النظام.

---

## نظرة عامة

عندما تتوقف طريقة `async`، تحتاج المكتبة إلى مكان لتخزين آلة الحالة المُولَّدة من المُترجم وآلية اكتمال يمكن للمُنتظِر الاشتراك فيها. في `System.Threading.Tasks`، هذا هو كائن `Task` نفسه — تخصيص كومة ذاكرة واحد لكل استدعاء. في Valkarn Tasks، يؤدي هذا الدور كائنات مُجمَّعة تُطبّق `VlkTask.ISource`.

يتضمن تصميم مجموعة الموارد ثلاثة أهداف:

1. **صفر عمليات ذرية على المسار الحار للخيط الرئيسي.** حلقة اللعبة في Unity أحادية الخيط باتفاق. يجب أن تكون الاستعارة والإرجاع من الخيط الرئيسي قراءات وكتابات عادية.
2. **وصول آمن عبر الخيوط.** المهام الخلفية التي تستخدم `VlkTask.Run` تعمل على خيوط مجموعة الخيوط. يجب أن تتعامل مجموعة الموارد مع الاستعارة/الإرجاع المتزامن بشكل صحيح.
3. **نمو محدود مع تقليص تكيّفي.** لا يجب أن تنمو مجموعات الموارد بلا حدود بعد ارتفاع مفاجئ في الحركة، لكنها لا يجب أن تتقلص بشكل جائر لتُعيد التخصيص باستمرار.

---

## `VlkTaskPool<T>`

`VlkTaskPool<T>` هو فئة مجموعة الموارد الأساسية. إنها `internal sealed` — لا تتفاعل معها مباشرةً، لكن فهمها يشرح أين تذهب تخصيصاتك.

```
VlkTaskPool<T>
  |
  +-- fastItem: T            (فتحة ذاكرة مؤقتة للخيط الرئيسي فقط، قراءة/كتابة عادية)
  |
  +-- stackHead: T           (رأس مكدس Treiber، CAS للأمان عبر الخيوط)
  +-- stackSize: int
  |
  +-- maxSize: int           (محدود بـ VlkTask.DefaultMaxPoolSize)
  +-- totalCreated: int      (يتتبع التخصيصات طوال العمر لنسبة التقليص)
```

### الفتحة السريعة (الخيط الرئيسي)

حقل `fastItem` فتحة محجوزة واحدة لآخر كائن مُرجَع. في الخيط الرئيسي، الاستعارة والإرجاع هما قراءة وكتابة عادية — لا عمليات ذرية، لا دوران. هذا يغطي الغالبية العظمى من عمليات حلقة اللعبة في Unity.

```
الاستعارة (الخيط الرئيسي):
  fastItem != null  →  أخذه (fastItem = null)، إرجاعه   [صفر عمليات ذرية]
  fastItem == null  →  المتابعة إلى مكدس Treiber

الإرجاع (الخيط الرئيسي):
  fastItem == null  →  fastItem = item                   [صفر عمليات ذرية]
  fastItem != null  →  المتابعة إلى مكدس Treiber
```

### مكدس Treiber (الفيض / خيوط الخلفية)

عندما تكون الفتحة السريعة مشغولة (أو عندما لا يكون الخيط المُستدعِي هو الخيط الرئيسي)، تستخدم مجموعة الموارد مكدس Treiber خاليًا من الأقفال — قائمة مترابطة كلاسيكية باستخدام مقارنة-وتبادل (CAS):

```
الاستعارة (أي خيط):
  while (true):
    head = Volatile.Read(stackHead)
    if head == null: return null (مجموعة الموارد فارغة)
    next = head.NextNode
    if CAS(stackHead, next, head) == head: return head  // فاز بالسباق
    spinner.SpinOnce()                                   // خسر، أعد المحاولة

الإرجاع (أي خيط):
  if stackSize >= maxSize: return false (مجموعة الموارد ممتلئة، تجاهل العنصر)
  while (true):
    head = Volatile.Read(stackHead)
    item.NextNode = head
    if CAS(stackHead, item, head) == head: stackSize++; return true
    spinner.SpinOnce()
```

المكدس داخلي: كل كائن مُجمَّع يخزن مؤشر `NextNode` الخاص به، لذا لا يُحتاج إلى عقدة غلاف خارجية. هذا مُفرَض بواجهة `IPoolNode<T>`.

### توجيه الخيوط

```csharp
internal static class VlkTaskPoolShared
{
    internal static volatile int MainThreadId;
}
```

جميع نُسَخ مجموعة الموارد تشترك في `MainThreadId` واحد. تعمليات الاستعارة/الإرجاع تفحص `Thread.CurrentThread.ManagedThreadId == MainThreadId` للتوجيه إلى المسار الصحيح. حقل `volatile` يضمن الرؤية عبر الخيوط بعد نشر المعرّف عند بدء التشغيل.

---

## `IPoolNode<T>`

أي نوع يشارك في مجموعة الموارد يجب أن يُطبّق هذه الواجهة:

```csharp
internal interface IPoolNode<T> where T : class
{
    ref T NextNode { get; }
}
```

`ref T NextNode` يُرجع مرجعًا للحقل داخل الكائن الذي يخزن المؤشر التالي. تكتب مجموعة الموارد مباشرةً إلى هذا الحقل عبر ref، مما يُزيل الحاجة لأي عقدة غلاف منفصلة. جميع الأنواع المُجمَّعة في المكتبة — المُشغّلات والوعود والمُجمِّعات — تُطبّق هذه الواجهة بإعلان حقل خاص وعرضه:

```csharp
// مثال من PooledPromise<T>
PooledPromise<T> nextNode;
public ref PooledPromise<T> NextNode => ref nextNode;
```

---

## دورة حياة مجموعة الموارد: الاكتساب، الاستخدام، الإرجاع

دورة الحياة الكاملة لكائن مُجمَّع هي:

```
المُستدعي يستدعي طريقة غير متزامنة
  |
  +--> AsyncVlkTaskMethodBuilder.AwaitUnsafeOnCompleted()
        |
        +--> runner == null? نعم --> Pool.TryRent() ?? Pool.TrackNew(new Runner())
              |
              +--> runner مخزّن في المُنشئ؛ آلة الحالة منسوخة إلى runner
              |
              +--> awaiter.UnsafeOnCompleted(runner.MoveNextAction)
              |
              [... العمل الغير متزامن يتقدم ...]
              |
              +--> runner.SetResult() / runner.SetException()
                    |
                    +--> VlkTaskCompletionCore.TrySetResult() --> استدعاء الاستمرار
                          |
                          +--> مُنتظِر المُستدعي يستدعي GetResult(token)
                                |
                                +--> core.GetResult(token) -- يقرأ النتيجة أو يعيد الرمي
                                |
                                +--> TryReturn():
                                      stateMachine = default
                                      core.Reset()           // يزيد الجيل
                                      Pool.TryReturn(this)
```

طريقة `TryReturn` دائمًا تمسح آلة الحالة قبل استدعاء `core.Reset()`. هذا الترتيب مهم: `Reset()` تزيد عداد الجيل، مما يجعل الفتحة مرئية كمتاحة للمستعيرين المتزامنين. إذا مُسحت آلة الحالة بعد `Reset()`، يمكن لمستعير على خيط آخر الحصول على الفتحة وتغيير آلة حالته.

---

## `VlkTaskCompletionCore<TResult>`

`VlkTaskCompletionCore<TResult>` هو `internal struct` مضمّن داخل كل كائن مُجمَّع. إنه آلة الحالة الفعلية للوعد — تتتبع حالة الاكتمال، وتخزن النتائج والأخطاء، وتحل سباق `OnCompleted` (تسجيل استمرار) و`TrySetResult` (الإشارة بالاكتمال).

```
الحقول:
  result:          TResult     -- قيمة النجاح
  error:           object      -- ExceptionDispatchInfo أو OperationCanceledException
  errorKind:       byte        -- 0=لا شيء، 1=معطوب، 2=ملغى(OCE)، 3=ملغى(EDI)
  generation:      int         -- يزيد بثبات؛ يُصبَّ إلى uint لمقارنة الرموز
  completedCount:  int         -- 0=معلق، 1=مُطالَب به، 2=مكتمل (نشر ثنائي المرحلة)
  continuation:    Action<object>
  continuationState: object
  hasUnhandledError: bool
```

### بروتوكول الاكتمال ثنائي المرحلة

يستخدم الاكتمال CAS ثنائي المرحلة للأمان على ARM64 حيث تُحتاج أزواج store-release / load-acquire:

```
TrySetResult(value):
  المرحلة 1: CAS(completedCount, 0 -> 1)   -- الادعاء بالملكية الحصرية
  المرحلة 2: كتابة النتيجة
  المرحلة 3: Volatile.Write(completedCount, 2)  -- النشر بدلالات الإصدار
  المرحلة 4: InvokeContinuation()
```

يستخدم القراء `Volatile.Read(completedCount)` (دلالات الاستحواذ) قبل قراءة النتيجة، مما يضمن رؤية القيمة المكتوبة في المرحلة 2.

### حل سباق `OnCompleted` و`TrySetResult`

ثلاثة أنماط يمكن أن تحدث:

```
النمط أ — OnCompleted أولًا:
  OnCompleted يخزن الاستمرار عبر CAS(continuation, null -> cont)
  TrySetResult يقرأ الاستمرار غير الفارغ -> ينفّذه

النمط ب — TrySetResult أولًا (المسار السريع المتزامن):
  TrySetResult يضع ContinuationSentinel عبر CAS(continuation, null -> sentinel)
  OnCompleted يقرأ sentinel -> ينفّذ الاستمرار مضمّنًا فورًا

النمط ج (سباق متزامن):
  ج.1: OnCompleted يفوز بـ CAS -> TrySetResult يقرأه -> ينفّذ
  ج.2: TrySetResult يفوز بـ CAS (يضع sentinel) -> OnCompleted يكتشف sentinel -> ينفّذ مضمّنًا
```

الحارس هو كائن `Action<object>` ثابت يُستخدم فقط كقيمة علامة — لا يُستدعى فعليًا أبدًا كمندوب.

### التحقق من الرمز وأمان ABA

كل استدعاء لـ`GetStatus` و`GetResult` و`OnCompleted` يتحقق من صحة `uint token` مقابل `generation` الحالية. عندما تستدعي `Reset()` دالة `Interlocked.Increment(ref generation)`، فإن أي بنية `VlkTask` تحمل الرمز القديم ستتلقى `InvalidOperationException` بدلًا من العمل بصمت على حالة مُعاد تدويرها. يُعتبر التفاف عداد جيل 32 بت (يتطلب ~4 مليار إعادة استخدام لفتحة واحدة) مستحيلًا عمليًا.

### إعادة التعيين والإبلاغ عن الأخطاء غير الملاحظة

تُستدعى `Reset()` عند وقت إرجاع مجموعة الموارد. قبل زيادة الجيل، تفحص ما إذا كان خطأ قد خُزِّن لكن لم يُلاحَظ أبدًا (أي أن `GetResult` لم يُستدعَ بعد خطأ). إذا كان الأمر كذلك، تنشر الاستثناء عبر `VlkTask.UnobservedException`. تُبلَّغ أخطاء الإلغاء فقط إذا كانت `LogUnobservedCancellations` مُفعَّلة في `VlkTaskSettings`، لأن الإلغاء غالبًا مقصود.

بالنسبة لكائنات `Promise` و`Promise<T>` غير المُجمَّعة، يحدث الإبلاغ عن الأخطاء غير الملاحظة من المُنهي عبر `ReportUnobservedIfNeeded()`، الذي يتبع نفس المنطق دون مسح الحالة.

---

## تهيئة مجموعة الموارد

ثلاث إعدادات تتحكم في حجم مجموعة الموارد. في بنيات Unity تُقرأ من أصل `VlkTaskSettings` ScriptableObject (مع قيم افتراضية احتياطية)، ويمكن تجاوزها في وقت التشغيل عبر الخصائص الثابتة:

```csharp
// الحد الأقصى للكائنات لكل نوع مجموعة موارد (لكل TStateMachine أو نوع وعد)
VlkTask.DefaultMaxPoolSize = 256;   // الافتراضي: 256

// كم إطارًا بين فحوصات التقليص (إطارات Unity PlayerLoop)
VlkTask.TrimCheckInterval = 300;    // الافتراضي: 300 (~5 ثوانٍ عند 60fps)

// الحد الأدنى للكائنات المحتفظ بها بعد مرور تقليص
VlkTask.MinPoolSize = 8;            // الافتراضي: 8
```

`DefaultMaxPoolSize` هو السقف المُطبَّق عند إنشاء مجموعة الموارد. يُفرَض لكل نُسخة مجموعة موارد، وليس عالميًا.

### تقليص مجموعة الموارد

يستدعي `PlayerLoopHelper` دالة `PoolRegistry.TrimAll(minPoolSize)` كل `TrimCheckInterval` إطار في الخيط الرئيسي. كل مجموعة موارد تستخدم استراتيجية التخلف:

```
كل فحص تقليص:
  currentSize = fastItem.count + stackSize
  if currentSize <= MinPoolSize: أعد تعيين عدد التوالي، تخطَّ

  ratio = currentSize / totalCreated
  if ratio > 0.5 (مجموعة الموارد تحمل > 50% من جميع الكائنات المنشأة):
    trimConsecutiveCount++
    if trimConsecutiveCount >= hysteresisThreshold:
      أطلق بعض كسر (releaseRatio) من الكائنات الزائدة من المكدس
      (fastItem محفوظ — إنه أكثر الفتحات ملاءمة للكاش)
  else:
    أعد تعيين trimConsecutiveCount
```

التخلف يمنع ارتفاعًا مفاجئًا في الحركة من التسبب فورًا في تخصيص جميع الكائنات ثم تقليصها فورًا. تُحفظ الفتحة السريعة دائمًا أثناء التقليص لأنها تمثل العنصر الأحدث استخدامًا وبالتالي الأرجح أن يُحتاج إليه مجددًا.

---

## `PoolRegistry` والمراقبة

كل `VlkTaskPool<T>` تسجّل نفسها في `PoolRegistry` العالمي عند الإنشاء. يحتفظ السجل بقائمة من مراجع `IPoolInfo` التي تعرض:

```csharp
internal interface IPoolInfo
{
    Type PooledType { get; }
    int Size { get; }
    int MaxSize { get; }
    bool IsAlive { get; }
    int Trim(int minPoolSize);
}
```

يمكنك تعداد جميع مجموعات الموارد النشطة في وقت التشغيل باستخدام واجهة برمجة عامة:

```csharp
foreach (var (type, size, maxSize) in VlkTask.GetPoolInfo())
{
    Debug.Log($"{type.Name}: {size}/{maxSize}");
}
```

هذه هي نفس البيانات التي تعرضها نافذة **Task Tracker** في محرر Unity. تستطلع النافذة `GetPoolInfo()` وتعرض جدولًا حيًا بإشغال مجموعة الموارد، مما يتيح رؤية ما إذا كانت مجموعات الموارد دافئة، وما إذا كان أي نوع يصل باستمرار إلى سقفه، وما إذا كان التقليص يعمل كما هو متوقع.

تُزال إدخالات مجموعة الموارد الميتة (حيث تُرجع `IsAlive` قيمة false) بكسل من قائمة السجل أثناء استدعاءات `GetAll()` و`TrimAll()`، مما يمنع السجل من النمو إلى ما لا نهاية إذا أُزيلت نُسَخ مجموعة الموارد بواسطة جامع النفايات.

---

## `PooledPromise` و`PooledPromise<T>`

هذه مصادر اكتمال مُجمَّعة مخصصة للاستخدام في أنماط async المخصصة — على سبيل المثال، تغليف واجهة برمجة مبنية على الاستدعاءات أو قناة منتج/مستهلك متكررة.

```csharp
// اكتساب وعد معلق من مجموعة الموارد
var promise = VlkTask.PooledPromise<string>.Create(out uint token);
VlkTask<string> task = promise.Task;

// سلّم المهمة إلى مستهلك
// ... لاحقًا، من أي خيط ...
promise.TrySetResult("hello");

// عندما يُنتظِر المستهلك المهمة ويُستدعى GetResult،
// يُعيد الوعد تعيين نفسه ويعود إلى مجموعة الموارد تلقائيًا.
```

الخصائص الرئيسية:

- `Create(out uint token)` يستعير من مجموعة الموارد أو يُخصص نسخة جديدة تتتبعها مجموعة الموارد.
- `CreateCompleted(T result, out uint token)` يفعل نفس الشيء لكن يُشير فورًا بالنتيجة، لذا المهمة مكتملة بالفعل عند إرجاعها.
- بعد استدعاء `GetResult` على المهمة الدعم، تُطلَق `TryReturn()`: الوعد يستدعي `core.Reset()` ويُرجع نفسه إلى مجموعة الموارد.
- حارس ضد الإرجاع المزدوج (`Interlocked.Exchange(ref returned, 1)`) يمنع تلف مجموعة الموارد إذا استُدعي `GetResult` مرتين.

**البديل غير المُجمَّع: `Promise` و`Promise<T>`.** هذه فئات مُخصصة في الكومة لا تُرجَع إلى مجموعة موارد. استخدمها للعمليات طويلة الأمد حيث العمر غير متوقع أو حيث يجب أن يتجاوز المصدر دورات انتظار متعددة. تعتمد على مُنهٍ للإبلاغ عن الاستثناءات غير الملاحظة.

---

## مجموعات موارد المُجمِّعات

تستخدم مُجمِّعات `WhenAll` و`WhenAny` أيضًا مجموعات الموارد. كل تركيبة من الأرقام والأنواع لها مجموعة موارد خاصة بها:

| المُجمِّع | نوع مجموعة الموارد |
|-----------|-----------|
| `WhenAll(task1, task2)` (مكتوب) | `VlkTaskPool<WhenAllPromise<T1, T2>>` |
| `WhenAll(IEnumerable<VlkTask<T>>)` | `VlkTaskPool<WhenAllArrayPromise<T>>` |
| `WhenAll(task1, task2)` (void) | `VlkTaskPool<WhenAllVoidPromise2>` |
| `WhenAny(task1, task2)` (مكتوب) | `VlkTaskPool<WhenAnyPromise2<T>>` |
| `WhenAny(IEnumerable<VlkTask<T>>)` | `VlkTaskPool<WhenAnyArrayPromise<T>>` |

المُجمِّعات المبنية على المصفوفات (`WhenAll<T>(IEnumerable<...>)` و`WhenAny<T>(IEnumerable<...>)`) تستخدم `System.Buffers.ArrayPool<T>.Shared` لمصفوفات المصادر/الرموز الداخلية، لذا تُعاد تدوير تلك المصفوفات أيضًا بدلًا من تخصيصها في كل استدعاء.

جميع المُجمِّعات تطبّق نفس الاختصار الخالي من التخصيص: إذا كانت جميع المدخلات مكتملة بشكل متزامن عند استدعاء `WhenAll` أو `WhenAny`، لا يُنشأ أي كائن مُجمَّع جديد.

```csharp
// صفر تخصيص — كلتا المهمتين مكتملتان متزامنًا
var t1 = VlkTask.FromResult(1);
var t2 = VlkTask.FromResult(2);
VlkTask<(int, int)> combined = VlkTask.WhenAll(t1, t2);
// combined.source == null؛ النتيجة هي (1, 2) مخزّنة مضمّنةً
```
