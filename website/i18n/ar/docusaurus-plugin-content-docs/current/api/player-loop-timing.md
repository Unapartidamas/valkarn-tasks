---
sidebar_position: 2
title: PlayerLoopTiming
---

# PlayerLoopTiming

```csharp
public enum PlayerLoopTiming : byte
```

مساحة الأسماء: `UnaPartidaMas.Valkarn.Tasks`

يُحدد مرحلة Unity PlayerLoop التي تستخدمها عملية ValkarnTasks للاستئناف بعد التعليق. تمرير قيمة `PlayerLoopTiming` يتحكم في **وقت الإطار** الذي تعمل فيه استمرارية `await` الخاصة بك، و**وقت** فحص العناصر المتكررة كالتأخيرات وشروط الانتظار.

قيم الـ enum تطابق تمامًا `PlayerLoopTiming` الخاص بـ UniTask، مما يُبسّط الترحيل من UniTask.

---

## الافتراضي

`PlayerLoopTiming.Update` (القيمة `8`) هو الافتراضي لجميع العمليات. إذا لم تُمرر وسيطة توقيت، يُستخدم `Update`.

---

## القيم

### Initialization

```
القيمة: 0
المرحلة الأصلية: UnityEngine.PlayerLoop.Initialization
الموضع: أول نظام فرعي في المرحلة
```

يعمل في بداية مرحلة Initialization، قبل الأنظمة الفرعية لتهيئة Unity الخاصة في تلك المرحلة. يُطلق مرة في الإطار، قبل EarlyUpdate. نادرًا مفيد لكود اللعب لكن يمكن أن يكون ذا صلة للأنظمة التي يجب أن تقرأ أو تُعدّ الحالة قبل أن يلمسها أي شيء آخر في الإطار.

---

### LastInitialization

```
القيمة: 1
المرحلة الأصلية: UnityEngine.PlayerLoop.Initialization
الموضع: آخر نظام فرعي في المرحلة
```

يعمل في نهاية مرحلة Initialization، بعد الأنظمة الفرعية للتهيئة المدمجة في Unity. فضّله على `Initialization` إذا كنت بحاجة لتوقيت مرحلة التهيئة لكن تريد السماح لأنظمة Unity الخاصة بالعمل أولًا.

---

### EarlyUpdate

```
القيمة: 2
المرحلة الأصلية: UnityEngine.PlayerLoop.EarlyUpdate
الموضع: أول نظام فرعي في المرحلة
```

يعمل في بداية EarlyUpdate، قبل معالجة Unity لأحداث الإدخال وقبل خطوة محاكاة الفيزياء. هذه أبكر نقطة في الإطار تتوفر فيها بيانات الإدخال من الإطار السابق. مفيد للأنظمة التي يجب أن تأخذ عيّنات الإدخال قبل تشغيل أي كود لعبة.

---

### LastEarlyUpdate

```
القيمة: 3
المرحلة الأصلية: UnityEngine.PlayerLoop.EarlyUpdate
الموضع: آخر نظام فرعي في المرحلة
```

يعمل في نهاية EarlyUpdate، بعد اكتمال الأنظمة الفرعية المبكرة الخاصة بـ Unity.

---

### FixedUpdate

```
القيمة: 4
المرحلة الأصلية: UnityEngine.PlayerLoop.FixedUpdate
الموضع: أول نظام فرعي في المرحلة
```

يعمل في بداية كل خطوة FixedUpdate. يتوافق هذا مع توقيت `MonoBehaviour.FixedUpdate()`. قد تُشغّل Unity صفرًا أو أكثر من خطوات FixedUpdate لكل إطار مُصيَّر بحسب كيفية محاذاة خطوة زمن الفيزياء مع فارق زمن الإطار.

استخدم هذا التوقيت لأي شيء يجب أن يبقى متزامنًا مع محاكاة الفيزياء: قراءة سرعات `Rigidbody`، تطبيق القوى، أو انتظار عدد من الخطوات المحدد بالفيزياء.

```csharp
// انتظار 10 خطوات فيزياء
await ValkarnTask.DelayFrame(10, PlayerLoopTiming.FixedUpdate, ct);

// انتظار حتى يصبح شرط الفيزياء صحيحًا، محسوب كل خطوة فيزياء
await ValkarnTask.WaitUntil(() => rb.velocity.magnitude < 0.1f,
    PlayerLoopTiming.FixedUpdate, ct);
```

---

### LastFixedUpdate

```
القيمة: 5
المرحلة الأصلية: UnityEngine.PlayerLoop.FixedUpdate
الموضع: آخر نظام فرعي في المرحلة
```

يعمل في نهاية مرحلة FixedUpdate، بعد اكتمال الأنظمة الفرعية للفيزياء في Unity (بما في ذلك `Physics.Simulate`). استخدم هذا لقراءة نتائج الفيزياء بعد تقدم المحاكاة.

---

### PreUpdate

```
القيمة: 6
المرحلة الأصلية: UnityEngine.PlayerLoop.PreUpdate
الموضع: أول نظام فرعي في المرحلة
```

يعمل في بداية مرحلة PreUpdate، التي تعمل بعد FixedUpdate وقبل Update. تستخدم Unity PreUpdate لمهام مثل تحديثات مناطق الرياح ومعالجة أحداث الشبكة.

---

### LastPreUpdate

```
القيمة: 7
المرحلة الأصلية: UnityEngine.PlayerLoop.PreUpdate
الموضع: آخر نظام فرعي في المرحلة
```

يعمل في نهاية PreUpdate.

---

### Update

```
القيمة: 8  (الافتراضي)
المرحلة الأصلية: UnityEngine.PlayerLoop.Update
الموضع: أول نظام فرعي في المرحلة
```

**التوقيت الافتراضي** لجميع عمليات ValkarnTasks. يعمل في بداية مرحلة Update، حيث يُطلق `MonoBehaviour.Update()`. هذا هو الخيار الصحيح لغالبية كود اللعب.

```csharp
// كل هذه تستخدم Update افتراضيًا
await ValkarnTask.Yield();
await ValkarnTask.Delay(1000, ct);
await ValkarnTask.WaitUntil(() => _ready, ct: ct);
await ValkarnTask.NextFrame(ct: ct);
await ValkarnTask.DelayFrame(3, ct: ct);

// صريح — مطابق للأعلاه
await ValkarnTask.Yield(PlayerLoopTiming.Update);
```

---

### LastUpdate

```
القيمة: 9
المرحلة الأصلية: UnityEngine.PlayerLoop.Update
الموضع: آخر نظام فرعي في المرحلة
```

يعمل في نهاية مرحلة Update، بعد جميع استدعاءات `MonoBehaviour.Update()` وأي أنظمة فرعية Update أخرى. استخدم هذا عندما يجب أن تُلاحظ استمرارتك نتائج `Update()` للسكريبتات الأخرى — على سبيل المثال، استطلاع قيمة تكتبها سكريبت أخرى أثناء `Update`.

```csharp
// الاستئناف بعد جميع استدعاءات MonoBehaviour.Update() في هذا الإطار
await ValkarnTask.Yield(PlayerLoopTiming.LastUpdate);
```

---

### PreLateUpdate

```
القيمة: 10
المرحلة الأصلية: UnityEngine.PlayerLoop.PreLateUpdate
الموضع: أول نظام فرعي في المرحلة
```

يعمل في بداية مرحلة PreLateUpdate، حيث يُطلق `MonoBehaviour.LateUpdate()`. استخدم هذا لمتابعة الكاميرا وضبط التحويل وأي شيء يجب أن يتفاعل مع المواضع النهائية المحددة أثناء `Update`.

```csharp
// الاستئناف في نفس نقطة MonoBehaviour.LateUpdate()
await ValkarnTask.Yield(PlayerLoopTiming.PreLateUpdate);
```

---

### LastPreLateUpdate

```
القيمة: 11
المرحلة الأصلية: UnityEngine.PlayerLoop.PreLateUpdate
الموضع: آخر نظام فرعي في المرحلة
```

يعمل في نهاية مرحلة PreLateUpdate، بعد جميع استدعاءات `MonoBehaviour.LateUpdate()`. استخدم هذا عندما تحتاج لقراءة القيم التي تضبطها السكريبتات أثناء `LateUpdate`.

---

### PostLateUpdate

```
القيمة: 12
المرحلة الأصلية: UnityEngine.PlayerLoop.PostLateUpdate
الموضع: أول نظام فرعي في المرحلة
```

يعمل في بداية PostLateUpdate. تُطلق هذه المرحلة بعد تقديم الرسم للإطار. تستخدمها Unity لمهام مثل عرض مخزن الإطار وتنظيف حالة الرسم. إنه أيضًا المكان الذي تُطلق فيه استدعاءات `Camera.onPostRender`.

استخدم هذا التوقيت لعمليات نهاية الإطار: التقاط لقطات الشاشة، تفريغ تدفق المستويات، أو أي عمل يجب أن يحدث بعد رسم المشهد بالكامل لكن قبل بدء الإطار التالي.

---

### LastPostLateUpdate

```
القيمة: 13
المرحلة الأصلية: UnityEngine.PlayerLoop.PostLateUpdate
الموضع: آخر نظام فرعي في المرحلة
```

آخر نقطة في حلقة الإطار القياسية قبل إعادة تعيين Unity لحالة الإطار المحلية وبدء الإطار التالي. هذا النهاية المطلقة للإطار لأغراض التوقيت.

---

### TimeUpdate

```
القيمة: 14
المرحلة الأصلية: UnityEngine.PlayerLoop.TimeUpdate
الموضع: أول نظام فرعي في المرحلة
```

يعمل في بداية مرحلة TimeUpdate. تستخدم Unity هذه المرحلة لتقدم الحالة المرتبطة بالوقت (مثل `Time.time`). يُطلق هذا التوقيت قبل EarlyUpdate في إصدارات Unity الأحدث.

هذا التوقيت نادرًا مطلوب في كود اللعب. يوجد بشكل أساسي للأنظمة التي يجب أن تعترض أو تُلاحظ تقدم الوقت قبل أن يقرأ أي نظام آخر قيم الوقت الجديدة.

---

### LastTimeUpdate

```
القيمة: 15
المرحلة الأصلية: UnityEngine.PlayerLoop.TimeUpdate
الموضع: آخر نظام فرعي في المرحلة
```

يعمل في نهاية مرحلة TimeUpdate، بعد اكتمال الأنظمة الفرعية المرتبطة بالوقت في Unity.

---

## جدول الملخص

| القيمة | الاسم | مرحلة Unity | الموضع | استدعاء MonoBehaviour المقابل |
|---|---|---|---|---|
| 0 | `Initialization` | `Initialization` | الأول | — |
| 1 | `LastInitialization` | `Initialization` | الأخير | — |
| 2 | `EarlyUpdate` | `EarlyUpdate` | الأول | — |
| 3 | `LastEarlyUpdate` | `EarlyUpdate` | الأخير | — |
| 4 | `FixedUpdate` | `FixedUpdate` | الأول | `FixedUpdate()` |
| 5 | `LastFixedUpdate` | `FixedUpdate` | الأخير | بعد `FixedUpdate()` |
| 6 | `PreUpdate` | `PreUpdate` | الأول | — |
| 7 | `LastPreUpdate` | `PreUpdate` | الأخير | — |
| **8** | **`Update`** (الافتراضي) | **`Update`** | **الأول** | **`Update()`** |
| 9 | `LastUpdate` | `Update` | الأخير | بعد جميع `Update()` |
| 10 | `PreLateUpdate` | `PreLateUpdate` | الأول | `LateUpdate()` |
| 11 | `LastPreLateUpdate` | `PreLateUpdate` | الأخير | بعد جميع `LateUpdate()` |
| 12 | `PostLateUpdate` | `PostLateUpdate` | الأول | بعد الرسم |
| 13 | `LastPostLateUpdate` | `PostLateUpdate` | الأخير | نهاية الإطار |
| 14 | `TimeUpdate` | `TimeUpdate` | الأول | — |
| 15 | `LastTimeUpdate` | `TimeUpdate` | الأخير | — |

---

## الواجهات البرمجية التي تقبل PlayerLoopTiming

جميع واجهات برمجة ValkarnTasks المبنية على الوقت والشروط تقبل مُعامل `PlayerLoopTiming` اختياريًا. الافتراضي دائمًا `Update`.

| الطريقة | التوقيع |
|---|---|
| `ValkarnTask.Yield` | `Yield(PlayerLoopTiming timing = Update)` |
| `ValkarnTask.NextFrame` | `NextFrame(PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.DelayFrame` | `DelayFrame(int frameCount, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.Delay(int)` | `Delay(int ms, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.Delay(TimeSpan)` | `Delay(TimeSpan delay, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.WaitUntil` | `WaitUntil(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.WaitWhile` | `WaitWhile(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |

---

## أمثلة كود

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using System.Threading;

// التنازل للتكتة التالية من Update (الافتراضي)
await ValkarnTask.Yield();

// التنازل للتكتة التالية من FixedUpdate — استخدم داخل كود مدفوع بالفيزياء
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// انتظار 500 ms باستخدام deltaTime المقيّس، محسوب كل Update
await ValkarnTask.Delay(500, ct: destroyCancellationToken);

// انتظار 500 ms باستخدام deltaTime غير المقيّس — غير متأثر بـ Time.timeScale
await ValkarnTask.Delay(
    500,
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// انتظار 500 ms باستخدام وقت الحائط الفعلي (مبني على Stopwatch)
await ValkarnTask.Delay(
    500,
    DelayType.Realtime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// انتظار حتى يُضبط علم، محسوب في LateUpdate
bool _isReady;
await ValkarnTask.WaitUntil(() => _isReady, PlayerLoopTiming.PreLateUpdate, destroyCancellationToken);

// انتظار أثناء التحميل، محسوب في نهاية الإطار
await ValkarnTask.WaitWhile(() => _loading, PlayerLoopTiming.LastPostLateUpdate, destroyCancellationToken);

// تخطي 5 خطوات فيزياء بالضبط
await ValkarnTask.DelayFrame(5, PlayerLoopTiming.FixedUpdate, destroyCancellationToken);

// تقدم إطار مُصيَّر كامل
await ValkarnTask.NextFrame(PlayerLoopTiming.Update, destroyCancellationToken);
```
