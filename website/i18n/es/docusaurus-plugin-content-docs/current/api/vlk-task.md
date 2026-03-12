---
sidebar_position: 1
title: VlkTask
---

# VlkTask

`VlkTask` es el tipo awaitable principal. Es un `readonly struct` — sin asignación en el montón en el camino feliz (cuando la tarea se completa sincrónicamente o vía grupo).

```csharp
namespace UnaPartidaMas.Valkarn.Tasks;

public readonly struct VlkTask : IEquatable<VlkTask>
```

---

## Métodos de fábrica estáticos

### Delay

```csharp
// Milisegundos (usa PlayerLoopTiming.Update por defecto)
VlkTask VlkTask.Delay(int millisecondsDelay)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(int millisecondsDelay, CancellationToken cancellationToken)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing, CancellationToken cancellationToken)

// Sobrecargas de TimeSpan
VlkTask VlkTask.Delay(TimeSpan delay)
VlkTask VlkTask.Delay(TimeSpan delay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(TimeSpan delay, CancellationToken cancellationToken)
```

### Yield

```csharp
// Ceder al siguiente fotograma (timing Update por defecto)
VlkTask VlkTask.Yield()
VlkTask VlkTask.Yield(PlayerLoopTiming timing)
VlkTask VlkTask.Yield(CancellationToken cancellationToken)
```

### WhenAll

```csharp
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2)
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2, VlkTask task3)
// ... hasta 8 parámetros

// Con valores de retorno — desestructuración de tuplas compatible
VlkTask<(T1, T2)>     VlkTask.WhenAll<T1, T2>(VlkTask<T1>, VlkTask<T2>)
VlkTask<(T1, T2, T3)> VlkTask.WhenAll<T1, T2, T3>(...)
// ... hasta 8

// Sobrecargas de colección
VlkTask VlkTask.WhenAll(IEnumerable<VlkTask> tasks)
VlkTask<T[]> VlkTask.WhenAll<T>(IEnumerable<VlkTask<T>> tasks)
```

### WhenAny

```csharp
VlkTask<int> VlkTask.WhenAny(VlkTask task1, VlkTask task2)   // devuelve el índice del primero completado
VlkTask<T>   VlkTask.WhenAny<T>(VlkTask<T> task1, VlkTask<T> task2) // devuelve el valor del primero
```

### Cambio de hilo

```csharp
VlkTask VlkTask.SwitchToMainThread()
VlkTask VlkTask.SwitchToThreadPool()
VlkTask VlkTask.SwitchToSynchronizationContext(SynchronizationContext context)
```

### Completado / Never

```csharp
VlkTask VlkTask.CompletedTask      // pre-completado, cero asignaciones
VlkTask<T> VlkTask.FromResult<T>(T value)
VlkTask VlkTask.FromCanceled(CancellationToken ct)
VlkTask<T> VlkTask.FromCanceled<T>(CancellationToken ct)
VlkTask VlkTask.FromException(Exception ex)
VlkTask<T> VlkTask.FromException<T>(Exception ex)
VlkTask VlkTask.Never                // nunca se completa
```

### Run

```csharp
// Ejecuta el delegado en el grupo de hilos
VlkTask VlkTask.Run(Action action)
VlkTask VlkTask.Run(Func<VlkTask> factory)
VlkTask<T> VlkTask.Run<T>(Func<T> func)
VlkTask<T> VlkTask.Run<T>(Func<VlkTask<T>> factory)
```

### Forget

```csharp
// Fire-and-forget: suprimir la advertencia CS4014 de forma segura
void VlkTask.Forget(VlkTask task)
void VlkTask.Forget(VlkTask task, Action<Exception> exceptionHandler)
```

---

## Miembros de instancia

```csharp
VlkTaskStatus Status { get; }
bool IsCompleted { get; }
bool IsCompletedSuccessfully { get; }
bool IsFaulted { get; }
bool IsCanceled { get; }

// Obtener resultado (lanza si ha fallado/cancelado)
void GetResult()

// Awaiter
VlkTaskAwaiter GetAwaiter()

// Convertir a ValueTask
ValueTask AsValueTask()
```

---

## Condiciones de espera

```csharp
// Esperar hasta que una condición sea verdadera
VlkTask VlkTask.WaitUntil(Func<bool> condition)
VlkTask VlkTask.WaitUntil(Func<bool> condition, PlayerLoopTiming timing)

// Esperar mientras una condición sea verdadera
VlkTask VlkTask.WaitWhile(Func<bool> condition)

// Esperar un número fijo de fotogramas
VlkTask VlkTask.WaitForFrames(int frameCount)
VlkTask VlkTask.NextFrame()
```

---

## Diagnósticos del grupo

```csharp
// Devuelve (Type type, int currentSize, int maxSize) por grupo
IEnumerable<(Type, int, int)> VlkTask.GetPoolInfo()
```

Disponible en la ventana **Window → Valkarn Tasks → Task Tracker**.
