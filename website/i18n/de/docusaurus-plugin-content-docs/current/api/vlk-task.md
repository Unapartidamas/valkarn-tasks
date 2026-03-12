---
sidebar_position: 1
title: ValkarnTask
---

# ValkarnTask

`ValkarnTask` ist der zentrale awaitable Typ. Es ist ein `readonly struct` — keine Heap-Allokation auf dem Erfolgspfad (wenn die Task synchron oder über einen Pool abschließt).

```csharp
namespace UnaPartidaMas.Valkarn.Tasks;

public readonly struct ValkarnTask : IEquatable<ValkarnTask>
```

---

## Statische Factory-Methoden

### Delay

```csharp
// Millisekunden (verwendet standardmäßig PlayerLoopTiming.Update)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, CancellationToken cancellationToken)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, PlayerLoopTiming timing, CancellationToken cancellationToken)

// TimeSpan-Überladungen
ValkarnTask ValkarnTask.Delay(TimeSpan delay)
ValkarnTask ValkarnTask.Delay(TimeSpan delay, PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Delay(TimeSpan delay, CancellationToken cancellationToken)
```

### Yield

```csharp
// Zum nächsten Frame abgeben (standardmäßig Update-Zeitpunkt)
ValkarnTask ValkarnTask.Yield()
ValkarnTask ValkarnTask.Yield(PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Yield(CancellationToken cancellationToken)
```

### WhenAll

```csharp
ValkarnTask ValkarnTask.WhenAll(ValkarnTask task1, ValkarnTask task2)
ValkarnTask ValkarnTask.WhenAll(ValkarnTask task1, ValkarnTask task2, ValkarnTask task3)
// ... bis zu 8 Parameter

// Mit Rückgabewerten — Tupel-Destructuring wird unterstützt
ValkarnTask<(T1, T2)>     ValkarnTask.WhenAll<T1, T2>(ValkarnTask<T1>, ValkarnTask<T2>)
ValkarnTask<(T1, T2, T3)> ValkarnTask.WhenAll<T1, T2, T3>(...)
// ... bis zu 8

// Sammlungsüberladungen
ValkarnTask ValkarnTask.WhenAll(IEnumerable<ValkarnTask> tasks)
ValkarnTask<T[]> ValkarnTask.WhenAll<T>(IEnumerable<ValkarnTask<T>> tasks)
```

### WhenAny

```csharp
ValkarnTask<int> ValkarnTask.WhenAny(ValkarnTask task1, ValkarnTask task2)   // gibt Index der ersten abgeschlossenen zurück
ValkarnTask<T>   ValkarnTask.WhenAny<T>(ValkarnTask<T> task1, ValkarnTask<T> task2) // gibt Wert der ersten zurück
```

### Thread-Wechsel

```csharp
ValkarnTask ValkarnTask.SwitchToMainThread()
ValkarnTask ValkarnTask.SwitchToThreadPool()
ValkarnTask ValkarnTask.SwitchToSynchronizationContext(SynchronizationContext context)
```

### Abgeschlossen / Niemals

```csharp
ValkarnTask ValkarnTask.CompletedTask      // vorab abgeschlossen, null Allokation
ValkarnTask<T> ValkarnTask.FromResult<T>(T value)
ValkarnTask ValkarnTask.FromCanceled(CancellationToken ct)
ValkarnTask<T> ValkarnTask.FromCanceled<T>(CancellationToken ct)
ValkarnTask ValkarnTask.FromException(Exception ex)
ValkarnTask<T> ValkarnTask.FromException<T>(Exception ex)
ValkarnTask ValkarnTask.Never                // schließt nie ab
```

### Run

```csharp
// Führt Delegate auf Thread-Pool aus
ValkarnTask ValkarnTask.Run(Action action)
ValkarnTask ValkarnTask.Run(Func<ValkarnTask> factory)
ValkarnTask<T> ValkarnTask.Run<T>(Func<T> func)
ValkarnTask<T> ValkarnTask.Run<T>(Func<ValkarnTask<T>> factory)
```

### Forget

```csharp
// Fire-and-Forget: CS4014-Warnung sicher unterdrücken
void ValkarnTask.Forget(ValkarnTask task)
void ValkarnTask.Forget(ValkarnTask task, Action<Exception> exceptionHandler)
```

---

## Instanzmember

```csharp
ValkarnTaskStatus Status { get; }
bool IsCompleted { get; }
bool IsCompletedSuccessfully { get; }
bool IsFaulted { get; }
bool IsCanceled { get; }

// Ergebnis abrufen (wirft bei Fehler/Abbruch)
void GetResult()

// Awaiter
ValkarnTaskAwaiter GetAwaiter()

// In ValueTask umwandeln
ValueTask AsValueTask()
```

---

## Wartebedingungen

```csharp
// Warten, bis eine Bedingung wahr ist
ValkarnTask ValkarnTask.WaitUntil(Func<bool> condition)
ValkarnTask ValkarnTask.WaitUntil(Func<bool> condition, PlayerLoopTiming timing)

// Warten, während eine Bedingung wahr ist
ValkarnTask ValkarnTask.WaitWhile(Func<bool> condition)

// Auf eine feste Anzahl von Frames warten
ValkarnTask ValkarnTask.WaitForFrames(int frameCount)
ValkarnTask ValkarnTask.NextFrame()
```

---

## Pool-Diagnose

```csharp
// Gibt (Type type, int currentSize, int maxSize) pro Pool zurück
IEnumerable<(Type, int, int)> ValkarnTask.GetPoolInfo()
```

Verfügbar im Fenster **Window → Valkarn Tasks → Task Tracker**.
