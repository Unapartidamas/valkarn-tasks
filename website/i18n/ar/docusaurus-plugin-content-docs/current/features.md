---
sidebar_position: 4
title: الميزات
---

# الميزات

مرجع الميزات الكامل لـ Valkarn Tasks.

---

## البنية الأساسية غير المتزامنة

### بنية ValkarnTask

نوع إرجاع async قائم على struct بدون تخصيص للذاكرة، يحل محل كل من `UniTask` و`Awaitable` الخاص بـ Unity.

```csharp
async ValkarnTask LoadLevel() { ... }
async ValkarnTask<int> CountEnemies() { ... }
```

- **مسار سريع متزامن بدون تخصيص** — إذا اكتملت الطريقة دون تعليق مطلقاً، لا تخصيص على الكومة.
- **مسار async مع pool** — إذا تعلقت الطريقة، يُؤخذ runner آلة الحالة من pool محدود وقابل للتقليص. لا boxing، مجمعات محدودة مع قص تلقائي.
- **تجميع IL2CPP-first** — عمليات pool على الخيط الرئيسي تستخدم صفر عمليات ذرية. يُعاقب IL2CPP على `Volatile` بمقدار 9.2× وعلى `Interlocked` بمقدار 2.9× مقارنة بـ Mono؛ يتجنب Valkarn كليهما في المسار الساخن.
- **أمان الرمز المميز الجيلي** — عداد جيل `uint` لكل فتحة pool. 4,294,967,296 دورة لكل فتحة قبل التصادم — مستحيل عملياً. (يستخدم UniTask رمزاً مميزاً `short`: التصادم بعد ~18 دقيقة من العمل غير المتزامن النشط.)

### Result\<T\> — معالجة أخطاء بدون استثناءات

```csharp
var result = await loadTask.AsResult();

if (result.Succeeded)    Use(result.Value);
else if (result.IsCanceled) ShowRetry();
else                         Debug.LogError(result.Error);
```

`Result<T>` و`Result` هي قيم `readonly struct` تمثل نتيجة مهمة دون رمي استثناءات. كلاهما يدعم التحويل الضمني إلى `bool` (صحيح عند النجاح).

### مصادر الإكمال اليدوي

```csharp
var promise = new ValkarnTask.Promise<string>();
promise.TrySetResult("done");
string value = await promise.Task;
```

يدعم `TrySetResult` و`TrySetException` و`TrySetCanceled`. الإبلاغ عن الاستثناءات غير الملاحَظة القائم على finalizer يضمن عدم فقدان الأخطاء بصمت.

### مصادر الإكمال مع pool

متغيرات pool ذات إعادة تعيين تلقائية للأنماط المتكررة (تستخدمها القنوات والمدمجات داخلياً):

```csharp
var source = ValkarnTask.PooledPromise<int>.Create(out uint token);
source.TrySetResult(42);
int value = await source.Task; // يعود source إلى pool تلقائياً
```

---

## جسر Awaitable

تشغيل بيني شفاف مع `Awaitable` الخاص بـ Unity — بدون تحويل يدوي:

```csharp
async ValkarnTask LoadGame()
{
    await SceneManager.LoadSceneAsync("Level2"); // Unity Awaitable — يعمل مباشرة
    await Resources.LoadAsync<Texture2D>("hero"); // Unity Awaitable — يعمل مباشرة
    await ValkarnTask.Delay(1000);                // أصلي Valkarn
}
```

يكتشف المولّد المصدري انتظارات `Awaitable` ويولّد المحوّل تلقائياً.

---

## إلغاء دورة الحياة

### تلقائي (مُولَّد من الكود المصدري)

حدّد الكلاس بـ `partial` — المولّد المصدري يتولى الباقي:

```csharp
public partial class EnemySpawner : MonoBehaviour
{
    async ValkarnTask Start()
    {
        while (true)
        {
            await ValkarnTask.Delay(3000);
            SpawnWave();
            // يُلغى تلقائياً عند تدمير هذا الـ GameObject.
        }
    }
}
```

- **MonoBehaviour** — مرتبط بـ `destroyCancellationToken`
- **ScriptableObject** — مرتبط بمدة حياة التطبيق
- **كلاس عادي** — لا ربط تلقائي (مطلوب رمز يدوي)

### تعطيل

```csharp
[NoAutoCancel]
async ValkarnTask BackgroundSync()
{
    await SyncToServer(); // لا يُلغى تلقائياً عند التدمير
}
```

### تجاوز CancellationToken يدوي

تمرير `CancellationToken` صريح يتجاوز الرمز المحقون تلقائياً لدورة الحياة:

```csharp
async ValkarnTask DoWork(CancellationToken ct)
{
    await ValkarnTask.Delay(1000, ct);
}
```

### لا إلغاء للمهام الشقيقة

لا تلغي المهام المهام الشقيقة. `WhenAll` ينتظر جميع المهام؛ `WhenAny` يُرجع النتيجة الأولى لكن المهام الخاسرة تستمر في التشغيل. هذا يمنع تلف البيانات عندما يكون للمهام آثار جانبية.

---

## الأقسام الحرجة

للعمليات التي يجب ألا تُقطع بإلغاء دورة الحياة:

```csharp
async ValkarnTask SaveProgress()
{
    var data = await LoadPlayerData();        // قابل للإلغاء

    await using (ValkarnTask.Critical())
    {
        await db.Insert(data);               // لا يُلغى حتى لو تم تدمير GO
        await db.Commit();
    }                                        // يُطبَّق الإلغاء المعلق هنا

    await SendNotification();                // قابل للإلغاء مجدداً
}
```

داخل القسم الحرج، يكون الإلغاء **مؤجلاً** — وليس متجاهلاً. عند انتهاء القسم، يُطبَّق الإلغاء المعلق.

---

## المدمجات Combinators

### WhenAll (محدد النوع)

```csharp
// مباشر — يرمي استثناء إذا فشلت أي مهمة
var (enemies, map) = await ValkarnTask.WhenAll(LoadEnemies(), LoadMap());

// آمن — ادمج مع AsResult() لسلوك بدون رمي
var (a, b) = await ValkarnTask.WhenAll(
    LoadEnemies().AsResult(), LoadMap().AsResult());
```

مسار سريع متزامن بدون تخصيص عندما تكون جميع المهام مكتملة بالفعل. التحميل الزائد `IEnumerable<ValkarnTask<T>>` يستخدم `ArrayPool<T>` للمصفوفات الداخلية.

### WhenAll (void)

```csharp
await ValkarnTask.WhenAll(SaveA(), SaveB(), SaveC());
await ValkarnTask.WhenAll(taskList); // IEnumerable<ValkarnTask>
```

### WhenAny

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(DownloadFromA(), DownloadFromB());
```

يُرجع أول نتيجة مكتملة. تستمر المهام الخاسرة في التشغيل بشكل طبيعي.

### Fire-and-forget

```csharp
SendAnalytics("event").Forget();

[FireAndForget]
async ValkarnTask SendAnalytics(string eventName) { ... }
// المتصلون لا يحتاجون إلى .Forget() — لا تحذير يُولَّد
```

`.Forget()` يوجه الأخطاء إلى `ValkarnTask.UnobservedException`. لا تُبتلَع بصمت أبداً.

### AsNonGeneric

```csharp
ValkarnTask voidTask = typedTask.AsNonGeneric();
```

---

## الوقت والتأخير

```csharp
await ValkarnTask.Delay(1000);                                    // ميلي ثانية
await ValkarnTask.Delay(TimeSpan.FromSeconds(2));                  // TimeSpan
await ValkarnTask.Delay(1000, DelayType.UnscaledDeltaTime);       // تجاهل timescale
await ValkarnTask.Delay(1000, DelayType.Realtime);                 // قائم على Stopwatch

await ValkarnTask.Yield();                                         // علامة PlayerLoop التالية
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);             // توقيت محدد
await ValkarnTask.NextFrame();                                      // الإطار التالي مضمون
await ValkarnTask.DelayFrame(5);                                    // N إطاراً

await ValkarnTask.WaitUntil(() => player.IsReady);
await ValkarnTask.WaitWhile(() => isLoading);
```

---

## التبديل بين الخيوط

```csharp
async ValkarnTask ProcessData()
{
    var raw = await DownloadData();

    await ValkarnTask.SwitchToThreadPool();
    var processed = HeavyComputation(raw);  // خيط خلفي

    await ValkarnTask.SwitchToMainThread();
    ApplyToGameObject(processed);           // الخيط الرئيسي
}
```

---

## 16 توقيتاً لـ PlayerLoop

| المجموعة | التوقيتات |
|---------|---------|
| Initialization | `Initialization`، `LastInitialization` |
| EarlyUpdate | `EarlyUpdate`، `LastEarlyUpdate` |
| FixedUpdate | `FixedUpdate`، `LastFixedUpdate` |
| PreUpdate | `PreUpdate`، `LastPreUpdate` |
| Update | `Update`، `LastUpdate` |
| PreLateUpdate | `PreLateUpdate`، `LastPreLateUpdate` |
| PostLateUpdate | `PostLateUpdate`، `LastPostLateUpdate` |
| TimeUpdate | `TimeUpdate`، `LastTimeUpdate` |

تستخدم جميع العمليات `PlayerLoopTiming.Update` افتراضياً ما لم يُحدَّد غير ذلك.

---

## القنوات

```csharp
// غير محدود
var channel = ValkarnTask.Channel.CreateUnbounded<EnemySpawnRequest>();

// محدود — backpressure عند الامتلاء
var channel = ValkarnTask.Channel.CreateBounded<LogEntry>(capacity: 100);

// متعدد المستهلكين
var channel = ValkarnTask.Channel.CreateUnbounded<WorkItem>(multiConsumer: true);

// المنتج
await channel.Writer.WriteAsync(entry);
bool accepted = channel.Writer.TryWrite(entry);

// المستهلك
await foreach (var item in channel.Reader.ReadAllAsync())
    Process(item);

// الإكمال
channel.Writer.Complete();
await channel.Reader.Completion;
```

---

## الاختبار الحتمي (TestClock)

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

جميع العمليات الزمنية تقرأ من `TimeProvider.Current`. في الاختبارات، استبدلها بـ `TestClock`. `AdvanceFrame()` يحاكي علامة player loop واحدة.

---

## جسر نظام Job

```csharp
var job = new PathfindingJob { start = a, end = b, results = results };
await job.ScheduleAsync();
// NativeArray الخاصة بـ results قابلة للقراءة فوراً بعد await

await job.ScheduleParallelAsync(dataCount, batchSize); // IJobParallelFor

// الإلغاء — أكمل job handle قبل الإبلاغ عن الإلغاء (بدون تسرب job)
await job.ScheduleAsync(cancellationToken);
```

---

## التشخيصات في وقت الترجمة

| الكود | الخطورة | الوصف |
|------|---------|-------|
| TT001 | تحذير | Double-await / use-after-free على `ValkarnTask` |
| TT002 | خطأ | نتيجة `async ValkarnTask` مستخدمة كجملة تعبير — يجب انتظارها أو `.Forget()` |
| TT010 | معلومة | طريقة async في MonoBehaviour تُلغى تلقائياً عند Destroy |
| TT011 | تحذير | يحتوي `WhenAll` على مهام بدورات حياة مختلفة |
| TT012 | تحذير | حلقة async بدون فحص إلغاء (حلقة زومبي محتملة) |
| TT013 | تحذير | `ValkarnTask` مُرجَع لكن لم يُنتظر ولم يُتجاهل صراحةً |
| TT014 | تحذير | `[NoAutoCancel]` بدون معامل `CancellationToken` يدوي |
| TT015 | معلومة | انتظار `Awaitable` داخل `ValkarnTask` — تم توليد محوّل bridge |
| TT016 | تحذير | طريقة async بدون تعبير await |
| TT017 | تحذير | `[FireAndForget]` على `ValkarnTask<T>` — يتجاهل قيمة الإرجاع |

---

## إدارة Pool

كل runner لطريقة async مُجمَّع عبر `ValkarnPool<T>`:

- **الخيط الرئيسي** — مكدس خالٍ من القفل، صفر عمليات ذرية
- **الخيوط الخلفية** — Treiber lock-free stack مع عمليات CAS
- **قص قائم على الإطار** — كل 300 إطار (~5 ثوانٍ عند 60fps)، تُحرَّر الكائنات الزائدة تدريجياً
- **لا تنخفض أبداً** دون الحد الأدنى القابل للتهيئة (الافتراضي: 8)

راقب في وقت التشغيل:

```csharp
foreach (var (type, size, maxSize) in ValkarnTask.GetPoolInfo())
    Debug.Log($"{type}: {size}/{maxSize}");
```

---

## ValkarnTaskSettings

قم بالتهيئة عبر `ScriptableObject` (`Assets > Create > Valkarn > Tasks > Task Settings`، ضعه في `Resources/`):

| الإعداد | الافتراضي | الوصف |
|---------|---------|-------|
| `DefaultMaxPoolSize` | 256 | الحد الأقصى للعناصر لكل نوع pool |
| `MinPoolSize` | 8 | لا تقص أبداً دون هذا الحد |
| `TrimCheckInterval` | 300 | الإطارات بين فحوصات القص |
| `TrimHysteresisCount` | 2 | فحوصات متتالية قبل القص |
| `TrimReleaseRatio` | 0.25 | نسبة الفائض المُحرَّر لكل دورة |
| `EnableAutoCancel` | true | ربط مهام MonoBehaviour تلقائياً بـ destroyCancellationToken |
| `LogUnobservedCancellations` | false | تسجيل الإلغاءات غير الملاحَظة كتحذيرات |
| `MaxExceptionLogsPerFrame` | 10 | حد سجلات الاستثناء لكل إطار |

---

## معالجة الأخطاء

```csharp
// الاستثناءات غير الملاحَظة — تُطلَق بشكل حتمي عند إرجاع pool
ValkarnTask.UnobservedException += ex =>
    Debug.LogError($"Unobserved: {ex}");
```

- `WhenAll` — يرمي أول استثناء، يوجه الاستثناءات الإضافية إلى `UnobservedException`
- `WhenAny` — يُرمى استثناء الفائز؛ أعطال الخاسرين تذهب إلى `UnobservedException`
- إلغاء دورة الحياة — `OperationCanceledException` مُكبَّت افتراضياً (قابل للتهيئة)

### طرق المصنع

```csharp
ValkarnTask.CompletedTask          // void، بدون تخصيص
ValkarnTask.FromResult<T>(value)   // محدد النوع، بدون تخصيص
ValkarnTask.FromException(ex)      // معطوب
ValkarnTask.FromException<T>(ex)
ValkarnTask.FromCanceled()         // مُلغى
ValkarnTask.FromCanceled<T>()
ValkarnTask.Never                  // لا يكتمل أبداً (sentinel لـ WhenAny)
```
