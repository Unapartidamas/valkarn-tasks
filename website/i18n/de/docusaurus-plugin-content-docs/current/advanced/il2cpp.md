---
sidebar_position: 2
title: IL2CPP-Kompatibilität
---

# IL2CPP-Kompatibilität

Valkarn Tasks ist von Grund auf darauf ausgelegt, unter IL2CPP korrekt zu funktionieren. Diese Seite beschreibt alle getroffenen Maßnahmen und was Sie tun — oder nicht tun — müssen, um sicher auf IL2CPP-Plattformen wie iOS, WebGL und Konsolen zu veröffentlichen.

---

## Warum IL2CPP besondere Sorgfalt erfordert

IL2CPP konvertiert C# IL in C++-Quellcode und kompiliert ihn dann mit einem nativen Compiler. Zwei Funktionen der Pipeline sind für eine Async-Bibliothek relevant:

1. **Code-Stripping.** Unitys Managed-Code-Stripper (mit dem IL2CPP-Linker) entfernt Typen, Methoden und Felder, die nicht durch einen statisch analysierbaren Call-Graph referenziert werden. Typen, auf die nur durch Interface-Dispatch, generisches Sharing oder Reflection zugegriffen wird — was gepoolte Promise-Klassen und `ISource`-Implementierungen einschließt — können still entfernt werden.

2. **Generisches Sharing.** IL2CPP generiert kein separates natives Binary für jede generische Instanziierung. Stattdessen teilt es Code über Referenztypen und verwendet spezifische Instanziierungen für Werttypen. Dies kann Fehler in der Entwicklung (Mono) verbergen, die nur in IL2CPP-Builds auftreten.

---

## Die link.xml-Datei

Der primäre Schutz gegen Stripping ist die `link.xml`-Datei unter:

```
Runtime/link.xml
```

Ihr Inhalt:

```xml
<linker>
  <!-- Alle Typen in der Valkarn.Tasks-Laufzeit-Assembly erhalten.
       IL2CPP-Code-Stripping kann interne Typen entfernen, auf die nur via
       Interface-Dispatch, generisches Sharing oder Reflection zugegriffen wird
       (z. B. Promise-Klassen, gepoolte Runner, ISource-Implementierungen). -->
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks.Burst" preserve="all"/>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks.ECS" preserve="all"/>
</linker>
```

`preserve="all"` weist den Linker an, jeden Typ, jede Methode, jedes Feld und jeden Konstruktor in diesen Assemblies beizubehalten, unabhängig davon, was die statische Analyse findet. Dies ist die sicherste Einstellung für eine Bibliothek, deren interne Typen durch generische Parameter aufgerufen werden, die der Stripper nicht verfolgen kann.

Unity erkennt und wendet `link.xml`-Dateien automatisch an, wenn sie in einem Paketordner abgelegt werden, der in das Projekt importiert wird. Es ist kein manueller Schritt erforderlich.

Wenn Sie den Quellcode **forken** oder **einbetten**, anstatt das Paket zu verwenden, kopieren Sie `link.xml` in einen `Resources`-benachbarten Ordner oder legen Sie es irgendwo ab, wo Unity es gemäß der [Unity Managed Code Stripping-Dokumentation](https://docs.unity3d.com/Manual/ManagedCodeStripping.html) findet.

---

## Interne Typen, die ohne link.xml entfernt würden

Die folgenden Kategorien interner Typen sind das primäre Stripping-Risiko:

### Gepoolte Promise-Klassen (ISource-Implementierungen)

Jeder Kombinator und jeder Delay-Typ erstellt eine gepoolte Promise-Klasse, die `ValkarnTask.ISource` oder `ValkarnTask.ISource<T>` implementiert:

- `AsyncValkarnTaskRunner<TStateMachine>` — der gepoolte Zustandsmaschinen-Runner, einer pro async-Methode (spezialisiert auf `TStateMachine`)
- `WhenAllPromise<T1, T2>`, `WhenAllArrayPromise<T>`, `WhenAllVoidPromise2`, `WhenAllVoidPromise3`, `WhenAllVoidArrayPromise`
- `DeltaTimeDelayPromise`, `UnscaledDeltaTimeDelayPromise`, `RealtimeDelayPromise`
- `CanceledSource`, `CanceledSource<T>`, `ExceptionSource`, `ExceptionSource<T>`, `NeverSource`
- Channel-Interna: `BoundedChannel<T>`, `UnboundedChannel<T>` und ihre Reader/Writer-Typen

Diese Typen werden über generische Factory-Methoden instanziiert (`ValkarnTaskPool<T>.GetOrCreate`). Der statische Analyse-Call-Graph beginnt bei einem generischen Methodenaufruf und kann nicht zuverlässig alle konkreten `T`-Instanziierungen verfolgen, sodass ohne `link.xml` jeder dieser Typen entfernt werden kann.

### ValkarnTaskPool\<T\>

```csharp
internal sealed class ValkarnTaskPool<T> : IPoolInfo where T : class, IPoolNode<T>
```

Der Pool ist generisch über seinen Elementtyp. Jede Promise-Klasse hat ihr eigenes statisches Pool-Feld. Wenn ein bestimmter Promise-Typ in einer Szene unbenutzt ist, können der Pool und die Promise-Klasse zusammen entfernt werden.

### ValkarnTaskCompletionCore\<T\>

Der interne Abschluss-Kern ist ein Werttyp, der in jeder Promise verwendet wird. Er hält den Continuation-Callback, das Token und den Abschlusszustand. Er wird niemals namentlich von außerhalb der Bibliothek referenziert.

---

## Generische Typeinschränkungen und IL2CPP

Der benutzerdefinierte Async-Method-Builder ist generisch über den Zustandsmaschinentyp:

```csharp
public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
    ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : ICriticalNotifyCompletion
    where TStateMachine : IAsyncStateMachine
```

Und der Runner ist generisch über die Zustandsmaschine:

```csharp
internal sealed class AsyncValkarnTaskRunner<TStateMachine>
    : IStateMachineRunnerPromise, IPoolNode<AsyncValkarnTaskRunner<TStateMachine>>
    where TStateMachine : IAsyncStateMachine
```

Unter IL2CPP wird für jede distincte `TStateMachine` ein neuer konkreter Typ generiert (da Zustandsmaschinen Structs sind, die volle generische Spezialisierung erfordern). Das bedeutet:

- Jede `async ValkarnTask`-Methode in Ihrem Projekt erzeugt einen separaten nativen `AsyncValkarnTaskRunner<TYourStateMachine>`-Typ.
- Wenn der Stripper `AsyncValkarnTaskRunner<T>` entfernt, bevor alle Instanziierungen gesehen wurden, können einige async-Methoden zur Laufzeit abstürzen.
- `preserve="all"` in `link.xml` verhindert dies.

---

## Managed Code Stripping Level

Unitys Stripping-Level wird in **Player Settings → Other Settings → Managed Stripping Level** festgelegt.

| Level       | Status mit Valkarn Tasks                                                           |
|-------------|------------------------------------------------------------------------------------|
| Disabled    | Sicher. Kein Stripping findet statt.                                               |
| Low         | Sicher. Entfernt nur eindeutig unbenutzte Assemblies.                              |
| Medium      | Sicher **mit `link.xml`**. Die enthaltene `link.xml` erhält alle Laufzeittypen.  |
| High        | Sicher **mit `link.xml`**. Gleiche Abdeckung; `link.xml` ist die Schutzschicht.  |

Alle Stripping-Level bis einschließlich **High** sind sicher, solange die `link.xml`-Datei vorhanden und angewendet wird. Entfernen oder modifizieren Sie `link.xml` nicht, ohne zu verstehen, welche internen Typen Sie dem Stripper aussetzen.

---

## Verwendung des [Preserve]-Attributs

Eine Suche im Runtime-Quellcode findet keine auf einzelne Member angewendeten `[UnityEngine.Scripting.Preserve]`-Attribute. Der gewählte Ansatz ist die Assembly-level-Bewahrung über `link.xml` anstelle von Per-Member-Attributen. Dies ist beabsichtigt:

- Per-Member-`[Preserve]` erfordert das Annotieren jeder gepoolten Klasse einzeln, einschließlich aller zukünftigen Ergänzungen.
- Assembly-level `preserve="all"` in `link.xml` ist einfacher, fehleranfälliger und garantiert die Abdeckung von in zukünftigen Versionen hinzugefügten Typen.

Wenn Sie Valkarn Tasks in eine größere `link.xml`-Datei integrieren müssen, anstatt die paketgebündelte zu verwenden, lautet die entsprechende Direktive:

```xml
<assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
```

---

## IL2CPP-First Pool-Design

Der Objekt-Pool (`ValkarnTaskPool<T>`) enthält einen expliziten Hinweis in seinem Dokumentationskommentar:

> IL2CPP-first: main thread operations use zero atomics.

Der Pool verwendet zwei Zugriffspfade:

- **Schnellpfad (Haupt-Thread):** Ein einzelnes `fastItem`-Feld wird mit einfachem Feldzugriff gelesen/geschrieben — keine `Volatile`- oder `Interlocked`-Operationen. Dies vermeidet den Overhead atomarer Operationen auf dem Haupt-Thread, wo IL2CPP sie nicht immer optimieren kann.
- **Überlaufpfad (beliebiger Thread):** Ein Treiber-Lock-Free-Stack mit `Interlocked.CompareExchange` für Korrektheit unter gleichzeitigem Zugriff von Hintergrund-Threads (z. B. `ValkarnTask.RunOnThreadPool`).

Die Haupt-Thread-Identität wird in einem `volatile int`-Feld in `ValkarnTaskPoolShared.MainThreadId` gespeichert, das beim Start über `RuntimeInitializeOnLoadMethod` einmalig veröffentlicht wird. IL2CPP behandelt `volatile`-Felder korrekt.

---

## Konsolen-Plattform-Überlegungen

Konsolen-Plattformen (PlayStation, Xbox, Nintendo Switch) verwenden ausschließlich IL2CPP. Folgendes gilt:

- Die `link.xml`-Abdeckung ist dieselbe wie für andere IL2CPP-Ziele. Keine konsolenspezifischen zusätzlichen Bewahrungen sind erforderlich.
- `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` wird auf allen Plattformen korrekt ausgelöst, die Unity für die Konsolenzertifizierung unterstützt.
- `Interlocked`- und `Volatile`-Operationen werden auf allen Konsolen-IL2CPP-Zielen unterstützt. Der Treiber-Stack des Pools ist sicher.
- Generisches Sharing für Struct-`TStateMachine`-Instanziierungen gilt auf Konsolen. Jede `async ValkarnTask`-Methode generiert ihren eigenen nativen Typ — das ist erwartetes Verhalten und wird korrekt behandelt.
- Wenn eine Konsolen-Plattform zusätzliche AOT-Anforderungen durchsetzt, bestätigen Sie, dass Ihre `link.xml` korrekt erkannt wird, indem Sie das Build-Protokoll auf „Stripping assembly: UnaPartidaMas.Valkarn.Tasks" prüfen. Wenn die Assembly entfernt wird, wurde die `link.xml` nicht gefunden.

---

## Überprüfen, dass nichts entfernt wurde

Nach dem Erstellen für ein IL2CPP-Ziel:

1. **Prüfen Sie das Build-Protokoll.** Unity gibt Stripping-Entscheidungen aus. Suchen Sie nach `UnaPartidaMas.Valkarn.Tasks`. Wenn Sie `Stripping class`-Meldungen für interne Valkarn-Typen sehen, wurde die `link.xml` nicht angewendet.

2. **Führen Sie einen Smoke-Test durch.** Ein minimaler Test, der `ValkarnTask.Delay`, `WhenAll` und eine benutzerdefinierte `ValkarnTaskCompletionSource` ausübt, instanziiert die am häufigsten entfernten Typen. Wenn diese drei Szenarien den Build überleben, ist der Kern intakt.

3. **„Strip Engine Code" selektiv aktivieren.** Wenn Sie High-Stripping verwenden müssen und die gebündelte `link.xml` nicht verwenden können, aktivieren Sie Stripping schrittweise und führen Sie Tests nach jeder Erhöhung des Stripping-Levels durch.

4. **IL2CPP-Build mit aktiviertem Development Build.** Development Builds enthalten zusätzliche Diagnosen. Wenn ein Typ fehlt, meldet die Laufzeit `TypeInitializationException` oder eine Null-Referenz an der Aufrufstelle des fehlenden Typs. Gleichen Sie den Stack-Trace mit den im Abschnitt „Interne Typen" aufgelisteten Typen ab.

---

## Zusammenfassungs-Checkliste

- `Runtime/link.xml` ist im Paket vorhanden — stellen Sie sicher, dass sie nicht versehentlich gelöscht wurde.
- Managed Stripping Level kann auf jedes Level gesetzt werden; High wird unterstützt.
- Es müssen keine `[Preserve]`-Attribute zum Anwendungscode hinzugefügt werden.
- Es sind keine AOT-Hint-Attribute oder Code-Registrierungsaufrufe erforderlich.
- Konsolen-Builds verwenden dieselbe `link.xml`; keine plattformspezifischen Ergänzungen sind erforderlich.
- Wenn Sie den Quellcode forken, nehmen Sie `link.xml` mit.
