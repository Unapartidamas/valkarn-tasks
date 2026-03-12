---
sidebar_position: 1
title: Analyzer Rules
---

# Analyzer Rules

Valkarn Tasks दो Roslyn analyzer packages ship करता है जो package import होते ही automatically activate हो जाते हैं:

- **`UnaPartidaMas.Valkarn.Tasks.SourceGen.dll`** — ValkarnTask correctness और Unity lifecycle के लिए specific rules। Rule IDs `TT` से शुरू होते हैं।
- **`UnaPartidaMas.Valkarn.Tasks.Analyzer.dll`** — UniTask से migrate हो रहे codebases के लिए migration rules। Rule IDs `MIG` से शुरू होते हैं। ये rules **केवल तभी fire होते हैं जब compilation में `Cysharp.Threading.Tasks.UniTask` present हो**, इसलिए नए projects में ये silent रहते हैं।

दोनों packages pre-compiled DLLs हैं जो package के `Analyzers/` folder में located हैं। Unity उन्हें `.asmdef` reference के माध्यम से Roslyn analyzers के रूप में load करता है; `_TestRunner~` project उन्हें `TestRunner.csproj` में `<Analyzer>` items के माध्यम से load करता है।

---

## Correctness Rules (TT)

ये rules `ValkarnTask` के single-consumption nature और fire-and-forget misuse से related bugs catch करते हैं।

---

### TT001 — ValkarnTask already awaited

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTask            |
| Code fix   | No                     |

`ValkarnTask` single-consumption है: एक बार await होने के बाद, internal token stale हो जाता है। Same variable पर दूसरा `await` उस stale token को encounter करेगा और throw करेगा। Analyzer तब detect करता है जब same local variable, parameter, या `ValkarnTask`/`ValkarnTask<T>` type के field को same method के अंदर एक से अधिक बार await किया जाए।

**इस पर trigger होता है:**
```csharp
async ValkarnTask Bad()
{
    ValkarnTask work = DoWorkAsync();
    await work;   // पहला await — ठीक है
    await work;   // TT001: already awaited
}
```

**Fix:** यदि आपको result पर branch करनी है, पहले await से पहले `.AsResult()` के साथ capture करें, या restructure करें ताकि task exactly एक बार await हो।
```csharp
async ValkarnTask Good()
{
    var result = await DoWorkAsync().AsResult();
    // दोनों branches में result का उपयोग करें
}
```

---

### TT002 — ValkarnTask not awaited or discarded

| Property   | Value                  |
|------------|------------------------|
| Severity   | **Error**              |
| Category   | ValkarnTask            |
| Code fix   | No                     |

एक `ValkarnTask`-returning method call जो expression statement के रूप में use हो — न await, न assign, और न `.Forget()` के साथ — एक silent bug है। Task के अंदर throw हुई exceptions कभी observed नहीं होतीं, और task का pooled state machine कभी pool में return नहीं होता।

Analyzer expression statements check करता है जहाँ expression type `ValkarnTask` या `ValkarnTask<T>` resolve होती है। यह skip करता है:
- Assignment expressions (`tasks[i] = DoWork()`)
- `.Forget()` में ending वाली chains (intentional fire-and-forget)

**इस पर trigger होता है:**
```csharp
void Bad()
{
    LoadDataAsync();      // TT002: awaited या discarded नहीं
    ProcessItemAsync();   // TT002
}
```

**Fix — await करें:**
```csharp
async ValkarnTask Good()
{
    await LoadDataAsync();
    await ProcessItemAsync();
}
```

**Fix — explicit fire-and-forget:**
```csharp
void GoodFireAndForget()
{
    LoadDataAsync().Forget();
}
```

---

### TT013 — ValkarnTask returned but never consumed

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTask            |
| Code fix   | No                     |

यह rule ID **future data-flow analysis के लिए reserved है**। इसका उद्देश्य assigned-but-never-awaited pattern catch करना है — `ValkarnTask` को variable में store करना और फिर कभी await या discard न करना — जो TT002 cover नहीं करता क्योंकि TT002 केवल bare expression statements examine करता है।

Current implementation कोई syntax actions register नहीं करती। Data-flow analysis implement होने पर, TT013 TT002 को complement करेगा इसे covering करके:
```csharp
async ValkarnTask Bad()
{
    ValkarnTask task = DoWorkAsync();  // assign लेकिन कभी await नहीं
    DoOtherThing();
    // task abandoned — TT013 (future)
}
```

`[FireAndForget]` से decorated methods exempt होंगे।

---

## Lifecycle Rules (TT)

ये rules MonoBehaviour lifetime management और cancellation से relate करते हैं।

---

### TT010 — Auto-cancel active for MonoBehaviour async method

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTask            |
| Code fix   | No                     |

एक informational hint। `UnityEngine.MonoBehaviour` से inherit करने वाले class के अंदर कोई भी `async ValkarnTask` (या `async ValkarnTask<T>`) method automatically cancel होगा जब object destroy होगा, **जब तक** method `[NoAutoCancel]` से decorated न हो। यह diagnostic IDE में उस behavior को visible बनाती है बिना developer को उसे याद रखने की आवश्यकता के।

**इस पर trigger होता है:**
```csharp
public class MyBehaviour : MonoBehaviour
{
    async ValkarnTask LoadLevel()  // TT010: auto-cancel active
    {
        await ValkarnTask.Delay(2000);
    }
}
```

**Opt out करने के लिए** (और उस method के लिए TT010 suppress करने के लिए), `[NoAutoCancel]` add करें:
```csharp
[NoAutoCancel]
async ValkarnTask LoadLevel(CancellationToken ct)
{
    await ValkarnTask.Delay(2000, ct);
}
```

---

### TT011 — WhenAll mixes different lifetime scopes

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTask            |
| Code fix   | No                     |

`ValkarnTask.WhenAll` को different lifetime scopes के tasks के साथ call करने पर surprising partial-cancellation behavior हो सकता है। यदि एक task `MonoBehaviour` से bound है (उस class के instance method द्वारा return जो `MonoBehaviour` inherit करता है) और दूसरा unbound है (static task, या non-MonoBehaviour class से), object destroy होने पर एक task cancel होगा लेकिन दूसरा नहीं, combinator को indeterminate state में छोड़ देगा।

**इस पर trigger होता है:**
```csharp
// मान लें EnemyAI : MonoBehaviour
await ValkarnTask.WhenAll(
    enemyAI.PatrolAsync(),   // bound: Destroy पर auto-cancel
    GlobalMusic.FadeAsync()  // unbound: indefinitely जीता है
);
// TT011: WhenAll lifetimes mix करता है — PatrolAsync() vs GlobalMusic.FadeAsync()
```

**Fix — दोनों tasks को shared cancellation token दें:**
```csharp
using var cts = new CancellationTokenSource();
await ValkarnTask.WhenAll(
    enemyAI.PatrolAsync(cts.Token),
    GlobalMusic.FadeAsync(cts.Token)
);
```

---

### TT012 — Async loop without cancellation check

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTask            |
| Code fix   | No                     |

एक `async ValkarnTask` method जिसमें `for`, `while`, `foreach`, या `do`-`while` loop है जिसके body में कोई cancellation check नहीं है, एक "zombie loop" है: यदि lifecycle cancellation trigger होती है (जैसे MonoBehaviour destroy होता है), auto-cancel signal loop break नहीं कर सकता। Loop तब भी run करता रहता है जब owning object gone हो।

Analyzer loop body को cancellation check वाला मानता है यदि इसमें निम्नलिखित में से कोई भी हो:
- एक `await` expression (awaited operation token observe कर सकता है और `OperationCanceledException` throw कर सकता है)
- `ThrowIfCancellationRequested` (identifier या member access के रूप में)
- `IsCancellationRequested` (identifier या member access के रूप में)

Check nested lambdas या local functions में **descend नहीं** करता।

**इस पर trigger होता है:**
```csharp
async ValkarnTask PollForever(CancellationToken ct)
{
    while (true)                     // TT012: body में कोई cancellation check नहीं
    {
        ProcessNextItem();
        Thread.Sleep(16);            // synchronous — await नहीं
    }
}
```

**Fix — await या explicit check add करें:**
```csharp
async ValkarnTask PollForever(CancellationToken ct)
{
    while (true)
    {
        ct.ThrowIfCancellationRequested();
        ProcessNextItem();
        await ValkarnTask.Yield();       // check को satisfy भी करता है
    }
}
```

---

### TT014 — [NoAutoCancel] without CancellationToken parameter

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTask            |
| Code fix   | No                     |

`MonoBehaviour` में `async ValkarnTask` method पर `[NoAutoCancel]` method को automatic lifecycle cancellation से opt out करता है। लेकिन यदि method में कोई `CancellationToken` parameter नहीं है, तो उसके पास cancellation observe करने का कोई mechanism नहीं है, जो attribute को pointless बनाता है और लगभग निश्चित रूप से forgotten parameter indicate करता है।

**इस पर trigger होता है:**
```csharp
public class Enemy : MonoBehaviour
{
    [NoAutoCancel]
    async ValkarnTask Chase()      // TT014: [NoAutoCancel] लेकिन CancellationToken parameter नहीं
    {
        await ValkarnTask.Delay(1000);
    }
}
```

**Fix — `CancellationToken` parameter add करें:**
```csharp
[NoAutoCancel]
async ValkarnTask Chase(CancellationToken ct)
{
    await ValkarnTask.Delay(1000, ct);
}
```

---

## Informational Rules (TT)

---

### TT015 — Awaitable bridge adapter generated

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTask            |
| Code fix   | No                     |

जब आप `async ValkarnTask` method के अंदर Unity `Awaitable` (`UnityEngine` से) को `await` करते हैं, Valkarn Tasks `AwaitableBridge` के माध्यम से automatically एक bridge adapter generate करता है। यह diagnostic informational है: यह confirm करता है कि bridge in effect है। कोई action आवश्यक नहीं है।

**इस पर trigger होता है:**
```csharp
async ValkarnTask LoadScene()
{
    await SceneManager.LoadSceneAsync("Main");  // TT015: bridge adapter generated
}
```

कोई change आवश्यक नहीं। Bridge conversion transparently handle करता है।

---

## Code Quality Rules (TT)

---

### TT016 — Async method without await

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTask            |
| Code fix   | No                     |

एक `async ValkarnTask` (या `async ValkarnTask<T>`) method जिसमें कोई `await` expressions नहीं हैं — `await foreach`, `await using`, और `using` declarations के अंदर `await` सहित — बिना किसी benefit के state machine allocation का full overhead incur करता है। Compiler फिर भी state machine generate करता है, लेकिन चूँकि कोई suspension point नहीं है, method हमेशा synchronously complete होती है।

Analyzer check करता है: `AwaitExpressionSyntax`, `await` keyword वाले `foreach`, `await` keyword वाले `using` declarations, और `await` keyword वाले `using` statements।

**इस पर trigger होता है:**
```csharp
async ValkarnTask<int> ComputeTotal()  // TT016: body में कोई await नहीं
{
    return items.Sum(x => x.Value);
}
```

**Fix — `async` remove करें और completed task return करें:**
```csharp
ValkarnTask<int> ComputeTotal()
{
    return ValkarnTask.FromResult(items.Sum(x => x.Value));
}
```

या void return के लिए:
```csharp
ValkarnTask DoSetup()
{
    Initialize();
    return ValkarnTask.CompletedTask;
}
```

---

### TT017 — [FireAndForget] on ValkarnTask\<T\>

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTask            |
| Code fix   | No                     |

`[FireAndForget]` signal करता है कि एक method intentionally result await किए बिना run होती है। इसे `ValkarnTask<T>` (typed result) return करने वाले method पर apply करने पर `T` हमेशा discard होता है, जो typed return को meaningless बनाता है। Method को `ValkarnTask` (void) return करनी चाहिए।

**इस पर trigger होता है:**
```csharp
[FireAndForget]
async ValkarnTask<int> SendReport()   // TT017: return value discarded है
{
    await UploadAsync();
    return 42;                     // यह 42 कभी देखा नहीं जाता
}
```

**Fix — return type को `ValkarnTask` में बदलें:**
```csharp
[FireAndForget]
async ValkarnTask SendReport()
{
    await UploadAsync();
}
```

---

## Migration Rules (MIG)

Migration analyzer **केवल तभी activate होता है जब compilation में `Cysharp.Threading.Tasks.UniTask` type present हो**। Rules UniTask से Valkarn Tasks में transition guide करने के लिए informational या warnings हैं। इनमें से किसी भी rule में auto-fixes नहीं हैं; नीचे descriptions manual change explain करती हैं।

---

### MIG001 — UniTask type detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTaskMigration   |

`UniTask` type (struct itself या `UniTask<T>`) का उपयोग detect हुआ। इसे `ValkarnTask` या `ValkarnTask<T>` equivalent से replace करें।

```csharp
// पहले
UniTask<Sprite> LoadSprite(string path) { ... }

// बाद में
ValkarnTask<Sprite> LoadSprite(string path) { ... }
```

---

### MIG002 — cancelImmediately parameter is unnecessary

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTaskMigration   |

UniTask के `Delay` और अन्य time-based methods fast cancellation opt into करने के लिए `cancelImmediately` parameter accept करते थे। Valkarn Tasks default रूप से immediately cancel करता है — कोई `cancelImmediately` parameter नहीं है। Argument remove करें।

```csharp
// पहले
await UniTask.Delay(1000, cancelImmediately: true, cancellationToken: ct);

// बाद में
await ValkarnTask.Delay(1000, ct);
```

---

### MIG003 — SuppressCancellationThrow() detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTaskMigration   |

UniTask का `.SuppressCancellationThrow()` cancelled task को throwing के बिना `(bool isCancelled, T result)` tuple में convert करता था। Valkarn Tasks same purpose के लिए `.AsResult()` उपयोग करता है, जो `Result<T>` struct return करता है।

```csharp
// पहले
var (isCancelled, value) = await myUniTask.SuppressCancellationThrow();

// बाद में
var result = await myValkarnTask.AsResult();
if (result.IsCanceled) { ... }
T value = result.Value;
```

---

### MIG004 — Awaitable return type detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTaskMigration   |

एक method Unity का `Awaitable` type return करती है। इसे `ValkarnTask` से replace करने पर विचार करें, जो Valkarn Tasks runtime के साथ natively integrate होता है और auto-cancellation, pooling, और full combinator set support करता है।

---

### MIG005 — SingleConsumerUnbounded channel detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTaskMigration   |

UniTask का `Channel.CreateSingleConsumerUnbounded<T>()` बिना backpressure का channel create करता था। Load के तहत unbounded memory growth prevent करने के लिए इसे `ValkarnTask.Channel.CreateBounded<T>(capacity)` से replace करने पर विचार करें।

```csharp
// पहले
var ch = Channel.CreateSingleConsumerUnbounded<Event>();

// बाद में (backpressure के साथ)
var ch = ValkarnTask.Channel.CreateBounded<Event>(capacity: 256);

// या यदि unbounded intentional है
var ch = ValkarnTask.Channel.CreateUnbounded<Event>();
```

---

### MIG006 — UniTask PlayerLoopTiming detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTaskMigration   |

`Cysharp.Threading.Tasks` namespace से `PlayerLoopTiming` enum value detect हुई। `using` directive को `UnaPartidaMas.Valkarn.Tasks` में बदलें; enum value names identical हैं।

```csharp
// पहले
using Cysharp.Threading.Tasks;
timing = PlayerLoopTiming.Update;

// बाद में
using UnaPartidaMas.Valkarn.Tasks;
timing = PlayerLoopTiming.Update;  // same name, different namespace
```

---

### MIG007 — async UniTaskVoid detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTaskMigration   |

`async UniTaskVoid` UniTask का fire-and-forget method pattern था। Valkarn Tasks इसे दो options से replace करता है:

```csharp
// पहले
async UniTaskVoid StartLoadAsync() { ... }

// बाद में — option 1: [FireAndForget] attribute
[FireAndForget]
async ValkarnTask StartLoadAsync() { ... }

// बाद में — option 2: standard ValkarnTask + call site पर .Forget()
async ValkarnTask StartLoadAsync() { ... }
// इस प्रकार call करें:
StartLoadAsync().Forget();
```

---

### MIG008 — Awaitable MainThreadAsync() detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTaskMigration   |

`Awaitable.MainThreadAsync()` Unity के `Awaitable` API में main thread पर वापस switch करने के लिए use होता था। Valkarn Tasks PlayerLoop integration के माध्यम से default रूप से continuations main thread पर run करता है, इसलिए explicit `MainThreadAsync()` calls आमतौर पर unnecessary हैं और हटाए जा सकते हैं।

---

### MIG009 — UniTask.RunOnThreadPool detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTaskMigration   |

`ValkarnTask.RunOnThreadPool` से replace करें। API same है।

```csharp
// पहले
await UniTask.RunOnThreadPool(() => HeavyWork());

// बाद में
await ValkarnTask.RunOnThreadPool(() => HeavyWork());
```

---

### MIG010 — .ToCoroutine() detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTaskMigration   |

`.ToCoroutine()` legacy coroutine callers के लिए UniTask bridge था। Consuming code को `async ValkarnTask` method के रूप में rewrite करें।

```csharp
// पहले
IEnumerator LegacyCaller() { yield return MyUniTask().ToCoroutine(); }

// बाद में
async ValkarnTask ModernCaller() { await MyValkarnTask(); }
```

---

### MIG011 — UniTask.Create() detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTaskMigration   |

`UniTask.Create(Func<UniTask>)` एक factory delegate wrap करता था। Manually controlled completion के लिए `ValkarnTask.Promise<T>` pattern से replace करें।

```csharp
// पहले
var task = UniTask.Create(async () => { await DoWork(); return 42; });

// बाद में
var promise = new ValkarnTaskCompletionSource<int>();
DoWorkThenComplete(promise);
var task = promise.Task;
```

---

### MIG012 — UniTask.Lazy/Defer detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTaskMigration   |

`UniTask.Lazy<T>` और `UniTask.Defer` exist करते थे allocation avoid करने के लिए जब task synchronously complete हो सकता था। Valkarn Tasks का zero-allocation synchronous fast path built-in है: `ValkarnTask.CompletedTask` या `ValkarnTask.FromResult(value)` return करने पर कभी allocate नहीं होता। `Lazy`/`Defer` wrappers remove करें।

---

### MIG013 — .ToUniTask()/.AsUniTask() detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTaskMigration   |

Unity `Awaitable` से UniTask में conversion calls। इन्हें remove करें; Valkarn Tasks `Awaitable` natively bridge करता है (TT015 देखें)।

---

### MIG014 — UniTaskAsyncEnumerable detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Warning                |
| Category   | ValkarnTaskMigration   |

UniTask का अपना `IUniTaskAsyncEnumerable<T>` और `UniTaskAsyncEnumerable` utilities थे। इसके बजाय `System.Linq.Async` के साथ BCL से `IAsyncEnumerable<T>` उपयोग करें। `IAsyncEnumerable<T>` C# `await foreach` द्वारा natively supported है।

```csharp
// पहले
IUniTaskAsyncEnumerable<int> GetItems() { ... }

// बाद में
IAsyncEnumerable<int> GetItems() { ... }
```

---

### MIG015 — TimeoutController detected

| Property   | Value                  |
|------------|------------------------|
| Severity   | Info                   |
| Category   | ValkarnTaskMigration   |

UniTask का `TimeoutController` reusable timeouts के लिए एक helper था। Standard `CancellationTokenSource` से replace करें जो `TimeSpan` के साथ constructed हो, जिसे BCL directly support करता है।

```csharp
// पहले
var controller = new TimeoutController();
var ct = controller.Timeout(TimeSpan.FromSeconds(5));

// बाद में
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var ct = cts.Token;
```

---

## Rules Suppress करना

Standard Roslyn suppression mechanisms सभी rules के लिए काम करते हैं:

```csharp
// एक single occurrence inline suppress करें
#pragma warning disable TT012
while (true) { DoSomething(); }
#pragma warning restore TT012
```

या `.editorconfig` के माध्यम से project-wide suppress करने के लिए:
```ini
[*.cs]
dotnet_diagnostic.TT012.severity = none
```

Migration rules (`MIG*`) को same तरीके से suppress किया जा सकता है, या migration complete होने के बाद `.editorconfig` में severity को `none` set करके globally disable किया जा सकता है।
