---
sidebar_position: 1
title: ValkarnTask
---

# ValkarnTask

`ValkarnTask` es el tipo awaitable principal. Es un `readonly struct` — sin asignación en el montón en el camino feliz (cuando la tarea se completa sincrónicamente o vía grupo).

```csharp
namespace UnaPartidaMas.Valkarn.Tasks;

public readonly struct ValkarnTask : IEquatable<ValkarnTask>
```

---

## Métodos de fábrica estáticos

### Delay

```csharp
// Milisegundos (usa PlayerLoopTiming.Update por defecto)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, CancellationToken cancellationToken)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, PlayerLoopTiming timing, CancellationToken cancellationToken)

// Sobrecargas de TimeSpan
ValkarnTask ValkarnTask.Delay(TimeSpan delay)
ValkarnTask ValkarnTask.Delay(TimeSpan delay, PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Delay(TimeSpan delay, CancellationToken cancellationToken)
```

### Yield

```csharp
// Ceder al siguiente fotograma (timing Update por defecto)
ValkarnTask ValkarnTask.Yield()
ValkarnTask ValkarnTask.Yield(PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Yield(CancellationToken cancellationToken)
```

### WhenAll

```csharp
ValkarnTask ValkarnTask.WhenAll(ValkarnTask task1, ValkarnTask task2)
ValkarnTask ValkarnTask.WhenAll(ValkarnTask task1, ValkarnTask task2, ValkarnTask task3)
// ... hasta 8 parámetros

// Con valores de retorno — desestructuración de tuplas compatible
ValkarnTask<(T1, T2)>     ValkarnTask.WhenAll<T1, T2>(ValkarnTask<T1>, ValkarnTask<T2>)
ValkarnTask<(T1, T2, T3)> ValkarnTask.WhenAll<T1, T2, T3>(...)
// ... hasta 8

// Sobrecargas de colección
ValkarnTask ValkarnTask.WhenAll(IEnumerable<ValkarnTask> tasks)
ValkarnTask<T[]> ValkarnTask.WhenAll<T>(IEnumerable<ValkarnTask<T>> tasks)
```

### WhenAny

```csharp
ValkarnTask<int> ValkarnTask.WhenAny(ValkarnTask task1, ValkarnTask task2)   // devuelve el índice del primero completado
ValkarnTask<T>   ValkarnTask.WhenAny<T>(ValkarnTask<T> task1, ValkarnTask<T> task2) // devuelve el valor del primero
```

### Cambio de hilo

```csharp
ValkarnTask ValkarnTask.SwitchToMainThread()
ValkarnTask ValkarnTask.SwitchToThreadPool()
ValkarnTask ValkarnTask.SwitchToSynchronizationContext(SynchronizationContext context)
```

### Completado / Never

```csharp
ValkarnTask ValkarnTask.CompletedTask      // pre-completado, cero asignaciones
ValkarnTask<T> ValkarnTask.FromResult<T>(T value)
ValkarnTask ValkarnTask.FromCanceled(CancellationToken ct)
ValkarnTask<T> ValkarnTask.FromCanceled<T>(CancellationToken ct)
ValkarnTask ValkarnTask.FromException(Exception ex)
ValkarnTask<T> ValkarnTask.FromException<T>(Exception ex)
ValkarnTask ValkarnTask.Never                // nunca se completa
```

### Run

```csharp
// Ejecuta el delegado en el grupo de hilos
ValkarnTask ValkarnTask.Run(Action action)
ValkarnTask ValkarnTask.Run(Func<ValkarnTask> factory)
ValkarnTask<T> ValkarnTask.Run<T>(Func<T> func)
ValkarnTask<T> ValkarnTask.Run<T>(Func<ValkarnTask<T>> factory)
```

### Forget

```csharp
// Fire-and-forget: suprimir la advertencia CS4014 de forma segura
void ValkarnTask.Forget(ValkarnTask task)
void ValkarnTask.Forget(ValkarnTask task, Action<Exception> exceptionHandler)
```

---

## Miembros de instancia

```csharp
ValkarnTaskStatus Status { get; }
bool IsCompleted { get; }
bool IsCompletedSuccessfully { get; }
bool IsFaulted { get; }
bool IsCanceled { get; }

// Obtener resultado (lanza si ha fallado/cancelado)
void GetResult()

// Awaiter
ValkarnTaskAwaiter GetAwaiter()

// Convertir a ValueTask
ValueTask AsValueTask()
```

---

## Condiciones de espera

```csharp
// Esperar hasta que una condición sea verdadera
ValkarnTask ValkarnTask.WaitUntil(Func<bool> condition)
ValkarnTask ValkarnTask.WaitUntil(Func<bool> condition, PlayerLoopTiming timing)

// Esperar mientras una condición sea verdadera
ValkarnTask ValkarnTask.WaitWhile(Func<bool> condition)

// Esperar un número fijo de fotogramas
ValkarnTask ValkarnTask.WaitForFrames(int frameCount)
ValkarnTask ValkarnTask.NextFrame()
```

---

## Diagnósticos del grupo

```csharp
// Devuelve (Type type, int currentSize, int maxSize) por grupo
IEnumerable<(Type, int, int)> ValkarnTask.GetPoolInfo()
```

Disponible en la ventana **Window → Valkarn Tasks → Task Tracker**.
