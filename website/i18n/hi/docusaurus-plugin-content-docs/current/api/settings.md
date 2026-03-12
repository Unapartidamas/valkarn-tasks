---
sidebar_position: 4
title: Settings & Result
---

# ValkarnTaskSettings

`ValkarnTaskSettings` एक Unity `ScriptableObject` है जो Valkarn Tasks के runtime behavior को control करता है — primarily object pool जो async state machines और promise objects को recycle करता है play के दौरान garbage से बचने के लिए।

---

## Settings Asset बनाना

1. Project window में, एक `Resources` folder पर right-click करें (यदि आपके पास नहीं है तो एक बनाएँ)।
2. **Assets > Create > Valkarn Tasks > Task Settings** select करें।
3. File को `ValkarnTaskSettings` name दें और `Resources` folder के अंदर place करें।

File का नाम exactly `ValkarnTaskSettings` होना चाहिए और आपके project में कहीं भी `Resources` नामक folder में होनी चाहिए। Asset runtime पर `Resources.Load<ValkarnTaskSettings>("ValkarnTaskSettings")` के साथ load होता है।

यदि कोई asset नहीं मिला, सभी settings अपने built-in defaults पर fall back होते हैं। Library asset के बिना correctly काम करती है — इसे बनाना केवल तब आवश्यक है जब आप defaults बदलना चाहते हैं।

---

## Runtime पर Settings Access करना

Unity builds में, settings एक cached singleton के माध्यम से asset से read होती हैं:

```csharp
ValkarnTaskSettings settings = ValkarnTaskSettings.Instance;
```

commonly needed pool parameters convenience के लिए directly `ValkarnTask` पर भी surfaced हैं:

```csharp
int max      = ValkarnTask.DefaultMaxPoolSize;   // ValkarnTaskSettings.Instance से read करता है
int min      = ValkarnTask.MinPoolSize;
int interval = ValkarnTask.TrimCheckInterval;
```

Non-Unity builds (tests, standalone .NET) में, `ValkarnTaskSettings` mutable properties के साथ एक static class है `ScriptableObject` के बजाय। Same property names apply होते हैं और directly लिखे जा सकते हैं:

```csharp
// केवल Non-Unity / test builds
ValkarnTask.DefaultMaxPoolSize = 512;
ValkarnTask.TrimCheckInterval  = 600;
ValkarnTask.MinPoolSize        = 16;
```

---

## Configurable Properties

### Pool Configuration

#### DefaultMaxPoolSize

| | |
|-|-|
| Type | `int` |
| Default | `256` |
| Valid range | `8` – `1024` |
| Inspector tooltip | "प्रति pool type maximum items। Excess items trim होते हैं।" |

प्रति type प्रत्येक pool में retained maximum objects की संख्या। प्रत्येक distinct generic instantiation (जैसे `PooledPromise<int>`, `PooledPromise<string>`) का अपना pool इस value पर capped है।

जब task complete होता है और उसका internal object pool में return होता है, यदि pool पहले से `DefaultMaxPoolSize` items hold करता है तो returned object discard होता है (GC eligible)। यह burst async activity के बाद unbounded memory growth को रोकता है।

यदि profiling sustained high-throughput async workloads के दौरान frequent GC allocations दिखाता है तो इस value को बढ़ाएँ। Memory pressure concern है और tasks frequently reused नहीं हैं तो घटाएँ।

#### MinPoolSize

| | |
|-|-|
| Type | `int` |
| Default | `8` |
| Valid range | `1` – `64` |
| Inspector tooltip | "Minimum pool size — इससे नीचे कभी shrink न करें।" |

Pool trim pass किसी भी pool को इस count से नीचे कभी reduce नहीं करेगा। यह guarantee करता है कि warm pool baseline हमेशा available है, quiet period के बाद allocation spikes से बचाता है जहाँ trim pass सब कुछ release कर देती।

#### TrimCheckInterval

| | |
|-|-|
| Type | `int` |
| Default | `300` |
| Valid range | `30` – `1000` |
| Inspector tooltip | "Trim checks के बीच frames। 60fps पर 300 ≈ 5 सेकंड।" |

Pool trim passes के बीच कितने frames elapse होते हैं। Trim pass सभी registered pools walk करती है और excess objects (जो `MinPoolSize` से ऊपर हैं) release करती है यदि pool consistently oversized रहा है।

60 fps पर default value 300 लगभग 5 seconds के बीच checks के बराबर है। इस value को lower करें यदि आप अधिक aggressive, frequent trimming चाहते हैं (अधिक frequent trim काम की cost पर)। Raise करें यदि trim passes profiling में spikes दिख रहे हों।

#### TrimHysteresisCount

| | |
|-|-|
| Type | `int` |
| Default | `2` |
| Valid range | `1` – `10` |
| Inspector tooltip | "Trimming से पहले consecutive above-threshold checks की संख्या।" |

Pool केवल तभी trim होता है जब यह इतने consecutive trim cycles के लिए oversized observe किया गया हो। यह thrashing से रोकता है — यदि game में brief spike हो फिर quiet period, hysteresis count 2 का मतलब है pool एक quiet cycle survive करता है objects release शुरू करने से पहले।

#### TrimReleaseRatio

| | |
|-|-|
| Type | `float` |
| Default | `0.25` |
| Valid range | `0.1` – `1.0` |
| Inspector tooltip | "प्रति trim cycle release होने वाला excess का fraction (0.25 = 25%)।" |

जब pool trimmed होता है, excess capacity (`MinPoolSize` से ऊपर items) का यह fraction सभी एक साथ के बजाय per cycle release होता है। `0.25` का value मतलब है प्रत्येक trim pass overage का 25% remove करता है। यह gradual release उस pool size में sudden drop से बचाता है जो load फिर pick up होने पर allocation burst cause कर सकता है।

`1.0` set करें यदि आप चाहते हैं कि सभी excess प्रत्येक cycle पर immediately release हों।

---

### Lifecycle

#### EnableAutoCancel

| | |
|-|-|
| Type | `bool` |
| Default | `true` |
| Inspector tooltip | "MonoBehaviour tasks को destroyCancellationToken से auto-bind करें।" |

Enabled होने पर, `MonoBehaviour` से started tasks automatically उस `MonoBehaviour` के `destroyCancellationToken` से linked होते हैं। यदि task running होते `MonoBehaviour` destroy हो जाता है, task destroyed object के विरुद्ध execute continue करने के बजाय canceled होता है।

इसे केवल तभी disable करें यदि आप manually cancellation manage कर रहे हैं और automatic binding नहीं चाहते।

---

### Error Handling

#### LogUnobservedCancellations

| | |
|-|-|
| Type | `bool` |
| Default | `false` |
| Inspector tooltip | "Unobserved cancellations को warnings के रूप में log करें।" |

Default रूप से, एक task जो canceled है लेकिन कभी awaited नहीं (unobserved cancellation) silently ignored होता है। यह enable करें एक warning log करने के लिए जब ऐसा होता है। Development के दौरान fire-and-forget tasks find करने के लिए useful जो silently cancel होते हैं।

Unobserved faults इस setting के बावजूद `ValkarnTask.UnobservedException` event के माध्यम से हमेशा reported होते हैं।

#### MaxExceptionLogsPerFrame

| | |
|-|-|
| Type | `int` |
| Default | `10` |
| Valid range | `1` – `100` |
| Inspector tooltip | "Spam रोकने के लिए प्रति frame maximum exception logs।" |

Single frame में emitted unobserved exception log entries की संख्या cap करता है। यदि same frame में कई tasks fault होते हैं (उदाहरण के लिए network failure के बाद), यह console को सैकड़ों identical stack traces से flood होने से रोकता है।

---

## Runtime पर Pool Trimming कैसे काम करता है

प्रत्येक Unity player loop update पर, `PlayerLoopHelper` एक frame counter increment करता है। जब counter `TrimCheckInterval` reach करता है, यह `PoolRegistry.TrimAll(MinPoolSize)` call करता है। हर pool जो registered है check करता है कि क्या वह कम से कम `TrimHysteresisCount` consecutive checks के लिए over capacity रहा है। यदि हाँ, तो यह अपने excess का `TrimReleaseRatio` release करता है।

Pools first use पर automatically register होते हैं। `ValkarnTask.GetPoolInfo()` method सभी currently registered pools का एक snapshot return करता है उनके type, current size, और max size के साथ — यही Task Tracker window display करती है।

```
Frame 0 ──────────────────────────────────────────────────────────
  Player loop runs
  Pool A: size 200, max 256 — normal, कोई trim आवश्यक नहीं

Frame 300 ────────────────────────────────────────────────────────
  TrimAll fires
  Pool A: size 18, max 256 — MinPoolSize(8) से ऊपर, लेकिन पहला excess check
  hysteresisCount for A = 1 (अभी threshold 2 पर नहीं)

Frame 600 ────────────────────────────────────────────────────────
  TrimAll fires
  Pool A: size 18, max 256 — MinPoolSize(8) से ऊपर, दूसरा excess check
  hysteresisCount for A = 2 — threshold reached!
  Excess = 18 - 8 = 10; 10 * 0.25 = 2 objects release करें
  Pool A: size 16
```

---

## Task Tracker Window

**Window > Valkarn Tasks > Task Tracker** के माध्यम से खोलें।

Task Tracker एक Editor-only window है जो Play Mode में आपके live pool state दिखाती है। यह configurable interval पर refresh होती है (default 0.5 seconds, toolbar में slider के माध्यम से 0.1 से 5 seconds तक adjustable)।

### Pools tab

हर pool type list करता है जो last domain reload के बाद active रहा है, current size (largest first) के अनुसार sorted। प्रत्येक row दिखाता है:

| Column | विवरण |
|--------|-------------|
| Type | Pooled object का type name, generic arguments expanded के साथ |
| Size | Pool में objects की current संख्या |
| Max | इस pool के लिए `DefaultMaxPoolSize` ceiling |
| Usage | `Size / Max` को percentage के रूप में दिखाने वाला progress bar |

यदि कोई pools अभी तक उपयोग नहीं हुए, एक message पढ़ता है "No pools active. Pools are created on first use."

### Config tab

`ValkarnTask.DefaultMaxPoolSize`, `ValkarnTask.TrimCheckInterval`, और `ValkarnTask.MinPoolSize` से read किए गए तीन live pool parameters दिखाता है। Values केवल Play Mode में दिखाई जाती हैं — Edit Mode में एक note live values देखने के लिए Play Mode enter करने को instruct करता है।

Config tab `ValkarnTaskSettings` asset का reference भी display करता है (यदि `Resources` में एक exist करता है), ताकि आप inspect या modify करने के लिए click through कर सकें। यदि कोई asset नहीं मिला, एक warning आपको एक बनाने की ओर direct करता है।

---

## Result&lt;T&gt;

`Result<T>` throwing के बिना किसी operation के outcome represent करने के लिए एक discriminated union struct है। यह `WhenAll` combinators द्वारा per-task outcomes report करने के लिए उपयोग किया जाने वाला return type है, और general-purpose result pattern के रूप में भी available है।

```csharp
public readonly struct Result<T>
{
    public bool IsSuccess  { get; }
    public bool IsFailure  { get; }   // true यदि faulted या canceled
    public bool IsFaulted  { get; }
    public bool IsCanceled { get; }

    public ValkarnTask.Status Status { get; }
    public T Value    { get; }        // succeeded नहीं होने पर throws
    public Exception Error { get; }   // faulted नहीं होने पर null

    public static Result<T> Success(T value);
    public static Result<T> Failure(string error);    // InvalidOperationException में wrap करता है
    public static Result<T> Faulted(Exception error);
    public static Result<T> Canceled(OperationCanceledException oce = null);

    public static implicit operator bool(Result<T> r);  // succeeded होने पर true
}
```

Void tasks के लिए same shape लेकिन `Value` के बिना एक non-generic `Result` exist करता है।

### AsResult extension methods

अपने code में `try/catch` के बिना किसी `ValkarnTask` या `ValkarnTask<T>` को `Result` में convert करें:

```csharp
public static ValkarnTask<Result<T>> AsResult<T>(this ValkarnTask<T> task);
public static ValkarnTask<Result>    AsResult(this ValkarnTask task);
```

यदि underlying task पहले से synchronously completed है (zero-allocation fast path), `AsResult` async state machine create किए बिना immediately return करता है। अन्यथा यह एक async method में wrap होता है जो `OperationCanceledException` और सभी अन्य exceptions को catch करता है, उन्हें appropriate `Result` variant में translate करता है।

### Result&lt;T&gt; का उपयोग कब करें

`Result<T>` exceptions catch करने के बजाय उपयोग करें जब:

- आप parallel में multiple tasks call कर रहे हैं और बिना whole batch short-circuit किए per-task outcomes चाहते हैं।
- आप fallible operations को type-safe तरीके से express करना चाहते हैं बिना exception control flow के।
- आप एक method से return कर रहे हैं जिसे caller `try/catch` में wrap नहीं करना चाहते।

```csharp
// Multiple tasks fire करें, सभी outcomes regardless of individual failures प्राप्त करें
Result<int>[] results = await ValkarnTask.WhenAll(
    FetchScoreAsync(playerA).AsResult(),
    FetchScoreAsync(playerB).AsResult(),
    FetchScoreAsync(playerC).AsResult()
);

foreach (var r in results)
{
    if (r.IsSuccess)
        Debug.Log($"Score: {r.Value}");
    else if (r.IsFaulted)
        Debug.LogError($"Failed: {r.Error.Message}");
    else
        Debug.Log("रद्द किया गया");
}
```

Result पर branch करने के preferred तरीके `IsSuccess` check करना या implicit `bool` operator उपयोग करना हैं:

```csharp
var result = await SomeOperationAsync().AsResult();

if (result)
{
    Use(result.Value);
}
```

`IsSuccess` `false` होने पर `result.Value` access करने पर `InvalidOperationException` throw होती है।

### Factory methods

| Method | Status set | Error set |
|--------|-----------|-----------|
| `Result<T>.Success(value)` | `Succeeded` | कोई नहीं |
| `Result<T>.Failure(message)` | `Faulted` | `new InvalidOperationException(message)` |
| `Result<T>.Faulted(exception)` | `Faulted` | provided exception |
| `Result<T>.Canceled(oce?)` | `Canceled` | provided `OperationCanceledException`, या `null` |

`Succeeded` property `Result` और `Result<T>` दोनों पर exist करती है लेकिन `[Obsolete]` marked है — consistency के लिए `IsSuccess` उपयोग करें `IsFailure`, `IsFaulted`, और `IsCanceled` के साथ।
