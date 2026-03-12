---
sidebar_position: 2
title: IL2CPP Compatibility
---

# IL2CPP Compatibility

Valkarn Tasks ground up से IL2CPP के तहत correctly काम करने के लिए designed है। यह page हर उस measure को describe करती है जो ली गई है और आपको क्या करना है — या नहीं करना है — iOS, WebGL, और consoles जैसे IL2CPP platforms पर safely ship करने के लिए।

---

## IL2CPP को Special Care क्यों चाहिए

IL2CPP C# IL को C++ source code में convert करता है और फिर उसे native compiler के साथ compile करता है। Pipeline की दो features एक async library के लिए relevant हैं:

1. **Code stripping.** Unity का managed code stripper (IL2CPP के linker का उपयोग करके) उन types, methods, और fields को remove करता है जो statically-analysable call graph द्वारा referenced नहीं हैं। Types जो केवल interface dispatch, generic sharing, या reflection के माध्यम से access होते हैं — जिसमें pooled promise classes और `ISource` implementations शामिल हैं — silently stripped हो सकते हैं।

2. **Generic sharing.** IL2CPP हर generic instantiation के लिए एक separate native binary generate नहीं करता। इसके बजाय, यह reference types में code share करता है और value types के लिए specific instantiations उपयोग करता है। यह development (Mono) में bugs hide कर सकता है जो केवल IL2CPP builds में surface होते हैं।

---

## link.xml File

Stripping के विरुद्ध primary defence `link.xml` file है, जो यहाँ located है:

```
Runtime/link.xml
```

इसकी contents:

```xml
<linker>
  <!-- Valkarn.Tasks runtime assembly में सभी types preserve करें।
       IL2CPP code stripping internal types remove कर सकता है जो केवल
       interface dispatch, generic sharing, या reflection के माध्यम से access होते हैं
       (जैसे promise classes, pooled runners, ISource implementations)। -->
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks.Burst" preserve="all"/>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks.ECS" preserve="all"/>
</linker>
```

`preserve="all"` linker को उन assemblies में हर type, method, field, और constructor keep करने का instruction देता है, भले ही static analysis क्या find करे। यह ऐसी library के लिए safest setting है जिसके internal types generic parameters के माध्यम से access होते हैं जिन्हें stripper trace नहीं कर सकता।

Unity `link.xml` files automatically discover और apply करता है जब वे package folder के अंदर placed हों जो project में import हो। कोई manual step आवश्यक नहीं है।

यदि आप package use करने के बजाय source **fork** या **embed** कर रहे हैं, `link.xml` को `Resources`-adjacent folder में copy करें या इसे कहीं भी place करें जहाँ Unity इसे [Unity Managed Code Stripping documentation](https://docs.unity3d.com/Manual/ManagedCodeStripping.html) के अनुसार find कर सके।

---

## Internal Types जो link.xml के बिना Strip होते

निम्नलिखित categories के internal types primary stripping risk हैं:

### Pooled Promise Classes (ISource Implementations)

हर combinator और delay type एक pooled promise class create करता है जो `ValkarnTask.ISource` या `ValkarnTask.ISource<T>` implement करता है:

- `AsyncValkarnTaskRunner<TStateMachine>` — pooled state machine runner, प्रत्येक async method के लिए एक (`TStateMachine` पर specialized)
- `WhenAllPromise<T1, T2>`, `WhenAllArrayPromise<T>`, `WhenAllVoidPromise2`, `WhenAllVoidPromise3`, `WhenAllVoidArrayPromise`
- `DeltaTimeDelayPromise`, `UnscaledDeltaTimeDelayPromise`, `RealtimeDelayPromise`
- `CanceledSource`, `CanceledSource<T>`, `ExceptionSource`, `ExceptionSource<T>`, `NeverSource`
- Channel internals: `BoundedChannel<T>`, `UnboundedChannel<T>`, और उनके reader/writer types

ये types generic factory methods (`ValkarnTaskPool<T>.GetOrCreate`) के माध्यम से instantiate होते हैं। Static analysis call graph एक generic method call पर start होता है और सभी concrete `T` instantiations reliably trace नहीं कर सकता, इसलिए `link.xml` के बिना इनमें से कोई भी type strip हो सकता है।

### ValkarnTaskPool\<T\>

```csharp
internal sealed class ValkarnTaskPool<T> : IPoolInfo where T : class, IPoolNode<T>
```

Pool अपने element type पर generic है। प्रत्येक promise class का अपना static pool field होता है। यदि किसी दिए गए promise type का scene में उपयोग नहीं है, pool और promise class दोनों एक साथ strip हो सकते हैं।

### ValkarnTaskCompletionCore\<T\>

Internal completion core एक value type है जो हर promise के अंदर use होता है। यह continuation callback, token, और completion state hold करता है। Library के बाहर से इसे कभी name से reference नहीं किया जाता।

---

## Generic Type Constraints और IL2CPP

Custom async method builder state machine type पर generic है:

```csharp
public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
    ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : ICriticalNotifyCompletion
    where TStateMachine : IAsyncStateMachine
```

और runner state machine पर generic है:

```csharp
internal sealed class AsyncValkarnTaskRunner<TStateMachine>
    : IStateMachineRunnerPromise, IPoolNode<AsyncValkarnTaskRunner<TStateMachine>>
    where TStateMachine : IAsyncStateMachine
```

IL2CPP के तहत, प्रत्येक distinct `TStateMachine` (क्योंकि state machines structs हैं, जिन्हें full generic specialisation की आवश्यकता होती है) के लिए एक new concrete type generate होता है। इसका मतलब है:

- आपके project में हर `async ValkarnTask` method एक separate `AsyncValkarnTaskRunner<TYourStateMachine>` native type produce करती है।
- यदि stripper सभी instantiations देखने से पहले `AsyncValkarnTaskRunner<T>` remove करता है, कुछ async methods runtime पर crash कर सकते हैं।
- `link.xml` में `preserve="all"` इसे prevent करता है।

---

## Managed Code Stripping Level

Unity का stripping level **Player Settings → Other Settings → Managed Stripping Level** में set होता है।

| Level       | Valkarn Tasks के साथ Status                                                      |
|-------------|----------------------------------------------------------------------------------|
| Disabled    | Safe। कोई stripping नहीं होती।                                                   |
| Low         | Safe। केवल clearly unused assemblies remove करता है।                            |
| Medium      | **`link.xml` के साथ** Safe। Included `link.xml` सभी runtime types preserve करता है। |
| High        | **`link.xml` के साथ** Safe। Same coverage; `link.xml` protection layer है।      |

**High** तक सभी stripping levels safe हैं जब तक `link.xml` file present और applied हो। `link.xml` को बिना यह समझे remove या modify न करें कि आप stripper को किन internal types के सामने expose कर रहे हैं।

---

## [Preserve] Attribute Usage

Runtime source की search से पता चलता है कि individual members पर कोई `[UnityEngine.Scripting.Preserve]` attribute apply नहीं है। Chosen approach per-member attributes के बजाय `link.xml` के माध्यम से assembly-level preservation है। यह intentional है:

- Per-member `[Preserve]` के लिए हर pooled class को individually annotate करना आवश्यक है, जिसमें सभी future additions शामिल हैं।
- `link.xml` में Assembly-level `preserve="all"` simpler, less error-prone है, और future versions में add होने वाले types को cover करने की guarantee देता है।

यदि आपको package-bundled के बजाय larger `link.xml` file में Valkarn Tasks integrate करना है, equivalent directive यह है:

```xml
<assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
```

---

## IL2CPP-First Pool Design

Object pool (`ValkarnTaskPool<T>`) के documentation comment में explicit note है:

> IL2CPP-first: main thread operations use zero atomics.

Pool दो access paths उपयोग करता है:

- **Fast path (main thread):** एक single `fastItem` field plain field access के साथ read/write होता है — कोई `Volatile` या `Interlocked` operations नहीं। यह main thread पर atomic operations का overhead avoid करता है, जहाँ IL2CPP उन्हें हमेशा optimize नहीं कर सकता।
- **Overflow path (any thread):** Background threads से concurrent access के तहत correctness के लिए `Interlocked.CompareExchange` के साथ Treiber lock-free stack (जैसे `ValkarnTask.RunOnThreadPool`)।

Main thread identity `ValkarnTaskPoolShared.MainThreadId` में `volatile int` field में stored है, startup पर `RuntimeInitializeOnLoadMethod` के माध्यम से once publish होती है। IL2CPP `volatile` fields को correctly handle करता है।

---

## Console Platform Considerations

Console platforms (PlayStation, Xbox, Nintendo Switch) exclusively IL2CPP उपयोग करते हैं। निम्नलिखित apply होता है:

- `link.xml` coverage अन्य IL2CPP targets के same है। कोई console-specific additional preservation आवश्यक नहीं है।
- `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` console certification के लिए Unity support करने वाले सभी platforms पर correctly fire होता है।
- `Interlocked` और `Volatile` operations सभी console IL2CPP targets पर supported हैं। Pool का Treiber stack safe है।
- Console platforms पर struct `TStateMachine` instantiations के लिए generic sharing apply होता है। प्रत्येक `async ValkarnTask` method अपना native type generate करती है — यह expected behaviour है और correctly handled है।
- यदि कोई console platform additional AOT requirements enforce करता है, build log में "Stripping assembly: UnaPartidaMas.Valkarn.Tasks" check करके verify करें कि आपका `link.xml` correctly picked up हुआ। यदि assembly strip होती है, `link.xml` discover नहीं हुई थी।

---

## Verify करना कि कुछ Strip नहीं हुआ

IL2CPP target के लिए build करने के बाद:

1. **Build log check करें।** Unity stripping decisions print करता है। `UnaPartidaMas.Valkarn.Tasks` खोजें। यदि आप internal Valkarn types के लिए `Stripping class` messages देखते हैं, तो `link.xml` apply नहीं हुई।

2. **Smoke test run करें।** एक minimal test जो `ValkarnTask.Delay`, `WhenAll`, और custom `ValkarnTaskCompletionSource` exercise करे, सबसे frequently stripped types instantiate करेगा। यदि वे तीन scenarios build survive करते हैं, core intact है।

3. **"Strip Engine Code" selectively enable करें।** यदि आपको High stripping उपयोग करना है और bundled `link.xml` use नहीं कर सकते, incrementally stripping enable करें और प्रत्येक stripping level increase के बाद tests run करें।

4. **Development Build enabled के साथ IL2CPP build।** Development builds additional diagnostics include करते हैं। यदि कोई type missing है, runtime missing type के call site पर `TypeInitializationException` या null reference report करेगा। Stack trace को ऊपर "Internal Types" section में listed types से match करें।

---

## Summary Checklist

- `Runtime/link.xml` package में present है — verify करें कि यह accidentally delete नहीं हुई।
- Managed stripping level किसी भी level पर set हो सकता है; High supported है।
- Application code में कोई `[Preserve]` attributes add करने की आवश्यकता नहीं है।
- कोई AOT hint attributes या code registration calls आवश्यक नहीं हैं।
- Console builds same `link.xml` उपयोग करते हैं; कोई platform-specific additions आवश्यक नहीं हैं।
- यदि आप source fork करते हैं, `link.xml` को साथ carry करें।
