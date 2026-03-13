---
sidebar_position: 7
title: ValkarnSemaphore
---

# ValkarnSemaphore

`ValkarnSemaphore` ist ein asynchroner Semaphor ohne Allokationen, der auf `ValkarnTask` aufbaut. Er funktioniert wie `SemaphoreSlim`, integriert sich jedoch nativ in die Valkarn Tasks-Pipeline — keine `Task`-Allokationen, kein GC-Druck im eingeschwungenen Zustand.

```csharp
namespace UnaPartidaMas.Valkarn.Tasks
```

**Schneller Pfad (ohne Konkurrenz):** gibt `ValkarnTask.CompletedTask` sofort zurück — null Allokationen.
**Langsamer Pfad (mit Konkurrenz):** Warteknoten werden aus einem begrenzten Pool ausgeliehen und nach Abschluss zurückgegeben — kein GC im eingeschwungenen Zustand.

---

## Konstruktor

```csharp
public ValkarnSemaphore(int initialCount, int maxCount)
```

| Parameter | Beschreibung |
|-----------|-------------|
| `initialCount` | Anzahl der sofort verfügbaren Slots. Muss in `[0, maxCount]` liegen. |
| `maxCount` | Obergrenze für die Slot-Anzahl. Muss > 0 sein. |

---

## Eigenschaften

```csharp
public int CurrentCount { get; } // Aktuell verfügbare Slots (thread-sicher)
```

---

## Methoden

### WaitAsync

```csharp
public ValkarnTask WaitAsync(CancellationToken ct = default)
```

Belegt asynchron einen Slot. Gibt sofort einen abgeschlossenen `ValkarnTask` zurück, wenn ein Slot verfügbar ist (null Allokationen). Suspendiert den Aufrufer, bis `Release()` aufgerufen wird, falls kein Slot verfügbar ist. Wartende werden in FIFO-Reihenfolge bedient.

Wirft `OperationCanceledException`, wenn `ct` während des Wartens ausgelöst wird.

### Wait

```csharp
public void Wait(CancellationToken ct = default)
```

Synchrone Variante. Blockiert den aufrufenden Thread, wenn kein Slot verfügbar ist. Nur verwenden, wenn `await` nicht möglich ist (z. B. nicht-asynchrone Einstiegspunkte).

### Release

```csharp
public void Release(int releaseCount = 1)
```

Gibt `releaseCount` Slots zurück. Ausstehende Wartende werden in FIFO-Reihenfolge außerhalb der internen Sperre abgeschlossen — Fortsetzungen laufen niemals mit gehaltener Sperre.

Wirft `InvalidOperationException`, wenn die Freigabe `maxCount` überschreiten würde.

### Dispose

```csharp
public void Dispose()
```

Bricht alle ausstehenden Wartenden ab und gibt Ressourcen frei. Idempotent.

---

## Verwendung

### Gegenseitiger Ausschluss (1 Slot)

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

### Begrenzte Parallelität (N Slots)

```csharp
// Erlaubt bis zu 4 gleichzeitige Netzwerkanfragen.
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

### Produzenten- / Konsumenten-Tor

```csharp
// Beginnt mit 0 Slots — der Konsument wartet, bis der Produzent signalisiert.
var gate = new ValkarnSemaphore(initialCount: 0, maxCount: 1);

async ValkarnTask ProducerAsync(CancellationToken ct)
{
    await ProduceDataAsync(ct);
    gate.Release(); // Konsumenten signalisieren
}

async ValkarnTask ConsumerAsync(CancellationToken ct)
{
    await gate.WaitAsync(ct); // auf Signal warten
    await ConsumeDataAsync(ct);
}
```

---

## Thread-Sicherheit

Alle Operationen sind thread-sicher. `Release()` kann von jedem Thread aus aufgerufen werden, einschließlich Hintergrund-Threads. Abschlüsse werden außerhalb der internen Sperre ausgeführt — eine Fortsetzung, die erneut in den Semaphor eintritt, wird keinen Deadlock verursachen.

---

## Vergleich mit SemaphoreSlim

| | `ValkarnSemaphore` | `SemaphoreSlim` |
|---|---|---|
| Allokation schneller Pfad | Null | Null |
| Allokation langsamer Pfad | Gepoolter Knoten, kein GC | `Task`-Allokation |
| Integration mit ValkarnTask | Ja | Erfordert `.AsValkarnTask()` |
| `IDisposable` | Ja | Ja |
| FIFO-Reihenfolge | Ja | Ja |
| Abbruch | Ja | Ja |
