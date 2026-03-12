---
sidebar_position: 3
title: مصادر الاكتمال
---

# ValkarnTaskCompletionSource

تمنحك `ValkarnTaskCompletionSource<T>` والنوع غير الجنيريكي `ValkarnTaskCompletionSource` تحكمًا يدويًا في `ValkarnTask`. إنهما المكافئ الغير المتزامن لكتابة النتيجة بنفسك — تحمل كائن مصدر، وتُسلّم `.Task` الخاص به للمُستدعين، ثم تحله أو تُخطئه أو تُلغيه من موقع استدعاء منفصل.

هذا هو مكافئ Valkarn Tasks لـ`TaskCompletionSource<T>` من BCL، لكنه مدعوم بـ`ValkarnTask.Promise<T>` للحفاظ على نموذج التخصيص متسقًا مع بقية المكتبة.

---

## ValkarnTaskCompletionSource&lt;T&gt;

```csharp
public class ValkarnTaskCompletionSource<T>
{
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled();
}
```

### Task

```csharp
public ValkarnTask<T> Task { get; }
```

المهمة التي سيُلاحظها الكود المنتظِر. وزّعها على جميع المُستدعين الذين يحتاجون للانتظار على النتيجة. يمكن لعدة مُستدعين `await` نفس المهمة بشكل متزامن.

### TrySetResult

```csharp
public bool TrySetResult(T result);
```

يُكمل المهمة بنجاح بالقيمة المعطاة. تستأنف جميع الاستمرارات المنتظِرة. يُرجع `true` إذا قُبل الاكتمال؛ يُرجع `false` إذا كانت المهمة مكتملة بالفعل (بأي استدعاء `TrySet*` سابق). لا يرمي أبدًا.

### TrySetException

```csharp
public bool TrySetException(Exception ex);
```

يُخطئ المهمة بالاستثناء المُقدَّم. الكود المنتظِر يتلقى الاستثناء عند استدعاء `await`. يرمي `ArgumentNullException` إذا كانت `ex` هي `null`. يُرجع `true` إذا قُبل، `false` إذا كانت مكتملة بالفعل.

### TrySetCanceled

```csharp
public bool TrySetCanceled();
```

يُلغي المهمة. الكود المنتظِر يتلقى `OperationCanceledException`. يُرجع `true` إذا قُبل، `false` إذا كانت مكتملة بالفعل.

---

## ValkarnTaskCompletionSource غير الجنيريكي

لا توجد فئة `ValkarnTaskCompletionSource` غير جنيريكية منفصلة في واجهة برمجة العامة. للمهام اليدوية التي تُرجع void، استخدم `ValkarnTask.Promise` مباشرةً:

```csharp
var promise = new ValkarnTask.Promise();
ValkarnTask task = promise.Task;

promise.TrySetResult();    // اكتمال
promise.TrySetException(ex);
promise.TrySetCanceled();
```

`ValkarnTask.Promise` يعرض نفس سطح `TrySet*` ونفس حماية الاكتمال المزدوج، لكنه يُنتج `ValkarnTask` غير جنيريكي بدلًا من `ValkarnTask<T>`.

---

## حماية الاكتمال المزدوج

جميع طرق `TrySet*` آمنة للاستدعاء من أي خيط في أي وقت، بما في ذلك بشكل متزامن. الاستدعاء الأول الذي يفوز بـCAS على آلة الحالة الداخلية ينجح؛ كل استدعاء لاحق يُرجع `false` وليس له أي تأثير. هذا يعني:

- استدعاء `TrySetResult` مرتين لا يفعل شيئًا في الاستدعاء الثاني.
- استدعاء `TrySetResult` بعد `TrySetException` لا يفعل شيئًا.
- خيطان يتسابقان لإكمال نفس المصدر في نفس الوقت آمنان — واحد يفوز، والآخر يُتجاهل بصمت.

إذا أردت معرفة أي مُستدعٍ "فاز"، افحص قيمة الإرجاع. إذا لم يهمك (إشارة fire-and-forget)، يمكنك تجاهله بأمان.

```csharp
// سباق آمن — واحد منهما فقط سيُكمل المهمة فعلًا
_ = source.TrySetResult(value);
_ = source.TrySetCanceled();
```

---

## الأنماط الشائعة

### تجسير واجهة برمجة مبنية على الاستدعاءات

كثير من واجهات برمجة Unity والمنصة تُسلّم النتائج عبر استدعاءات بدلًا من async/await. `ValkarnTaskCompletionSource<T>` يتيح لك تغليفها بشكل نظيف.

```csharp
public ValkarnTask<Texture2D> LoadTextureAsync(string url)
{
    var tcs = new ValkarnTaskCompletionSource<Texture2D>();

    StartCoroutine(LoadCoroutine(url, tcs));

    return tcs.Task;
}

IEnumerator LoadCoroutine(string url, ValkarnTaskCompletionSource<Texture2D> tcs)
{
    var request = UnityWebRequestTexture.GetTexture(url);
    yield return request.SendWebRequest();

    if (request.result == UnityWebRequest.Result.Success)
        tcs.TrySetResult(DownloadHandlerTexture.GetContent(request));
    else
        tcs.TrySetException(new Exception(request.error));
}
```

المُستدعون يمكنهم الآن ببساطة `await` المهمة المُرجَعة:

```csharp
Texture2D tex = await LoadTextureAsync("https://example.com/image.png");
```

### إشارة أحادية الاستخدام (بوابة غير متزامنة)

استخدم `ValkarnTask.Promise` عندما تحتاج إشارة تُطلق مرة واحدة وتُلغي حجب أي عدد من المنتظِرين. هذا مماثل لـ`ManualResetEventSlim` لكن أصلي غير متزامن.

```csharp
public class AsyncGate
{
    readonly ValkarnTask.Promise _promise = new();

    // أي عدد من المُستدعين يمكنهم انتظار هذا
    public ValkarnTask WaitAsync() => _promise.Task;

    // استدعِ مرة واحدة لإلغاء حجب الجميع
    public void Open() => _promise.TrySetResult();
}

// الاستخدام
var gate = new AsyncGate();

// عدة أنظمة تنتظر البوابة بشكل مستقل
async ValkarnTask SystemAAsync()
{
    await gate.WaitAsync();
    // المتابعة بعد فتح البوابة
}

async ValkarnTask SystemBAsync()
{
    await gate.WaitAsync();
    // يُستأنف في نفس وقت SystemA
}

// في مكان آخر — افتح البوابة
gate.Open();
```

بمجرد استدعاء `TrySetResult()`، تكتمل جميع استدعاءات `await` الحالية والمستقبلية على المهمة فورًا (بشكل متزامن إذا كانت المهمة مكتملة بالفعل عند تشغيلها).

### تغليف عملية غير متزامنة من طرف ثالث مع دعم الإلغاء

```csharp
public ValkarnTask<Result> RunWithTimeoutAsync(
    Func<ValkarnTask<Result>> operation,
    float timeoutSeconds,
    CancellationToken ct)
{
    var tcs = new ValkarnTaskCompletionSource<Result>();

    RunCoreAsync(tcs, operation, timeoutSeconds, ct).Forget();

    return tcs.Task;
}

async ValkarnTask RunCoreAsync(
    ValkarnTaskCompletionSource<Result> tcs,
    Func<ValkarnTask<Result>> operation,
    float timeout,
    CancellationToken ct)
{
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
    linked.CancelAfter(TimeSpan.FromSeconds(timeout));

    try
    {
        var result = await operation();
        tcs.TrySetResult(result);
    }
    catch (OperationCanceledException)
    {
        tcs.TrySetCanceled();
    }
    catch (Exception ex)
    {
        tcs.TrySetException(ex);
    }
}
```

### بوابة التهيئة المؤجلة

نمط شائع في Unity هو كشف مهمة "جاهز" يمكن للمكوّنات انتظارها بغض النظر عن بدء التهيئة.

```csharp
public class ServiceBus : MonoBehaviour
{
    readonly ValkarnTask.Promise _readyPromise = new();

    // يمكن لأي شخص انتظار هذا في أي وقت — قبل أو بعد Initialize()
    public ValkarnTask Ready => _readyPromise.Task;

    async void Start()
    {
        await LoadConfigAsync();
        await ConnectAsync();
        _readyPromise.TrySetResult();  // يُلغي حجب جميع المنتظِرين
    }
}

// في أي مكوّن آخر
async ValkarnTask OnEnableAsync()
{
    await ServiceBus.Instance.Ready;  // ينتظر إذا لم يكن جاهزًا، يعود فورًا إذا كان جاهزًا
    DoWork();
}
```

---

## العلاقة بـ ValkarnTask.Promise

`ValkarnTaskCompletionSource<T>` هو غلاف عام رفيع حول `ValkarnTask.Promise<T>`. كلاهما يوفر نفس الوظيفة. الفرق هو اتفاقية التسمية:

| النوع | يُرجع | الاستخدام الشائع |
|------|---------|------------|
| `ValkarnTaskCompletionSource<T>` | `ValkarnTask<T>` | واجهة برمجة عامة، يُحاكي أسلوب `TaskCompletionSource<T>` في BCL |
| `ValkarnTask.Promise<T>` | `ValkarnTask<T>` | استخدام داخلي، أكثر مباشرةً |
| `ValkarnTask.Promise` | `ValkarnTask` | إشارات void (بوابات، أحداث) |

الثلاثة يدعمون مهمتهم بـ`ValkarnTaskCompletionCore<T>`، آلة الحالة الداخلية المبنية على البنى التي تتعامل مع أمان الخيوط وسباق الاستمرار/النتيجة باستخدام بروتوكول ثنائي المرحلة مبني على CAS.
