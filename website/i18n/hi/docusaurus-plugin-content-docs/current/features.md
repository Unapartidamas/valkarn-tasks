---
sidebar_position: 4
title: फीचर्स
---

# फीचर्स

Valkarn Tasks के लिए पूर्ण फीचर संदर्भ।

---

## मूल async प्रिमिटिव

### ValkarnTask स्ट्रक्चर

एक शून्य-आवंटन, struct-आधारित async रिटर्न टाइप जो `UniTask` और Unity के `Awaitable` दोनों को प्रतिस्थापित करता है।

```csharp
async ValkarnTask LoadLevel() { ... }
async ValkarnTask<int> CountEnemies() { ... }
```

- **शून्य-आवंटन सिंक्रोनस फास्ट पाथ** — यदि मेथड कभी सस्पेंड हुए बिना पूर्ण होती है, तो हीप आवंटन शून्य होता है।
- **पूल्ड async पाथ** — यदि मेथड सस्पेंड होती है, तो स्टेट मशीन रनर एक बाउंडेड, श्रिंकेबल पूल से लिया जाता है। शून्य boxing, स्वचालित ट्रिमिंग के साथ बाउंडेड पूल।
- **IL2CPP-first पूलिंग** — मेन थ्रेड पर पूल ऑपरेशन शून्य atomics उपयोग करते हैं। IL2CPP Mono की तुलना में `Volatile` को 9.2× और `Interlocked` को 2.9× पेनल्टी देता है; Valkarn हॉट पाथ पर दोनों से बचता है।
- **जेनरेशनल टोकन सेफ्टी** — प्रति पूल स्लॉट एक `uint` जेनरेशन काउंटर। टकराव से पहले प्रति स्लॉट 4,294,967,296 साइकिल — व्यवहार में असंभव। (UniTask एक `short` टोकन उपयोग करता है: ~18 मिनट सक्रिय async काम के बाद टकराव।)

### Result\<T\> — exception-मुक्त एरर हैंडलिंग

```csharp
var result = await loadTask.AsResult();

if (result.Succeeded)    Use(result.Value);
else if (result.IsCanceled) ShowRetry();
else                         Debug.LogError(result.Error);
```

`Result<T>` और `Result` `readonly struct` वैल्यू हैं जो बिना थ्रो किए टास्क के परिणाम को दर्शाते हैं। दोनों `bool` में इम्प्लिसिट कनवर्शन सपोर्ट करते हैं (सफल होने पर true)।

### मैन्युअल कम्प्लीशन सोर्स

```csharp
var promise = new ValkarnTask.Promise<string>();
promise.TrySetResult("done");
string value = await promise.Task;
```

`TrySetResult`, `TrySetException`, और `TrySetCanceled` को सपोर्ट करता है। Finalizer-आधारित अनऑब्ज़र्व्ड एक्सेप्शन रिपोर्टिंग सुनिश्चित करती है कि एरर कभी चुपके से न खो जाएं।

### पूल्ड कम्प्लीशन सोर्स

दोहराने वाले पैटर्न के लिए ऑटो-रीसेट पूल्ड वेरिएंट (चैनल और कम्बाइनर द्वारा आंतरिक रूप से उपयोग):

```csharp
var source = ValkarnTask.PooledPromise<int>.Create(out uint token);
source.TrySetResult(42);
int value = await source.Task; // source स्वचालित रूप से पूल में वापस जाता है
```

---

## Awaitable ब्रिज

Unity के `Awaitable` के साथ पारदर्शी इंटरऑप — कोई मैन्युअल कनवर्शन नहीं:

```csharp
async ValkarnTask LoadGame()
{
    await SceneManager.LoadSceneAsync("Level2"); // Unity Awaitable — सीधे काम करता है
    await Resources.LoadAsync<Texture2D>("hero"); // Unity Awaitable — सीधे काम करता है
    await ValkarnTask.Delay(1000);                // Valkarn नेटिव
}
```

सोर्स जेनरेटर `Awaitable` awaits का पता लगाता है और एडेप्टर स्वचालित रूप से जेनरेट करता है।

---

## लाइफसाइकिल कैंसलेशन

### स्वचालित (सोर्स-जेनरेटेड)

क्लास को `partial` मार्क करें — सोर्स जेनरेटर बाकी करता है:

```csharp
public partial class EnemySpawner : MonoBehaviour
{
    async ValkarnTask Start()
    {
        while (true)
        {
            await ValkarnTask.Delay(3000);
            SpawnWave();
            // जब यह GameObject नष्ट होता है तो स्वचालित रूप से कैंसल।
        }
    }
}
```

- **MonoBehaviour** — `destroyCancellationToken` से बाउंड
- **ScriptableObject** — एप्लिकेशन लाइफटाइम से बाउंड
- **Plain class** — कोई स्वचालित बाइंडिंग नहीं (मैन्युअल टोकन आवश्यक)

### ऑप्ट-आउट

```csharp
[NoAutoCancel]
async ValkarnTask BackgroundSync()
{
    await SyncToServer(); // नष्ट होने पर ऑटो-कैंसल नहीं
}
```

### मैन्युअल CancellationToken ओवरराइड

एक स्पष्ट `CancellationToken` पास करना ऑटो-इंजेक्टेड लाइफसाइकिल टोकन को ओवरराइड करता है:

```csharp
async ValkarnTask DoWork(CancellationToken ct)
{
    await ValkarnTask.Delay(1000, ct);
}
```

### कोई सिब्लिंग कैंसलेशन नहीं

टास्क कभी सिब्लिंग टास्क को कैंसल नहीं करते। `WhenAll` सभी टास्क का इंतजार करता है; `WhenAny` पहला परिणाम लौटाता है लेकिन हारने वाले टास्क चलते रहते हैं। यह डेटा करप्शन को रोकता है जब टास्क के साइड इफेक्ट होते हैं।

---

## क्रिटिकल सेक्शन

उन ऑपरेशनों के लिए जो लाइफसाइकिल कैंसलेशन द्वारा बाधित नहीं होनी चाहिए:

```csharp
async ValkarnTask SaveProgress()
{
    var data = await LoadPlayerData();        // कैंसल योग्य

    await using (ValkarnTask.Critical())
    {
        await db.Insert(data);               // GO नष्ट होने पर भी कैंसल नहीं होता
        await db.Commit();
    }                                        // पेंडिंग कैंसलेशन यहाँ लागू होता है

    await SendNotification();                // फिर से कैंसल योग्य
}
```

क्रिटिकल सेक्शन के अंदर, कैंसलेशन **स्थगित** है — नज़रअंदाज़ नहीं। जब सेक्शन समाप्त होता है, पेंडिंग कैंसलेशन लागू होता है।

---

## कम्बाइनर

### WhenAll (टाइप्ड)

```csharp
// डायरेक्ट — किसी टास्क के फेल होने पर थ्रो करता है
var (enemies, map) = await ValkarnTask.WhenAll(LoadEnemies(), LoadMap());

// सेफ — नॉन-थ्रोइंग बिहेवियर के लिए AsResult() से रैप करें
var (a, b) = await ValkarnTask.WhenAll(
    LoadEnemies().AsResult(), LoadMap().AsResult());
```

जब सभी टास्क पहले से पूर्ण हों तो शून्य-आवंटन सिंक फास्ट पाथ। `IEnumerable<ValkarnTask<T>>` ओवरलोड आंतरिक एरे के लिए `ArrayPool<T>` उपयोग करता है।

### WhenAll (void)

```csharp
await ValkarnTask.WhenAll(SaveA(), SaveB(), SaveC());
await ValkarnTask.WhenAll(taskList); // IEnumerable<ValkarnTask>
```

### WhenAny

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(DownloadFromA(), DownloadFromB());
```

पहला पूर्ण परिणाम लौटाता है। हारने वाले टास्क स्वाभाविक रूप से चलते रहते हैं।

### Fire-and-forget

```csharp
SendAnalytics("event").Forget();

[FireAndForget]
async ValkarnTask SendAnalytics(string eventName) { ... }
// कॉलर को .Forget() की जरूरत नहीं — कोई वार्निंग जेनरेट नहीं
```

`.Forget()` एरर को `ValkarnTask.UnobservedException` पर रूट करता है। कभी चुपके से नहीं निगला जाता।

### AsNonGeneric

```csharp
ValkarnTask voidTask = typedTask.AsNonGeneric();
```

---

## समय और देरी

```csharp
await ValkarnTask.Delay(1000);                                    // मिलीसेकंड
await ValkarnTask.Delay(TimeSpan.FromSeconds(2));                  // TimeSpan
await ValkarnTask.Delay(1000, DelayType.UnscaledDeltaTime);       // timescale नज़रअंदाज़ करें
await ValkarnTask.Delay(1000, DelayType.Realtime);                 // Stopwatch-आधारित

await ValkarnTask.Yield();                                         // अगला PlayerLoop टिक
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);             // विशिष्ट टाइमिंग
await ValkarnTask.NextFrame();                                      // गारंटीड अगला फ्रेम
await ValkarnTask.DelayFrame(5);                                    // N फ्रेम

await ValkarnTask.WaitUntil(() => player.IsReady);
await ValkarnTask.WaitWhile(() => isLoading);
```

---

## थ्रेड स्विचिंग

```csharp
async ValkarnTask ProcessData()
{
    var raw = await DownloadData();

    await ValkarnTask.SwitchToThreadPool();
    var processed = HeavyComputation(raw);  // बैकग्राउंड थ्रेड

    await ValkarnTask.SwitchToMainThread();
    ApplyToGameObject(processed);           // मेन थ्रेड
}
```

---

## 16 PlayerLoop टाइमिंग

| ग्रुप | टाइमिंग |
|------|---------|
| Initialization | `Initialization`, `LastInitialization` |
| EarlyUpdate | `EarlyUpdate`, `LastEarlyUpdate` |
| FixedUpdate | `FixedUpdate`, `LastFixedUpdate` |
| PreUpdate | `PreUpdate`, `LastPreUpdate` |
| Update | `Update`, `LastUpdate` |
| PreLateUpdate | `PreLateUpdate`, `LastPreLateUpdate` |
| PostLateUpdate | `PostLateUpdate`, `LastPostLateUpdate` |
| TimeUpdate | `TimeUpdate`, `LastTimeUpdate` |

सभी ऑपरेशन डिफ़ॉल्ट रूप से `PlayerLoopTiming.Update` उपयोग करते हैं जब तक अन्यथा निर्दिष्ट न हो।

---

## चैनल

```csharp
// अनबाउंडेड
var channel = ValkarnTask.Channel.CreateUnbounded<EnemySpawnRequest>();

// बाउंडेड — भरने पर backpressure
var channel = ValkarnTask.Channel.CreateBounded<LogEntry>(capacity: 100);

// मल्टी-कंज़्यूमर
var channel = ValkarnTask.Channel.CreateUnbounded<WorkItem>(multiConsumer: true);

// प्रोड्यूसर
await channel.Writer.WriteAsync(entry);
bool accepted = channel.Writer.TryWrite(entry);

// कंज़्यूमर
await foreach (var item in channel.Reader.ReadAllAsync())
    Process(item);

// कम्प्लीशन
channel.Writer.Complete();
await channel.Reader.Completion;
```

---

## डिटर्मिनिस्टिक टेस्टिंग (TestClock)

```csharp
[Test]
public void Respawn_WaitsThreeSeconds()
{
    var clock = new TestClock();
    var task = spawner.RespawnEnemy();

    clock.Advance(TimeSpan.FromSeconds(2));
    Assert.IsFalse(task.IsCompleted);

    clock.Advance(TimeSpan.FromSeconds(1));
    Assert.IsTrue(task.IsCompleted);
}
```

सभी टाइम-डिपेंडेंट ऑपरेशन `TimeProvider.Current` से पढ़ते हैं। टेस्ट में, इसे `TestClock` से बदलें। `AdvanceFrame()` एक single player loop टिक सिमुलेट करता है।

---

## Job System ब्रिज

```csharp
var job = new PathfindingJob { start = a, end = b, results = results };
await job.ScheduleAsync();
// await के तुरंत बाद results NativeArray पठनीय है

await job.ScheduleParallelAsync(dataCount, batchSize); // IJobParallelFor

// कैंसलेशन — कैंसलेशन रिपोर्ट करने से पहले job handle पूर्ण करें (कोई job लीक नहीं)
await job.ScheduleAsync(cancellationToken);
```

---

## कम्पाइल-टाइम डायग्नोस्टिक्स

| कोड | गंभीरता | विवरण |
|----|---------|-------|
| TT001 | वार्निंग | `ValkarnTask` पर डबल-await / use-after-free |
| TT002 | एरर | `async ValkarnTask` परिणाम एक्सप्रेशन स्टेटमेंट के रूप में उपयोग — await या `.Forget()` होना चाहिए |
| TT010 | इन्फो | MonoBehaviour में async मेथड Destroy पर ऑटो-कैंसल |
| TT011 | वार्निंग | `WhenAll` में अलग-अलग लाइफटाइम वाले टास्क हैं |
| TT012 | वार्निंग | कैंसलेशन चेक के बिना async लूप (संभावित ज़ॉम्बी लूप) |
| TT013 | वार्निंग | `ValkarnTask` रिटर्न किया लेकिन कभी await नहीं और स्पष्ट रूप से डिस्कार्ड नहीं |
| TT014 | वार्निंग | मैन्युअल `CancellationToken` पैरामीटर के बिना `[NoAutoCancel]` |
| TT015 | इन्फो | `ValkarnTask` के अंदर `Awaitable` await करना — ब्रिज एडेप्टर जेनरेट |
| TT016 | वार्निंग | बिना await एक्सप्रेशन के async मेथड |
| TT017 | वार्निंग | `ValkarnTask<T>` पर `[FireAndForget]` — रिटर्न वैल्यू डिस्कार्ड करता है |

---

## पूल प्रबंधन

हर async मेथड रनर `ValkarnPool<T>` के माध्यम से पूल किया जाता है:

- **मेन थ्रेड** — लॉक-फ्री स्टैक, शून्य atomics
- **बैकग्राउंड थ्रेड** — CAS ऑपरेशन के साथ Treiber लॉक-फ्री स्टैक
- **फ्रेम-आधारित ट्रिमिंग** — हर 300 फ्रेम (~5s at 60fps), अधिक ऑब्जेक्ट धीरे-धीरे रिलीज़ होते हैं
- **कभी नहीं घटाता** कॉन्फिगर करने योग्य न्यूनतम से नीचे (डिफ़ॉल्ट: 8)

रनटाइम पर मॉनिटर करें:

```csharp
foreach (var (type, size, maxSize) in ValkarnTask.GetPoolInfo())
    Debug.Log($"{type}: {size}/{maxSize}");
```

---

## ValkarnTaskSettings

`ScriptableObject` के माध्यम से कॉन्फिगर करें (`Assets > Create > Valkarn > Tasks > Task Settings`, `Resources/` में रखें):

| सेटिंग | डिफ़ॉल्ट | विवरण |
|--------|---------|-------|
| `DefaultMaxPoolSize` | 256 | प्रति पूल टाइप अधिकतम आइटम |
| `MinPoolSize` | 8 | इससे नीचे कभी ट्रिम न करें |
| `TrimCheckInterval` | 300 | ट्रिम चेक के बीच फ्रेम |
| `TrimHysteresisCount` | 2 | ट्रिमिंग से पहले लगातार चेक |
| `TrimReleaseRatio` | 0.25 | प्रति साइकिल रिलीज़ किया गया अधिक का अंश |
| `EnableAutoCancel` | true | MonoBehaviour टास्क को destroyCancellationToken से ऑटो-बाइंड |
| `LogUnobservedCancellations` | false | अनऑब्ज़र्व्ड कैंसलेशन को वार्निंग के रूप में लॉग करें |
| `MaxExceptionLogsPerFrame` | 10 | प्रति फ्रेम एक्सेप्शन लॉग कैप |

---

## एरर हैंडलिंग

```csharp
// अनऑब्ज़र्व्ड एक्सेप्शन — पूल रिटर्न पर डिटर्मिनिस्टिकली फायर
ValkarnTask.UnobservedException += ex =>
    Debug.LogError($"Unobserved: {ex}");
```

- `WhenAll` — पहला एक्सेप्शन थ्रो करता है, अतिरिक्त को `UnobservedException` पर रूट करता है
- `WhenAny` — विजेता का एक्सेप्शन थ्रो होता है; हारने वालों की फॉल्ट `UnobservedException` पर जाती है
- लाइफसाइकिल कैंसलेशन — `OperationCanceledException` डिफ़ॉल्ट रूप से सप्रेस (कॉन्फिगर करने योग्य)

### फैक्ट्री मेथड

```csharp
ValkarnTask.CompletedTask          // void, शून्य-आवंटन
ValkarnTask.FromResult<T>(value)   // टाइप्ड, शून्य-आवंटन
ValkarnTask.FromException(ex)      // फॉल्टेड
ValkarnTask.FromException<T>(ex)
ValkarnTask.FromCanceled()         // कैंसल्ड
ValkarnTask.FromCanceled<T>()
ValkarnTask.Never                  // कभी पूर्ण नहीं होता (WhenAny के लिए sentinel)
```
