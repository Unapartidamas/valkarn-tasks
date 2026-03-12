---
sidebar_position: 2
title: Channels
---

# Channels

Channels bieten eine thread-sichere, asynchron-fähige Pipeline zur Datenübergabe zwischen Produzenten und Konsumenten. Sie eignen sich besonders für Spielsysteme, bei denen Arbeit auf einer Seite generiert wird (Hintergrund-Threads, Event-Callbacks, Job-Abschlüsse) und auf einer anderen konsumiert werden muss (Haupt-Thread, Worker-Pools).

## Einen Channel erstellen

Channels werden über die statische `VlkTask.Channel`-Factory-Klasse erstellt. Es gibt keinen öffentlichen Konstruktor.

### Unbegrenzter Channel

```csharp
Channel<T> channel = VlkTask.Channel.CreateUnbounded<T>(bool multiConsumer = false);
```

Ein unbegrenzter Channel hat kein Kapazitätslimit. `WriteAsync` und `TryWrite` gelingen immer sofort, solange der Channel nicht abgeschlossen wurde. Elemente sammeln sich in einer internen `Queue<T>` an, bis ein Konsument sie liest.

**Parameter**

| Parameter | Standard | Beschreibung |
|-----------|---------|-------------|
| `multiConsumer` | `false` | Wenn `true`, werden mehrere gleichzeitige `ReadAsync`-Aufrufe unterstützt (konkurrierende Konsumenten). Wenn `false`, darf jeweils nur ein Leser `ReadAsync` abwarten. |

### Begrenzter Channel

```csharp
Channel<T> channel = VlkTask.Channel.CreateBounded<T>(int capacity, bool multiConsumer = false);
```

Ein begrenzter Channel hält höchstens `capacity` Elemente in einem Ringpuffer fester Größe. Wenn der Puffer voll ist, suspendiert `WriteAsync` den aufrufenden Code asynchron, bis Platz verfügbar wird (Gegendruck). `TryWrite` gibt sofort `false` zurück, wenn er voll ist, statt zu warten.

**Parameter**

| Parameter | Standard | Beschreibung |
|-----------|---------|-------------|
| `capacity` | erforderlich | Maximale Anzahl von Elementen, die der Puffer halten kann. Muss größer als null sein. |
| `multiConsumer` | `false` | Wie bei unbegrenzt — ermöglicht mehrere gleichzeitige Leser. |

**Zwischen begrenzt und unbegrenzt wählen**

Verwenden Sie `CreateBounded`, wenn Sie Gegendruck anwenden möchten — d. h. wenn Sie möchten, dass Produzenten automatisch langsamer werden, wenn Konsumenten zurückfallen. Verwenden Sie `CreateUnbounded`, wenn die Produzenten-Rate natürlich begrenzt ist (z. B. Eingabe-Events) und unbegrenztes Puffern akzeptabel ist, oder wenn Sie das Speicherwachstum bereits berücksichtigt haben.

---

## Channel&lt;T&gt;

`Channel<T>` ist ein Container, der zwei Seiten der Pipeline als separate Objekte verfügbar macht.

```csharp
public sealed class Channel<T>
{
    public ChannelReader<T> Reader { get; }
    public ChannelWriter<T> Writer { get; }
}
```

Behalten Sie die `Writer`-Referenz auf der Produzentenseite und die `Reader`-Referenz auf der Konsumentenseite. Es gibt keine Anforderung, dass sie sich auf demselben Thread befinden.

---

## ChannelWriter&lt;T&gt;

`ChannelWriter<T>` ist die Schreibseite des Channels. Erhalten Sie sie über `channel.Writer`.

### TryWrite

```csharp
public abstract bool TryWrite(T item);
```

Versucht, ein Element ohne Suspension zu schreiben. Gibt `true` zurück, wenn das Element akzeptiert wurde; gibt `false` zurück, wenn der Channel voll (begrenzt) oder abgeschlossen ist.

Verwenden Sie `TryWrite` auf heißen Pfaden, wo Sie es sich leisten können, Elemente zu verwerfen, oder wenn Sie in einer Schleife abfragen und Allokationen durch asynchrone Zustandsmaschinen vermeiden möchten.

```csharp
if (!channel.Writer.TryWrite(item))
{
    // Channel ist voll oder geschlossen — entsprechend behandeln
}
```

### WriteAsync

```csharp
public abstract VlkTask WriteAsync(T item);
```

Schreibt ein Element in den Channel und suspendiert den Aufrufer asynchron, wenn nötig.

- **Unbegrenzte Channels**: schließt immer synchron ab (null-Allokations-Schnellpfad), solange der Channel offen ist.
- **Begrenzte Channels**: schließt synchron ab, wenn im Puffer Platz ist; suspendiert den Aufrufer und reiht einen ausstehenden-Schreiber-Eintrag ein, wenn der Puffer voll ist. Der Aufrufer wird fortgesetzt, sobald ein Konsument ein Element liest und einen Slot freimacht.

Wirft `ChannelClosedException`, wenn der Channel vor oder während des Schreibens über `Complete()` abgeschlossen wurde.

```csharp
await channel.Writer.WriteAsync(item);
```

Mehrere Produzenten können `WriteAsync` gleichzeitig auf einem begrenzten Channel aufrufen. Jeder suspendierte Schreiber wird eingereiht und in FIFO-Reihenfolge entsperrt, wenn Platz verfügbar wird.

### Complete

```csharp
public abstract void Complete();
```

Signalisiert, dass keine weiteren Elemente geschrieben werden. Nach dem Aufruf von `Complete()`:

- Alle bereits geschriebenen Elemente bleiben im Puffer und können noch konsumiert werden.
- Neue Aufrufe von `WriteAsync` oder `TryWrite` schlagen mit `ChannelClosedException` fehl.
- Sobald der Puffer vollständig geleert ist, schließt `ChannelReader<T>.Completion` ab und alle ausstehenden oder zukünftigen `ReadAsync`-Aufrufe werfen `ChannelClosedException`.

`Complete()` ist idempotent bei einem einzigen Aufruf — mehrfaches Aufrufen ist sicher (nachfolgende Aufrufe werden ignoriert).

```csharp
// Ende der Arbeit signalisieren
channel.Writer.Complete();
```

---

## ChannelReader&lt;T&gt;

`ChannelReader<T>` ist die Leseseite des Channels. Erhalten Sie sie über `channel.Reader`.

### ReadAsync

```csharp
public abstract VlkTask<T> ReadAsync();
```

Liest das nächste Element aus dem Channel. Wenn kein Element aktuell verfügbar ist, suspendiert der Aufrufer asynchron, bis eines ankommt. Wenn der Channel sowohl abgeschlossen als auch vollständig geleert ist, wird `ChannelClosedException` geworfen.

```csharp
T item = await channel.Reader.ReadAsync();
```

**Einzelkonsumenten-Modus** (Standard): Nur ein `ReadAsync` darf gleichzeitig aktiv sein. Der Versuch, ein zweites gleichzeitiges `ReadAsync` zu starten, wirft sofort. Diese Einschränkung ermöglicht eine interne null-Allokations-Optimierung — der Leser-Kern ist direkt in die Channel-Implementierung eingebettet, anstatt pro Aufruf aus einem Pool ausgeliehen zu werden.

**Mehrkonsumenten-Modus** (`multiConsumer: true`): Eine beliebige Anzahl von `ReadAsync`-Aufrufen kann gleichzeitig ausstehen. Jeder wartende Aufrufer wird eingereiht und in FIFO-Reihenfolge aufgelöst, wenn Elemente verfügbar werden.

### TryRead

```csharp
public abstract bool TryRead(out T item);
```

Versucht, ein Element ohne Suspension zu lesen. Gibt `true` zurück und befüllt `item`, wenn ein Element verfügbar war; gibt `false` zurück, wenn der Channel leer ist (setzt `item` auf `default`).

`TryRead` unterscheidet nicht zwischen einem leeren-aber-offenen Channel und einem leeren-und-geschlossenen Channel. Verwenden Sie `Completion`, um den geschlossenen Zustand zu erkennen, wenn Sie `TryRead` in einer Polling-Schleife verwenden.

### ReadAllAsync

```csharp
public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default);
```

Gibt ein `IAsyncEnumerable<T>` zurück, das über alle Elemente iteriert, bis der Channel abgeschlossen und geleert ist. Die Enumeration endet sauber ohne Weitergabe von `ChannelClosedException` — sie hört einfach auf.

```csharp
await foreach (var item in channel.Reader.ReadAllAsync(ct))
{
    Process(item);
}
// Hier angekommen, wenn Channel vollständig und leer ist
```

Das an `ReadAllAsync` übergebene Abbruch-Token wird als Fallback verwendet. Wenn `GetAsyncEnumerator` auch ein Token übergeben wird (wie `await foreach` via `WithCancellation` tut), hat das `foreach`-seitige Token Vorrang.

### Completion

```csharp
public abstract VlkTask Completion { get; }
```

Ein `VlkTask`, das abschließt, wenn der Channel nach dem Aufruf von `Complete()` vollständig geleert ist. Konkret:

- Wenn `Complete()` auf einem bereits leeren Channel aufgerufen wird, wird `Completion` sofort aufgelöst.
- Wenn `Complete()` aufgerufen wird, während noch Elemente im Puffer sind, wird `Completion` erst aufgelöst, nachdem das letzte Element konsumiert wurde.

Das Abwarten von `Completion` ist der kanonische Weg, auf das Ende einer Pipeline zu warten.

```csharp
channel.Writer.Complete();
await channel.Reader.Completion;
// Alle Elemente wurden konsumiert
```

---

## ChannelClosedException

```csharp
public sealed class ChannelClosedException : InvalidOperationException
```

Wird in zwei Umständen geworfen:

1. **Lesen aus einem abgeschlossenen, geleerten Channel** — `ReadAsync()` wirft, wenn der Channel als abgeschlossen markiert ist und keine Elemente mehr vorhanden sind.
2. **Schreiben in einen abgeschlossenen Channel** — `WriteAsync()` wirft, wenn `Complete()` vor dem Schreiben aufgerufen wurde.

`ChannelClosedException` erbt von `InvalidOperationException`. Es wird nicht von `TryRead` oder `TryWrite` geworfen, die stattdessen `false` zurückgeben.

Konstruktoren:

```csharp
new ChannelClosedException()
new ChannelClosedException(string message)
new ChannelClosedException(Exception innerException)
```

---

## Begrenzter Channel: Gegendruck im Detail

Wenn der Puffer eines begrenzten Channels voll ist und `WriteAsync` aufgerufen wird, wird der Schreiber suspendiert und ein ausstehender-Schreiber-Eintrag intern eingereiht. Der Schreiber behält sein Element. Wenn ein Konsument `ReadAsync` oder `TryRead` aufruft und ein Element aus der Warteschlange entfernt:

1. Der freigewordene Slot wird sofort vom ältesten ausstehenden Schreiber beansprucht.
2. Das Element dieses Schreibers wird in den Puffer gelegt.
3. Der awaiting-Code des Schreibers wird fortgesetzt.

Das bedeutet, dass ein voller begrenzter Channel nie Elemente verliert und nie Pufferkapazität verschwendet — es gibt immer eine Eins-zu-eins-Entsprechung zwischen einem freiwerdenden Slot und einem entsperrten blockierten Schreiber. In Fällen, in denen ein Leser ankommt, während ausstehende Schreiber warten, der Puffer aber leer ist, wird das Element direkt übergeben, ohne den Puffer zu berühren.

---

## Muster

### Einfacher Produzent/Konsument

```csharp
var channel = VlkTask.Channel.CreateUnbounded<WorkItem>();

// Produzent (läuft z. B. auf einem Hintergrund-Thread oder aus Callbacks)
async VlkTask ProduceAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var work = await FetchNextWorkItemAsync(ct);
        await channel.Writer.WriteAsync(work);
    }
    channel.Writer.Complete();
}

// Konsument (läuft wo immer Sie es aufrufen)
async VlkTask ConsumeAsync()
{
    await foreach (var item in channel.Reader.ReadAllAsync())
    {
        await ProcessAsync(item);
    }
}
```

### Mehrere Produzenten, einzelner Konsument

Mehrere Produzenten können jeweils eine Referenz auf `channel.Writer` halten und `WriteAsync` gleichzeitig aufrufen. Alle Channel-Operationen sind durch eine interne Sperre geschützt, daher ist dies sicher.

```csharp
var channel = VlkTask.Channel.CreateBounded<Event>(capacity: 64);

// Mehrere Produzenten schreiben gleichzeitig
VlkTask ProducerA() => ProduceFrom(sourceA, channel.Writer);
VlkTask ProducerB() => ProduceFrom(sourceB, channel.Writer);
VlkTask ProducerC() => ProduceFrom(sourceC, channel.Writer);

// Einzelner Konsument (Standard — kein multiConsumer-Flag benötigt)
async VlkTask ConsumerAsync()
{
    await foreach (var ev in channel.Reader.ReadAllAsync())
        HandleEvent(ev);
}
```

Wenn Sie mehrere Produzenten mit einem begrenzten Channel verwenden, koordinieren Sie `Complete()` sorgfältig — rufen Sie es erst auf, nachdem alle Produzenten das Schreiben beendet haben, sonst könnten manche Schreiber `ChannelClosedException` erhalten.

### Mehrere Produzenten, mehrere Konsumenten

```csharp
// multiConsumer: true ermöglicht gleichzeitiges ReadAsync von mehreren Konsumenten
var channel = VlkTask.Channel.CreateUnbounded<Job>(multiConsumer: true);

async VlkTask WorkerAsync(int id, CancellationToken ct)
{
    try
    {
        while (true)
        {
            var job = await channel.Reader.ReadAsync();
            await ExecuteJobAsync(job, ct);
        }
    }
    catch (ChannelClosedException)
    {
        // Channel ist fertig — ordentlich beenden
    }
}
```

Jedes Element wird genau einem Konsumenten zugestellt. Konsumenten konkurrieren um Elemente in FIFO-Reihenfolge (der Konsument, der am längsten gewartet hat, erhält das nächste verfügbare Element).

### Ordnungsgemäßes Herunterfahren

Die empfohlene Abschaltsequenz ist:

1. Alle Produzenten stoppen signalisieren (z. B. ihr `CancellationToken` abbrechen).
2. `channel.Writer.Complete()` aufrufen, nachdem alle Produzenten das Schreiben beendet haben.
3. `channel.Reader.Completion` abwarten, um zu bestätigen, dass alle Elemente konsumiert wurden.

```csharp
cts.Cancel();                        // Produzenten stoppen
await allProducersTask;              // auf deren Ende warten
channel.Writer.Complete();           // Channel versiegeln
await channel.Reader.Completion;     // verbleibende Elemente abschöpfen
```

Wenn Sie `ReadAllAsync` verwenden, passiert Schritt 3 automatisch — die `await foreach`-Schleife endet, wenn der Channel abgeschlossen und leer ist.

---

## Vergleich mit System.Threading.Channels

| Funktion | Valkarn Channels | System.Threading.Channels |
|---------|-----------------|---------------------------|
| Rückgabetyp | `VlkTask` / `VlkTask<T>` | `ValueTask` / `ValueTask<T>` |
| Allokation (heißer Pfad) | Null (einzelner Konsument, unbegrenzt) | Nahezu null |
| `WaitToReadAsync` | Nicht vorhanden — `ReadAsync` oder `ReadAllAsync` verwenden | Vorhanden |
| `TryComplete(Exception)` | Nicht vorhanden — `Complete()` verwenden | Vorhanden |
| `Count` / `CanCount` | Nicht verfügbar | Bei einigen Channel-Typen vorhanden |
| Drop-Richtlinie bei vollem Channel | Nicht unterstützt — `WriteAsync` blockiert | `DropWrite`, `DropNewest`, `DropOldest`, `Wait` |
| Asynchron enumerable | `ReadAllAsync()` | `ReadAllAsync()` |
| Thread-Sicherheit | Vollständig (sperrenbasiert) | Vollständig (sperrenbasiert) |

Der Hauptunterschied ist, dass Valkarn Channels nativ mit `VlkTask` für null-Overhead-Abwarten in Unity-Builds integrieren, und der Einzelkonsumenten-Pfad eine Pool-Ausleihe/-Rückgabe bei jedem `ReadAsync`-Aufruf vermeidet, indem der Abschluss-Kern direkt in den Channel eingebettet wird.
