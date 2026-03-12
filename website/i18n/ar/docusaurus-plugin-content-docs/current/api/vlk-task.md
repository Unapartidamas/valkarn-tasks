---
sidebar_position: 1
title: VlkTask
---

# VlkTask

`VlkTask` هو النوع الأساسي القابل للانتظار. إنه `readonly struct` — لا تخصيص كومة ذاكرة على المسار الناجح (عندما تكتمل المهمة بشكل متزامن أو عبر مجموعة الموارد).

```csharp
namespace UnaPartidaMas.Valkarn.Tasks;

public readonly struct VlkTask : IEquatable<VlkTask>
```

---

## طرق المصنع الثابتة

### Delay

```csharp
// بالميلي ثانية (يستخدم PlayerLoopTiming.Update افتراضيًا)
VlkTask VlkTask.Delay(int millisecondsDelay)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(int millisecondsDelay, CancellationToken cancellationToken)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing, CancellationToken cancellationToken)

// تحميلات زائدة بـ TimeSpan
VlkTask VlkTask.Delay(TimeSpan delay)
VlkTask VlkTask.Delay(TimeSpan delay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(TimeSpan delay, CancellationToken cancellationToken)
```

### Yield

```csharp
// التنازل للإطار التالي (توقيت Update افتراضيًا)
VlkTask VlkTask.Yield()
VlkTask VlkTask.Yield(PlayerLoopTiming timing)
VlkTask VlkTask.Yield(CancellationToken cancellationToken)
```

### WhenAll

```csharp
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2)
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2, VlkTask task3)
// ... حتى 8 معاملات

// مع قيم الإرجاع — تفكيك الصفوف مدعوم
VlkTask<(T1, T2)>     VlkTask.WhenAll<T1, T2>(VlkTask<T1>, VlkTask<T2>)
VlkTask<(T1, T2, T3)> VlkTask.WhenAll<T1, T2, T3>(...)
// ... حتى 8

// تحميلات زائدة للمجموعات
VlkTask VlkTask.WhenAll(IEnumerable<VlkTask> tasks)
VlkTask<T[]> VlkTask.WhenAll<T>(IEnumerable<VlkTask<T>> tasks)
```

### WhenAny

```csharp
VlkTask<int> VlkTask.WhenAny(VlkTask task1, VlkTask task2)   // يُرجع فهرس أول مكتمل
VlkTask<T>   VlkTask.WhenAny<T>(VlkTask<T> task1, VlkTask<T> task2) // يُرجع قيمة الأول
```

### تبديل الخيوط

```csharp
VlkTask VlkTask.SwitchToMainThread()
VlkTask VlkTask.SwitchToThreadPool()
VlkTask VlkTask.SwitchToSynchronizationContext(SynchronizationContext context)
```

### مكتمل / لن يكتمل

```csharp
VlkTask VlkTask.CompletedTask      // مكتملة مسبقًا، صفر تخصيص
VlkTask<T> VlkTask.FromResult<T>(T value)
VlkTask VlkTask.FromCanceled(CancellationToken ct)
VlkTask<T> VlkTask.FromCanceled<T>(CancellationToken ct)
VlkTask VlkTask.FromException(Exception ex)
VlkTask<T> VlkTask.FromException<T>(Exception ex)
VlkTask VlkTask.Never                // لن تكتمل أبدًا
```

### Run

```csharp
// يُشغّل المندوب على مجموعة الخيوط
VlkTask VlkTask.Run(Action action)
VlkTask VlkTask.Run(Func<VlkTask> factory)
VlkTask<T> VlkTask.Run<T>(Func<T> func)
VlkTask<T> VlkTask.Run<T>(Func<VlkTask<T>> factory)
```

### Forget

```csharp
// fire-and-forget: كتم تحذير CS4014 بأمان
void VlkTask.Forget(VlkTask task)
void VlkTask.Forget(VlkTask task, Action<Exception> exceptionHandler)
```

---

## الأعضاء النسخية

```csharp
VlkTaskStatus Status { get; }
bool IsCompleted { get; }
bool IsCompletedSuccessfully { get; }
bool IsFaulted { get; }
bool IsCanceled { get; }

// الحصول على النتيجة (يرمي إذا كانت معطوبة/ملغاة)
void GetResult()

// المُنتظِر
VlkTaskAwaiter GetAwaiter()

// التحويل إلى ValueTask
ValueTask AsValueTask()
```

---

## شروط الانتظار

```csharp
// انتظار حتى يصبح الشرط صحيحًا
VlkTask VlkTask.WaitUntil(Func<bool> condition)
VlkTask VlkTask.WaitUntil(Func<bool> condition, PlayerLoopTiming timing)

// انتظار بينما الشرط صحيح
VlkTask VlkTask.WaitWhile(Func<bool> condition)

// انتظار عدد ثابت من الإطارات
VlkTask VlkTask.WaitForFrames(int frameCount)
VlkTask VlkTask.NextFrame()
```

---

## تشخيصات مجموعة الموارد

```csharp
// يُرجع (Type type, int currentSize, int maxSize) لكل مجموعة موارد
IEnumerable<(Type, int, int)> VlkTask.GetPoolInfo()
```

متاحة في نافذة **Window → Valkarn Tasks → Task Tracker**.
