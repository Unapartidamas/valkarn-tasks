---
sidebar_position: 1
title: المهام المبنية على البنى
---

# المهام المبنية على البنى

`VlkTask` و`VlkTask<T>` هما نوعا الإرجاع الأساسيان للكود غير المتزامن في Valkarn Tasks. على عكس `System.Threading.Tasks.Task` الذي هو نوع مرجعي يُخصص دائمًا في كومة الذاكرة، كلا نوعَي مهام Valkarn هما قيم `readonly struct`. تشرح هذه الصفحة ما يعنيه ذلك عمليًا، وكيف يعمل المسار السريع الخالي من التخصيص، وكيف يتكامل المُترجم مع آلية async/await.

---

## لماذا `readonly struct`؟

يجب تخصيص مهمة مبنية على فئة كـ`Task<T>` في كومة الذاكرة في كل مرة يُستدعى فيها طريقة غير متزامنة، حتى للطرق التي تكتمل بشكل متزامن. في حلقة لعبة Unity تعمل بـ 60 إطار/ثانية، يمكن أن تتراكم المئات من العمليات الغير متزامنة الصغيرة في كل إطار مسببةً ضغطًا ملحوظًا على جامع النفايات.

`VlkTask` و`VlkTask<T>` مُعلَّنتان كـ`readonly partial struct`:

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncVlkTaskMethodBuilder))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct VlkTask
{
    internal readonly ISource source;
    internal readonly uint token;
}
```

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncVlkTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct VlkTask<T>
{
    internal readonly VlkTask.ISource<T> source;
    internal readonly T result;
    internal readonly uint token;
}
```

كونها بنية يعني أن قيمة المهمة نفسها تعيش في المكدس (أو مضمّنةً في كائنها الأصل) بدلًا من كومة الذاكرة. مُعدِّل `readonly` يضمن أن المُترجم يمكنه استنتاج عدم التغيير ويمنع أخطاء النسخ العرضية. `StructLayout.Auto` يتيح لوقت التشغيل تحسين ترتيب الحقول للمنصة المستهدفة.

### الثابت الرئيسي: `source == null`

التصميم مبني على ثابت واحد:

> عندما يكون `source` هو `null`، فإن المهمة مكتملة بشكل متزامن دون خطأ. لا يتورط أي كائن في كومة الذاكرة.

`VlkTask.CompletedTask` هو `default(VlkTask)` — حقل `source` الخاص به هو null، لذا لا يُكلف شيئًا. `VlkTask<T>` يحمل نتيجته مضمّنةً في حقل `result`، مما يجعل `VlkTask.FromResult(value)` استدعاءً خاليًا من التخصيص:

```csharp
// صفر تخصيص — source هو null، النتيجة مخزّنة مضمّنةً
VlkTask<int> task = VlkTask.FromResult(42);

// صفر تخصيص أيضًا — source هو null
VlkTask done = VlkTask.CompletedTask;
```

---

## المسار السريع الخالي من التخصيص

عندما تكتمل طريقة `async` دون أن تتوقف أبدًا (لا يُنتج `await` عملية غير مكتملة)، تعمل الطريقة بأكملها بشكل متزامن على الخيط المُستدعِي. يكتشف المُنشئ هذا ويُرجع مهمة مع `source == null`.

تفحص المُنتظِر هذا فورًا:

```csharp
public bool IsCompleted
{
    get
    {
        var s = task.source;
        return s == null || s.GetStatus(task.token).IsCompleted();
    }
}
```

عندما يكون `IsCompleted` صحيحًا قبل استدعاء `OnCompleted`، لا تُسجّل آلة الحالة استمرارًا. يُستدعى `GetResult` فورًا، وبالنسبة لـ`VlkTask<T>` مع `source == null`، تُقرأ النتيجة من حقل `result` المضمّن في البنية:

```csharp
public T GetResult()
{
    var s = task.source;
    if (s == null)
        return task.result;   // مضمّن، لا استدعاء ISource
    return s.GetResult(task.token);
}
```

لا يُنشأ أي كائن، ولا يحدث إرسال واجهة، ولا يُخصص أي مندوب استمرار. ينحل الانتظار بالكامل كقراءة مباشرة للقيمة.

### عندما يكون مصدر ضروريًا

إذا تعلّقت طريقة غير متزامنة (انتظرت شيئًا لم يكتمل بعد)، يُخصص المُنشئ كائن `AsyncVlkTaskRunner<TStateMachine>` مُجمَّعًا (أو `AsyncVlkTaskRunner<TStateMachine, TResult>` للنوع الجنيريكي). يعمل هذا الكائن وظيفتين: يحمل آلة الحالة المُولَّدة من المُترجم بالقيمة ويُطبّق `VlkTask.ISource`، لذا يمكن استخدامه مباشرةً كمصدر دعم للمهمة. المهمة المُرجَعة للمُستدعين تُغلّف هذا المُشغّل مع رمز جيلي من نوع `uint`.

عند الاكتمال، عندما يستدعي المُستدعي `GetResult` على المُنتظِر، يُعيد المُشغّل تعيين نفسه ويعود إلى مجموعة موارده — لذا يُوزَّع التخصيص عبر استدعاءات طرق كثيرة.

---

## واجهة `ISource`

العقد بين بنية `VlkTask` وكائن دعمها الغير متزامن هو `VlkTask.ISource`:

```csharp
public interface ISource
{
    VlkTask.Status GetStatus(uint token);
    void GetResult(uint token);
    void OnCompleted(Action<object> continuation, object state, uint token);
    VlkTask.Status UnsafeGetStatus();
}

public interface ISource<out T> : ISource
{
    new T GetResult(uint token);
}
```

أي كائن يُطبّق `ISource` يمكنه دعم `VlkTask`. تشحن المكتبة عدة تطبيقات:

| النوع | الغرض |
|------|---------|
| `AsyncVlkTaskRunner<TStateMachine>` | يدعم كل طريقة `async VlkTask` (داخلي) |
| `AsyncVlkTaskRunner<TStateMachine, TResult>` | يدعم كل طريقة `async VlkTask<T>` (داخلي) |
| `VlkTask.PooledPromise` | مصدر اكتمال يدوي مع إرجاع تلقائي لمجموعة الموارد |
| `VlkTask.PooledPromise<T>` | النوع الجنيريكي للسابق |
| `VlkTask.Promise` | مصدر اكتمال يدوي بدون تجميع (عمليات طويلة الأمد) |
| `VlkTask.Promise<T>` | النوع الجنيريكي للسابق |

مُعامل `uint token` حارس جيلي. عند إعادة تعيين مصدر مُجمَّع للإعادة الاستخدام، يزداد عداد جيله. أي بنية `VlkTask` تحمل الرمز القديم ستتلقى `InvalidOperationException` فورًا بدلًا من قراءة حالة مُعاد تدويرها بصمت.

---

## `VlkTask` مقابل `VlkTask<T>`

| الميزة | `VlkTask` | `VlkTask<T>` |
|---------|-----------|-------------|
| قيمة الإرجاع | لا شيء (مكافئ void) | `T` |
| تخزين النتيجة المضمّنة | لا حقل `result` | حقل `result` (النوع `T`) |
| `GetResult` للمُنتظِر | `void` | يُرجع `T` |
| نوع المُنشئ | `AsyncVlkTaskMethodBuilder` | `AsyncVlkTaskMethodBuilder<TResult>` |
| القيمة المكتملة متزامنًا | `VlkTask.CompletedTask` | `VlkTask.FromResult(value)` |
| التحويل للنوع غير الجنيريكي | غير مطبّق | `.AsNonGeneric()` |

استخدم `VlkTask` عندما لا تُرجع طريقة غير متزامنة قيمة ذات معنى، و`VlkTask<T>` عندما تُنتج نتيجة. يمكنك دائمًا تحويل `VlkTask<T>` إلى `VlkTask` عبر `AsNonGeneric()` عندما تحتاج لمزج المهام المكتوبة وغير المكتوبة في مُجمِّعات كـ`WhenAll`.

---

## كيف يعمل مُنشئ الطريقة غير المتزامنة

يبحث مُترجم C# عن النوع المُسمَّى في `[AsyncMethodBuilder(...)]` على نوع الإرجاع. بالنسبة لـ`VlkTask`، هذا هو `AsyncVlkTaskMethodBuilder`. بالنسبة لـ`VlkTask<T>`، هو `AsyncVlkTaskMethodBuilder<TResult>`.

المُنشئ نفسه هو بنية لتجنب تخصيص كومة الذاكرة لكائن المُنشئ فقط. له حقلان (ثلاثة للنوع الجنيريكي):

```csharp
public struct AsyncVlkTaskMethodBuilder
{
    IStateMachineRunnerPromise runner;   // null حتى التعليق الأول
    Exception syncException;            // يُعيَّن فقط على مسار الخطأ المتزامن
}

public struct AsyncVlkTaskMethodBuilder<TResult>
{
    IStateMachineRunnerPromise<TResult> runner;
    Exception syncException;
    TResult result;                     // يُعيَّن فقط على مسار النجاح المتزامن
}
```

### دورة حياة المُنشئ

يستدعي المُترجم هذه الطرق بالترتيب:

**1. `Create()`** — يُرجع مُنشئًا افتراضيًا (جميع الحقول null/default). لا تخصيص.

**2. `Start(ref stateMachine)`** — يستدعي `stateMachine.MoveNext()` بشكل متزامن. إذا اكتملت الطريقة دون الوصول إلى `await` غير مكتمل، يُستدعى `SetResult`/`SetException` ويبقى `runner` null.

**3. `AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine)`** — يُستدعى عندما تواجه الطريقة `await` غير مكتمل. إذا كان `runner` null (أول تعليق)، يستعير أو ينشئ `AsyncVlkTaskRunner` وينسخ آلة الحالة إليه. ثم يستدعي `awaiter.UnsafeOnCompleted(runner.MoveNextAction)` لتسجيل استمرار آلة الحالة.

**4. `SetResult()` / `SetException(exception)`** — يُشير إلى الاكتمال في `VlkTaskCompletionCore` للمُشغّل، مما يُوقظ أي مُنتظِر مسجَّل.

**5. خاصية `Task`** — يفحصها المُستدعي للحصول على قيمة `VlkTask`. على مسار النجاح المتزامن (`runner == null && syncException == null`)، يُرجع `default` (أو `new VlkTask<T>(result)` للنوع الجنيريكي) — صفر تخصيص. على المسار الغير متزامن، يُغلّف المُشغّل كمصدر.

التحسين الحاسم هو أن `runner` يُخصص بتكاسل. إذا اكتملت الطريقة بشكل متزامن (الحالة الشائعة لإصابات الكاش والحراس والإرجاعات المبكرة)، لا يُستعار أي كائن مُجمَّع.

---

## حالات `VlkTaskStatus`

تُمثَّل الحالة بـ`enum` بحجم `byte` مُضمَّن داخل `VlkTask`:

```csharp
public enum Status : byte
{
    Pending   = 0,   // لم يكتمل بعد
    Succeeded = 1,   // اكتمل بشكل طبيعي
    Faulted   = 2,   // اكتمل مع استثناء غير معالج
    Canceled  = 3    // اكتمل عبر OperationCanceledException
}
```

يمكنك فحص الحالة مباشرةً:

```csharp
VlkTask task = SomeOperation();
VlkTask.Status status = task.GetStatus();

switch (status)
{
    case VlkTask.Status.Pending:
        // لا يزال يعمل — لا يمكن استدعاء GetResult
        break;
    case VlkTask.Status.Succeeded:
        // اكتمل بشكل طبيعي
        break;
    case VlkTask.Status.Faulted:
        // اكتمل مع استثناء — GetResult سيعيد رمي الاستثناء
        break;
    case VlkTask.Status.Canceled:
        // اكتمل مع OperationCanceledException
        break;
}
```

بالنسبة للمسار السريع المكتمل متزامنًا (حيث `source == null`)، يُرجع `GetStatus()` قيمة `Succeeded` دون أي استدعاء واجهة:

```csharp
public Status GetStatus()
{
    if (source == null) return Status.Succeeded;
    return source.GetStatus(token);
}
```

خاصية `IsCompleted` تتبع نفس النمط وتُرجع `true` لأي حالة غير `Pending`.

---

## تداعيات IL2CPP

يُحوّل IL2CPP كود C# إلى كود C++ قبل البناء للكود الأصلي. الأنواع الجنيريكية ذات القيم — بما في ذلك البنى — تُخصَّص بالكامل في الكود المُولَّد، وهذا له عواقب مهمة لهذه المكتبة.

**تخصيص آلة الحالة.** يُولّد المُترجم بنية آلة حالة فريدة لكل طريقة غير متزامنة. لذا `AsyncVlkTaskRunner<TStateMachine>` فريد أيضًا لكل طريقة غير متزامنة، و`VlkTaskPool<AsyncVlkTaskRunner<TStateMachine>>` مجموعة موارد منفصلة لكل طريقة. هذا في الواقع مفيد: مجموعة الموارد لا تُشارَك أبدًا عبر أنواع غير متوافقة، مما يُزيل أي خطر لتشابك الأنواع.

**لا تعبئة لآلة الحالة.** آلة الحالة مخزّنة بالقيمة داخل كائن المُشغّل، وليس معبّأة. يتعامل IL2CPP مع هذا بشكل صحيح لأن المُشغّل هو `sealed class` مع حقل `TStateMachine` ملموس.

**حماية الاستئصال.** سمة `[AsyncMethodBuilder]` تُبقي أنواع المُنشئ حية. ومع ذلك، إذا استخدمت `VlkTask.ISource` عبر مرجع واجهة في IL2CPP مع الاستئصال الجائر، أضف مدخلاً لـ`link.xml` يحفظ تجميعة `UnaPartidaMas.Valkarn.Tasks`:

```xml
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```

**`ICriticalNotifyCompletion`.** تُطبّق بنى المُنتظِر `ICriticalNotifyCompletion`، مما يُخبر المُترجم باستدعاء `UnsafeOnCompleted` بدلًا من `OnCompleted`. النوع "غير الآمن" يتخطى عمدًا التقاط `ExecutionContext`. هذا صحيح لـ Unity — لا يوجد `SynchronizationContext` في تهيئة Unity الافتراضية، والتقاطه سيُضيف تكلفة دون فائدة. تحت IL2CPP، هذا يتجنب أيضًا تكلفة مسار `ExecutionContext.Run` الذي تدفعه `Task` القياسية دائمًا.

---

## أمثلة عملية

### الإرجاع المبكر بدون تخصيص

```csharp
// async VlkTask<int> تكتمل بشكل متزامن على المسار الحار
async VlkTask<int> GetCachedValue(string key)
{
    if (_cache.TryGetValue(key, out var value))
        return value;            // المُترجم يستدعي SetResult(value)؛ المصدر يبقى null

    var result = await FetchFromDatabaseAsync(key);
    _cache[key] = result;
    return result;
}
```

عندما تكون القيمة مخبأة، لا تتوقف الطريقة أبدًا. `VlkTask<int>` المُرجَعة لها `source == null` وتحمل النتيجة مضمّنةً. لا يحدث أي تخصيص لكومة الذاكرة على هذا المسار.

### فحص IsCompleted قبل الانتظار

```csharp
VlkTask<Texture2D> loadTask = LoadTextureAsync("sprites/hero.png");

if (loadTask.IsCompleted)
{
    // اكتملت — GetAwaiter().GetResult() يقرأ النتيجة المضمّنة بدون استدعاء ISource
    Texture2D tex = loadTask.GetAwaiter().GetResult();
    ApplyTexture(tex);
}
else
{
    // غير متزامن فعليًا — سجّل استمرارًا
    ApplyTextureAsync(loadTask).Forget();
}
```

### مراقبة الاستثناءات غير المعالجة

المهام المعطوبة التي لم تُنتظَر أبدًا (أنماط fire-and-forget) تُبلّغ عن استثناءاتها عبر حدث `VlkTask.UnobservedException`. يُثار هذا بشكل حتمي عند وقت إرجاع مجموعة الموارد للمصادر المُجمَّعة، أو من المُنهي للمهام المدعومة بـ`Promise`.

```csharp
VlkTask.UnobservedException += ex =>
{
    Debug.LogError($"[VlkTask] غير ملاحظ: {ex}");
};
```

الحدث آمن للخيوط؛ يمكن إضافة المعالجات أو إزالتها من أي خيط باستخدام حلقة مقارنة-وتبادل خالية من الأقفال.
