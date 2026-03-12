---
sidebar_position: 1
title: VlkTask
---

# VlkTask

`VlkTask` est le type awaitable de base. C'est un `readonly struct` — pas d'allocation sur le tas sur le chemin nominal (quand la tâche se termine de manière synchrone ou via le pool).

```csharp
namespace UnaPartidaMas.Valkarn.Tasks;

public readonly struct VlkTask : IEquatable<VlkTask>
```

---

## Méthodes de fabrique statiques

### Delay

```csharp
// Millisecondes (utilise PlayerLoopTiming.Update par défaut)
VlkTask VlkTask.Delay(int millisecondsDelay)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(int millisecondsDelay, CancellationToken cancellationToken)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing, CancellationToken cancellationToken)

// Surcharges TimeSpan
VlkTask VlkTask.Delay(TimeSpan delay)
VlkTask VlkTask.Delay(TimeSpan delay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(TimeSpan delay, CancellationToken cancellationToken)
```

### Yield

```csharp
// Céder à la prochaine frame (timing Update par défaut)
VlkTask VlkTask.Yield()
VlkTask VlkTask.Yield(PlayerLoopTiming timing)
VlkTask VlkTask.Yield(CancellationToken cancellationToken)
```

### WhenAll

```csharp
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2)
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2, VlkTask task3)
// ... jusqu'à 8 paramètres

// Avec valeurs de retour — déstructuration de tuple supportée
VlkTask<(T1, T2)>     VlkTask.WhenAll<T1, T2>(VlkTask<T1>, VlkTask<T2>)
VlkTask<(T1, T2, T3)> VlkTask.WhenAll<T1, T2, T3>(...)
// ... jusqu'à 8

// Surcharges de collection
VlkTask VlkTask.WhenAll(IEnumerable<VlkTask> tasks)
VlkTask<T[]> VlkTask.WhenAll<T>(IEnumerable<VlkTask<T>> tasks)
```

### WhenAny

```csharp
VlkTask<int> VlkTask.WhenAny(VlkTask task1, VlkTask task2)   // retourne l'index du premier terminé
VlkTask<T>   VlkTask.WhenAny<T>(VlkTask<T> task1, VlkTask<T> task2) // retourne la valeur du premier
```

### Changement de thread

```csharp
VlkTask VlkTask.SwitchToMainThread()
VlkTask VlkTask.SwitchToThreadPool()
VlkTask VlkTask.SwitchToSynchronizationContext(SynchronizationContext context)
```

### Terminé / Never

```csharp
VlkTask VlkTask.CompletedTask      // pré-terminé, zéro allocation
VlkTask<T> VlkTask.FromResult<T>(T value)
VlkTask VlkTask.FromCanceled(CancellationToken ct)
VlkTask<T> VlkTask.FromCanceled<T>(CancellationToken ct)
VlkTask VlkTask.FromException(Exception ex)
VlkTask<T> VlkTask.FromException<T>(Exception ex)
VlkTask VlkTask.Never                // ne se termine jamais
```

### Run

```csharp
// Exécute le délégué sur le thread pool
VlkTask VlkTask.Run(Action action)
VlkTask VlkTask.Run(Func<VlkTask> factory)
VlkTask<T> VlkTask.Run<T>(Func<T> func)
VlkTask<T> VlkTask.Run<T>(Func<VlkTask<T>> factory)
```

### Forget

```csharp
// Fire-and-forget : supprimer l'avertissement CS4014 en toute sécurité
void VlkTask.Forget(VlkTask task)
void VlkTask.Forget(VlkTask task, Action<Exception> exceptionHandler)
```

---

## Membres d'instance

```csharp
VlkTaskStatus Status { get; }
bool IsCompleted { get; }
bool IsCompletedSuccessfully { get; }
bool IsFaulted { get; }
bool IsCanceled { get; }

// Obtenir le résultat (lève une exception si faulté/annulé)
void GetResult()

// Awaiter
VlkTaskAwaiter GetAwaiter()

// Convertir en ValueTask
ValueTask AsValueTask()
```

---

## Conditions d'attente

```csharp
// Attendre jusqu'à ce qu'une condition soit vraie
VlkTask VlkTask.WaitUntil(Func<bool> condition)
VlkTask VlkTask.WaitUntil(Func<bool> condition, PlayerLoopTiming timing)

// Attendre tant qu'une condition est vraie
VlkTask VlkTask.WaitWhile(Func<bool> condition)

// Attendre un nombre fixe de frames
VlkTask VlkTask.WaitForFrames(int frameCount)
VlkTask VlkTask.NextFrame()
```

---

## Diagnostics du pool

```csharp
// Retourne (Type type, int currentSize, int maxSize) par pool
IEnumerable<(Type, int, int)> VlkTask.GetPoolInfo()
```

Disponible dans la fenêtre **Window → Valkarn Tasks → Task Tracker**.
