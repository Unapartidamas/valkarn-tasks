---
sidebar_position: 5
title: आर्किटेक्चर
---

# आर्किटेक्चर

Valkarn Tasks के आंतरिक का तकनीकी अवलोकन।

---

## उच्च-स्तरीय संरचना

```
┌─────────────────────────────────────────────────────────────────┐
│                    कम्पाइल टाइम                                   │
│                                                                   │
│  ┌──────────────┐  ┌──────────────────┐  ┌───────────────────┐  │
│  │  Lifecycle    │  │  Awaitable       │  │  Diagnostics      │  │
│  │  Analyzer     │  │  Bridge Gen      │  │  (TT001–TT017)    │  │
│  └──────────────┘  └──────────────────┘  └───────────────────┘  │
│                                                                   │
│  ┌──────────────┐  ┌──────────────────┐                          │
│  │  Job Bridge   │  │  Combinator      │                          │
│  │  Gen          │  │  Gen             │                          │
│  └──────────────┘  └──────────────────┘                          │
├─────────────────────────────────────────────────────────────────┤
│                    रनटाइम                                         │
│                                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐  │
│  │ ValkarnTask │  │ Result<T>  │  │ ValkarnPool│  │ Completion│  │
│  │  struct    │  │  struct    │  │  bounded   │  │  Core<T>  │  │
│  └────────────┘  └────────────┘  └────────────┘  └───────────┘  │
│                                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐  │
│  │ PlayerLoop │  │Continuation│  │  Channels  │  │ TestClock │  │
│  │  Helper    │  │   Queue    │  │            │  │           │  │
│  └────────────┘  └────────────┘  └────────────┘  └───────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### असेंबली लेआउट

```
ValkarnTask.Runtime      — गेम के साथ शिप होता है
ValkarnTask.SourceGen    — केवल कम्पाइल-टाइम (सोर्स जेनरेटर)
ValkarnTask.Analyzer     — केवल कम्पाइल-टाइम (डायग्नोस्टिक्स + कोड फिक्स)
ValkarnTask.Testing      — TestClock + टेस्ट यूटिलिटीज़
```

---

## ValkarnTask स्ट्रक्चर

```csharp
[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask
{
    internal readonly ValkarnTask.ISource source;
    internal readonly ulong token; // पैक्ड: हाई 32 बिट्स = जेनरेशन, लो 32 = स्लॉट इंडेक्स
}

[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
{
    internal readonly ValkarnTask.ISource<T> source;
    internal readonly T result;  // सिंक फास्ट पाथ पर inline
    internal readonly ulong token;
}
```

**मुख्य इनवेरिएंट:** `source == null` का मतलब है टास्क बिना किसी एरर के सिंक्रोनसली पूर्ण हुई — कोई हीप ऑब्जेक्ट शामिल नहीं। `ValkarnTask.CompletedTask` `default(ValkarnTask)` है; `ValkarnTask.FromResult(value)` परिणाम inline स्टोर करता है।

### जेनरेशनल टोकन

```csharp
// पैक
ulong token = ((ulong)generation << 32) | slotIndex;

// अनपैक
uint slotIndex  = (uint)(token & 0xFFFFFFFF);
uint generation = (uint)(token >> 32);
```

हर ISource कॉल पर वेलिडेट: `slots[slotIndex].generation == expectedGeneration`। एक रीसाइकल्ड पूल स्लॉट के पुराने संदर्भ से तुरंत `InvalidOperationException` थ्रो होता है। प्रति स्लॉट 4 अरब जेनरेशन — व्यवहार में टकराव असंभव है। (UniTask `short` उपयोग करता है — ~18 मिनट के बाद टकराव।)

---

## ISource कॉन्ट्रैक्ट

```csharp
public interface ISource
{
    Status GetStatus(ulong token);
    void   GetResult(ulong token);
    void   OnCompleted(Action<object> continuation, object state, ulong token);
    Status UnsafeGetStatus();
}

public interface ISource<out T> : ISource
{
    new T GetResult(ulong token);
}
```

`ISource` को इम्प्लीमेंट करने वाली कोई भी ऑब्जेक्ट `ValkarnTask` को बैक कर सकती है। बिल्ट-इन इम्प्लीमेंटेशन:

| टाइप | उद्देश्य |
|------|---------|
| `AsyncValkarnRunner<TStateMachine>` | हर `async ValkarnTask` मेथड को बैक करता है |
| `AsyncValkarnRunner<TStateMachine, T>` | हर `async ValkarnTask<T>` मेथड को बैक करता है |
| `ValkarnTask.PooledPromise[<T>]` | मैन्युअल कम्प्लीशन, ऑटो पूल रिटर्न |
| `ValkarnTask.Promise[<T>]` | मैन्युअल कम्प्लीशन, पूल नहीं (लंबे समय तक) |
| `ExceptionSource` | `FromException` को बैक करता है |
| `CanceledSource` | `FromCanceled` को बैक करता है |
| `NeverSource` | Singleton — Pending से कभी ट्रांज़िशन नहीं |

---

## Async मेथड बिल्डर

C# कम्पाइलर कस्टम async रिटर्न टाइप के लिए बिल्डर प्रोटोकॉल चलाता है:

```
Create()
  └─ struct बिल्डर रिटर्न करता है (शून्य आवंटन)

Start(ref stateMachine)
  └─ स्टेट मशीन सिंक्रोनसली चलाता है
     ├─ बिना सस्पेंड के पूर्ण → SetResult(), runner null रहता है
     │  └─ Task default(ValkarnTask) रिटर्न करता है  ← शून्य आवंटन
     └─ अधूरे await से टकराता है → AwaitUnsafeOnCompleted()
           └─ पूल से AsyncValkarnRunner रेंट करता है
              स्टेट मशीन को runner में कॉपी करता है (वैल्यू से, boxing नहीं)
              awaitable पर continuation रजिस्टर करता है
              └─ Task runner को ISource के रूप में रैप करता है  ← async पाथ
```

बिल्डर खुद एक `struct` है — केवल बिल्डर के लिए कोई आवंटन नहीं। `runner` **lazy** आवंटित होता है: यदि कोई मेथड सिंक्रोनसली पूर्ण होती है, तो कोई पूल रेंट नहीं होता।

---

## स्टेट मशीन रनर और पूल

`AsyncValkarnRunner<TStateMachine>` कम्पाइलर-जेनरेटेड स्टेट मशीन को **वैल्यू से** (boxing के बिना) होल्ड करता है और `ISource` के रूप में कार्य करता है। पहले सस्पेंशन पर `ValkarnPool<T>` से रेंट होता है और `GetResult` पर रिटर्न होता है।

चूंकि `TStateMachine` प्रति async मेथड एक अद्वितीय टाइप है (प्रति क्लोज़्ड जेनेरिक इंस्टेंशिएशन), C# जेनेरिक स्पेशलाइज़ेशन के माध्यम से हर async मेथड को अपना पूल स्वचालित रूप से मिलता है।

### ValkarnPool\<T\>

| संदर्भ | संरचना | कारण |
|--------|--------|-----|
| Unity मेन थ्रेड | सिंगल-थ्रेडेड स्टैक | कोई सिंक्रोनाइज़ेशन नहीं — सबसे तेज़ संभव |
| बैकग्राउंड थ्रेड | Treiber लॉक-फ्री स्टैक | CAS ऑपरेशन, कोई लॉक नहीं |

थ्रेड संदर्भ `Thread.CurrentThread.IsBackground` के माध्यम से डिटेक्ट होता है। पूल शेप (क्षमता, ट्रिम रेट) `ValkarnTaskSettings` के माध्यम से कॉन्फिगर।

---

## ValkarnCompletionCore\<T\>

हर `ISource` इम्प्लीमेंटेशन के अंदर साझा स्टेट:

- वर्तमान `Status` (Pending / Succeeded / Faulted / Canceled)
- परिणाम मूल्य (जेनेरिक सोर्स)
- एक्सेप्शन या `OperationCanceledException` (एरर पाथ)
- रजिस्टर्ड continuation delegate + स्टेट

स्टेट ट्रांज़िशन `Interlocked.CompareExchange` उपयोग करते हैं — लॉक-फ्री, थ्रेड-सेफ। एक **डबल-कम्प्लीट गार्ड** सुनिश्चित करता है कि केवल पहला `TrySet*` कॉल जीते; बाद के कॉल साइलेंट no-ops हैं।

---

## PlayerLoop इंटीग्रेशन

`PlayerLoopHelper` स्टार्टअप पर Unity के PlayerLoop में लाइटवेट रनर कॉलबैक इंसर्ट करता है (`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]`)।

हर `PlayerLoopTiming` वैल्यू एक फेज के अनुरूप होती है। जब `await ValkarnTask.Yield(timing)` कॉल होता है, continuation उस फेज के रनर में कतारबद्ध होती है और अगली बार Unity इसे रीच करे तब डिस्पैच होती है।

```
Initialization  →  EarlyUpdate  →  FixedUpdate  →  PreUpdate
→  Update  →  PreLateUpdate  →  PostLateUpdate  →  TimeUpdate
  (+ हर फेज के लिए Last* वेरिएंट)
```

---

## सोर्स जेनरेटर

Roslyn सोर्स जेनरेटर कम्पाइल टाइम पर चलता है। `async ValkarnTask` मेथड वाले हर `partial` क्लास के लिए जो `MonoBehaviour` एक्सटेंड करता है, यह एक partial क्लास फाइल जेनरेट करता है जो:

1. `_valkarnCancelToken` फील्ड डिक्लेयर करता है
2. `Awake` में इसे `destroyCancellationToken` से असाइन करता है
3. हर async मेथड को रैप करता है टोकन को स्वचालित रूप से थ्रेड करने के लिए

जेनरेटेड फाइल कभी debugger में नहीं दिखती और कभी यूज़र सोर्स को मॉडिफाई नहीं करती।

जेनरेटर यह भी प्रोड्यूस करता है:

- **Awaitable ब्रिज एडेप्टर** — जब `async ValkarnTask` के अंदर `Awaitable` await होती है
- **Job async रैपर** — जब `IJob` / `IJobParallelFor` टाइप्स डिटेक्ट होते हैं
- **कम्बाइनर पूल** — 2–8 arity टपल के लिए टाइप्ड `WhenAll`/`WhenAny` सोर्स

---

## Roslyn एनालाइज़र

17 `DiagnosticAnalyzer` नियम `Analyzers/netstandard2.0/` में शिप होते हैं। ये Unity Editor में और CI पर C# कम्पाइलर पास के दौरान चलते हैं:

- सभी टाइप रेज़ोल्यूशन के लिए `SemanticModel` उपयोग करते हैं (स्ट्रिंग मिलान नहीं)
- `ValkarnTypeHelper` साझा यूटिलिटी किसी भी `ValkarnTask` वेरिएंट को डिटेक्ट करती है
- ज़ॉम्बी-लूप एनालाइज़र सही तरीके से नेस्टेड लोकल फंक्शन और लैम्बडा को स्किप करता है
- माइग्रेशन एनालाइज़र (MIG001–MIG015) केवल तभी ऑटो-एक्टिवेट होते हैं जब UniTask / Awaitable रेफरेंस हों — अन्यथा निष्क्रिय

---

## Burst और ECS लेयर

तीन वैकल्पिक मॉड्यूल, प्रत्येक `#if` define चेक द्वारा गार्डेड:

| मॉड्यूल | आवश्यक | उद्देश्य |
|--------|--------|---------|
| `JobBridge` | `Unity.Jobs` | `JobHandle` को awaitable के रूप में रैप करता है; प्रत्येक PlayerLoop टिक पर `handle.IsCompleted` पोल करता है |
| `AsyncSystemBase` | `Unity.Entities` | async सपोर्ट के साथ ECS सिस्टम बेस क्लास |
| `BurstScheduler` | `Unity.Burst` + `Unity.Collections` | async संदर्भ से Burst जॉब्स शेड्यूल करता है; `NativeTimerHeap` प्रबंधित करता है |

`NativeTimerHeap` उच्च-सटीकता टाइमर के लिए Burst-कम्पैटिबल min-heap है जो मैनेजड हीप आवंटन से पूरी तरह बचता है।

---

## एडिटर इंटीग्रेशन

**Valkarn Hub** (`Tools → Valkarn → Hub`) सभी इंस्टॉल किए गए Valkarn पैकेज को स्वचालित रूप से खोजने के लिए `TypeCache.GetTypesDerivedFrom<IValkarnEditorPanel>()` उपयोग करता है। कोई मैन्युअल रजिस्ट्रेशन आवश्यक नहीं।

`TasksTrackerPanel` हर 0.5 सेकंड (कॉन्फिगर करने योग्य) पर पूल डायग्नोस्टिक्स रिफ्रेश करने के लिए `EditorApplication.update` की सदस्यता लेता है और त्वरित पहुंच के लिए `ValkarnTaskSettings` एसेट संदर्भ सामने लाता है।

---

## IL2CPP संबंधी विचार

- स्टेट मशीन रनर के अंदर **वैल्यू से** स्टोर होती है — कोई boxing नहीं, IL2CPP सही तरीके से हैंडल करता है
- हर async मेथड का रनर एक अलग जेनेरिक स्पेशलाइज़ेशन है — टाइप-सेफ, कोई क्रॉस-कंटेमिनेशन नहीं
- Awaiter structs `ICriticalNotifyCompletion` इम्प्लीमेंट करती हैं — कम्पाइलर `UnsafeOnCompleted` कॉल करता है, `ExecutionContext` कैप्चर स्किप करते हुए (Unity के डिफ़ॉल्ट कॉन्फिग में कोई ओवरहेड नहीं)
- यदि aggressive stripping सक्षम है, तो runtime assembly प्रिज़र्व करें:

```xml
<!-- link.xml -->
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```
