---
sidebar_position: 2
title: VlkTask<T>
---

# `VlkTask<T>`

`VlkTask<T>` هو نوع المهمة غير المتزامنة الذي يُرجع قيمة في Valkarn Tasks. إنه `readonly struct`، يحمل إما نتيجة مضمّنة (عندما يكتمل بشكل متزامن) أو مرجعًا لكائن مصدر مُجمَّع (عندما يكتمل بشكل غير متزامن).

**مساحة الأسماء:** `UnaPartidaMas.Valkarn.Tasks`

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncVlkTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct VlkTask<T>
```

لا توجد قيود جنيريكية على `T`. أي نوع — نوع قيمة، نوع مرجعي، بنية، أو فئة — صالح.

---

## إنشاء النُّسَخ

### المهام المكتملة بشكل متزامن

هذه طرق المصنع تُرجع `VlkTask<T>` بدون كائن مصدر دعم. صفر تخصيص.

#### `VlkTask.FromResult<T>(T value)`

يُرجع `VlkTask<T>` مكتملًا يحمل `value` مضمّنةً. مُعلَّن كطريقة ثابتة على نوع `VlkTask` غير الجنيريكي.

```csharp
public static VlkTask<T> FromResult<T>(T value)
```

```csharp
VlkTask<int> task = VlkTask.FromResult(42);
VlkTask<string> name = VlkTask.FromResult("Valkarn");
VlkTask<Vector3> pos = VlkTask.FromResult(transform.position);
```

البنية المُرجَعة لها `source == null`. انتظارها لا يتكبّد تخصيص استمرار — يرى المُترجم `IsCompleted == true` فورًا.

#### `VlkTask.FromException<T>(Exception exception)`

يُرجع `VlkTask<T>` معطوبًا. انتظاره يُعيد رمي الاستثناء مع حفظ تتبع المكدس الأصلي عبر `ExceptionDispatchInfo`.

```csharp
public static VlkTask<T> FromException<T>(Exception exception)
```

```csharp
VlkTask<Texture2D> LoadTexture(string path)
{
    if (string.IsNullOrEmpty(path))
        return VlkTask.FromException<Texture2D>(
            new ArgumentException("يجب ألا يكون المسار فارغًا.", nameof(path)));

    return LoadTextureAsync(path);
}
```

#### `VlkTask.FromCanceled<T>(CancellationToken ct = default)`

يُرجع `VlkTask<T>` مُلغى. انتظاره يرمي `OperationCanceledException`.

```csharp
public static VlkTask<T> FromCanceled<T>(CancellationToken ct = default)
```

```csharp
VlkTask<byte[]> Download(string url, CancellationToken ct)
{
    if (ct.IsCancellationRequested)
        return VlkTask.FromCanceled<byte[]>(ct);

    return DownloadAsync(url, ct);
}
```

### عبر طرق `async`

أي طريقة `async` مُعلَّنة لإرجاع `VlkTask<T>` تستخدم `AsyncVlkTaskMethodBuilder<TResult>` تلقائيًا:

```csharp
async VlkTask<int> ComputeAsync()
{
    await VlkTask.Yield();
    return 42;
}
```

يُولّد المُترجم آلة حالة. إذا اكتملت الطريقة بشكل متزامن (لا تتوقف أبدًا)، تُرجع `AsyncVlkTaskMethodBuilder<T>.Task` القيمة `new VlkTask<T>(result)` مع `source == null` — صفر تخصيص.

---

## تشغيل العمل على مجموعة الخيوط

هذه الطرق تُشغّل مندوبًا على مجموعة خيوط .NET وتُرجع النتيجة على الخيط الرئيسي (في `PlayerLoopTiming` المحدد). إنها غلافات مريحة فوق نوعيات `RunOnThreadPool` الأطول اسمًا.

#### `VlkTask.Run<T>(Func<T> func, PlayerLoopTiming timing, CancellationToken ct)`

يُشغّل `Func<T>` متزامنًا على مجموعة الخيوط.

```csharp
public static VlkTask<T> Run<T>(
    Func<T> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
// الحساب على مجموعة الخيوط، تصل النتيجة إلى الخيط الرئيسي في Update التالي
int hash = await VlkTask.Run(() => ComputeExpensiveHash(data));
```

#### `VlkTask.Run<T>(Func<VlkTask<T>> func, PlayerLoopTiming timing, CancellationToken ct)`

يُشغّل `Func<VlkTask<T>>` غير متزامن على مجموعة الخيوط. استخدم هذا عندما يكون العمل نفسه غير متزامن (مثل I/O الملفات).

```csharp
public static VlkTask<T> Run<T>(
    Func<VlkTask<T>> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
string json = await VlkTask.Run(async () =>
{
    using var reader = File.OpenText("data.json");
    return await reader.ReadToEndAsync();
});
```

كلا تحميلَي `Run` يلغيان مبكرًا إذا كان الرمز مُلغى بالفعل، ويعودان إلى الخيط الرئيسي في `timing` بعد اكتمال العمل.

---

## الأعضاء النسخية

### `IsCompleted`

```csharp
public bool IsCompleted { get; }
```

يُرجع `true` إذا اكتملت المهمة في أي حالة نهائية (نجاح أو خطأ أو إلغاء). بالنسبة للمهام المكتملة بشكل متزامن (`source == null`)، تُرجع دائمًا `true` بدون إرسال واجهة.

```csharp
var task = SomeLongOperation();
if (task.IsCompleted)
{
    int result = task.GetAwaiter().GetResult();
    Use(result);
}
```

### `GetStatus()`

```csharp
public VlkTask.Status GetStatus()
```

يُرجع `VlkTask.Status` الحالية. القيم الممكنة: `Pending`، `Succeeded`، `Faulted`، `Canceled`. بالنسبة لـ`source == null`، تُرجع دائمًا `Succeeded`.

### `GetAwaiter()`

```csharp
public Awaiter GetAwaiter()
```

يُرجع بنية `Awaiter`. تستخدمها المُترجم لتطبيق `await`. يمكنك أيضًا استدعاؤها مباشرةً للحصول على النتيجة بشكل متزامن (آمن فقط عندما تكون `IsCompleted` صحيحة).

```csharp
VlkTask<int> task = VlkTask.FromResult(10);
int value = task.GetAwaiter().GetResult(); // آمن — مكتملة متزامنًا
```

استدعاء `GetResult()` على مهمة معلقة يرمي `InvalidOperationException`.

### `AsNonGeneric()`

```csharp
public VlkTask AsNonGeneric()
```

يُحوّل هذا `VlkTask<T>` إلى `VlkTask` غير جنيريكي، متجاهلًا نوع النتيجة. المهمة الناتجة تشترك في نفس المصدر الأساسي والرمز، لذا تكتمل في نفس الوقت.

```csharp
VlkTask<int> typedTask = ComputeAsync();
VlkTask voidTask = typedTask.AsNonGeneric();
await voidTask;   // ينتظر الاكتمال، يتجاهل النتيجة
```

هذا مفيد عند تمرير مهام مختلطة الأنواع إلى المُجمِّعات أو عندما تهتم فقط بتوقيت الاكتمال وليس القيمة.

---

## المُجمِّعات التي تُرجع `VlkTask<T>`

### `WhenAll` — التحميل الزائد بمهمتين مكتوبتين

```csharp
public static VlkTask<(T1, T2)> WhenAll<T1, T2>(
    VlkTask<T1> task1, VlkTask<T2> task2)
```

يُشغّل كلتا المهمتين بالتوازي ويُرجع صفًا من النتائج. إذا أخفقت أو أُلغيت أي مهمة، تفوز الاستثناء الأول وتُبلَّغ أخطاء الأخرى عبر `VlkTask.UnobservedException`.

**تفكيك الصفوف** يعمل بشكل طبيعي مع تفكيك C#:

```csharp
var (profile, inventory) = await VlkTask.WhenAll(
    FetchProfileAsync(userId),
    FetchInventoryAsync(userId)
);
```

**المسار السريع الخالي من التخصيص:** إذا كانت كلتا المهمتين مكتملتين بشكل متزامن عند موقع الاستدعاء، لا يُنشأ أي كائن مُجمَّع وتُرجع صف النتائج مضمّنًا.

### `WhenAll` — التحميل الزائد بمجموعة مكتوبة

```csharp
public static VlkTask<T[]> WhenAll<T>(IEnumerable<VlkTask<T>> tasks)
```

ينتظر جميع المهام في المجموعة بالتوازي ويُرجع `T[]` بترتيب الفهرس.

```csharp
var urls = new[] { "https://a.com", "https://b.com", "https://c.com" };
VlkTask<string>[] downloads = urls.Select(u => DownloadAsync(u)).ToArray();
string[] results = await VlkTask.WhenAll(downloads);
```

إذا كانت المجموعة فارغة، يُرجع `VlkTask.FromResult(Array.Empty<T>())` — صفر تخصيص. إذا كانت جميع المهام مكتملة بشكل متزامن بالفعل، يُبنى مصفوفة النتائج مضمّنةً دون إنشاء وعد مُجمِّع.

### `WhenAny` — التحميل الزائد بمهمتين مكتوبتين

```csharp
public static VlkTask<(int winnerIndex, T result)> WhenAny<T>(
    VlkTask<T> task1, VlkTask<T> task2)
```

يعود فور اكتمال أي من المهمتين. صف النتائج يحتوي على فهرس الفائز (يبدأ من 0) وقيمته. المهام الخاسرة تستمر في التشغيل؛ أخطاؤها (إن وُجدت) تُبلَّغ عبر `VlkTask.UnobservedException`. إلغاء الخاسرين لا يُبلَّغ عنه عمدًا.

```csharp
var (winnerIndex, result) = await VlkTask.WhenAny(
    FetchFromCacheAsync(key),
    FetchFromNetworkAsync(key)
);

if (winnerIndex == 0)
    Debug.Log("فازت الكاش");
```

### `WhenAny` — التحميل الزائد بمجموعة مكتوبة

```csharp
public static VlkTask<(int winnerIndex, T result)> WhenAny<T>(
    IEnumerable<VlkTask<T>> tasks)
```

نفس دلالات التحميل الزائد بمهمتين، مُوسَّعة لأي عدد من المهام. يتطلب مهمة واحدة على الأقل؛ يرمي `ArgumentException` للمجموعات الفارغة.

```csharp
var tasks = servers.Select(s => s.FetchAsync(query)).ToArray();
var (winnerIndex, data) = await VlkTask.WhenAny(tasks);
Debug.Log($"الخادم {winnerIndex} استجاب أولًا");
```

---

## طرق المصنع المريحة

### `VlkTask.Create<T>(Func<VlkTask<T>> factory)`

يستدعي مندوب المصنع ويُنتظر المهمة الناتجة. مفيد عندما تريد تأجيل بناء عملية غير متزامنة.

```csharp
public static async VlkTask<T> Create<T>(Func<VlkTask<T>> factory)
```

```csharp
var result = await VlkTask.Create(() => LoadLevelDataAsync(levelId));
```

---

## بنية `Awaiter` (متداخلة)

`VlkTask<T>.Awaiter` هو المُنتظِر للمُترجم. إنه `readonly struct` يُطبّق `ICriticalNotifyCompletion`. عادةً لا تتفاعل معه مباشرةً.

```csharp
public readonly struct Awaiter : ICriticalNotifyCompletion
{
    public bool IsCompleted { get; }
    public T GetResult();
    public void OnCompleted(Action continuation);
    public void UnsafeOnCompleted(Action continuation);
}
```

`UnsafeOnCompleted` هو المسار الذي يستخدمه `AsyncVlkTaskMethodBuilder<T>`. تسمية "unsafe" تعني عدم التقاط `ExecutionContext` — هذا مقصود لـ Unity حيث لا يوجد `SynchronizationContext`.

عندما تكون `IsCompleted` صحيحة، استدعاء `GetResult()` يقرأ حقل `result` المضمّن (للمهام المتزامنة) أو يستدعي عبر واجهة المصدر (للمهام الغير متزامنة). كلا المسارين مُعلَّمان بـ`[MethodImpl(MethodImplOptions.AggressiveInlining)]`.

---

## طرق الامتداد على `VlkTask<T>`

### `AsResult<T>()`

```csharp
public static VlkTask<Result<T>> AsResult<T>(this VlkTask<T> task)
```

يُغلّف `VlkTask<T>` في `VlkTask<Result<T>>`، مُلتقطًا أي استثناء أو إلغاء وترميزه في قيمة `Result<T>`. هذا يتجنب try/catch في موقع الاستدعاء.

```csharp
Result<string> result = await FetchDataAsync(url).AsResult();

if (result.IsSuccess)
    Process(result.Value);
else if (result.IsCanceled)
    Debug.Log("مُلغى");
else
    Debug.LogError(result.Exception);
```

**المسار السريع المتزامن:** إذا كانت مهمة المصدر مكتملة بشكل متزامن بالفعل، تُرجع `AsResult` بشكل متزامن بدون آلية async.

---

## `Promise<T>` — مصدر اكتمال يدوي

`VlkTask.Promise<T>` هو مصدر اكتمال يدوي مُخصص في الكومة للحالات التي تحتاج فيها للتحكم في متى يكتمل `VlkTask<T>`، وعمره غير محدود بعملية غير متزامنة واحدة.

```csharp
public class Promise<T>
{
    public VlkTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

```csharp
// تغليف واجهة برمجة مبنية على الاستدعاءات
var promise = new VlkTask.Promise<string>();

SomeCallbackApi.OnComplete += value => promise.TrySetResult(value);
SomeCallbackApi.OnError   += ex    => promise.TrySetException(ex);

string result = await promise.Task;
```

على عكس `PooledPromise<T>`، لا يُجمَّع `Promise<T>`. يستخدم مُنهيًا لاكتشاف والإبلاغ عن الاستثناءات غير الملاحظة إذا أخفقت المهمة ولم ينتظرها المُستدعي.

للأنماط ذات التردد العالي (حلقات المنتج/المستهلك، العمليات لكل إطار)، فضّل `VlkTask.PooledPromise<T>` الذي يعود تلقائيًا إلى مجموعة الموارد بعد استدعاء `GetResult`.

---

## `PooledPromise<T>` — مصدر اكتمال يدوي مُجمَّع

```csharp
public sealed class PooledPromise<T> : VlkTask.ISource<T>, IPoolNode<PooledPromise<T>>
{
    public static PooledPromise<T> Create(out uint token);
    public static PooledPromise<T> CreateCompleted(T result, out uint token);
    public VlkTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

بعد استدعاء `GetResult` على المهمة الدعم، يُعيد الوعد تعيين `VlkTaskCompletionCore<T>` ويُرجع نفسه إلى مجموعة الموارد. حارس ضد الإرجاع المزدوج يضمن حدوث ذلك مرة واحدة فقط حتى إذا استُدعي `GetResult` بشكل متزامن.

```csharp
// النمط: إنتاج VlkTask<T> يكتمل عند الاستعداد
var promise = VlkTask.PooledPromise<int>.Create(out uint token);
VlkTask<int> task = promise.Task;

// إرسال العمل بشكل غير متزامن
ThreadPool.QueueUserWorkItem(_ =>
{
    int result = DoWork();
    promise.TrySetResult(result);
});

// ينتظر المستهلك؛ عند الاكتمال يعود الوعد إلى مجموعة الموارد تلقائيًا
int value = await task;
```

---

## ملخص طرق الحصول على `VlkTask<T>`

| الطريقة | متى تستخدمها |
|--------|------------|
| `return value` داخل `async VlkTask<T>` | الطرق الغير متزامنة العادية |
| `VlkTask.FromResult(value)` | الإرجاعات السريعة المتزامنة |
| `VlkTask.FromException<T>(ex)` | المهام المعطوبة مسبقًا |
| `VlkTask.FromCanceled<T>(ct)` | المهام المُلغاة مسبقًا |
| `VlkTask.Run<T>(Func<T>, ...)` | إفراغ مجموعة الخيوط |
| `VlkTask.Run<T>(Func<VlkTask<T>>, ...)` | عمل مجموعة الخيوط الغير متزامن |
| `VlkTask.WhenAll<T1,T2>(t1, t2)` | انتظار مهمتين مكتوبتين، الحصول على صف |
| `VlkTask.WhenAll<T>(IEnumerable<...>)` | انتظار N مهمة مكتوبة، الحصول على مصفوفة |
| `VlkTask.WhenAny<T>(t1, t2)` | أولى مهمتين مكتوبتين |
| `VlkTask.WhenAny<T>(IEnumerable<...>)` | أولى N مهمة مكتوبة |
| `task.AsResult<T>()` | تغليف آمن من الاستثناءات |
| `new VlkTask.Promise<T>()` → `.Task` | اكتمال يدوي طويل الأمد |
| `VlkTask.PooledPromise<T>.Create(...)` → `.Task` | اكتمال يدوي عالي التردد |
