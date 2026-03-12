---
sidebar_position: 1
title: Struct Tasks
---

# Struct Tasks

`VlkTask` और `VlkTask<T>` Valkarn Tasks में core async return types हैं। `System.Threading.Tasks.Task` के विपरीत, जो एक reference type है और हमेशा हीप पर आवंटित होता है, दोनों Valkarn task types `readonly struct` values हैं। यह page बताती है कि इसका व्यवहार में क्या अर्थ है, zero-allocation fast path कैसे काम करता है, और compiler async/await machinery के साथ कैसे integrate होता है।

---

## `readonly struct` क्यों?

एक class-based task जैसे `Task<T>` को हर बार heap पर आवंटित किया जाना चाहिए जब एक async method call होती है, यहाँ तक कि उन methods के लिए जो synchronously complete होती हैं। Unity game loop में 60 fps पर चलते हुए, प्रत्येक frame में सैकड़ों छोटी async operations measurable GC pressure जोड़ सकती हैं।

`VlkTask` और `VlkTask<T>` को `readonly partial struct` के रूप में declare किया गया है:

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

एक struct होने का अर्थ है कि task value स्वयं stack पर (या अपने parent object के अंदर inline) रहती है, हीप पर नहीं। `readonly` modifier यह सुनिश्चित करता है कि compiler immutability के बारे में reason कर सके और accidental copying bugs को रोके। `StructLayout.Auto` runtime को target platform के लिए field ordering optimize करने देता है।

### मुख्य invariant: `source == null`

design एक single invariant के इर्द-गिर्द बनाया गया है:

> जब `source` `null` होता है, task synchronously बिना किसी error के complete होता है। कोई heap object involve नहीं है।

`VlkTask.CompletedTask` `default(VlkTask)` है — इसका `source` field null है, इसलिए इसकी कोई cost नहीं। `VlkTask<T>` अपना result `result` field में inline carry करता है, जो `VlkTask.FromResult(value)` को भी zero-allocation call बनाता है:

```csharp
// शून्य आवंटन — source null है, result inline stored है
VlkTask<int> task = VlkTask.FromResult(42);

// भी शून्य आवंटन — source null है
VlkTask done = VlkTask.CompletedTask;
```

---

## Zero-allocation happy path

जब एक `async` method बिना कभी suspend हुए complete होती है (कोई `await` किसी incomplete operation को yield नहीं करता), पूरी method calling thread पर synchronously चलती है। builder इसे detect करता है और `source == null` के साथ एक task लौटाता है।

awaiter इसे तुरंत check करता है:

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

जब `IsCompleted` `OnCompleted` के कभी call होने से पहले true होता है, state machine एक continuation register नहीं करता। `GetResult` तुरंत call होता है, और `source == null` वाले `VlkTask<T>` के लिए, result struct के inline `result` field से पढ़ा जाता है:

```csharp
public T GetResult()
{
    var s = task.source;
    if (s == null)
        return task.result;   // inline, कोई ISource call नहीं
    return s.GetResult(task.token);
}
```

कोई object नहीं बनता, कोई interface dispatch नहीं होता, और कोई continuation delegate आवंटित नहीं होता। पूरा await एक direct value read के रूप में resolve होता है।

### जब एक source की आवश्यकता होती है

यदि एक async method suspend होती है (कुछ ऐसा await करती है जो अभी तक complete नहीं है), builder एक pooled `AsyncVlkTaskRunner<TStateMachine>` object (या generic variant के लिए `AsyncVlkTaskRunner<TStateMachine, TResult>`) आवंटित करता है। यह object double duty serve करता है: यह compiler-generated state machine को value से hold करता है और `VlkTask.ISource` implement करता है, ताकि इसे directly task के backing source के रूप में उपयोग किया जा सके। callers को लौटाया गया task इस runner को एक generational `uint` token के साथ wrap करता है।

completion पर, जब caller awaiter पर `GetResult` call करता है, runner खुद को reset करता है और अपने pool में लौट जाता है — इसलिए allocation कई method invocations में amortized होता है।

---

## `ISource` interface

`VlkTask` struct और उसके asynchronous backing object के बीच contract `VlkTask.ISource` है:

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

कोई भी object जो `ISource` implement करता है, एक `VlkTask` को back कर सकता है। library कई implementations ship करती है:

| Type | Purpose |
|------|---------|
| `AsyncVlkTaskRunner<TStateMachine>` | हर `async VlkTask` method को back करता है (internal) |
| `AsyncVlkTaskRunner<TStateMachine, TResult>` | हर `async VlkTask<T>` method को back करता है (internal) |
| `VlkTask.PooledPromise` | automatic pool return के साथ manual completion source |
| `VlkTask.PooledPromise<T>` | उपरोक्त का generic variant |
| `VlkTask.Promise` | pooling के बिना manual completion source (long-lived operations) |
| `VlkTask.Promise<T>` | उपरोक्त का generic variant |

`uint token` parameter एक generational guard है। जब एक pooled source reuse के लिए reset होता है, इसका generation counter increment होता है। पुराने token वाला कोई भी `VlkTask` struct recycled state को silently read करने के बजाय तुरंत `InvalidOperationException` receive करेगा।

---

## `VlkTask` vs `VlkTask<T>`

| Feature | `VlkTask` | `VlkTask<T>` |
|---------|-----------|-------------|
| Return value | कोई नहीं (void equivalent) | `T` |
| Inline result storage | कोई `result` field नहीं | `result` field (type `T`) |
| Awaiter `GetResult` | `void` | `T` लौटाता है |
| Builder type | `AsyncVlkTaskMethodBuilder` | `AsyncVlkTaskMethodBuilder<TResult>` |
| Sync-completed value | `VlkTask.CompletedTask` | `VlkTask.FromResult(value)` |
| Non-generic में convert करें | लागू नहीं | `.AsNonGeneric()` |

`VlkTask` का उपयोग तब करें जब async method का कोई meaningful return value नहीं है, और `VlkTask<T>` का उपयोग तब करें जब यह एक result produce करती है। आप हमेशा `AsNonGeneric()` के माध्यम से `VlkTask<T>` को `VlkTask` में downcast कर सकते हैं जब आपको `WhenAll` जैसे combinators में typed और untyped tasks mix करने की आवश्यकता हो।

---

## async method builder कैसे काम करता है

C# compiler return type पर `[AsyncMethodBuilder(...)]` में named type को देखता है। `VlkTask` के लिए, वह `AsyncVlkTaskMethodBuilder` है। `VlkTask<T>` के लिए, यह `AsyncVlkTaskMethodBuilder<TResult>` है।

builder स्वयं builder object के लिए heap allocation से बचने के लिए एक struct है। इसके दो fields हैं (generic variant के लिए तीन):

```csharp
public struct AsyncVlkTaskMethodBuilder
{
    IStateMachineRunnerPromise runner;   // पहले suspension तक null
    Exception syncException;            // केवल sync-faulted path पर set
}

public struct AsyncVlkTaskMethodBuilder<TResult>
{
    IStateMachineRunnerPromise<TResult> runner;
    Exception syncException;
    TResult result;                     // केवल sync-success path पर set
}
```

### Builder lifecycle

compiler इन methods को क्रम में call करता है:

**1. `Create()`** — एक default builder (सभी fields null/default) लौटाता है। कोई allocation नहीं।

**2. `Start(ref stateMachine)`** — `stateMachine.MoveNext()` को synchronously call करता है। यदि method एक incomplete `await` hit किए बिना complete होती है, `SetResult`/`SetException` call होता है और `runner` null रहता है।

**3. `AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine)`** — जब method एक incomplete `await` encounter करती है तब call होता है। यदि `runner` null है (पहला suspension), यह एक `AsyncVlkTaskRunner` rent या create करता है और state machine को उसमें copy करता है। फिर यह state machine continuation register करने के लिए `awaiter.UnsafeOnCompleted(runner.MoveNextAction)` call करता है।

**4. `SetResult()` / `SetException(exception)`** — runner के `VlkTaskCompletionCore` में completion signal करता है, जो किसी registered awaiter को जगाता है।

**5. `Task` property** — caller द्वारा `VlkTask` value प्राप्त करने के लिए check किया जाता है। sync-success path पर (`runner == null && syncException == null`), `default` लौटाता है (या generic variant के लिए `new VlkTask<T>(result)`) — शून्य allocation। async path पर, runner को source के रूप में wrap करता है।

critical optimization यह है कि `runner` lazily आवंटित होता है। यदि कोई method synchronously complete होती है (cache hits, guards, early returns के लिए सामान्य case), कोई pooled object कभी rent नहीं किया जाता।

---

## `VlkTaskStatus` states

Status `VlkTask` के अंदर nested एक `byte`-sized enum द्वारा represent की जाती है:

```csharp
public enum Status : byte
{
    Pending   = 0,   // अभी तक complete नहीं
    Succeeded = 1,   // normally complete
    Faulted   = 2,   // unhandled exception के साथ complete
    Canceled  = 3    // OperationCanceledException के माध्यम से complete
}
```

आप status directly check कर सकते हैं:

```csharp
VlkTask task = SomeOperation();
VlkTask.Status status = task.GetStatus();

switch (status)
{
    case VlkTask.Status.Pending:
        // अभी चल रहा है — GetResult call नहीं कर सकते
        break;
    case VlkTask.Status.Succeeded:
        // normally complete
        break;
    case VlkTask.Status.Faulted:
        // exception के साथ complete — GetResult rethrow करेगा
        break;
    case VlkTask.Status.Canceled:
        // OperationCanceledException के साथ complete
        break;
}
```

sync-completed fast path के लिए (जहाँ `source == null`), `GetStatus()` किसी भी interface call के बिना `Succeeded` लौटाता है:

```csharp
public Status GetStatus()
{
    if (source == null) return Status.Succeeded;
    return source.GetStatus(token);
}
```

`IsCompleted` property वही pattern follow करती है और किसी भी non-`Pending` state के लिए `true` लौटाती है।

---

## IL2CPP implications

IL2CPP C# को native code के लिए build करने से पहले C++ में compile करता है। Generic value types — structs सहित — generated code में fully specialized हैं, जिसके इस library के लिए महत्वपूर्ण परिणाम हैं।

**State machine specialization.** Compiler प्रत्येक async method के लिए एक unique state machine struct generate करता है। `AsyncVlkTaskRunner<TStateMachine>` इसलिए प्रत्येक async method के लिए भी unique है, और `VlkTaskPool<AsyncVlkTaskRunner<TStateMachine>>` method के अनुसार एक अलग pool है। यह वास्तव में फायदेमंद है: pool incompatible types में कभी shared नहीं होता, किसी भी type confusion के जोखिम को समाप्त करता है।

**State machine का कोई boxing नहीं।** State machine runner object के अंदर value से stored है, boxed नहीं। IL2CPP इसे correctly handle करता है क्योंकि runner एक `sealed class` है जिसमें concrete `TStateMachine` field है।

**Stripping protection.** `[AsyncMethodBuilder]` attribute builder types को alive रखता है। हालांकि, यदि आप IL2CPP में aggressive stripping के साथ interface reference के माध्यम से `VlkTask.ISource` का उपयोग करते हैं, तो `UnaPartidaMas.Valkarn.Tasks` assembly को preserve करने वाला एक `link.xml` entry जोड़ें:

```xml
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```

**`ICriticalNotifyCompletion`.** awaiter structs `ICriticalNotifyCompletion` implement करते हैं, जो compiler को `OnCompleted` के बजाय `UnsafeOnCompleted` call करने के लिए कहता है। "unsafe" variant जानबूझकर `ExecutionContext` capture को skip करता है। यह Unity के लिए correct है — Unity के default configuration में कोई `SynchronizationContext` नहीं है, और एक capture करना बिना किसी benefit के overhead जोड़ेगा।

---

## व्यावहारिक उदाहरण

### बिना allocation के early return

```csharp
// hot path पर synchronously complete होने वाला async VlkTask<int>
async VlkTask<int> GetCachedValue(string key)
{
    if (_cache.TryGetValue(key, out var value))
        return value;            // compiler SetResult(value) call करता है; source null रहता है

    var result = await FetchFromDatabaseAsync(key);
    _cache[key] = result;
    return result;
}
```

जब value cached होती है, method कभी suspend नहीं होती। लौटाए गए `VlkTask<int>` में `source == null` है और result inline carry है। इस path पर कोई heap allocation नहीं होता।

### Await करने से पहले IsCompleted check करना

```csharp
VlkTask<Texture2D> loadTask = LoadTextureAsync("sprites/hero.png");

if (loadTask.IsCompleted)
{
    // पहले से done — GetAwaiter().GetResult() बिना किसी ISource call के inline result पढ़ता है
    Texture2D tex = loadTask.GetAwaiter().GetResult();
    ApplyTexture(tex);
}
else
{
    // वास्तव में async — continuation register करें
    ApplyTextureAsync(loadTask).Forget();
}
```

### Unhandled exceptions observe करना

Faulted tasks जो कभी await नहीं की गईं (fire-and-forget patterns) अपनी exceptions `VlkTask.UnobservedException` event के माध्यम से report करती हैं। यह pooled sources के लिए pool-return time पर deterministically raise होता है, या `Promise`-backed tasks के finalizer से।

```csharp
VlkTask.UnobservedException += ex =>
{
    Debug.LogError($"[VlkTask] Unobserved: {ex}");
};
```

Event thread-safe है; handlers को किसी भी thread से lock-free compare-exchange loop का उपयोग करके add या remove किया जा सकता है।
