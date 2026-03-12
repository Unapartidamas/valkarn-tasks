---
sidebar_position: 1
title: VlkTask
---

# VlkTask

`VlkTask` ist der zentrale awaitable Typ. Es ist ein `readonly struct` — keine Heap-Allokation auf dem Erfolgspfad (wenn die Task synchron oder über einen Pool abschließt).

```csharp
namespace UnaPartidaMas.Valkarn.Tasks;

public readonly struct VlkTask : IEquatable<VlkTask>
```

---

## Statische Factory-Methoden

### Delay

```csharp
// Millisekunden (verwendet standardmäßig PlayerLoopTiming.Update)
VlkTask VlkTask.Delay(int millisecondsDelay)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(int millisecondsDelay, CancellationToken cancellationToken)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing, CancellationToken cancellationToken)

// TimeSpan-Überladungen
VlkTask VlkTask.Delay(TimeSpan delay)
VlkTask VlkTask.Delay(TimeSpan delay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(TimeSpan delay, CancellationToken cancellationToken)
```

### Yield

```csharp
// Zum nächsten Frame abgeben (standardmäßig Update-Zeitpunkt)
VlkTask VlkTask.Yield()
VlkTask VlkTask.Yield(PlayerLoopTiming timing)
VlkTask VlkTask.Yield(CancellationToken cancellationToken)
```

### WhenAll

```csharp
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2)
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2, VlkTask task3)
// ... bis zu 8 Parameter

// Mit Rückgabewerten — Tupel-Destructuring wird unterstützt
VlkTask<(T1, T2)>     VlkTask.WhenAll<T1, T2>(VlkTask<T1>, VlkTask<T2>)
VlkTask<(T1, T2, T3)> VlkTask.WhenAll<T1, T2, T3>(...)
// ... bis zu 8

// Sammlungsüberladungen
VlkTask VlkTask.WhenAll(IEnumerable<VlkTask> tasks)
VlkTask<T[]> VlkTask.WhenAll<T>(IEnumerable<VlkTask<T>> tasks)
```

### WhenAny

```csharp
VlkTask<int> VlkTask.WhenAny(VlkTask task1, VlkTask task2)   // gibt Index der ersten abgeschlossenen zurück
VlkTask<T>   VlkTask.WhenAny<T>(VlkTask<T> task1, VlkTask<T> task2) // gibt Wert der ersten zurück
```

### Thread-Wechsel

```csharp
VlkTask VlkTask.SwitchToMainThread()
VlkTask VlkTask.SwitchToThreadPool()
VlkTask VlkTask.SwitchToSynchronizationContext(SynchronizationContext context)
```

### Abgeschlossen / Niemals

```csharp
VlkTask VlkTask.CompletedTask      // vorab abgeschlossen, null Allokation
VlkTask<T> VlkTask.FromResult<T>(T value)
VlkTask VlkTask.FromCanceled(CancellationToken ct)
VlkTask<T> VlkTask.FromCanceled<T>(CancellationToken ct)
VlkTask VlkTask.FromException(Exception ex)
VlkTask<T> VlkTask.FromException<T>(Exception ex)
VlkTask VlkTask.Never                // schließt nie ab
```

### Run

```csharp
// Führt Delegate auf Thread-Pool aus
VlkTask VlkTask.Run(Action action)
VlkTask VlkTask.Run(Func<VlkTask> factory)
VlkTask<T> VlkTask.Run<T>(Func<T> func)
VlkTask<T> VlkTask.Run<T>(Func<VlkTask<T>> factory)
```

### Forget

```csharp
// Fire-and-Forget: CS4014-Warnung sicher unterdrücken
void VlkTask.Forget(VlkTask task)
void VlkTask.Forget(VlkTask task, Action<Exception> exceptionHandler)
```

---

## Instanzmember

```csharp
VlkTaskStatus Status { get; }
bool IsCompleted { get; }
bool IsCompletedSuccessfully { get; }
bool IsFaulted { get; }
bool IsCanceled { get; }

// Ergebnis abrufen (wirft bei Fehler/Abbruch)
void GetResult()

// Awaiter
VlkTaskAwaiter GetAwaiter()

// In ValueTask umwandeln
ValueTask AsValueTask()
```

---

## Wartebedingungen

```csharp
// Warten, bis eine Bedingung wahr ist
VlkTask VlkTask.WaitUntil(Func<bool> condition)
VlkTask VlkTask.WaitUntil(Func<bool> condition, PlayerLoopTiming timing)

// Warten, während eine Bedingung wahr ist
VlkTask VlkTask.WaitWhile(Func<bool> condition)

// Auf eine feste Anzahl von Frames warten
VlkTask VlkTask.WaitForFrames(int frameCount)
VlkTask VlkTask.NextFrame()
```

---

## Pool-Diagnose

```csharp
// Gibt (Type type, int currentSize, int maxSize) pro Pool zurück
IEnumerable<(Type, int, int)> VlkTask.GetPoolInfo()
```

Verfügbar im Fenster **Window → Valkarn Tasks → Task Tracker**.
