---
sidebar_position: 2
title: Object Pooling
---

# Object Pooling

Valkarn Tasks common async paths पर GC allocations को pooling के माध्यम से eliminate करता है — वे objects जो हर `VlkTask` को back करते हैं। यह page pool architecture explain करती है — objects कैसे store होते हैं, कैसे acquire और return किए जाते हैं, और system क्या lifecycle guarantees प्रदान करता है।

---

## Overview

जब एक `async` method suspend होती है, library को compiler-generated state machine store करने और एक completion mechanism की आवश्यकता होती है जिसे awaiter subscribe कर सके। `System.Threading.Tasks` में, यह `Task` object है — per call एक heap allocation। Valkarn Tasks में, यह role pooled objects द्वारा play की जाती है जो `VlkTask.ISource` implement करते हैं।

Pool design के तीन goals हैं:

1. **मुख्य thread hot path पर zero atomics।** Unity का game loop convention से single-threaded है। मुख्य thread से Rent और return plain reads और writes होने चाहिए।
2. **Safe cross-thread access।** `VlkTask.Run` का उपयोग करने वाले background tasks thread-pool threads पर operate करते हैं। Pool concurrent rent/return को correctly handle करना चाहिए।
3. **Adaptive trimming के साथ bounded growth।** Pools को traffic spike के बाद बिना limit के बढ़ना नहीं चाहिए, लेकिन इतने aggressively shrink भी नहीं होने चाहिए कि वे constantly re-allocate करें।

---

## `VlkTaskPool<T>`

`VlkTaskPool<T>` core pool class है। यह `internal sealed` है — आप इसके साथ directly interact नहीं करते, लेकिन इसे समझने से पता चलता है कि आपके allocations कहाँ जाते हैं।

```
VlkTaskPool<T>
  |
  +-- fastItem: T            (single-slot cache, केवल मुख्य thread, plain read/write)
  |
  +-- stackHead: T           (Treiber stack head, cross-thread safety के लिए CAS-based)
  +-- stackSize: int
  |
  +-- maxSize: int           (VlkTask.DefaultMaxPoolSize द्वारा bounded)
  +-- totalCreated: int      (trim ratio के लिए lifetime allocations track करता है)
```

### Fast slot (मुख्य thread)

`fastItem` field most recently returned object के लिए एक single reserved slot है। मुख्य thread पर, rent और return एक plain read और write है — कोई atomics नहीं, कोई spinning नहीं। यह Unity game-loop operations की overwhelming majority को cover करता है।

```
Rent (मुख्य thread):
  fastItem != null  →  इसे लें (fastItem = null), return करें   [zero atomics]
  fastItem == null  →  Treiber stack पर fall through

Return (मुख्य thread):
  fastItem == null  →  fastItem = item                        [zero atomics]
  fastItem != null  →  Treiber stack पर fall through
```

### Treiber stack (overflow / background threads)

जब fast slot occupied हो (या जब calling thread मुख्य thread न हो), pool lock-free Treiber stack का उपयोग करता है — compare-and-swap (CAS) का उपयोग करके एक classic intrusive linked list:

```
Rent (कोई भी thread):
  while (true):
    head = Volatile.Read(stackHead)
    if head == null: null return (pool empty)
    next = head.NextNode
    if CAS(stackHead, next, head) == head: head return करें  // race जीती
    spinner.SpinOnce()                                   // हारे, retry करें

Return (कोई भी thread):
  if stackSize >= maxSize: false return (pool full, item drop करें)
  while (true):
    head = Volatile.Read(stackHead)
    item.NextNode = head
    if CAS(stackHead, item, head) == head: stackSize++; true return करें
    spinner.SpinOnce()
```

Stack intrusive है: प्रत्येक pooled object अपना `NextNode` pointer store करता है, इसलिए कोई external wrapper node आवश्यक नहीं। यह `IPoolNode<T>` interface द्वारा enforce किया जाता है।

### Thread routing

```csharp
internal static class VlkTaskPoolShared
{
    internal static volatile int MainThreadId;
}
```

सभी pool instances एक single `MainThreadId` share करते हैं। Rent/return operations correct path पर route करने के लिए `Thread.CurrentThread.ManagedThreadId == MainThreadId` check करते हैं। `volatile` field startup पर ID publish होने के बाद cross-thread visibility सुनिश्चित करता है।

---

## `IPoolNode<T>`

Pool में participate करने वाले किसी भी type को यह interface implement करनी होगी:

```csharp
internal interface IPoolNode<T> where T : class
{
    ref T NextNode { get; }
}
```

`ref T NextNode` object के अंदर उस field का reference लौटाता है जो next-pointer store करता है। Pool इस field को ref के माध्यम से directly write करता है, किसी separate wrapper node को eliminate करता है। Library में सभी pooled types — runners, promises, combinators — इस interface को एक private field declare करके और इसे expose करके implement करते हैं:

```csharp
// PooledPromise<T> से उदाहरण
PooledPromise<T> nextNode;
public ref PooledPromise<T> NextNode => ref nextNode;
```

---

## Pool lifecycle: acquire, use, return

एक pooled object का पूरा lifecycle है:

```
Caller async method invoke करता है
  |
  +--> AsyncVlkTaskMethodBuilder.AwaitUnsafeOnCompleted()
        |
        +--> runner == null? हाँ --> Pool.TryRent() ?? Pool.TrackNew(new Runner())
              |
              +--> runner builder में stored; state machine runner में copied
              |
              +--> awaiter.UnsafeOnCompleted(runner.MoveNextAction)
              |
              [... async work proceeds ...]
              |
              +--> runner.SetResult() / runner.SetException()
                    |
                    +--> VlkTaskCompletionCore.TrySetResult() --> continuation invoke
                          |
                          +--> caller का awaiter GetResult(token) call करता है
                                |
                                +--> core.GetResult(token) -- result read या rethrow
                                |
                                +--> TryReturn():
                                      stateMachine = default
                                      core.Reset()           // generation increment
                                      Pool.TryReturn(this)
```

`TryReturn` method हमेशा `core.Reset()` call करने से पहले state machine clear करती है। यह ordering matter करता है: `Reset()` generation counter increment करता है, concurrent renters को slot available दिखाता है। यदि state machine `Reset()` के बाद clear किया जाता, तो दूसरे thread पर एक renter slot obtain कर सकता था और उसका state machine overwrite हो सकता था।

---

## `VlkTaskCompletionCore<TResult>`

`VlkTaskCompletionCore<TResult>` एक `internal struct` है जो हर pooled object के अंदर embedded है। यह promise का actual state machine है — completion state track करता है, results और errors store करता है, और `OnCompleted` (continuation register करना) और `TrySetResult` (completion signal करना) के बीच race resolve करता है।

```
Fields:
  result:          TResult     -- success value
  error:           object      -- ExceptionDispatchInfo या OperationCanceledException
  errorKind:       byte        -- 0=none, 1=faulted, 2=canceled(OCE), 3=canceled(EDI)
  generation:      int         -- monotonically increasing; token comparison के लिए uint में cast
  completedCount:  int         -- 0=pending, 1=claimed, 2=completed (two-phase publish)
  continuation:    Action<object>
  continuationState: object
  hasUnhandledError: bool
```

### Two-phase completion protocol

Completion ARM64 पर safe होने के लिए two-phase CAS का उपयोग करती है जहाँ store-release / load-acquire pairs आवश्यक हैं:

```
TrySetResult(value):
  Phase 1: CAS(completedCount, 0 -> 1)   -- exclusive ownership claim
  Phase 2: result write
  Phase 3: Volatile.Write(completedCount, 2)  -- release semantics के साथ publish
  Phase 4: InvokeContinuation()
```

Readers `Volatile.Read(completedCount)` (acquire semantics) का उपयोग करते हैं result read करने से पहले, यह सुनिश्चित करते हुए कि वे Phase 2 में लिखा गया value देखते हैं।

### `OnCompleted` और `TrySetResult` के बीच race resolution

तीन patterns हो सकते हैं:

```
Pattern A — OnCompleted पहले:
  OnCompleted CAS के माध्यम से continuation store करता है (null -> cont)
  TrySetResult non-null continuation read करता है -> invoke करता है

Pattern B — TrySetResult पहले (sync fast path):
  TrySetResult CAS के माध्यम से ContinuationSentinel place करता है (null -> sentinel)
  OnCompleted sentinel read करता है -> continuation inline immediately invoke करता है

Pattern C (concurrent race):
  C.1: OnCompleted CAS जीतता है -> TrySetResult इसे read करता है -> invoke करता है
  C.2: TrySetResult CAS जीतता है (sentinel place करता है) -> OnCompleted sentinel detect करता है -> inline invoke करता है
```

Sentinel एक static `Action<object>` object है जिसे purely marker value के रूप में उपयोग किया जाता है — यह वास्तव में कभी delegate के रूप में invoke नहीं होता।

### Token validation और ABA safety

`GetStatus`, `GetResult`, और `OnCompleted` के हर call current `generation` के विरुद्ध `uint token` validate करते हैं। जब `Reset()` `Interlocked.Increment(ref generation)` call करता है, पुराना token hold करने वाला कोई भी outstanding `VlkTask` struct recycled state पर silently operate करने के बजाय `InvalidOperationException` receive करेगा। एक 32-bit generation counter wrap around (एक single slot के ~4 billion reuses की आवश्यकता) व्यवहार में effectively impossible माना जाता है।

### Reset और unobserved error reporting

`Reset()` pool-return time पर call होता है। Generation increment करने से पहले, यह check करता है कि क्या कोई error stored थी लेकिन कभी observed नहीं हुई (यानी, fault के बाद `GetResult` कभी call नहीं हुआ)। यदि हाँ, तो यह `VlkTask.UnobservedException` के माध्यम से exception publish करता है। Cancellation errors केवल तभी report की जाती हैं जब `VlkTaskSettings` में `LogUnobservedCancellations` enabled हो, क्योंकि cancellation अक्सर intentional होती है।

Non-pooled `Promise` और `Promise<T>` objects के लिए, unobserved error reporting finalizer से `ReportUnobservedIfNeeded()` के माध्यम से होती है, जो state clear किए बिना same logic follow करती है।

---

## Pool configuration

तीन settings pool sizing control करती हैं। Unity builds में ये एक `VlkTaskSettings` ScriptableObject asset से read होती हैं (fallback defaults के साथ), और runtime पर static properties के माध्यम से override की जा सकती हैं:

```csharp
// प्रति pool type (प्रति TStateMachine या प्रति promise type) maximum objects
VlkTask.DefaultMaxPoolSize = 256;   // default: 256

// Trim checks के बीच कितने frames (Unity PlayerLoop frames)
VlkTask.TrimCheckInterval = 300;    // default: 300 (~60fps पर 5 सेकंड)

// Trim pass के बाद रखने के लिए minimum objects
VlkTask.MinPoolSize = 8;            // default: 8
```

`DefaultMaxPoolSize` pool construction time पर applied ceiling है। यह globally नहीं, per pool instance enforce होता है — `AsyncVlkTaskRunner<LoadSceneStateMachine>` का pool और `AsyncVlkTaskRunner<FetchDataStateMachine>` का pool प्रत्येक का अपना ceiling है।

### Pool trimming

`PlayerLoopHelper` हर `TrimCheckInterval` frames पर मुख्य thread पर `PoolRegistry.TrimAll(minPoolSize)` invoke करता है। प्रत्येक pool एक hysteresis strategy का उपयोग करता है:

```
प्रत्येक trim check:
  currentSize = fastItem.count + stackSize
  if currentSize <= MinPoolSize: consecutive count reset, skip

  ratio = currentSize / totalCreated
  if ratio > 0.5 (pool > 50% ever-created objects hold करता है):
    trimConsecutiveCount++
    if trimConsecutiveCount >= hysteresisThreshold:
      stack से excess objects का कुछ fraction (releaseRatio) release करें
      (fastItem preserved है — यह most cache-friendly slot है)
  else:
    trimConsecutiveCount reset
```

Hysteresis एक brief traffic spike को immediately सभी objects को release करने से रोकता है। Fast slot हमेशा trimming के दौरान preserve होता है क्योंकि यह most recently used item है और इसलिए दोबारा चाहिए जाने की सबसे अधिक संभावना है।

---

## `PoolRegistry` और monitoring

हर `VlkTaskPool<T>` construction time पर global `PoolRegistry` के साथ खुद को register करता है। Registry `IPoolInfo` references की एक list maintain करती है, जो expose करती हैं:

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

आप public API का उपयोग करके runtime पर सभी active pools enumerate कर सकते हैं:

```csharp
foreach (var (type, size, maxSize) in VlkTask.GetPoolInfo())
{
    Debug.Log($"{type.Name}: {size}/{maxSize}");
}
```

यही data Unity Editor में **Task Tracker** window द्वारा surface किया जाता है। Window `GetPoolInfo()` poll करती है और pool occupancy की एक live table display करती है, जिससे आप देख सकते हैं कि pools warmed up हैं, कोई type consistently अपनी ceiling hit कर रही है, और trimming expected तरीके से काम कर रही है।

Dead pool entries (जहाँ `IsAlive` false लौटाता है) `GetAll()` और `TrimAll()` calls के दौरान registry list से lazily pruned होते हैं, registry को indefinitely grow होने से रोकते हैं।

---

## `PooledPromise` और `PooledPromise<T>`

ये pooled completion sources हैं जो custom async patterns में उपयोग के लिए intended हैं — उदाहरण के लिए, callback-based API या repeating producer/consumer channel को wrap करना।

```csharp
// pool से एक pending promise acquire करें
var promise = VlkTask.PooledPromise<string>.Create(out uint token);
VlkTask<string> task = promise.Task;

// task consumer को दें
// ... बाद में, किसी भी thread से ...
promise.TrySetResult("hello");

// जब consumer task await करता है और GetResult call होता है,
// promise reset होता है और automatically pool में लौट जाता है।
```

मुख्य विशेषताएँ:

- `Create(out uint token)` pool से rent करता है या pool द्वारा tracked एक new instance allocate करता है।
- `CreateCompleted(T result, out uint token)` वही करता है लेकिन immediately result signal करता है, ताकि task पहले से complete हो जब return हो।
- Backing task पर `GetResult` call होने के बाद, `TryReturn()` fires: promise `core.Reset()` call करता है और खुद को pool में return करता है।
- एक double-return guard (`Interlocked.Exchange(ref returned, 1)`) pool corruption को रोकता है यदि `GetResult` दो बार call हो।

**Non-pooled alternative: `Promise` और `Promise<T>`.** ये heap-allocated classes हैं जो pool में return नहीं होतीं। इनका उपयोग long-lived operations के लिए करें जहाँ lifetime unpredictable है या जहाँ same source को multiple await cycles survive करना है। वे unobserved exceptions report करने के लिए finalizer पर rely करते हैं।

---

## Combinator pools

`WhenAll` और `WhenAny` combinators भी pools का उपयोग करते हैं। प्रत्येक arity और type combination का अपना pool है:

| Combinator | Pool type |
|-----------|-----------|
| `WhenAll(task1, task2)` (typed) | `VlkTaskPool<WhenAllPromise<T1, T2>>` |
| `WhenAll(IEnumerable<VlkTask<T>>)` | `VlkTaskPool<WhenAllArrayPromise<T>>` |
| `WhenAll(task1, task2)` (void) | `VlkTaskPool<WhenAllVoidPromise2>` |
| `WhenAny(task1, task2)` (typed) | `VlkTaskPool<WhenAnyPromise2<T>>` |
| `WhenAny(IEnumerable<VlkTask<T>>)` | `VlkTaskPool<WhenAnyArrayPromise<T>>` |

Array-based combinators (`WhenAll<T>(IEnumerable<...>)` और `WhenAny<T>(IEnumerable<...>)`) अपने internal source/token arrays के लिए `System.Buffers.ArrayPool<T>.Shared` का उपयोग करते हैं, इसलिए वे arrays भी per call fresh allocate होने के बजाय recycled होते हैं।

सभी combinators same zero-alloc short-circuit apply करते हैं: यदि `WhenAll` या `WhenAny` call होने के point पर सभी inputs synchronously completed हैं, तो कोई new pooled object कभी create नहीं होता।

```csharp
// शून्य allocation — दोनों tasks sync-completed हैं
var t1 = VlkTask.FromResult(1);
var t2 = VlkTask.FromResult(2);
VlkTask<(int, int)> combined = VlkTask.WhenAll(t1, t2);
// combined.source == null; result (1, 2) inline stored है
```
