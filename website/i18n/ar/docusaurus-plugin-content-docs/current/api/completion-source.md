---
sidebar_position: 3
title: مصادر الاكتمال
---

# VlkTaskCompletionSource

تمنحك `VlkTaskCompletionSource<T>` والنوع غير الجنيريكي `VlkTaskCompletionSource` تحكمًا يدويًا في `VlkTask`. إنهما المكافئ الغير المتزامن لكتابة النتيجة بنفسك — تحمل كائن مصدر، وتُسلّم `.Task` الخاص به للمُستدعين، ثم تحله أو تُخطئه أو تُلغيه من موقع استدعاء منفصل.

هذا هو مكافئ Valkarn Tasks لـ`TaskCompletionSource<T>` من BCL، لكنه مدعوم بـ`VlkTask.Promise<T>` للحفاظ على نموذج التخصيص متسقًا مع بقية المكتبة.

---

## VlkTaskCompletionSource&lt;T&gt;

```csharp
public class VlkTaskCompletionSource<T>
{
    public VlkTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled();
}
```

### Task

```csharp
public VlkTask<T> Task { get; }
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

## VlkTaskCompletionSource غير الجنيريكي

لا توجد فئة `VlkTaskCompletionSource` غير جنيريكية منفصلة في واجهة برمجة العامة. للمهام اليدوية التي تُرجع void، استخدم `VlkTask.Promise` مباشرةً:

```csharp
var promise = new VlkTask.Promise();
VlkTask task = promise.Task;

promise.TrySetResult();    // اكتمال
promise.TrySetException(ex);
promise.TrySetCanceled();
```

`VlkTask.Promise` يعرض نفس سطح `TrySet*` ونفس حماية الاكتمال المزدوج، لكنه يُنتج `VlkTask` غير جنيريكي بدلًا من `VlkTask<T>`.

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

كثير من واجهات برمجة Unity والمنصة تُسلّم النتائج عبر استدعاءات بدلًا من async/await. `VlkTaskCompletionSource<T>` يتيح لك تغليفها بشكل نظيف.

```csharp
public VlkTask<Texture2D> LoadTextureAsync(string url)
{
    var tcs = new VlkTaskCompletionSource<Texture2D>();

    StartCoroutine(LoadCoroutine(url, tcs));

    return tcs.Task;
}

IEnumerator LoadCoroutine(string url, VlkTaskCompletionSource<Texture2D> tcs)
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

استخدم `VlkTask.Promise` عندما تحتاج إشارة تُطلق مرة واحدة وتُلغي حجب أي عدد من المنتظِرين. هذا مماثل لـ`ManualResetEventSlim` لكن أصلي غير متزامن.

```csharp
public class AsyncGate
{
    readonly VlkTask.Promise _promise = new();

    // أي عدد من المُستدعين يمكنهم انتظار هذا
    public VlkTask WaitAsync() => _promise.Task;

    // استدعِ مرة واحدة لإلغاء حجب الجميع
    public void Open() => _promise.TrySetResult();
}

// الاستخدام
var gate = new AsyncGate();

// عدة أنظمة تنتظر البوابة بشكل مستقل
async VlkTask SystemAAsync()
{
    await gate.WaitAsync();
    // المتابعة بعد فتح البوابة
}

async VlkTask SystemBAsync()
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
public VlkTask<Result> RunWithTimeoutAsync(
    Func<VlkTask<Result>> operation,
    float timeoutSeconds,
    CancellationToken ct)
{
    var tcs = new VlkTaskCompletionSource<Result>();

    RunCoreAsync(tcs, operation, timeoutSeconds, ct).Forget();

    return tcs.Task;
}

async VlkTask RunCoreAsync(
    VlkTaskCompletionSource<Result> tcs,
    Func<VlkTask<Result>> operation,
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
    readonly VlkTask.Promise _readyPromise = new();

    // يمكن لأي شخص انتظار هذا في أي وقت — قبل أو بعد Initialize()
    public VlkTask Ready => _readyPromise.Task;

    async void Start()
    {
        await LoadConfigAsync();
        await ConnectAsync();
        _readyPromise.TrySetResult();  // يُلغي حجب جميع المنتظِرين
    }
}

// في أي مكوّن آخر
async VlkTask OnEnableAsync()
{
    await ServiceBus.Instance.Ready;  // ينتظر إذا لم يكن جاهزًا، يعود فورًا إذا كان جاهزًا
    DoWork();
}
```

---

## العلاقة بـ VlkTask.Promise

`VlkTaskCompletionSource<T>` هو غلاف عام رفيع حول `VlkTask.Promise<T>`. كلاهما يوفر نفس الوظيفة. الفرق هو اتفاقية التسمية:

| النوع | يُرجع | الاستخدام الشائع |
|------|---------|------------|
| `VlkTaskCompletionSource<T>` | `VlkTask<T>` | واجهة برمجة عامة، يُحاكي أسلوب `TaskCompletionSource<T>` في BCL |
| `VlkTask.Promise<T>` | `VlkTask<T>` | استخدام داخلي، أكثر مباشرةً |
| `VlkTask.Promise` | `VlkTask` | إشارات void (بوابات، أحداث) |

الثلاثة يدعمون مهمتهم بـ`VlkTaskCompletionCore<T>`، آلة الحالة الداخلية المبنية على البنى التي تتعامل مع أمان الخيوط وسباق الاستمرار/النتيجة باستخدام بروتوكول ثنائي المرحلة مبني على CAS.
