---
sidebar_position: 1
title: Struct Tasks
---

# Struct Tasks

`VlkTask` und `VlkTask<T>` sind die zentralen asynchronen Rückgabetypen in Valkarn Tasks. Anders als `System.Threading.Tasks.Task`, das ein Referenztyp ist, der immer auf dem Heap alloziert, sind beide Valkarn-Task-Typen `readonly struct`-Werte. Diese Seite erklärt, was das in der Praxis bedeutet, wie der null-Allokations-Erfolgspfad funktioniert und wie der Compiler mit der async/await-Maschinerie zusammenarbeitet.

---

## Warum ein `readonly struct`?

Eine klassenbasierte Task wie `Task<T>` muss bei jedem Aufruf einer asynchronen Methode auf dem Heap alloziert werden, selbst für Methoden, die synchron abschließen. In einer Unity-Spielschleife, die mit 60 fps läuft, können Hunderte kleiner asynchroner Operationen pro Frame zu messbarem GC-Druck führen.

`VlkTask` und `VlkTask<T>` werden als `readonly partial struct` deklariert:

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncVlkTaskMethodBuilder))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct VlkTask
{
    internal readonly ISource source;
    internal readonly uint token;
}
```

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncVlkTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct VlkTask<T>
{
    internal readonly VlkTask.ISource<T> source;
    internal readonly T result;
    internal readonly uint token;
}
```

Als Struct lebt der Task-Wert selbst auf dem Stack (oder eingebettet in sein übergeordnetes Objekt) statt auf dem Heap. Der `readonly`-Modifikator stellt sicher, dass der Compiler über Unveränderlichkeit nachdenken kann und verhindert versehentliche Kopierungsfehler. `StructLayout.Auto` lässt die Laufzeit die Feldreihenfolge für die Zielplattform optimieren.

### Die Schlüsselinvariante: `source == null`

Das Design basiert auf einer einzigen Invariante:

> Wenn `source` `null` ist, ist die Task synchron abgeschlossen ohne Fehler. Es ist kein Heap-Objekt beteiligt.

`VlkTask.CompletedTask` ist `default(VlkTask)` — sein `source`-Feld ist null, also kostet es nichts. `VlkTask<T>` trägt sein Ergebnis inline im `result`-Feld, was auch `VlkTask.FromResult(value)` zu einem null-Allokations-Aufruf macht:

```csharp
// Null Allokation — source ist null, Ergebnis inline gespeichert
VlkTask<int> task = VlkTask.FromResult(42);

// Ebenfalls null Allokation — source ist null
VlkTask done = VlkTask.CompletedTask;
```

---

## Der null-Allokations-Erfolgspfad

Wenn eine `async`-Methode abschließt, ohne jemals zu suspendieren (kein `await` gibt an eine unvollständige Operation ab), läuft die gesamte Methode synchron auf dem aufrufenden Thread. Der Builder erkennt dies und gibt eine Task mit `source == null` zurück.

Der Awaiter prüft dies sofort:

```csharp
public bool IsCompleted
{
    get
    {
        var s = task.source;
        return s == null || s.GetStatus(task.token).IsCompleted();
    }
}
```

Wenn `IsCompleted` wahr ist, bevor `OnCompleted` jemals aufgerufen wird, registriert die Zustandsmaschine keine Fortsetzung. `GetResult` wird sofort aufgerufen, und für `VlkTask<T>` mit `source == null` wird das Ergebnis aus dem inline gespeicherten `result`-Feld des Structs gelesen:

```csharp
public T GetResult()
{
    var s = task.source;
    if (s == null)
        return task.result;   // inline, kein ISource-Aufruf
    return s.GetResult(task.token);
}
```

Es wird kein Objekt erstellt, kein Interface-Dispatch findet statt, und kein Fortsetzungs-Delegate wird alloziert. Das gesamte Await wird als direktes Wertelesen aufgelöst.

### Wenn eine Quelle benötigt wird

Wenn eine asynchrone Methode suspendiert (etwas abwartet, das noch nicht abgeschlossen ist), alloziert der Builder ein gepooltes `AsyncVlkTaskRunner<TStateMachine>`-Objekt (oder `AsyncVlkTaskRunner<TStateMachine, TResult>` für die generische Variante). Dieses Objekt dient einem doppelten Zweck: Es hält die compilergenerierte Zustandsmaschine als Wert und implementiert `VlkTask.ISource`, sodass es direkt als Backing-Quelle der Task verwendet werden kann. Die an Aufrufer zurückgegebene Task umhüllt diesen Runner zusammen mit einem generationalen `uint`-Token.

Bei Abschluss, wenn der Aufrufer `GetResult` am Awaiter aufruft, setzt sich der Runner zurück und kehrt in seinen Pool zurück — so wird die Allokation über viele Methodenaufrufe verteilt.

---

## Das `ISource`-Interface

Der Vertrag zwischen einem `VlkTask`-Struct und seinem asynchronen Backing-Objekt ist `VlkTask.ISource`:

```csharp
public interface ISource
{
    VlkTask.Status GetStatus(uint token);
    void GetResult(uint token);
    void OnCompleted(Action<object> continuation, object state, uint token);
    VlkTask.Status UnsafeGetStatus();
}

public interface ISource<out T> : ISource
{
    new T GetResult(uint token);
}
```

Jedes Objekt, das `ISource` implementiert, kann eine `VlkTask` unterstützen. Die Bibliothek liefert mehrere Implementierungen:

| Typ | Zweck |
|-----|-------|
| `AsyncVlkTaskRunner<TStateMachine>` | Unterstützt jede `async VlkTask`-Methode (intern) |
| `AsyncVlkTaskRunner<TStateMachine, TResult>` | Unterstützt jede `async VlkTask<T>`-Methode (intern) |
| `VlkTask.PooledPromise` | Manueller Abschluss-Source mit automatischer Pool-Rückgabe |
| `VlkTask.PooledPromise<T>` | Generische Variante des Obigen |
| `VlkTask.Promise` | Manueller Abschluss-Source ohne Pooling (langlebige Operationen) |
| `VlkTask.Promise<T>` | Generische Variante des Obigen |

Der `uint token`-Parameter ist eine Generationsschutz-Maßnahme. Wenn eine gepoolte Quelle für die Wiederverwendung zurückgesetzt wird, erhöht sich ihr Generationszähler. Jedes `VlkTask`-Struct, das das alte Token hält, erhält sofort eine `InvalidOperationException`, anstatt still den recycelten Zustand zu lesen.

---

## `VlkTask` vs. `VlkTask<T>`

| Funktion | `VlkTask` | `VlkTask<T>` |
|---------|-----------|-------------|
| Rückgabewert | Keiner (void-Äquivalent) | `T` |
| Inline-Ergebnisspeicherung | Kein `result`-Feld | `result`-Feld (Typ `T`) |
| Awaiter `GetResult` | `void` | Gibt `T` zurück |
| Builder-Typ | `AsyncVlkTaskMethodBuilder` | `AsyncVlkTaskMethodBuilder<TResult>` |
| Synchron-abgeschlossener Wert | `VlkTask.CompletedTask` | `VlkTask.FromResult(value)` |
| In nicht-generisch umwandeln | Nicht anwendbar | `.AsNonGeneric()` |

Verwenden Sie `VlkTask`, wenn eine asynchrone Methode keinen sinnvollen Rückgabewert hat, und `VlkTask<T>`, wenn sie ein Ergebnis liefert. Sie können ein `VlkTask<T>` jederzeit über `AsNonGeneric()` in ein `VlkTask` umwandeln, wenn Sie typisierte und untypisierte Tasks in Kombinatoren wie `WhenAll` mischen müssen.

---

## Wie der asynchrone Methoden-Builder funktioniert

Der C#-Compiler sucht nach dem in `[AsyncMethodBuilder(...)]` benannten Typ auf dem Rückgabetyp. Für `VlkTask` ist das `AsyncVlkTaskMethodBuilder`. Für `VlkTask<T>` ist es `AsyncVlkTaskMethodBuilder<TResult>`.

Der Builder selbst ist ein Struct, um eine Heap-Allokation allein für das Builder-Objekt zu vermeiden. Er hat zwei Felder (drei für die generische Variante):

```csharp
public struct AsyncVlkTaskMethodBuilder
{
    IStateMachineRunnerPromise runner;   // null bis zur ersten Suspension
    Exception syncException;            // nur bei sync-gefaultetem Pfad gesetzt
}

public struct AsyncVlkTaskMethodBuilder<TResult>
{
    IStateMachineRunnerPromise<TResult> runner;
    Exception syncException;
    TResult result;                     // nur bei sync-Erfolgs-Pfad gesetzt
}
```

### Builder-Lebenszyklus

Der Compiler ruft diese Methoden der Reihe nach auf:

**1. `Create()`** — gibt einen Standard-Builder zurück (alle Felder null/default). Keine Allokation.

**2. `Start(ref stateMachine)`** — ruft `stateMachine.MoveNext()` synchron auf. Wenn die Methode abschließt, ohne auf ein unvollständiges `await` zu treffen, wird `SetResult`/`SetException` aufgerufen und `runner` bleibt null.

**3. `AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine)`** — wird aufgerufen, wenn die Methode auf ein unvollständiges `await` trifft. Wenn `runner` null ist (erste Suspension), leiht oder erstellt es einen `AsyncVlkTaskRunner` und kopiert die Zustandsmaschine hinein. Dann ruft es `awaiter.UnsafeOnCompleted(runner.MoveNextAction)` auf, um die Zustandsmaschinen-Fortsetzung zu registrieren.

**4. `SetResult()` / `SetException(exception)`** — signalisiert den Abschluss in den `VlkTaskCompletionCore` des Runners, der jeden registrierten Awaiter aufweckt.

**5. `Task`-Eigenschaft** — vom Aufrufer geprüft, um den `VlkTask`-Wert zu erhalten. Auf dem sync-Erfolgs-Pfad (`runner == null && syncException == null`) gibt er `default` zurück (oder `new VlkTask<T>(result)` für die generische Variante) — null Allokation. Auf dem async-Pfad umhüllt er den Runner als Quelle.

Die kritische Optimierung ist, dass `runner` lazy alloziert wird. Wenn eine Methode synchron abschließt (der häufige Fall bei Cache-Treffern, Guards, frühen Returns), wird nie ein gepooltes Objekt ausgeliehen.

---

## `VlkTaskStatus`-Zustände

Status wird durch eine `byte`-große Enum dargestellt, die in `VlkTask` eingebettet ist:

```csharp
public enum Status : byte
{
    Pending   = 0,   // noch nicht abgeschlossen
    Succeeded = 1,   // normal abgeschlossen
    Faulted   = 2,   // mit einer unbehandelten Ausnahme abgeschlossen
    Canceled  = 3    // via OperationCanceledException abgeschlossen
}
```

Sie können den Status direkt prüfen:

```csharp
VlkTask task = SomeOperation();
VlkTask.Status status = task.GetStatus();

switch (status)
{
    case VlkTask.Status.Pending:
        // Läuft noch — GetResult kann nicht aufgerufen werden
        break;
    case VlkTask.Status.Succeeded:
        // Normal abgeschlossen
        break;
    case VlkTask.Status.Faulted:
        // Mit Ausnahme abgeschlossen — GetResult wird erneut werfen
        break;
    case VlkTask.Status.Canceled:
        // Mit OperationCanceledException abgeschlossen
        break;
}
```

Für den synchron-abgeschlossenen schnellen Pfad (wo `source == null`) gibt `GetStatus()` `Succeeded` ohne Interface-Aufruf zurück:

```csharp
public Status GetStatus()
{
    if (source == null) return Status.Succeeded;
    return source.GetStatus(token);
}
```

Die `IsCompleted`-Eigenschaft folgt demselben Muster und gibt `true` für jeden Nicht-`Pending`-Zustand zurück.

---

## IL2CPP-Implikationen

IL2CPP kompiliert C# zu C++, bevor es zu nativem Code gebaut wird. Generische Werttypen — einschließlich Structs — werden im generierten Code vollständig spezialisiert, was wichtige Konsequenzen für diese Bibliothek hat.

**Zustandsmaschinen-Spezialisierung.** Der Compiler generiert eine eindeutige Zustandsmaschinen-Struct pro asynchroner Methode. `AsyncVlkTaskRunner<TStateMachine>` ist daher auch pro asynchroner Methode eindeutig, und `VlkTaskPool<AsyncVlkTaskRunner<TStateMachine>>` ist ein separater Pool pro Methode. Dies ist tatsächlich vorteilhaft: Der Pool wird nie über inkompatible Typen geteilt und eliminiert jedes Risiko von Typverwechslungen.

**Kein Boxing der Zustandsmaschine.** Die Zustandsmaschine wird als Wert innerhalb des Runner-Objekts gespeichert, nicht geboxt. IL2CPP handhabt dies korrekt, weil der Runner eine `sealed class` mit einem konkreten `TStateMachine`-Feld ist.

**Stripping-Schutz.** Das `[AsyncMethodBuilder]`-Attribut hält die Builder-Typen am Leben. Wenn Sie jedoch `VlkTask.ISource` über eine Interface-Referenz in IL2CPP mit aggressivem Stripping verwenden, fügen Sie einen `link.xml`-Eintrag hinzu, der die `UnaPartidaMas.Valkarn.Tasks`-Assembly erhält:

```xml
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```

**`ICriticalNotifyCompletion`.** Die Awaiter-Structs implementieren `ICriticalNotifyCompletion`, was dem Compiler sagt, `UnsafeOnCompleted` statt `OnCompleted` aufzurufen. Die „unsafe"-Variante überspringt absichtlich die `ExecutionContext`-Erfassung. Dies ist für Unity korrekt — es gibt keinen `SynchronizationContext` in Unitys Standardkonfiguration, und dessen Erfassung würde Overhead für keinen Nutzen hinzufügen. Unter IL2CPP vermeidet dies auch den Overhead des `ExecutionContext.Run`-Pfads, den Standard-`Task` immer zahlt.

---

## Praktische Beispiele

### Frühes Zurückgeben ohne Allokation

```csharp
// async VlkTask<int>, die synchron auf dem heißen Pfad abschließt
async VlkTask<int> GetCachedValue(string key)
{
    if (_cache.TryGetValue(key, out var value))
        return value;            // Compiler ruft SetResult(value) auf; source bleibt null

    var result = await FetchFromDatabaseAsync(key);
    _cache[key] = result;
    return result;
}
```

Wenn der Wert gecacht ist, suspendiert die Methode nie. Das zurückgegebene `VlkTask<int>` hat `source == null` und trägt das Ergebnis inline. Auf diesem Pfad findet keine Heap-Allokation statt.

### IsCompleted vor dem Abwarten prüfen

```csharp
VlkTask<Texture2D> loadTask = LoadTextureAsync("sprites/hero.png");

if (loadTask.IsCompleted)
{
    // Bereits fertig — GetAwaiter().GetResult() liest inline-Ergebnis ohne ISource-Aufruf
    Texture2D tex = loadTask.GetAwaiter().GetResult();
    ApplyTexture(tex);
}
else
{
    // Wirklich asynchron — Fortsetzung registrieren
    ApplyTextureAsync(loadTask).Forget();
}
```

### Unbehandelte Ausnahmen beobachten

Gefaultete Tasks, die nie abgewartet werden (Fire-and-Forget-Muster), melden ihre Ausnahmen über das `VlkTask.UnobservedException`-Event. Dies wird deterministisch zum Pool-Rückgabezeitpunkt für gepoolte Quellen ausgelöst, oder vom Finalizer für `Promise`-gestützte Tasks.

```csharp
VlkTask.UnobservedException += ex =>
{
    Debug.LogError($"[VlkTask] Unbeobachtet: {ex}");
};
```

Das Event ist thread-sicher; Handler können über einen sperrfreien Compare-Exchange-Loop von jedem Thread aus hinzugefügt oder entfernt werden.
