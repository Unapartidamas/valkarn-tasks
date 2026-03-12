---
sidebar_position: 2
title: القنوات
---

# القنوات

توفر القنوات خط أنابيب آمن للخيوط وقادر على العمل الغير متزامن لتمرير البيانات بين المنتجين والمستهلكين. إنها مناسبة بشكل خاص لأنظمة الألعاب حيث يُولَّد العمل من جانب (خيوط الخلفية، استدعاءات الأحداث، اكتمال المهام) ويحتاج للاستهلاك من جانب آخر (الخيط الرئيسي، مجموعات العمال).

## إنشاء قناة

تُنشأ القنوات عبر فئة المصنع الثابتة `VlkTask.Channel`. لا يوجد منشئ عام.

### قناة غير محدودة

```csharp
Channel<T> channel = VlkTask.Channel.CreateUnbounded<T>(bool multiConsumer = false);
```

القناة غير المحدودة لا حد لسعتها. `WriteAsync` و`TryWrite` تنجحان دائمًا فورًا طالما لم تُكتمل القناة. تتراكم العناصر في `Queue<T>` داخلي حتى يقرأها مستهلك.

**المعاملات**

| المعامل | الافتراضي | الوصف |
|-----------|---------|-------------|
| `multiConsumer` | `false` | عند `true`، تُدعم استدعاءات `ReadAsync` المتزامنة المتعددة (مستهلكون متنافسون). عند `false`، يمكن لقارئ واحد فقط انتظار `ReadAsync` في وقت واحد. |

### قناة محدودة

```csharp
Channel<T> channel = VlkTask.Channel.CreateBounded<T>(int capacity, bool multiConsumer = false);
```

القناة المحدودة تحمل على الأكثر `capacity` عنصرًا في مخزن مؤقت حلقي ذي حجم ثابت. عندما يمتلئ المخزن، `WriteAsync` تعلّق الكود المُستدعِي بشكل غير متزامن حتى يتوفر مكان (ضغط عكسي). `TryWrite` تُرجع `false` فورًا عند الامتلاء بدلًا من الانتظار.

**المعاملات**

| المعامل | الافتراضي | الوصف |
|-----------|---------|-------------|
| `capacity` | مطلوب | الحد الأقصى للعناصر في المخزن المؤقت. يجب أن يكون أكبر من صفر. |
| `multiConsumer` | `false` | نفسه لغير المحدود — يُفعّل قراء متزامنين متعددين. |

**الاختيار بين المحدود وغير المحدود**

استخدم `CreateBounded` عندما تحتاج لتطبيق ضغط عكسي — أي عندما تريد أن يتباطأ المنتجون تلقائيًا إذا تأخر المستهلكون. استخدم `CreateUnbounded` عندما يكون معدل المنتج محدودًا بطبيعته (كأحداث الإدخال) وعدم الحد مقبول، أو عندما تكون قد أخذت في الاعتبار بالفعل نمو الذاكرة.

---

## Channel&lt;T&gt;

`Channel<T>` حاوية تكشف جانبَي خط الأنابيب ككائنات منفصلة.

```csharp
public sealed class Channel<T>
{
    public ChannelReader<T> Reader { get; }
    public ChannelWriter<T> Writer { get; }
}
```

احتفظ بمرجع `Writer` على جانب المنتج ومرجع `Reader` على جانب المستهلك. لا يُشترط أن يكونا على نفس الخيط.

---

## ChannelWriter&lt;T&gt;

`ChannelWriter<T>` هو جانب الكتابة من القناة. احصل عليه من `channel.Writer`.

### TryWrite

```csharp
public abstract bool TryWrite(T item);
```

يحاول كتابة عنصر بدون تعليق. يُرجع `true` إذا قُبل العنصر؛ يُرجع `false` إذا كانت القناة ممتلئة (محدودة) أو مكتملة.

استخدم `TryWrite` في المسارات الحارة حيث يمكنك تجاهل العناصر، أو عند الاستطلاع في حلقة وتريد تجنب تخصيص آلة الحالة الغير متزامنة.

```csharp
if (!channel.Writer.TryWrite(item))
{
    // القناة ممتلئة أو مغلقة — تعامل مع ذلك بشكل مناسب
}
```

### WriteAsync

```csharp
public abstract VlkTask WriteAsync(T item);
```

يكتب عنصرًا في القناة، مُعلِّقًا المُستدعي بشكل غير متزامن إذا لزم الأمر.

- **القنوات غير المحدودة**: تكتمل دائمًا بشكل متزامن (مسار سريع خالٍ من التخصيص) طالما القناة مفتوحة.
- **القنوات المحدودة**: تكتمل بشكل متزامن عندما يوجد مكان في المخزن؛ تُعلّق المُستدعي وتُضيف سجل كاتب معلق عندما يمتلئ المخزن. يستأنف المُستدعي بمجرد قراءة المستهلك لعنصر وتحرير مكان.

يرمي `ChannelClosedException` إذا اكتملت القناة عبر `Complete()` قبل أو أثناء الكتابة.

```csharp
await channel.Writer.WriteAsync(item);
```

يمكن لعدة منتجين استدعاء `WriteAsync` بشكل متزامن على قناة محدودة. كل كاتب مُعلَّق يُضاف إلى طابور ويُلغى حجبه بترتيب FIFO عند توفر مكان.

### Complete

```csharp
public abstract void Complete();
```

يُشير إلى أنه لن تُكتب المزيد من العناصر. بعد استدعاء `Complete()`:

- أي عناصر مكتوبة بالفعل تبقى في المخزن ويمكن استهلاكها.
- الاستدعاءات الجديدة لـ`WriteAsync` أو `TryWrite` ستفشل بـ`ChannelClosedException`.
- بمجرد تصريف المخزن بالكامل، يكتمل `ChannelReader<T>.Completion` وأي استدعاءات `ReadAsync` معلقة أو مستقبلية ترمي `ChannelClosedException`.

`Complete()` فائق الأثر في استدعاء واحد — استدعاؤه أكثر من مرة آمن (الاستدعاءات اللاحقة تُتجاهل).

```csharp
// الإشارة بنهاية العمل
channel.Writer.Complete();
```

---

## ChannelReader&lt;T&gt;

`ChannelReader<T>` هو جانب القراءة من القناة. احصل عليه من `channel.Reader`.

### ReadAsync

```csharp
public abstract VlkTask<T> ReadAsync();
```

يقرأ العنصر التالي من القناة. إذا لم يكن أي عنصر متاحًا حاليًا، يُعلَّق المُستدعي بشكل غير متزامن حتى يصل عنصر. عندما تكون القناة مكتملة ومُصرَّفة بالكامل، يرمي `ChannelClosedException`.

```csharp
T item = await channel.Reader.ReadAsync();
```

**وضع المستهلك الواحد** (الافتراضي): يمكن لاستدعاء `ReadAsync` واحد فقط أن يكون قيد التشغيل في وقت واحد. محاولة بدء ثانٍ متزامن ترمي فورًا. هذا القيد يُمكّن تحسينًا خاليًا من التخصيص داخليًا — يُضمَّن نواة القارئ مباشرةً في تطبيق القناة بدلًا من تخصيصها من مجموعة موارد لكل استدعاء.

**وضع متعدد المستهلكين** (`multiConsumer: true`): يمكن لأي عدد من استدعاءات `ReadAsync` أن تكون معلقة في نفس الوقت. كل مُستدعٍ معلق يُضاف إلى طابور ويُحل بترتيب FIFO عند توفر العناصر.

### TryRead

```csharp
public abstract bool TryRead(out T item);
```

يحاول قراءة عنصر بدون تعليق. يُرجع `true` ويُملئ `item` إذا كان عنصر متاحًا؛ يُرجع `false` إذا كانت القناة فارغة (يضبط `item` على `default`).

`TryRead` لا يُميّز بين قناة فارغة-لكن-مفتوحة وقناة فارغة-ومغلقة. استخدم `Completion` لاكتشاف الحالة المغلقة عند استخدام `TryRead` في حلقة استطلاع.

### ReadAllAsync

```csharp
public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default);
```

يُرجع `IAsyncEnumerable<T>` يتكرر على جميع العناصر حتى تكتمل القناة وتُصرَّف. ينتهي التعداد بنظافة دون نشر `ChannelClosedException` — يتوقف ببساطة.

```csharp
await foreach (var item in channel.Reader.ReadAllAsync(ct))
{
    Process(item);
}
// وصلنا هنا عندما اكتملت القناة وأصبحت فارغة
```

رمز الإلغاء المُمرَّر لـ`ReadAllAsync` يُستخدم كاحتياطي. إذا مُرِّر أيضًا رمز إلى `GetAsyncEnumerator` (كما يفعل `await foreach` عبر `WithCancellation`)، يسود رمز جانب `foreach`.

### Completion

```csharp
public abstract VlkTask Completion { get; }
```

`VlkTask` تكتمل عندما تُصرَّف القناة بالكامل بعد استدعاء `Complete()`. تحديدًا:

- إذا استُدعيت `Complete()` على قناة فارغة بالفعل، تُحل `Completion` فورًا.
- إذا استُدعيت `Complete()` بينما توجد عناصر في المخزن، تُحل `Completion` فقط بعد استهلاك آخر عنصر.

انتظار `Completion` هو الطريقة القياسية لانتظار انتهاء خط أنابيب.

```csharp
channel.Writer.Complete();
await channel.Reader.Completion;
// جميع العناصر استُهلكت
```

---

## ChannelClosedException

```csharp
public sealed class ChannelClosedException : InvalidOperationException
```

تُرمى في حالتين:

1. **القراءة من قناة مكتملة ومُصرَّفة** — `ReadAsync()` ترمي عندما اكتملت القناة ولا توجد عناصر متبقية.
2. **الكتابة في قناة مكتملة** — `WriteAsync()` ترمي عندما استُدعيت `Complete()` قبل الكتابة.

`ChannelClosedException` ترث من `InvalidOperationException`. لا تُرمى بـ`TryRead` أو `TryWrite`، اللذان يُرجعان `false` بدلًا من ذلك.

المُنشئات:

```csharp
new ChannelClosedException()
new ChannelClosedException(string message)
new ChannelClosedException(Exception innerException)
```

---

## القناة المحدودة: الضغط العكسي بالتفصيل

عندما يمتلئ مخزن القناة المحدودة ويُستدعى `WriteAsync`، يُعلَّق الكاتب ويُدرج سجل كاتب معلق داخليًا. الكاتب يحتفظ بعنصره. عندما يستدعي مستهلك `ReadAsync` أو `TryRead` ويُخرج عنصرًا:

1. يُطالَب فورًا بالمكان المحرَّر من قِبل أقدم كاتب معلق.
2. عنصر ذلك الكاتب يُوضع في المخزن.
3. كود الانتظار للكاتب يستأنف.

هذا يعني أن القناة المحدودة الممتلئة لا تفقد عناصر ولا تُهدر سعة المخزن — هناك دائمًا تطابق واحد-لواحد بين تحرر مكان واستئناف كاتب محجوب. في الحالات التي يصل فيها قارئ بينما كتّاب معلقون ينتظرون لكن المخزن فارغ، يُسلَّم العنصر مباشرةً دون لمس المخزن على الإطلاق.

---

## الأنماط

### منتج/مستهلك أساسي

```csharp
var channel = VlkTask.Channel.CreateUnbounded<WorkItem>();

// المنتج (مثلًا يعمل على خيط خلفية أو من استدعاءات)
async VlkTask ProduceAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var work = await FetchNextWorkItemAsync(ct);
        await channel.Writer.WriteAsync(work);
    }
    channel.Writer.Complete();
}

// المستهلك (يعمل أينما تختار استدعاءه)
async VlkTask ConsumeAsync()
{
    await foreach (var item in channel.Reader.ReadAllAsync())
    {
        await ProcessAsync(item);
    }
}
```

### منتجون متعددون، مستهلك واحد

يمكن لعدة منتجين كل منهم يحمل مرجعًا لـ`channel.Writer` واستدعاء `WriteAsync` بشكل متزامن. جميع عمليات القناة محمية بقفل داخلي، لذا هذا آمن.

```csharp
var channel = VlkTask.Channel.CreateBounded<Event>(capacity: 64);

// عدة منتجين يكتبون بشكل متزامن
VlkTask ProducerA() => ProduceFrom(sourceA, channel.Writer);
VlkTask ProducerB() => ProduceFrom(sourceB, channel.Writer);
VlkTask ProducerC() => ProduceFrom(sourceC, channel.Writer);

// مستهلك واحد (افتراضي — لا حاجة لعلم multiConsumer)
async VlkTask ConsumerAsync()
{
    await foreach (var ev in channel.Reader.ReadAllAsync())
        HandleEvent(ev);
}
```

عند استخدام منتجين متعددين مع قناة محدودة، نسّق `Complete()` بعناية — استدعِها فقط بعد انتهاء جميع المنتجين من الكتابة، وإلا قد يتلقى بعض الكتّاب `ChannelClosedException`.

### منتجون متعددون، مستهلكون متعددون

```csharp
// multiConsumer: true يُفعّل ReadAsync المتزامن من عدة مستهلكين
var channel = VlkTask.Channel.CreateUnbounded<Job>(multiConsumer: true);

async VlkTask WorkerAsync(int id, CancellationToken ct)
{
    try
    {
        while (true)
        {
            var job = await channel.Reader.ReadAsync();
            await ExecuteJobAsync(job, ct);
        }
    }
    catch (ChannelClosedException)
    {
        // القناة منتهية — اخرج بأناقة
    }
}
```

يُسلَّم كل عنصر لمستهلك واحد بالضبط. يتنافس المستهلكون على العناصر بترتيب FIFO (المستهلك الذي ينتظر أطول يحصل على العنصر التالي المتاح).

### الإغلاق الأنيق

تسلسل الإغلاق الموصى به هو:

1. أشِر للمنتجين بالإيقاف (مثلًا، ألغِ `CancellationToken` الخاص بهم).
2. استدعِ `channel.Writer.Complete()` بعد توقف جميع المنتجين عن الكتابة.
3. انتظر `channel.Reader.Completion` للتأكد من استهلاك جميع العناصر.

```csharp
cts.Cancel();                        // أوقف المنتجين
await allProducersTask;              // انتظر خروجهم
channel.Writer.Complete();           // أغلق القناة
await channel.Reader.Completion;     // صرّف العناصر المتبقية
```

إذا كنت تستخدم `ReadAllAsync`، تحدث الخطوة 3 تلقائيًا — حلقة `await foreach` تخرج عندما تكتمل القناة وتفرغ.

---

## المقارنة مع System.Threading.Channels

| الميزة | قنوات Valkarn | System.Threading.Channels |
|---------|-----------------|---------------------------|
| نوع الإرجاع | `VlkTask` / `VlkTask<T>` | `ValueTask` / `ValueTask<T>` |
| التخصيص (المسار الحار) | صفر (غير محدود أحادي المستهلك) | قريب من الصفر |
| `WaitToReadAsync` | غير موجود — استخدم `ReadAsync` أو `ReadAllAsync` | موجود |
| `TryComplete(Exception)` | غير موجود — استخدم `Complete()` | موجود |
| `Count` / `CanCount` | غير مكشوف | موجود في بعض أنواع القنوات |
| سياسة التجاهل عند الامتلاء | غير مدعوم — `WriteAsync` يُعلَّق | `DropWrite`، `DropNewest`، `DropOldest`، `Wait` |
| تعداد غير متزامن | `ReadAllAsync()` | `ReadAllAsync()` |
| أمان الخيوط | كامل (مبني على قفل) | كامل (مبني على قفل) |

الفرق الرئيسي هو أن قنوات Valkarn تتكامل بشكل أصلي مع `VlkTask` لانتظار بلا تكلفة في بنيات Unity، ومسار المستهلك الواحد يتجنب استعارة/إرجاع مجموعة موارد على كل استدعاء `ReadAsync` بتضمين نواة الاكتمال مباشرةً في القناة.
