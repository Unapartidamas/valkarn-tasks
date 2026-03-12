---
sidebar_position: 1
title: VlkTask
---

# VlkTask

`VlkTask` — основной ожидаемый тип. Это `readonly struct` — никаких аллокаций в куче на удачном пути (когда задача завершается синхронно или через пул).

```csharp
namespace UnaPartidaMas.Valkarn.Tasks;

public readonly struct VlkTask : IEquatable<VlkTask>
```

---

## Статические фабричные методы

### Delay

```csharp
// Миллисекунды (по умолчанию использует PlayerLoopTiming.Update)
VlkTask VlkTask.Delay(int millisecondsDelay)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(int millisecondsDelay, CancellationToken cancellationToken)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing, CancellationToken cancellationToken)

// Перегрузки с TimeSpan
VlkTask VlkTask.Delay(TimeSpan delay)
VlkTask VlkTask.Delay(TimeSpan delay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(TimeSpan delay, CancellationToken cancellationToken)
```

### Yield

```csharp
// Уступить до следующего кадра (по умолчанию фаза Update)
VlkTask VlkTask.Yield()
VlkTask VlkTask.Yield(PlayerLoopTiming timing)
VlkTask VlkTask.Yield(CancellationToken cancellationToken)
```

### WhenAll

```csharp
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2)
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2, VlkTask task3)
// ... до 8 параметров

// С возвращаемыми значениями — поддерживается деструктуризация кортежей
VlkTask<(T1, T2)>     VlkTask.WhenAll<T1, T2>(VlkTask<T1>, VlkTask<T2>)
VlkTask<(T1, T2, T3)> VlkTask.WhenAll<T1, T2, T3>(...)
// ... до 8

// Перегрузки с коллекциями
VlkTask VlkTask.WhenAll(IEnumerable<VlkTask> tasks)
VlkTask<T[]> VlkTask.WhenAll<T>(IEnumerable<VlkTask<T>> tasks)
```

### WhenAny

```csharp
VlkTask<int> VlkTask.WhenAny(VlkTask task1, VlkTask task2)   // возвращает индекс первого завершившегося
VlkTask<T>   VlkTask.WhenAny<T>(VlkTask<T> task1, VlkTask<T> task2) // возвращает значение первого
```

### Переключение потоков

```csharp
VlkTask VlkTask.SwitchToMainThread()
VlkTask VlkTask.SwitchToThreadPool()
VlkTask VlkTask.SwitchToSynchronizationContext(SynchronizationContext context)
```

### Завершённые / Never

```csharp
VlkTask VlkTask.CompletedTask      // предварительно завершена, ноль аллокаций
VlkTask<T> VlkTask.FromResult<T>(T value)
VlkTask VlkTask.FromCanceled(CancellationToken ct)
VlkTask<T> VlkTask.FromCanceled<T>(CancellationToken ct)
VlkTask VlkTask.FromException(Exception ex)
VlkTask<T> VlkTask.FromException<T>(Exception ex)
VlkTask VlkTask.Never                // никогда не завершается
```

### Run

```csharp
// Выполняет делегат в пуле потоков
VlkTask VlkTask.Run(Action action)
VlkTask VlkTask.Run(Func<VlkTask> factory)
VlkTask<T> VlkTask.Run<T>(Func<T> func)
VlkTask<T> VlkTask.Run<T>(Func<VlkTask<T>> factory)
```

### Forget

```csharp
// Fire-and-forget: безопасное подавление предупреждения CS4014
void VlkTask.Forget(VlkTask task)
void VlkTask.Forget(VlkTask task, Action<Exception> exceptionHandler)
```

---

## Члены экземпляра

```csharp
VlkTaskStatus Status { get; }
bool IsCompleted { get; }
bool IsCompletedSuccessfully { get; }
bool IsFaulted { get; }
bool IsCanceled { get; }

// Получить результат (бросает при ошибке/отмене)
void GetResult()

// Awaiter
VlkTaskAwaiter GetAwaiter()

// Преобразовать в ValueTask
ValueTask AsValueTask()
```

---

## Условия ожидания

```csharp
// Ждать выполнения условия
VlkTask VlkTask.WaitUntil(Func<bool> condition)
VlkTask VlkTask.WaitUntil(Func<bool> condition, PlayerLoopTiming timing)

// Ждать пока условие истинно
VlkTask VlkTask.WaitWhile(Func<bool> condition)

// Ждать фиксированное количество кадров
VlkTask VlkTask.WaitForFrames(int frameCount)
VlkTask VlkTask.NextFrame()
```

---

## Диагностика пула

```csharp
// Возвращает (Type type, int currentSize, int maxSize) для каждого пула
IEnumerable<(Type, int, int)> VlkTask.GetPoolInfo()
```

Доступно в окне **Window → Valkarn Tasks → Task Tracker**.
