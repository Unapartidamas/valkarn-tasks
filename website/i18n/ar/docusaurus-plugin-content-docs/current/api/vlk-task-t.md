---
sidebar_position: 2
title: ValkarnTask<T>
---

# `ValkarnTask<T>`

`ValkarnTask<T>` هو نوع المهمة غير المتزامنة الذي يُرجع قيمة في Valkarn Tasks. إنه `readonly struct`، يحمل إما نتيجة مضمّنة (عندما يكتمل بشكل متزامن) أو مرجعًا لكائن مصدر مُجمَّع (عندما يكتمل بشكل غير متزامن).

**مساحة الأسماء:** `UnaPartidaMas.Valkarn.Tasks`

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncValkarnTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
```

لا توجد قيود جنيريكية على `T`. أي نوع — نوع قيمة، نوع مرجعي، بنية، أو فئة — صالح.

---

## إنشاء النُّسَخ

### المهام المكتملة بشكل متزامن

هذه طرق المصنع تُرجع `ValkarnTask<T>` بدون كائن مصدر دعم. صفر تخصيص.

#### `ValkarnTask.FromResult<T>(T value)`

يُرجع `ValkarnTask<T>` مكتملًا يحمل `value` مضمّنةً. مُعلَّن كطريقة ثابتة على نوع `ValkarnTask` غير الجنيريكي.

```csharp
public static ValkarnTask<T> FromResult<T>(T value)
```

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(42);
ValkarnTask<string> name = ValkarnTask.FromResult("Valkarn");
ValkarnTask<Vector3> pos = ValkarnTask.FromResult(transform.position);
```

البنية المُرجَعة لها `source == null`. انتظارها لا يتكبّد تخصيص استمرار — يرى المُترجم `IsCompleted == true` فورًا.

#### `ValkarnTask.FromException<T>(Exception exception)`

يُرجع `ValkarnTask<T>` معطوبًا. انتظاره يُعيد رمي الاستثناء مع حفظ تتبع المكدس الأصلي عبر `ExceptionDispatchInfo`.

```csharp
public static ValkarnTask<T> FromException<T>(Exception exception)
```

```csharp
ValkarnTask<Texture2D> LoadTexture(string path)
{
    if (string.IsNullOrEmpty(path))
        return ValkarnTask.FromException<Texture2D>(
            new ArgumentException("يجب ألا يكون المسار فارغًا.", nameof(path)));

    return LoadTextureAsync(path);
}
```

#### `ValkarnTask.FromCanceled<T>(CancellationToken ct = default)`

يُرجع `ValkarnTask<T>` مُلغى. انتظاره يرمي `OperationCanceledException`.

```csharp
public static ValkarnTask<T> FromCanceled<T>(CancellationToken ct = default)
```

```csharp
ValkarnTask<byte[]> Download(string url, CancellationToken ct)
{
    if (ct.IsCancellationRequested)
        return ValkarnTask.FromCanceled<byte[]>(ct);

    return DownloadAsync(url, ct);
}
```

### عبر طرق `async`

أي طريقة `async` مُعلَّنة لإرجاع `ValkarnTask<T>` تستخدم `AsyncValkarnTaskMethodBuilder<TResult>` تلقائيًا:

```csharp
async ValkarnTask<int> ComputeAsync()
{
    await ValkarnTask.Yield();
    return 42;
}
```

يُولّد المُترجم آلة حالة. إذا اكتملت الطريقة بشكل متزامن (لا تتوقف أبدًا)، تُرجع `AsyncValkarnTaskMethodBuilder<T>.Task` القيمة `new ValkarnTask<T>(result)` مع `source == null` — صفر تخصيص.

---

## تشغيل العمل على مجموعة الخيوط

هذه الطرق تُشغّل مندوبًا على مجموعة خيوط .NET وتُرجع النتيجة على الخيط الرئيسي (في `PlayerLoopTiming` المحدد). إنها غلافات مريحة فوق نوعيات `RunOnThreadPool` الأطول اسمًا.

#### `ValkarnTask.Run<T>(Func<T> func, PlayerLoopTiming timing, CancellationToken ct)`

يُشغّل `Func<T>` متزامنًا على مجموعة الخيوط.

```csharp
public static ValkarnTask<T> Run<T>(
    Func<T> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
// الحساب على مجموعة الخيوط، تصل النتيجة إلى الخيط الرئيسي في Update التالي
int hash = await ValkarnTask.Run(() => ComputeExpensiveHash(data));
```

#### `ValkarnTask.Run<T>(Func<ValkarnTask<T>> func, PlayerLoopTiming timing, CancellationToken ct)`

يُشغّل `Func<ValkarnTask<T>>` غير متزامن على مجموعة الخيوط. استخدم هذا عندما يكون العمل نفسه غير متزامن (مثل I/O الملفات).

```csharp
public static ValkarnTask<T> Run<T>(
    Func<ValkarnTask<T>> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
string json = await ValkarnTask.Run(async () =>
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
public ValkarnTask.Status GetStatus()
```

يُرجع `ValkarnTask.Status` الحالية. القيم الممكنة: `Pending`، `Succeeded`، `Faulted`، `Canceled`. بالنسبة لـ`source == null`، تُرجع دائمًا `Succeeded`.

### `GetAwaiter()`

```csharp
public Awaiter GetAwaiter()
```

يُرجع بنية `Awaiter`. تستخدمها المُترجم لتطبيق `await`. يمكنك أيضًا استدعاؤها مباشرةً للحصول على النتيجة بشكل متزامن (آمن فقط عندما تكون `IsCompleted` صحيحة).

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(10);
int value = task.GetAwaiter().GetResult(); // آمن — مكتملة متزامنًا
```

استدعاء `GetResult()` على مهمة معلقة يرمي `InvalidOperationException`.

### `AsNonGeneric()`

```csharp
public ValkarnTask AsNonGeneric()
```

يُحوّل هذا `ValkarnTask<T>` إلى `ValkarnTask` غير جنيريكي، متجاهلًا نوع النتيجة. المهمة الناتجة تشترك في نفس المصدر الأساسي والرمز، لذا تكتمل في نفس الوقت.

```csharp
ValkarnTask<int> typedTask = ComputeAsync();
ValkarnTask voidTask = typedTask.AsNonGeneric();
await voidTask;   // ينتظر الاكتمال، يتجاهل النتيجة
```

هذا مفيد عند تمرير مهام مختلطة الأنواع إلى المُجمِّعات أو عندما تهتم فقط بتوقيت الاكتمال وليس القيمة.

---

## المُجمِّعات التي تُرجع `ValkarnTask<T>`

### `WhenAll` — التحميل الزائد بمهمتين مكتوبتين

```csharp
public static ValkarnTask<(T1, T2)> WhenAll<T1, T2>(
    ValkarnTask<T1> task1, ValkarnTask<T2> task2)
```

يُشغّل كلتا المهمتين بالتوازي ويُرجع صفًا من النتائج. إذا أخفقت أو أُلغيت أي مهمة، تفوز الاستثناء الأول وتُبلَّغ أخطاء الأخرى عبر `ValkarnTask.UnobservedException`.

**تفكيك الصفوف** يعمل بشكل طبيعي مع تفكيك C#:

```csharp
var (profile, inventory) = await ValkarnTask.WhenAll(
    FetchProfileAsync(userId),
    FetchInventoryAsync(userId)
);
```

**المسار السريع الخالي من التخصيص:** إذا كانت كلتا المهمتين مكتملتين بشكل متزامن عند موقع الاستدعاء، لا يُنشأ أي كائن مُجمَّع وتُرجع صف النتائج مضمّنًا.

### `WhenAll` — التحميل الزائد بمجموعة مكتوبة

```csharp
public static ValkarnTask<T[]> WhenAll<T>(IEnumerable<ValkarnTask<T>> tasks)
```

ينتظر جميع المهام في المجموعة بالتوازي ويُرجع `T[]` بترتيب الفهرس.

```csharp
var urls = new[] { "https://a.com", "https://b.com", "https://c.com" };
ValkarnTask<string>[] downloads = urls.Select(u => DownloadAsync(u)).ToArray();
string[] results = await ValkarnTask.WhenAll(downloads);
```

إذا كانت المجموعة فارغة، يُرجع `ValkarnTask.FromResult(Array.Empty<T>())` — صفر تخصيص. إذا كانت جميع المهام مكتملة بشكل متزامن بالفعل، يُبنى مصفوفة النتائج مضمّنةً دون إنشاء وعد مُجمِّع.

### `WhenAny` — التحميل الزائد بمهمتين مكتوبتين

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    ValkarnTask<T> task1, ValkarnTask<T> task2)
```

يعود فور اكتمال أي من المهمتين. صف النتائج يحتوي على فهرس الفائز (يبدأ من 0) وقيمته. المهام الخاسرة تستمر في التشغيل؛ أخطاؤها (إن وُجدت) تُبلَّغ عبر `ValkarnTask.UnobservedException`. إلغاء الخاسرين لا يُبلَّغ عنه عمدًا.

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(
    FetchFromCacheAsync(key),
    FetchFromNetworkAsync(key)
);

if (winnerIndex == 0)
    Debug.Log("فازت الكاش");
```

### `WhenAny` — التحميل الزائد بمجموعة مكتوبة

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    IEnumerable<ValkarnTask<T>> tasks)
```

نفس دلالات التحميل الزائد بمهمتين، مُوسَّعة لأي عدد من المهام. يتطلب مهمة واحدة على الأقل؛ يرمي `ArgumentException` للمجموعات الفارغة.

```csharp
var tasks = servers.Select(s => s.FetchAsync(query)).ToArray();
var (winnerIndex, data) = await ValkarnTask.WhenAny(tasks);
Debug.Log($"الخادم {winnerIndex} استجاب أولًا");
```

---

## طرق المصنع المريحة

### `ValkarnTask.Create<T>(Func<ValkarnTask<T>> factory)`

يستدعي مندوب المصنع ويُنتظر المهمة الناتجة. مفيد عندما تريد تأجيل بناء عملية غير متزامنة.

```csharp
public static async ValkarnTask<T> Create<T>(Func<ValkarnTask<T>> factory)
```

```csharp
var result = await ValkarnTask.Create(() => LoadLevelDataAsync(levelId));
```

---

## بنية `Awaiter` (متداخلة)

`ValkarnTask<T>.Awaiter` هو المُنتظِر للمُترجم. إنه `readonly struct` يُطبّق `ICriticalNotifyCompletion`. عادةً لا تتفاعل معه مباشرةً.

```csharp
public readonly struct Awaiter : ICriticalNotifyCompletion
{
    public bool IsCompleted { get; }
    public T GetResult();
    public void OnCompleted(Action continuation);
    public void UnsafeOnCompleted(Action continuation);
}
```

`UnsafeOnCompleted` هو المسار الذي يستخدمه `AsyncValkarnTaskMethodBuilder<T>`. تسمية "unsafe" تعني عدم التقاط `ExecutionContext` — هذا مقصود لـ Unity حيث لا يوجد `SynchronizationContext`.

عندما تكون `IsCompleted` صحيحة، استدعاء `GetResult()` يقرأ حقل `result` المضمّن (للمهام المتزامنة) أو يستدعي عبر واجهة المصدر (للمهام الغير متزامنة). كلا المسارين مُعلَّمان بـ`[MethodImpl(MethodImplOptions.AggressiveInlining)]`.

---

## طرق الامتداد على `ValkarnTask<T>`

### `AsResult<T>()`

```csharp
public static ValkarnTask<Result<T>> AsResult<T>(this ValkarnTask<T> task)
```

يُغلّف `ValkarnTask<T>` في `ValkarnTask<Result<T>>`، مُلتقطًا أي استثناء أو إلغاء وترميزه في قيمة `Result<T>`. هذا يتجنب try/catch في موقع الاستدعاء.

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

`ValkarnTask.Promise<T>` هو مصدر اكتمال يدوي مُخصص في الكومة للحالات التي تحتاج فيها للتحكم في متى يكتمل `ValkarnTask<T>`، وعمره غير محدود بعملية غير متزامنة واحدة.

```csharp
public class Promise<T>
{
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

```csharp
// تغليف واجهة برمجة مبنية على الاستدعاءات
var promise = new ValkarnTask.Promise<string>();

SomeCallbackApi.OnComplete += value => promise.TrySetResult(value);
SomeCallbackApi.OnError   += ex    => promise.TrySetException(ex);

string result = await promise.Task;
```

على عكس `PooledPromise<T>`، لا يُجمَّع `Promise<T>`. يستخدم مُنهيًا لاكتشاف والإبلاغ عن الاستثناءات غير الملاحظة إذا أخفقت المهمة ولم ينتظرها المُستدعي.

للأنماط ذات التردد العالي (حلقات المنتج/المستهلك، العمليات لكل إطار)، فضّل `ValkarnTask.PooledPromise<T>` الذي يعود تلقائيًا إلى مجموعة الموارد بعد استدعاء `GetResult`.

---

## `PooledPromise<T>` — مصدر اكتمال يدوي مُجمَّع

```csharp
public sealed class PooledPromise<T> : ValkarnTask.ISource<T>, IPoolNode<PooledPromise<T>>
{
    public static PooledPromise<T> Create(out uint token);
    public static PooledPromise<T> CreateCompleted(T result, out uint token);
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

بعد استدعاء `GetResult` على المهمة الدعم، يُعيد الوعد تعيين `ValkarnTaskCompletionCore<T>` ويُرجع نفسه إلى مجموعة الموارد. حارس ضد الإرجاع المزدوج يضمن حدوث ذلك مرة واحدة فقط حتى إذا استُدعي `GetResult` بشكل متزامن.

```csharp
// النمط: إنتاج ValkarnTask<T> يكتمل عند الاستعداد
var promise = ValkarnTask.PooledPromise<int>.Create(out uint token);
ValkarnTask<int> task = promise.Task;

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

## ملخص طرق الحصول على `ValkarnTask<T>`

| الطريقة | متى تستخدمها |
|--------|------------|
| `return value` داخل `async ValkarnTask<T>` | الطرق الغير متزامنة العادية |
| `ValkarnTask.FromResult(value)` | الإرجاعات السريعة المتزامنة |
| `ValkarnTask.FromException<T>(ex)` | المهام المعطوبة مسبقًا |
| `ValkarnTask.FromCanceled<T>(ct)` | المهام المُلغاة مسبقًا |
| `ValkarnTask.Run<T>(Func<T>, ...)` | إفراغ مجموعة الخيوط |
| `ValkarnTask.Run<T>(Func<ValkarnTask<T>>, ...)` | عمل مجموعة الخيوط الغير متزامن |
| `ValkarnTask.WhenAll<T1,T2>(t1, t2)` | انتظار مهمتين مكتوبتين، الحصول على صف |
| `ValkarnTask.WhenAll<T>(IEnumerable<...>)` | انتظار N مهمة مكتوبة، الحصول على مصفوفة |
| `ValkarnTask.WhenAny<T>(t1, t2)` | أولى مهمتين مكتوبتين |
| `ValkarnTask.WhenAny<T>(IEnumerable<...>)` | أولى N مهمة مكتوبة |
| `task.AsResult<T>()` | تغليف آمن من الاستثناءات |
| `new ValkarnTask.Promise<T>()` → `.Task` | اكتمال يدوي طويل الأمد |
| `ValkarnTask.PooledPromise<T>.Create(...)` → `.Task` | اكتمال يدوي عالي التردد |
