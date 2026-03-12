---
sidebar_position: 1
title: ValkarnTask
---

# ValkarnTask

`ValkarnTask` est le type awaitable de base. C'est un `readonly struct` — pas d'allocation sur le tas sur le chemin nominal (quand la tâche se termine de manière synchrone ou via le pool).

```csharp
namespace UnaPartidaMas.Valkarn.Tasks;

public readonly struct ValkarnTask : IEquatable<ValkarnTask>
```

---

## Méthodes de fabrique statiques

### Delay

```csharp
// Millisecondes (utilise PlayerLoopTiming.Update par défaut)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, CancellationToken cancellationToken)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, PlayerLoopTiming timing, CancellationToken cancellationToken)

// Surcharges TimeSpan
ValkarnTask ValkarnTask.Delay(TimeSpan delay)
ValkarnTask ValkarnTask.Delay(TimeSpan delay, PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Delay(TimeSpan delay, CancellationToken cancellationToken)
```

### Yield

```csharp
// Céder à la prochaine frame (timing Update par défaut)
ValkarnTask ValkarnTask.Yield()
ValkarnTask ValkarnTask.Yield(PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Yield(CancellationToken cancellationToken)
```

### WhenAll

```csharp
ValkarnTask ValkarnTask.WhenAll(ValkarnTask task1, ValkarnTask task2)
ValkarnTask ValkarnTask.WhenAll(ValkarnTask task1, ValkarnTask task2, ValkarnTask task3)
// ... jusqu'à 8 paramètres

// Avec valeurs de retour — déstructuration de tuple supportée
ValkarnTask<(T1, T2)>     ValkarnTask.WhenAll<T1, T2>(ValkarnTask<T1>, ValkarnTask<T2>)
ValkarnTask<(T1, T2, T3)> ValkarnTask.WhenAll<T1, T2, T3>(...)
// ... jusqu'à 8

// Surcharges de collection
ValkarnTask ValkarnTask.WhenAll(IEnumerable<ValkarnTask> tasks)
ValkarnTask<T[]> ValkarnTask.WhenAll<T>(IEnumerable<ValkarnTask<T>> tasks)
```

### WhenAny

```csharp
ValkarnTask<int> ValkarnTask.WhenAny(ValkarnTask task1, ValkarnTask task2)   // retourne l'index du premier terminé
ValkarnTask<T>   ValkarnTask.WhenAny<T>(ValkarnTask<T> task1, ValkarnTask<T> task2) // retourne la valeur du premier
```

### Changement de thread

```csharp
ValkarnTask ValkarnTask.SwitchToMainThread()
ValkarnTask ValkarnTask.SwitchToThreadPool()
ValkarnTask ValkarnTask.SwitchToSynchronizationContext(SynchronizationContext context)
```

### Terminé / Never

```csharp
ValkarnTask ValkarnTask.CompletedTask      // pré-terminé, zéro allocation
ValkarnTask<T> ValkarnTask.FromResult<T>(T value)
ValkarnTask ValkarnTask.FromCanceled(CancellationToken ct)
ValkarnTask<T> ValkarnTask.FromCanceled<T>(CancellationToken ct)
ValkarnTask ValkarnTask.FromException(Exception ex)
ValkarnTask<T> ValkarnTask.FromException<T>(Exception ex)
ValkarnTask ValkarnTask.Never                // ne se termine jamais
```

### Run

```csharp
// Exécute le délégué sur le thread pool
ValkarnTask ValkarnTask.Run(Action action)
ValkarnTask ValkarnTask.Run(Func<ValkarnTask> factory)
ValkarnTask<T> ValkarnTask.Run<T>(Func<T> func)
ValkarnTask<T> ValkarnTask.Run<T>(Func<ValkarnTask<T>> factory)
```

### Forget

```csharp
// Fire-and-forget : supprimer l'avertissement CS4014 en toute sécurité
void ValkarnTask.Forget(ValkarnTask task)
void ValkarnTask.Forget(ValkarnTask task, Action<Exception> exceptionHandler)
```

---

## Membres d'instance

```csharp
ValkarnTaskStatus Status { get; }
bool IsCompleted { get; }
bool IsCompletedSuccessfully { get; }
bool IsFaulted { get; }
bool IsCanceled { get; }

// Obtenir le résultat (lève une exception si faulté/annulé)
void GetResult()

// Awaiter
ValkarnTaskAwaiter GetAwaiter()

// Convertir en ValueTask
ValueTask AsValueTask()
```

---

## Conditions d'attente

```csharp
// Attendre jusqu'à ce qu'une condition soit vraie
ValkarnTask ValkarnTask.WaitUntil(Func<bool> condition)
ValkarnTask ValkarnTask.WaitUntil(Func<bool> condition, PlayerLoopTiming timing)

// Attendre tant qu'une condition est vraie
ValkarnTask ValkarnTask.WaitWhile(Func<bool> condition)

// Attendre un nombre fixe de frames
ValkarnTask ValkarnTask.WaitForFrames(int frameCount)
ValkarnTask ValkarnTask.NextFrame()
```

---

## Diagnostics du pool

```csharp
// Retourne (Type type, int currentSize, int maxSize) par pool
IEnumerable<(Type, int, int)> ValkarnTask.GetPoolInfo()
```

Disponible dans la fenêtre **Window → Valkarn Tasks → Task Tracker**.
