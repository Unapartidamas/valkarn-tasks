---
sidebar_position: 7
title: ValkarnSemaphore
---

# ValkarnSemaphore

`ValkarnSemaphore` هو إشارة انتظار غير متزامنة بدون تخصيص ذاكرة مبنية على `ValkarnTask`. تعمل مثل `SemaphoreSlim` لكنها تتكامل بشكل أصلي مع خط أنابيب Valkarn Tasks — بدون تخصيصات `Task`، بدون ضغط على جامع القمامة في الحالة المستقرة.

```csharp
namespace UnaPartidaMas.Valkarn.Tasks
```

**المسار السريع (بدون تنافس):** يُعيد `ValkarnTask.CompletedTask` فوراً — تخصيص صفري.
**المسار البطيء (مع تنافس):** يتم استعارة عقد الانتظار من مجموعة محدودة وإعادتها عند الاكتمال — جامع القمامة صفري في الحالة المستقرة.

---

## المُنشئ

```csharp
public ValkarnSemaphore(int initialCount, int maxCount)
```

| المعامل | الوصف |
|-----------|-------------|
| `initialCount` | عدد الفتحات المتاحة فوراً. يجب أن يكون في النطاق `[0, maxCount]`. |
| `maxCount` | الحد الأعلى لعدد الفتحات. يجب أن يكون > 0. |

---

## الخصائص

```csharp
public int CurrentCount { get; } // الفتحات المتاحة حالياً (آمن للخيوط)
```

---

## الأساليب

### WaitAsync

```csharp
public ValkarnTask WaitAsync(CancellationToken ct = default)
```

يحصل على فتحة واحدة بشكل غير متزامن. يُعيد `ValkarnTask` مكتملاً فوراً إذا كانت هناك فتحة متاحة (تخصيص صفري). يُعلق المُستدعي حتى يتم استدعاء `Release()` إذا لم تكن هناك فتحة متاحة. يتم خدمة المنتظرين بترتيب FIFO.

يُطلق `OperationCanceledException` إذا تم تفعيل `ct` أثناء الانتظار.

### Wait

```csharp
public void Wait(CancellationToken ct = default)
```

النسخة المتزامنة. تُعطل خيط الاستدعاء إذا لم تكن هناك فتحة متاحة. استخدمه فقط عندما لا يمكنك استخدام `await` (مثلاً نقاط الدخول غير المتزامنة).

### Release

```csharp
public void Release(int releaseCount = 1)
```

يُعيد `releaseCount` فتحة. يتم إكمال المنتظرين المعلقين بترتيب FIFO خارج القفل الداخلي — لا تعمل الاستمرارات أبداً مع القفل محتجزاً.

يُطلق `InvalidOperationException` إذا كان الإفراج سيتجاوز `maxCount`.

### Dispose

```csharp
public void Dispose()
```

يُلغي جميع المنتظرين المعلقين ويُحرر الموارد. عملية مثلية (idempotent).

---

## الاستخدام

### الاستبعاد المتبادل (فتحة واحدة)

```csharp
var sem = new ValkarnSemaphore(initialCount: 1, maxCount: 1);

async ValkarnTask WriteFileAsync(string path, string content, CancellationToken ct)
{
    await sem.WaitAsync(ct);
    try
    {
        await File.WriteAllTextAsync(path, content, ct);
    }
    finally
    {
        sem.Release();
    }
}
```

### التزامن المحدود (N فتحة)

```csharp
// السماح بما يصل إلى 4 طلبات شبكة متزامنة.
var sem = new ValkarnSemaphore(initialCount: 4, maxCount: 4);

async ValkarnTask FetchAsync(string url, CancellationToken ct)
{
    await sem.WaitAsync(ct);
    try
    {
        await DownloadAsync(url, ct);
    }
    finally
    {
        sem.Release();
    }
}
```

### بوابة المنتج / المستهلك

```csharp
// يبدأ بـ 0 فتحة — ينتظر المستهلك حتى يُشير المنتج.
var gate = new ValkarnSemaphore(initialCount: 0, maxCount: 1);

async ValkarnTask ProducerAsync(CancellationToken ct)
{
    await ProduceDataAsync(ct);
    gate.Release(); // إشارة للمستهلك
}

async ValkarnTask ConsumerAsync(CancellationToken ct)
{
    await gate.WaitAsync(ct); // انتظار الإشارة
    await ConsumeDataAsync(ct);
}
```

---

## أمان الخيوط

جميع العمليات آمنة للخيوط. يمكن استدعاء `Release()` من أي خيط، بما في ذلك خيوط الخلفية. يتم إرسال عمليات الاكتمال خارج القفل الداخلي — استمرارية تُعيد الدخول إلى الإشارة لن تُسبب توقفاً تاماً.

---

## المقارنة مع SemaphoreSlim

| | `ValkarnSemaphore` | `SemaphoreSlim` |
|---|---|---|
| تخصيص المسار السريع | صفر | صفر |
| تخصيص المسار البطيء | عقدة من المجموعة، جامع القمامة صفري | تخصيص `Task` |
| التكامل مع ValkarnTask | نعم | يتطلب `.AsValkarnTask()` |
| `IDisposable` | نعم | نعم |
| ترتيب FIFO | نعم | نعم |
| الإلغاء | نعم | نعم |
