---
sidebar_position: 1
title: ValkarnTask
---

# ValkarnTask

`ValkarnTask` هو النوع الأساسي القابل للانتظار. إنه `readonly struct` — لا تخصيص كومة ذاكرة على المسار الناجح (عندما تكتمل المهمة بشكل متزامن أو عبر مجموعة الموارد).

```csharp
namespace UnaPartidaMas.Valkarn.Tasks;

public readonly struct ValkarnTask : IEquatable<ValkarnTask>
```

---

## طرق المصنع الثابتة

### Delay

```csharp
// بالميلي ثانية (يستخدم PlayerLoopTiming.Update افتراضيًا)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, CancellationToken cancellationToken)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, PlayerLoopTiming timing, CancellationToken cancellationToken)

// تحميلات زائدة بـ TimeSpan
ValkarnTask ValkarnTask.Delay(TimeSpan delay)
ValkarnTask ValkarnTask.Delay(TimeSpan delay, PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Delay(TimeSpan delay, CancellationToken cancellationToken)
```

### Yield

```csharp
// التنازل للإطار التالي (توقيت Update افتراضيًا)
ValkarnTask ValkarnTask.Yield()
ValkarnTask ValkarnTask.Yield(PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Yield(CancellationToken cancellationToken)
```

### WhenAll

```csharp
ValkarnTask ValkarnTask.WhenAll(ValkarnTask task1, ValkarnTask task2)
ValkarnTask ValkarnTask.WhenAll(ValkarnTask task1, ValkarnTask task2, ValkarnTask task3)
// ... حتى 8 معاملات

// مع قيم الإرجاع — تفكيك الصفوف مدعوم
ValkarnTask<(T1, T2)>     ValkarnTask.WhenAll<T1, T2>(ValkarnTask<T1>, ValkarnTask<T2>)
ValkarnTask<(T1, T2, T3)> ValkarnTask.WhenAll<T1, T2, T3>(...)
// ... حتى 8

// تحميلات زائدة للمجموعات
ValkarnTask ValkarnTask.WhenAll(IEnumerable<ValkarnTask> tasks)
ValkarnTask<T[]> ValkarnTask.WhenAll<T>(IEnumerable<ValkarnTask<T>> tasks)
```

### WhenAny

```csharp
ValkarnTask<int> ValkarnTask.WhenAny(ValkarnTask task1, ValkarnTask task2)   // يُرجع فهرس أول مكتمل
ValkarnTask<T>   ValkarnTask.WhenAny<T>(ValkarnTask<T> task1, ValkarnTask<T> task2) // يُرجع قيمة الأول
```

### تبديل الخيوط

```csharp
ValkarnTask ValkarnTask.SwitchToMainThread()
ValkarnTask ValkarnTask.SwitchToThreadPool()
ValkarnTask ValkarnTask.SwitchToSynchronizationContext(SynchronizationContext context)
```

### مكتمل / لن يكتمل

```csharp
ValkarnTask ValkarnTask.CompletedTask      // مكتملة مسبقًا، صفر تخصيص
ValkarnTask<T> ValkarnTask.FromResult<T>(T value)
ValkarnTask ValkarnTask.FromCanceled(CancellationToken ct)
ValkarnTask<T> ValkarnTask.FromCanceled<T>(CancellationToken ct)
ValkarnTask ValkarnTask.FromException(Exception ex)
ValkarnTask<T> ValkarnTask.FromException<T>(Exception ex)
ValkarnTask ValkarnTask.Never                // لن تكتمل أبدًا
```

### Run

```csharp
// يُشغّل المندوب على مجموعة الخيوط
ValkarnTask ValkarnTask.Run(Action action)
ValkarnTask ValkarnTask.Run(Func<ValkarnTask> factory)
ValkarnTask<T> ValkarnTask.Run<T>(Func<T> func)
ValkarnTask<T> ValkarnTask.Run<T>(Func<ValkarnTask<T>> factory)
```

### Forget

```csharp
// fire-and-forget: كتم تحذير CS4014 بأمان
void ValkarnTask.Forget(ValkarnTask task)
void ValkarnTask.Forget(ValkarnTask task, Action<Exception> exceptionHandler)
```

---

## الأعضاء النسخية

```csharp
ValkarnTaskStatus Status { get; }
bool IsCompleted { get; }
bool IsCompletedSuccessfully { get; }
bool IsFaulted { get; }
bool IsCanceled { get; }

// الحصول على النتيجة (يرمي إذا كانت معطوبة/ملغاة)
void GetResult()

// المُنتظِر
ValkarnTaskAwaiter GetAwaiter()

// التحويل إلى ValueTask
ValueTask AsValueTask()
```

---

## شروط الانتظار

```csharp
// انتظار حتى يصبح الشرط صحيحًا
ValkarnTask ValkarnTask.WaitUntil(Func<bool> condition)
ValkarnTask ValkarnTask.WaitUntil(Func<bool> condition, PlayerLoopTiming timing)

// انتظار بينما الشرط صحيح
ValkarnTask ValkarnTask.WaitWhile(Func<bool> condition)

// انتظار عدد ثابت من الإطارات
ValkarnTask ValkarnTask.WaitForFrames(int frameCount)
ValkarnTask ValkarnTask.NextFrame()
```

---

## تشخيصات مجموعة الموارد

```csharp
// يُرجع (Type type, int currentSize, int maxSize) لكل مجموعة موارد
IEnumerable<(Type, int, int)> ValkarnTask.GetPoolInfo()
```

متاحة في نافذة **Window → Valkarn Tasks → Task Tracker**.
