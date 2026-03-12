---
sidebar_position: 4
title: Einstellungen & Ergebnis
---

# VlkTaskSettings

`VlkTaskSettings` ist ein Unity-`ScriptableObject`, das das Laufzeitverhalten von Valkarn Tasks steuert — primär den Objekt-Pool, der asynchrone Zustandsmaschinen und Promise-Objekte recycelt, um Garbage während des Spielbetriebs zu vermeiden.

---

## Das Einstellungs-Asset erstellen

1. Klicken Sie im Project-Fenster mit der rechten Maustaste auf einen `Resources`-Ordner (erstellen Sie einen, wenn Sie keinen haben).
2. Wählen Sie **Assets > Create > Valkarn Tasks > Task Settings**.
3. Benennen Sie die Datei `VlkTaskSettings` und platzieren Sie sie im `Resources`-Ordner.

Die Datei muss exakt `VlkTaskSettings` heißen und in einem Ordner namens `Resources` irgendwo in Ihrem Projekt liegen. Das Asset wird zur Laufzeit mit `Resources.Load<VlkTaskSettings>("VlkTaskSettings")` geladen.

Wenn kein Asset gefunden wird, fallen alle Einstellungen auf ihre eingebauten Standardwerte zurück. Die Bibliothek funktioniert korrekt ohne das Asset — das Erstellen ist nur dann notwendig, wenn Sie die Standardwerte ändern möchten.

---

## Einstellungen zur Laufzeit abrufen

In Unity-Builds werden Einstellungen aus dem Asset über ein gecachtes Singleton gelesen:

```csharp
VlkTaskSettings settings = VlkTaskSettings.Instance;
```

Die häufig benötigten Pool-Parameter sind auch direkt auf `VlkTask` für Bequemlichkeit verfügbar:

```csharp
int max      = VlkTask.DefaultMaxPoolSize;   // liest aus VlkTaskSettings.Instance
int min      = VlkTask.MinPoolSize;
int interval = VlkTask.TrimCheckInterval;
```

In Nicht-Unity-Builds (Tests, standalone .NET) ist `VlkTaskSettings` eine statische Klasse mit veränderbaren Eigenschaften statt eines `ScriptableObject`. Dieselben Eigenschaftsnamen gelten und können direkt geschrieben werden:

```csharp
// Nur Nicht-Unity / Test-Builds
VlkTask.DefaultMaxPoolSize = 512;
VlkTask.TrimCheckInterval  = 600;
VlkTask.MinPoolSize        = 16;
```

---

## Konfigurierbare Eigenschaften

### Pool-Konfiguration

#### DefaultMaxPoolSize

| | |
|-|-|
| Typ | `int` |
| Standard | `256` |
| Gültiger Bereich | `8` – `1024` |
| Inspector-Tooltip | "Maximale Elemente pro Pool-Typ. Überschüssige Elemente werden getrimmt." |

Die maximale Anzahl von Objekten, die in jedem Pool pro Typ behalten werden. Jede distinct generische Instantiierung (z. B. `PooledPromise<int>`, `PooledPromise<string>`) hat ihren eigenen Pool, der auf diesen Wert begrenzt ist.

Wenn eine Task abschließt und ihr internes Objekt in den Pool zurückgegeben wird, wird das zurückgegebene Objekt verworfen (für GC geeignet), wenn der Pool bereits `DefaultMaxPoolSize` Elemente hält. Dies verhindert unbegrenztes Speicherwachstum nach einem Burst asynchroner Aktivität.

Erhöhen Sie diesen Wert, wenn Profiling häufige GC-Allokationen bei anhaltend hohem asynchronem Durchsatz zeigt. Verringern Sie ihn, wenn Speicherdruck ein Problem ist und Tasks nicht häufig wiederverwendet werden.

#### MinPoolSize

| | |
|-|-|
| Typ | `int` |
| Standard | `8` |
| Gültiger Bereich | `1` – `64` |
| Inspector-Tooltip | "Minimale Pool-Größe — niemals darunter schrumpfen." |

Der Pool-Trim-Durchlauf wird keinen Pool unter diese Anzahl reduzieren. Dies garantiert, dass immer eine warme Pool-Baseline verfügbar ist und Allokations-Spitzen nach einer ruhigen Periode vermieden werden, in der der Trim-Durchlauf möglicherweise alles freigegeben hätte.

#### TrimCheckInterval

| | |
|-|-|
| Typ | `int` |
| Standard | `300` |
| Gültiger Bereich | `30` – `1000` |
| Inspector-Tooltip | "Frames zwischen Trim-Prüfungen. Bei 60fps entsprechen 300 ≈ 5 Sekunden." |

Wie viele Frames zwischen Pool-Trim-Durchläufen vergehen. Der Trim-Durchlauf geht alle registrierten Pools durch und gibt überschüssige Objekte frei (die über `MinPoolSize` liegen), wenn der Pool konsistent übergroß war.

Bei 60 fps entspricht der Standardwert von 300 etwa 5 Sekunden zwischen Prüfungen. Verringern Sie diesen Wert, wenn Sie aggressiveres, häufigeres Trimmen wünschen (auf Kosten häufigerer Trim-Arbeit). Erhöhen Sie ihn, wenn Trim-Durchläufe als Spitzen im Profiling erscheinen.

#### TrimHysteresisCount

| | |
|-|-|
| Typ | `int` |
| Standard | `2` |
| Gültiger Bereich | `1` – `10` |
| Inspector-Tooltip | "Anzahl aufeinanderfolgender überschwelliger Prüfungen vor dem Trimmen." |

Ein Pool wird erst getrimmt, nachdem er für diese Anzahl aufeinanderfolgender Trim-Zyklen als übergroß beobachtet wurde. Dies verhindert Thrashing — wenn das Spiel eine kurze Spitze gefolgt von einer ruhigen Periode hat, bedeutet ein Hysterese-Zähler von 2, dass der Pool einen ruhigen Zyklus überlebt, bevor er beginnt, Objekte freizugeben.

#### TrimReleaseRatio

| | |
|-|-|
| Typ | `float` |
| Standard | `0.25` |
| Gültiger Bereich | `0.1` – `1.0` |
| Inspector-Tooltip | "Brucheile des Überschusses, die pro Trim-Zyklus freigegeben werden (0.25 = 25%)." |

Wenn ein Pool getrimmt wird, wird dieser Anteil der überschüssigen Kapazität (Elemente über `MinPoolSize`) pro Zyklus statt alles auf einmal freigegeben. Ein Wert von `0.25` bedeutet, dass jeder Trim-Durchlauf 25% des Überhangs entfernt. Diese graduelle Freigabe vermeidet einen plötzlichen Abfall der Pool-Größe, der einen Allokations-Burst verursachen könnte, wenn die Last wieder zunimmt.

Setzen Sie dies auf `1.0`, wenn Sie möchten, dass alle Überschüsse bei jedem Zyklus sofort freigegeben werden.

---

### Lebenszyklus

#### EnableAutoCancel

| | |
|-|-|
| Typ | `bool` |
| Standard | `true` |
| Inspector-Tooltip | "MonoBehaviour-Tasks automatisch an destroyCancellationToken binden." |

Wenn aktiviert, werden Tasks, die von einem `MonoBehaviour` gestartet werden, automatisch mit dem `destroyCancellationToken` dieses `MonoBehaviour` verknüpft. Wenn das `MonoBehaviour` zerstört wird, während eine Task läuft, wird die Task abgebrochen, anstatt weiterhin gegen ein zerstörtes Objekt auszuführen.

Deaktivieren Sie dies nur, wenn Sie den Abbruch manuell verwalten und keine automatische Bindung wünschen.

---

### Fehlerbehandlung

#### LogUnobservedCancellations

| | |
|-|-|
| Typ | `bool` |
| Standard | `false` |
| Inspector-Tooltip | "Nicht beobachtete Abbrüche als Warnungen protokollieren." |

Standardmäßig wird eine Task, die abgebrochen, aber nie abgewartet wird (nicht beobachteter Abbruch), lautlos ignoriert. Aktivieren Sie dies, um eine Warnung zu protokollieren, wenn das passiert. Nützlich während der Entwicklung, um Fire-and-Forget-Tasks zu finden, die lautlos abbrechen.

Nicht beobachtete Fehler werden immer über das `VlkTask.UnobservedException`-Event gemeldet, unabhängig von dieser Einstellung.

#### MaxExceptionLogsPerFrame

| | |
|-|-|
| Typ | `int` |
| Standard | `10` |
| Gültiger Bereich | `1` – `100` |
| Inspector-Tooltip | "Maximale Ausnahme-Protokolleinträge pro Frame, um Spam zu verhindern." |

Begrenzt die Anzahl der nicht beobachteten Ausnahme-Protokolleinträge, die in einem einzigen Frame ausgegeben werden. Wenn viele Tasks im selben Frame fehlschlagen (z. B. nach einem Netzwerkfehler), verhindert dies, dass die Konsole mit Hunderten identischer Stack-Traces überflutet wird.

---

## Wie Pool-Trimmen zur Laufzeit funktioniert

Bei jedem Unity Player-Loop-Update inkrementiert `PlayerLoopHelper` einen Frame-Zähler. Wenn der Zähler `TrimCheckInterval` erreicht, ruft er `PoolRegistry.TrimAll(MinPoolSize)` auf. Jeder registrierte Pool prüft, ob er für mindestens `TrimHysteresisCount` aufeinanderfolgende Prüfungen über Kapazität war. Wenn ja, gibt er `TrimReleaseRatio` seines Überschusses frei.

Pools registrieren sich automatisch bei der ersten Verwendung. Die Methode `VlkTask.GetPoolInfo()` gibt einen Snapshot aller aktuell registrierten Pools mit ihrem Typ, ihrer aktuellen Größe und maximalen Größe zurück — dies ist, was das Task-Tracker-Fenster anzeigt.

```
Frame 0 ──────────────────────────────────────────────────────────
  Player Loop läuft
  Pool A: Größe 200, max 256 — normal, kein Trimmen nötig

Frame 300 ────────────────────────────────────────────────────────
  TrimAll wird ausgelöst
  Pool A: Größe 18, max 256 — über MinPoolSize(8), aber erste Überschuss-Prüfung
  hysteresisCount für A = 1 (noch nicht beim Schwellenwert von 2)

Frame 600 ────────────────────────────────────────────────────────
  TrimAll wird ausgelöst
  Pool A: Größe 18, max 256 — über MinPoolSize(8), zweite Überschuss-Prüfung
  hysteresisCount für A = 2 — Schwellenwert erreicht!
  Überschuss = 18 - 8 = 10; 10 * 0,25 = 2 Objekte freigeben
  Pool A: Größe 16
```

---

## Task-Tracker-Fenster

Öffnen über **Window > Valkarn Tasks > Task Tracker**.

Der Task-Tracker ist ein Editor-only-Fenster, das den Live-Pool-Zustand anzeigt, während Sie im Play-Modus sind. Er wird in einem konfigurierbaren Intervall aktualisiert (Standard 0,5 Sekunden, anpassbar von 0,1 bis 5 Sekunden über den Schieberegler in der Toolbar).

### Pools-Tab

Listet jeden Pool-Typ auf, der seit dem letzten Domain-Reload aktiv war, sortiert nach aktueller Größe (größte zuerst). Jede Zeile zeigt:

| Spalte | Beschreibung |
|--------|-------------|
| Typ | Der Typname des gepoolten Objekts, mit erweiterten generischen Argumenten |
| Größe | Aktuelle Anzahl von Objekten im Pool |
| Max | Die `DefaultMaxPoolSize`-Obergrenze für diesen Pool |
| Auslastung | Ein Fortschrittsbalken, der `Größe / Max` als Prozentsatz anzeigt |

Wenn noch keine Pools verwendet wurden, lautet eine Meldung: "Keine Pools aktiv. Pools werden bei der ersten Verwendung erstellt."

### Konfig-Tab

Zeigt die drei Live-Pool-Parameter, wie aus `VlkTask.DefaultMaxPoolSize`, `VlkTask.TrimCheckInterval` und `VlkTask.MinPoolSize` gelesen. Werte werden nur im Play-Modus angezeigt — im Edit-Modus weist eine Notiz Sie an, in den Play-Modus zu wechseln, um Live-Werte zu sehen.

Der Konfig-Tab zeigt auch eine Referenz auf das `VlkTaskSettings`-Asset (falls eines in `Resources` vorhanden ist), sodass Sie durchklicken können, um es zu inspizieren oder zu ändern. Wenn kein Asset gefunden wird, weist eine Warnung Sie an, eines zu erstellen.

---

## Result&lt;T&gt;

`Result<T>` ist eine diskriminierte Union-Struct zur Darstellung des Ergebnisses einer Operation ohne zu werfen. Es ist der Rückgabetyp, der von `WhenAll`-Kombinatoren verwendet wird, um pro-Task-Ergebnisse zu melden, und steht auch als allgemeines Ergebnis-Muster zur Verfügung.

```csharp
public readonly struct Result<T>
{
    public bool IsSuccess  { get; }
    public bool IsFailure  { get; }   // true bei Fehler oder Abbruch
    public bool IsFaulted  { get; }
    public bool IsCanceled { get; }

    public VlkTask.Status Status { get; }
    public T Value    { get; }        // wirft, wenn nicht erfolgreich
    public Exception Error { get; }   // null, wenn nicht gefaultet

    public static Result<T> Success(T value);
    public static Result<T> Failure(string error);    // umhüllt in InvalidOperationException
    public static Result<T> Faulted(Exception error);
    public static Result<T> Canceled(OperationCanceledException oce = null);

    public static implicit operator bool(Result<T> r);  // true, wenn erfolgreich
}
```

Ein nicht-generisches `Result` existiert für void-Tasks mit derselben Form, aber ohne `Value`.

### AsResult-Erweiterungsmethoden

Konvertieren Sie jedes `VlkTask` oder `VlkTask<T>` in ein `Result` ohne `try/catch` in Ihrem eigenen Code:

```csharp
public static VlkTask<Result<T>> AsResult<T>(this VlkTask<T> task);
public static VlkTask<Result>    AsResult(this VlkTask task);
```

Wenn die zugrundeliegende Task bereits synchron abgeschlossen ist (der null-Allokations-Schnellpfad), gibt `AsResult` sofort zurück ohne Erstellen einer asynchronen Zustandsmaschine. Andernfalls umhüllt es es in eine asynchrone Methode, die `OperationCanceledException` und alle anderen Ausnahmen abfängt und sie in die entsprechende `Result`-Variante übersetzt.

### Wann Result&lt;T&gt; verwenden

Verwenden Sie `Result<T>` statt Ausnahmen abzufangen, wenn:

- Sie mehrere Tasks parallel aufrufen und pro-Task-Ergebnisse ohne Kurzschluss des gesamten Batches möchten.
- Sie fehlbare Operationen typensicher ausdrücken möchten ohne Ausnahme-Kontrollfluss.
- Sie von einer Methode zurückgeben, die der Aufrufer möglicherweise nicht in `try/catch` einwickeln möchte.

```csharp
// Mehrere Tasks auslösen, alle Ergebnisse unabhängig von individuellen Fehlern erhalten
Result<int>[] results = await VlkTask.WhenAll(
    FetchScoreAsync(playerA).AsResult(),
    FetchScoreAsync(playerB).AsResult(),
    FetchScoreAsync(playerC).AsResult()
);

foreach (var r in results)
{
    if (r.IsSuccess)
        Debug.Log($"Punktzahl: {r.Value}");
    else if (r.IsFaulted)
        Debug.LogError($"Fehler: {r.Error.Message}");
    else
        Debug.Log("Abgebrochen");
}
```

Das Prüfen von `IsSuccess` oder der implizite `bool`-Operator sind die bevorzugten Wege, um auf einem Ergebnis zu verzweigen:

```csharp
var result = await SomeOperationAsync().AsResult();

if (result)
{
    Use(result.Value);
}
```

Der Zugriff auf `result.Value`, wenn `IsSuccess` `false` ist, wirft eine `InvalidOperationException`.

### Factory-Methoden

| Methode | Gesetzter Status | Gesetzter Fehler |
|--------|-----------|-----------|
| `Result<T>.Success(value)` | `Succeeded` | keiner |
| `Result<T>.Failure(message)` | `Faulted` | `new InvalidOperationException(message)` |
| `Result<T>.Faulted(exception)` | `Faulted` | die bereitgestellte Ausnahme |
| `Result<T>.Canceled(oce?)` | `Canceled` | die bereitgestellte `OperationCanceledException`, oder `null` |

Die `Succeeded`-Eigenschaft existiert sowohl auf `Result` als auch auf `Result<T>`, ist aber als `[Obsolete]` markiert — verwenden Sie stattdessen `IsSuccess` für Konsistenz mit `IsFailure`, `IsFaulted` und `IsCanceled`.
