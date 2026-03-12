---
sidebar_position: 2
title: Job & Awaitable-Bridges
---

# Job & Awaitable-Bridges

Valkarn Tasks bietet eine Reihe von Bridge-Typen, die Unitys Job-System und die `Awaitable`-API mit der `VlkTask`-Pipeline verbinden. Jede Bridge ist eine dünne, allokationsminimierungsschicht; es gibt keine versteckte Magie.

Alle hier beschriebenen Typen befinden sich im Namespace `UnaPartidaMas.Valkarn.Tasks.Bridge` und sind durch `#if UNITY_5_3_OR_NEWER` geschützt (oder `#if UNITY_2023_1_OR_NEWER` für Awaitable-Unterstützung).

---

## JobHandleExtensions — einen einzelnen JobHandle abwarten

Die einfachste Bridge. Rufen Sie `.ToVlkTask()` auf einem beliebigen `JobHandle` auf, um ein `VlkTask` zurückzuerhalten, das abschließt, wenn der Job fertig ist.

```csharp
public static VlkTask ToVlkTask(
    this JobHandle handle,
    PlayerLoopTiming timing          = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

### Wie es funktioniert

1. **Schnellpfad.** Wenn `handle.IsCompleted` bereits wahr ist, wird `handle.Complete()` sofort aufgerufen und `VlkTask.CompletedTask` wird zurückgegeben — null Allokation, keine PlayerLoop-Registrierung.
2. **Normaler Pfad.** Ein gepooltes `JobHandlePromise` wird ausgeliehen, auf dem PlayerLoop zum angegebenen `timing` registriert und in einem `VlkTask` eingewickelt zurückgegeben. Jeden Frame ruft `MoveNext()` `JobHandle.ScheduleBatchedJobs()` auf (um die Job-Warteschlange im Edit-Modus und Batch-Modus zu leeren) und prüft dann `handle.IsCompleted`. Wenn der Handle fertig ist, schließt das Promise die Task ab und kehrt in den Pool zurück.
3. **Abbruch.** Wenn das `CancellationToken` auslöst, wird der Handle zwangsweise abgeschlossen (`handle.Complete()` wird immer aufgerufen, um Job-System-Lecks zu verhindern) und die Task geht in den abgebrochenen Zustand über.

### Grundlegende Verwendung

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Jobs;

// Einen Job planen und sofort abwarten.
var handle = myJob.Schedule();
await handle.ToVlkTask();

// Mit einem nicht-Standard-Timing und Abbruch.
await handle.ToVlkTask(PlayerLoopTiming.PreUpdate, destroyCancellationToken);
```

---

## JobHandleWhenAll — mehrere JobHandles parallel abwarten

Wenn Sie mehrere unabhängige Jobs planen und erst nach deren Abschluss fortsetzen müssen, verwenden Sie `JobHandleExtensions.WhenAll`.

```csharp
// Einfachste Überladung: wartet auf alle Handles beim Update-Timing.
public static VlkTask WhenAll(params JobHandle[] handles)

// Vollständige Überladung: konfigurierbares Timing und Abbruch.
public static VlkTask WhenAll(
    JobHandle[]       handles,
    PlayerLoopTiming  timing,
    CancellationToken cancellationToken = default)

// Erweiterungsmethoden-Alias für die vollständige Überladung.
public static VlkTask ToVlkTask(
    this JobHandle[]  handles,
    PlayerLoopTiming  timing              = PlayerLoopTiming.Update,
    CancellationToken cancellationToken   = default)
```

### Wie es funktioniert

- **Schnellpfad.** Wenn jeder Handle im Array bereits abgeschlossen ist, werden alle Handles abgeschlossen und `VlkTask.CompletedTask` wird sofort zurückgegeben.
- **Leeres Array.** Gibt `VlkTask.CompletedTask` zurück.
- **Normaler Pfad.** Ein gepooltes `JobHandleArrayPromise` wird erstellt. Intern leiht es ein `JobHandle[]` aus `ArrayPool<JobHandle>.Shared` (vermeidet per-Aufruf-Heap-Allokation), kopiert die Eingabe-Handles hinein und registriert sich auf dem PlayerLoop. Jeden Frame iteriert es nur die noch ausstehenden Handles mit einer kompakten Swap-and-Shrink-Schleife und ruft `JobHandle.ScheduleBatchedJobs()` auf, um Worker am Laufen zu halten.
- **Abbruch.** Alle verbleibenden Handles werden zwangsweise abgeschlossen und die Task wird abgebrochen.

### Verwendung

```csharp
var handleA = jobA.Schedule();
var handleB = jobB.Schedule();
var handleC = jobC.Schedule();

// Auf alle drei warten.
await JobHandleExtensions.WhenAll(handleA, handleB, handleC);

// Oder die Erweiterungsmethode auf einem Array verwenden.
JobHandle[] handles = { handleA, handleB, handleC };
await handles.ToVlkTask();
```

---

## TempNativeArrayScope — NativeArray-Lebensdauer über await-Punkte hinweg

### Das Problem

`NativeArray<T>`, der mit `Allocator.TempJob` alloziert wird, hat eine kurze Lebensdauer. Wenn Sie einen allozieren, einen Job planen, den Job-Handle `await`en und dann vergessen, das Array zu entsorgen, meldet Unitys Sicherheitssystem ein Speicherleck. Ein einfaches `try`/`finally` funktioniert, kann aber in einer langen asynchronen Methode leicht falsch eingesetzt werden.

`TempNativeArrayScope<T>` ist ein Struct, das ein `NativeArray<T>` umhüllt und es entsorgt, wenn der Scope endet, unter Verwendung der `using`-Anweisung — das RAII-Muster auf nativen Speicher angewendet.

### API

```csharp
public struct TempNativeArrayScope<T> : IDisposable where T : struct
{
    // Greift auf das umhüllte Array zu. Wirft ObjectDisposedException, wenn bereits entsorgt.
    public NativeArray<T> Array { get; }

    // Wahr, wenn der Scope nicht entsorgt und das Array erstellt ist.
    public bool IsCreated { get; }

    // Alloziert ein neues NativeArray<T> mit Allocator.TempJob und übernimmt Eigentumsrecht.
    public static TempNativeArrayScope<T> Create(int length);

    // Übernimmt Eigentumsrecht eines bereits allozierten NativeArray<T>.
    public static TempNativeArrayScope<T> Wrap(NativeArray<T> existing);

    // Entsorgt das Array. Idempotent: sicher mehrfach aufzurufen.
    public void Dispose();
}

// Nicht-generischer Helfer (Typrückschluss-Bequemlichkeit).
public static class TempNativeArrayScope
{
    public static TempNativeArrayScope<T> Create<T>(int length) where T : struct;
    public static TempNativeArrayScope<T> Wrap<T>(NativeArray<T> existing) where T : struct;
}
```

`Dispose` verwendet ein einfaches `int`-Flag statt `Interlocked`, weil der Scope für Single-Threaded-Verwendung auf dem Haupt-Thread via `using var` ausgelegt ist.

### Verwendung

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Collections;
using Unity.Jobs;

async VlkTask ProcessDataAsync(int count, CancellationToken ct)
{
    // Die using-Anweisung garantiert, dass Dispose() beim Verlassen des Scopes aufgerufen wird,
    // sei es durch normalen Abschluss, Ausnahme oder Abbruch.
    using var inputScope  = TempNativeArrayScope.Create<float>(count);
    using var outputScope = TempNativeArrayScope.Create<float>(count);

    NativeArray<float> input  = inputScope.Array;
    NativeArray<float> output = outputScope.Array;

    // Eingabe befüllen, Job planen.
    var job = new MyProcessingJob { Input = input, Output = output };
    var handle = job.Schedule(count, 64);

    // Abwarten ohne den Haupt-Thread zu blockieren.
    // Die NativeArrays bleiben gültig — der Job läuft noch.
    await handle.ToVlkTask(cancellationToken: ct);

    // Der Job ist fertig. Ergebnisse hier lesen.
    float total = 0f;
    for (int i = 0; i < count; i++)
        total += output[i];

    UnityEngine.Debug.Log($"Summe: {total}");

    // inputScope.Dispose() und outputScope.Dispose() werden automatisch hier ausgeführt.
}
```

Sie können auch ein bereits alloziertes Array umhüllen:

```csharp
var existing = new NativeArray<int>(1024, Allocator.TempJob);
using var scope = TempNativeArrayScope.Wrap(existing);
// scope besitzt existing und wird es entsorgen.
```

### Häufige Fehlerquelle: NativeArray-Lebensdauer ohne Scope

Dieses Muster ist fehlerhaft und wird einen Sicherheitssystem-Fehler verursachen:

```csharp
// FALSCH: Array kann den Job überleben oder geleakt werden, wenn eine Ausnahme auftritt.
var array = new NativeArray<float>(1024, Allocator.TempJob);
var handle = job.Schedule(1024, 64);
await handle.ToVlkTask(); // Suspension-Punkt — Array muss am Leben bleiben
array.Dispose();          // niemals erreicht, wenn oben eine Ausnahme auftritt
```

Verwenden Sie `TempNativeArrayScope` oder ein `try`/`finally`, um die Entsorgung in allen Code-Pfaden zu garantieren.

---

## AwaitableBridge — Unity Awaitable in VlkTask konvertieren

`AwaitableBridge` bietet Erweiterungsmethoden zur Konvertierung von Unitys `Awaitable`- und `Awaitable<T>`-Typen (seit Unity 2023.1 verfügbar) in `VlkTask`-kompatible Awaiter.

**Hinweis:** `Awaitable` hat auch sein eigenes `GetAwaiter()`. Da C#-Überladungsauflösung Instanzmethoden gegenüber Erweiterungsmethoden bevorzugt, funktioniert das Schreiben von `await myAwaitable` innerhalb einer `async VlkTask`-Methode bereits korrekt — Unitys Awaiter implementiert `ICriticalNotifyCompletion` und der `VlkTask`-Builder akzeptiert es. Die `.AsVlkTask()`-Erweiterungsmethoden werden nur benötigt, wenn Sie ein `Awaitable` an einen Kombinator (`VlkTask.WhenAll`, `VlkTask.WhenAny`) übergeben oder als `VlkTask`-Variable speichern möchten.

Diese Datei ist durch `#if UNITY_2023_1_OR_NEWER` geschützt.

### API

```csharp
// Awaitable in einen VlkTask-kompatiblen Awaiter konvertieren.
public static AwaitableVlkTaskAwaiter   AsVlkTask(this Awaitable awaitable)

// Awaitable<T> in einen VlkTask-kompatiblen Awaiter konvertieren.
public static AwaitableVlkTaskAwaiter<T> AsVlkTask<T>(this Awaitable<T> awaitable)
```

Beide Awaiter implementieren `ICriticalNotifyCompletion`, was die `ExecutionContext`-Erfassung überspringt. Sie delegieren `IsCompleted`, `GetResult` und `OnCompleted` direkt an den umhüllten Unity-Awaiter.

### Verwendung

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnityEngine;

// Direktes Await — funktioniert ohne Konvertierung in einer async VlkTask-Methode.
async VlkTask DirectExample()
{
    await Awaitable.NextFrameAsync();       // Keine Konvertierung nötig.
    await Awaitable.WaitForSecondsAsync(1f);
}

// Explizite Konvertierung — für Kombinatoren und Speicherung benötigt.
async VlkTask CombinatorExample()
{
    VlkTask a = Awaitable.NextFrameAsync().AsVlkTask();
    VlkTask b = Awaitable.WaitForSecondsAsync(2f).AsVlkTask();
    await VlkTask.WhenAll(a, b);
}

// Generische Version mit einem Ergebnistyp.
async VlkTask<Texture2D> LoadTextureExample(string path)
{
    Awaitable<Texture2D> loadOp = LoadTextureAsync(path);
    return await loadOp.AsVlkTask();
}
```

---

## JobBridge — der quellgenerierte Wrapper

`JobBridge.cs` definiert `JobPromise<TJob>`, den generischen gepoolten Promise-Typ, der vom Quellgenerator verwendet wird. Es ist ein Implementierungsdetail; Sie werden es normalerweise nicht selbst instanziieren.

```csharp
// Fragt einen JobHandle jeden Frame ab. Wird von den quellgenerierten ScheduleAsync-Methoden verwendet.
public sealed class JobPromise<TJob> : VlkTask.ISource, IPlayerLoopItem, IPoolNode<JobPromise<TJob>>
    where TJob : struct
{
    public static JobPromise<TJob> Create(JobHandle handle, CancellationToken ct, out uint token);
}
```

Das Verhalten ist identisch mit `JobHandlePromise` (siehe `JobHandleExtensions`), außer dass es über den Job-Typ für Pool-Isolation generisch ist — jeder Job-Typ bekommt seinen eigenen Pool.

---

## Quellgenerator: JobBridgeGenerator

Der `JobBridgeGenerator` ist ein inkrementeller Roslyn-Quellgenerator (Klasse `UnaPartidaMas.Valkarn.Tasks.SourceGen.Generators.JobBridgeGenerator`), der automatisch `ScheduleAsync`-Erweiterungsmethoden für Ihre Job-Typen produziert.

### Was er erkennt

Der Generator scannt alle **öffentlichen** Structs in der Kompilierung, die eines der folgenden implementieren:
- `Unity.Jobs.IJob`
- `Unity.Jobs.IJobParallelFor`
- `Unity.Jobs.IJobFor`

Private und interne Job-Structs werden übersprungen. Wenn ein Struct in einem nicht-öffentlichen Typ verschachtelt ist, wird es ebenfalls übersprungen.

Der Generator tut nichts, wenn `UnaPartidaMas.Valkarn.Tasks.ValkarnTask` nicht in der Kompilierung gefunden wird, sodass er in Assemblies, die Valkarn Tasks nicht referenzieren, sicher ist.

### Was er generiert

Die Ausgabedatei ist `ValkarnTask.JobBridge.Generated.g.cs`. Für jeden erkannten Job-Typ gibt er eine `public static class __<TypeName>_AsyncExt` aus, die Folgendes enthält:

| Job-Interface | Generierte Methoden-Signatur |
|---|---|
| `IJob` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, CancellationToken ct = default)` |
| `IJobParallelFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleParallelAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |

Jede generierte Methode plant den Job mit den Standard-Unity-Erweiterungsmethoden und umhüllt dann den resultierenden `JobHandle` in einem `JobPromise<TJob>` und gibt ein `ValkarnTask` zurück.

Für verschachtelte Typen (z. B. ein Job-Struct innerhalb einer äußeren Klasse) verwendet der generierte Klassenname Unterstriche: `__Outer_Inner_AsyncExt`.

### Verwendung generierter Methoden

```csharp
// IJob-Beispiel
public struct MyCalculationJob : IJob
{
    public NativeArray<float> Data;
    public void Execute() { /* ... */ }
}

// Der Generator produziert:
//   public static ValkarnTask ScheduleAsync(this ref MyCalculationJob job, CancellationToken ct = default)

async VlkTask RunCalculation(CancellationToken ct)
{
    using var scope = TempNativeArrayScope.Create<float>(1024);
    var job = new MyCalculationJob { Data = scope.Array };
    await job.ScheduleAsync(ct);  // generierte Erweiterungsmethode
    // Ergebnisse aus scope.Array hier lesen.
}

// IJobParallelFor-Beispiel
public struct MyParallelJob : IJobParallelFor
{
    public NativeArray<float> Input;
    public NativeArray<float> Output;
    public void Execute(int index) { Output[index] = Input[index] * 2f; }
}

async VlkTask RunParallel(int length, CancellationToken ct)
{
    using var inputScope  = TempNativeArrayScope.Create<float>(length);
    using var outputScope = TempNativeArrayScope.Create<float>(length);
    var job = new MyParallelJob { Input = inputScope.Array, Output = outputScope.Array };
    await job.ScheduleAsync(length, innerLoopBatchCount: 64, ct);
}
```

---

## Quellgenerator: AwaitableBridgeGenerator

Der `AwaitableBridgeGenerator` erkennt, ob `UnityEngine.Awaitable` und `UnityEngine.Awaitable<T>` in der Kompilierung vorhanden sind und gibt `AsValkarnTask()`-Erweiterungsmethoden aus, wenn sie es sind.

Die Ausgabedatei ist `ValkarnTask.AwaitableBridge.Generated.g.cs`. Der generierte Code befindet sich in `namespace UnaPartidaMas.Valkarn.Tasks.Bridge` unter der Klasse `AwaitableBridgeExtensions`.

Generierte Methoden:

```csharp
// Wird ausgegeben, wenn UnityEngine.Awaitable gefunden wird:
public static async ValkarnTask AsValkarnTask(this Awaitable awaitable)
{
    await awaitable;
}

// Wird ausgegeben, wenn UnityEngine.Awaitable<T> gefunden wird:
public static async ValkarnTask<T> AsValkarnTask<T>(this Awaitable<T> awaitable)
{
    return await awaitable;
}
```

Dies sind `async ValkarnTask`-Methoden, die durch Valkarn Tasks' gepoolten async-Builder laufen — bei einem warmen Pool sind sie null-Allokationen.

Der Generator ist geschützt: Wenn `ValkarnTask` nicht in der Kompilierung ist, wird kein Code ausgegeben. Dies verhindert CS0246-Fehler in Assemblies, die Unity, aber nicht Valkarn Tasks referenzieren.

---

## Vollständiges Arbeitsbeispiel: Job-Bridge in einem ECS-System

Das Folgende stammt aus `Samples~/ECS/JobBridgeExample.cs`. Es zeigt das vollständige Muster zur Planung eines Burst-parallelen Jobs aus einem `ISystem`, zum Abwarten ohne Blockierung und zum Zurückschreiben von Ergebnissen.

```csharp
#if UNITY_5_3_OR_NEWER && VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnaPartidaMas.Valkarn.Tasks.ECS;

public partial struct JobBridgeExample : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<HealthData>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var worldCt = state.World.GetWorldCancellationToken();

        // Alle Entity-Daten synchron innerhalb OnUpdate extrahieren.
        // Asynchrone Methoden können keine ref-Parameter haben (CS1988), daher müssen Daten
        // hier kopiert und als Wert an die asynchrone Methode übergeben werden.
        var query       = SystemAPI.QueryBuilder().WithAll<HealthData>().Build();
        var entityCount = query.CalculateEntityCount();
        if (entityCount == 0) return;

        var entities    = query.ToEntityArray(Allocator.TempJob);
        var healthArray = query.ToComponentDataArray<HealthData>(Allocator.TempJob);
        var results     = new NativeArray<float>(entityCount, Allocator.TempJob);

        // Die asynchrone Methode übernimmt das Eigentumsrecht an den NativeArrays und entsorgt sie.
        ProcessHealthAsync(state.EntityManager, entities, healthArray, results, worldCt).Forget();
        state.Enabled = false;
    }

    public void OnDestroy(ref SystemState state) { }

    static async VlkTask ProcessHealthAsync(
        EntityManager entityManager,
        NativeArray<Entity> entities,
        NativeArray<HealthData> healthArray,
        NativeArray<float> results,
        CancellationToken ct)
    {
        try
        {
            // Phase 1: Den Burst-Job planen.
            var job = new HealthProcessingJob
            {
                HealthInputs     = healthArray,
                ProcessedOutputs = results,
            };
            var handle = job.Schedule(entities.Length, batchSize: 64);

            // Phase 2: Abschluss abwarten ohne den Haupt-Thread zu blockieren.
            await handle.ToVlkTask(cancellationToken: ct);

            // Phase 3: Ergebnisse anwenden. Wir sind wieder auf dem Haupt-Thread.
            ct.ThrowIfCancellationRequested();

            for (int i = 0; i < entities.Length; i++)
            {
                // Entity kann zerstört worden sein, während der Job lief.
                if (!entityManager.SafeEntityExists(entities[i]))
                    continue;

                entityManager.SetComponentData(entities[i], new HealthData
                {
                    CurrentHealth = results[i],
                });
            }
        }
        finally
        {
            // NativeArrays immer entsorgen — läuft bei Erfolg, Ausnahme oder Abbruch.
            if (entities.IsCreated)    entities.Dispose();
            if (healthArray.IsCreated) healthArray.Dispose();
            if (results.IsCreated)     results.Dispose();
        }
    }

    [BurstCompile]
    struct HealthProcessingJob : IJobParallelFor
    {
        [ReadOnly]  public NativeArray<HealthData> HealthInputs;
        [WriteOnly] public NativeArray<float>      ProcessedOutputs;

        public void Execute(int index)
        {
            var h = HealthInputs[index];
            var newHealth = h.CurrentHealth + h.RegenRate;
            if (newHealth > h.MaxHealth) newHealth = h.MaxHealth;
            ProcessedOutputs[index] = newHealth;
        }
    }

    struct HealthData : IComponentData
    {
        public float CurrentHealth;
        public float MaxHealth;
        public float RegenRate;
    }
}
#endif
```

---

## Zusammenfassung der Bridge-Typen

| Typ | Zweck | Allokation |
|---|---|---|
| `JobHandleExtensions.ToVlkTask()` | Einen einzelnen `JobHandle` abwarten | Null auf Schnellpfad; gepooltes Promise andernfalls |
| `JobHandleExtensions.WhenAll()` | Mehrere `JobHandle` parallel abwarten | Null auf Schnellpfad; gepooltes Promise + `ArrayPool`-Ausleihe andernfalls |
| `TempNativeArrayScope<T>` | RAII-Lebensdauer-Management für `NativeArray` | Keine (Struct) |
| `AwaitableBridge.AsVlkTask()` | `Awaitable`/`Awaitable<T>` in VlkTask konvertieren | Keine (Struct-Awaiter) |
| Generiertes `ScheduleAsync()` | Einen typisierten Job direkt abwarten | Gepooltes `JobPromise<TJob>` |
