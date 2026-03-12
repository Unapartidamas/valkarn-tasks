---
sidebar_position: 3
title: Pruebas
---

# Pruebas

Valkarn Tasks incluye una infraestructura de pruebas dedicada que te permite escribir pruebas unitarias rápidas y deterministas para código async — sin temporizadores reales, sin esperas de fotogramas, sin necesidad del Editor de Unity para el conjunto de pruebas principal.

---

## Descripción General

El soporte de pruebas reside en el ensamblado `Testing/` (`UnaPartidaMas.Valkarn.Tasks.Testing`), que es visible para el runtime mediante `InternalsVisibleTo`. Expone dos tipos públicos:

| Tipo | Propósito |
|------|-----------|
| `VlkTaskTestHelper` | Inicializa y desmonta el runtime de Valkarn Tasks para una sesión de pruebas |
| `TestClock` | Proveedor determinista de tiempo y fotogramas; reemplaza el `TimeProvider` de Unity durante las pruebas |

---

## VlkTaskTestHelper

`VlkTaskTestHelper` es una clase utilitaria estática en el espacio de nombres `UnaPartidaMas.Valkarn.Tasks.Testing`.

### Qué hace

`Setup()` realiza tres cosas:
1. Crea un `TestClock` y lo instala como `TimeProvider.Current`, reemplazando cualquier proveedor de tiempo real de Unity.
2. Llama a `PlayerLoopHelper.InitializeForTest()`, que asigna los arrays `ContinuationQueue` y `PlayerLoopRunner` para todos los 16 timings del PlayerLoop y marca el runtime como inicializado.
3. Devuelve el `TestClock` para que tu prueba pueda controlar el tiempo.

`Teardown()` revierte la configuración:
1. Instala un `TestClock` nuevo sin operaciones como `TimeProvider.Current` para evitar que el tiempo obsoleto se filtre entre fixtures de prueba.
2. Llama a `PlayerLoopHelper.ShutdownForTest()`, que desmonta las colas y los runners.

### Patrón de uso

```csharp
using NUnit.Framework;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Testing;

[TestFixture]
public class MyAsyncTests
{
    TestClock clock;

    [SetUp]
    public void SetUp()
    {
        clock = VlkTaskTestHelper.Setup();
    }

    [TearDown]
    public void TearDown()
    {
        VlkTaskTestHelper.Teardown();
    }

    // Las pruebas van aquí
}
```

Llama a `Setup` una vez por prueba en `[SetUp]` y a `Teardown` una vez por prueba en `[TearDown]`. El patrón garantiza que cada prueba comience con un estado de runtime limpio y no se filtre a la siguiente prueba.

### Delta time predeterminado

`Setup` acepta un parámetro opcional `defaultDeltaTime` (predeterminado: `1/60` segundos, es decir, 60 fps):

```csharp
clock = VlkTaskTestHelper.Setup(defaultDeltaTime: 1f / 30f);  // simulación a 30 fps
```

Este valor es usado por `TestClock.AdvanceFrame()` y `TestClock.AdvanceFrames(n)`.

---

## TestClock

`TestClock` implementa `ITimeProvider` y da a las pruebas control total sobre:

- El tiempo simulado (mediante `GetTimestamp()`, respaldado por ticks de `Stopwatch.Frequency`)
- `DeltaTime` y `UnscaledDeltaTime` para el fotograma actual
- `FrameCount`

### Avanzar el tiempo

#### `Advance(TimeSpan duration)`

Avanza el tiempo por la duración dada en un solo paso. La duración completa se aplica como `DeltaTime` para ese fotograma, `FrameCount` se incrementa en 1 y se procesan todos los timings del PlayerLoop. Después del procesamiento, `DeltaTime` se restaura al valor que tenía antes de la llamada.

```csharp
var task = VlkTask.Delay(3000);  // retraso de 3 segundos

clock.Advance(TimeSpan.FromSeconds(2));
Assert.IsFalse(task.IsCompleted);   // aún no

clock.Advance(TimeSpan.FromSeconds(1));
Assert.IsTrue(task.IsCompleted);    // exactamente en 3 segundos
```

Usa esto cuando quieras saltar a un punto específico en el tiempo sin simular fotogramas individuales.

#### `AdvanceFrame()`

Avanza un fotograma usando el `DeltaTime` actual. `FrameCount` se incrementa en 1, se procesan todos los timings del PlayerLoop y se inserta un `Thread.Sleep` de 1 ms antes del procesamiento. El sleep existe porque los hilos de trabajo en segundo plano (usados por `RunOnThreadPool` y la integración del Sistema de Jobs de Unity) necesitan una pequeña ventana para completarse entre ticks de fotograma — sin él, las pruebas ejecutan fotogramas consecutivos sin pausa, lo que provoca condiciones de carrera que no ocurren en fotogramas reales de Unity.

```csharp
clock.AdvanceFrame();
```

#### `AdvanceFrames(int count)`

Llama a `AdvanceFrame()` `count` veces.

```csharp
clock.AdvanceFrames(10);  // simular 10 fotogramas
```

#### `ProcessTick(PlayerLoopTiming timing)`

Procesa una única fase de timing del PlayerLoop sin avanzar el tiempo ni `FrameCount`. Útil cuando pruebas código programado en un timing específico (por ejemplo, `PlayerLoopTiming.FixedUpdate`) y no quieres procesar todos los demás timings.

```csharp
clock.ProcessTick(PlayerLoopTiming.FixedUpdate);
```

### Controlar el delta time

#### `SetDeltaTime(float deltaTime)`

Establece tanto `DeltaTime` como `UnscaledDeltaTime` al mismo valor para todos los fotogramas subsiguientes.

#### `SetDeltaTime(float deltaTime, float unscaledDeltaTime)`

Los establece de forma independiente para simular un `Time.timeScale` no unitario. Por ejemplo, para probar código que usa `DelayType.UnscaledDeltaTime` mientras el tiempo está pausado:

```csharp
clock.SetDeltaTime(deltaTime: 0f, unscaledDeltaTime: 0.05f);

var scaledTask = VlkTask.Delay(500, DelayType.DeltaTime);
var unscaledTask = VlkTask.Delay(500, DelayType.UnscaledDeltaTime);

clock.AdvanceFrames(9);  // 450 ms sin escala, 0 ms con escala
Assert.IsFalse(scaledTask.IsCompleted);    // el tiempo de juego pausado nunca alcanza 500ms
Assert.IsFalse(unscaledTask.IsCompleted); // 450ms < 500ms

clock.AdvanceFrame();  // 500 ms sin escala
Assert.IsTrue(unscaledTask.IsCompleted);  // retraso sin escala completado
Assert.IsFalse(scaledTask.IsCompleted);  // con escala sigue sin moverse
```

---

## Escribir Pruebas Unitarias para Métodos VlkTask Async

### Pruebas que completan sincrónicamente

Algunas operaciones de `VlkTask` completan sin suspenderse — por ejemplo, esperar `VlkTask.CompletedTask`, `VlkTask.FromResult(value)`, o un `VlkTaskCompletionSource` que fue completado antes de ser esperado. Estas no requieren un reloj en absoluto y pueden usar la clase `TestHelper` interna directamente (interna al ensamblado de pruebas):

```csharp
[TestFixture]
public class SyncTests
{
    [SetUp]
    public void SetUp()
    {
        // Solo establece el ID del hilo principal — no se necesita reloj ni PlayerLoop
        TestHelper.EnsureInitialized();
    }

    [Test]
    public void FromResult_ReturnsValue()
    {
        var task = VlkTask.FromResult(42);
        Assert.IsTrue(task.IsCompleted);
        Assert.AreEqual(42, task.GetAwaiter().GetResult());
    }
}
```

`TestHelper` (en `Tests/Editor/TestHelper.cs`) es un ayudante interno usado por el propio conjunto de pruebas:

- `TestHelper.EnsureInitialized()` establece `VlkTaskPoolShared.MainThreadId` al hilo actual. Esto es necesario para que las operaciones del grupo se enruten a la ruta rápida correcta (no atómica).
- `TestHelper.RunSync(VlkTask task)` afirma que la tarea ya está completada y llama a `GetResult()` — útil para probar rutas de código que deben completar sincrónicamente.
- `TestHelper.RunSync<T>(VlkTask<T> task)` hace lo mismo y devuelve el valor del resultado.
- `TestHelper.UnobservedExceptionCollector` se suscribe a `VlkTask.UnobservedException` durante la duración de su ámbito, recogiendo cualquier excepción no observada para su verificación.

### Pruebas que requieren tiempo

Cualquier prueba que involucre `VlkTask.Delay`, `VlkTask.Yield` o código programado en un timing del PlayerLoop requiere `VlkTaskTestHelper.Setup()` y el `TestClock` devuelto.

**Ejemplo: probar un método basado en retraso**

Supón que tienes:

```csharp
public static async VlkTask WaitAndLog(int ms)
{
    await VlkTask.Delay(ms);
    Log("done");
}
```

Pruébalo sin espera real:

```csharp
[TestFixture]
public class DelayTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = VlkTaskTestHelper.Setup();

    [TearDown]
    public void TearDown() => VlkTaskTestHelper.Teardown();

    [Test]
    public void WaitAndLog_CompletesAfterDelay()
    {
        var task = WaitAndLog(3000);
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(task.IsCompleted);

        // Recuperar resultado (lanza si falló)
        task.GetAwaiter().GetResult();
    }

    [Test]
    public void WaitAndLog_ZeroDelay_CompletesImmediately()
    {
        var task = WaitAndLog(0);
        // VlkTask.Delay(0) devuelve CompletedTask inmediatamente
        Assert.IsTrue(task.IsCompleted);
    }
}
```

### Probar cancelación

```csharp
[Test]
public void Delay_CancelledMidway_ThrowsOCE()
{
    var cts = new CancellationTokenSource();
    var task = VlkTask.Delay(5000, cts.Token);
    Assert.IsFalse(task.IsCompleted);

    cts.Cancel();
    // La cancelación se propaga en el siguiente tick
    clock.AdvanceFrame();

    Assert.IsTrue(task.IsCompleted);
    Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
}
```

### Recoger excepciones no observadas

```csharp
[Test]
public void FaultedTask_PublishesUnobservedException()
{
    using var collector = new TestHelper.UnobservedExceptionCollector();

    var tcs = new VlkTaskCompletionSource();
    var task = tcs.Task;

    // No esperar — fire and forget
    task.Forget();

    // Completar con excepción — dispara la ruta no observada
    tcs.TrySetException(new InvalidOperationException("oops"));

    Assert.AreEqual(1, collector.Exceptions.Count);
    Assert.IsInstanceOf<InvalidOperationException>(collector.Exceptions[0]);
}
```

---

## Ejemplo: Probar un Canal Productor/Consumidor

```csharp
[TestFixture]
public class ChannelPipelineTests
{
    [SetUp]
    public void SetUp() => TestHelper.EnsureInitialized();

    [Test]
    public void Producer_WritesItems_ConsumerReadsInOrder()
    {
        var channel = VlkTask.Channel.CreateUnbounded<int>();

        // Productor: escribir sincrónicamente
        channel.Writer.TryWrite(1);
        channel.Writer.TryWrite(2);
        channel.Writer.TryWrite(3);
        channel.Writer.Complete();

        // Consumidor: ReadAsync en un canal con elementos completa sincrónicamente
        Assert.AreEqual(1, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(2, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(3, TestHelper.RunSync(channel.Reader.ReadAsync()));
    }

    [Test]
    public void ReadAsync_BeforeWrite_PendsThenCompletesOnWrite()
    {
        var channel = VlkTask.Channel.CreateUnbounded<string>();

        // Lectura emitida antes de cualquier escritura
        var readTask = channel.Reader.ReadAsync();
        Assert.IsFalse(readTask.IsCompleted);

        // La escritura completa la lectura pendiente sincrónicamente
        channel.Writer.TryWrite("hello");
        Assert.IsTrue(readTask.IsCompleted);
        Assert.AreEqual("hello", readTask.GetAwaiter().GetResult());
    }

    [Test]
    public void Complete_DrainedChannel_CompletionTaskCompletes()
    {
        var channel = VlkTask.Channel.CreateUnbounded<int>();
        channel.Writer.TryWrite(42);

        var completion = channel.Reader.Completion;
        Assert.IsFalse(completion.IsCompleted);

        channel.Writer.Complete();
        Assert.IsFalse(completion.IsCompleted);  // elemento aún sin leer

        channel.Reader.TryRead(out _);
        Assert.IsTrue(completion.IsCompleted);   // drenado — se dispara la completación
    }

    [Test]
    public void ClosedChannel_Read_ThrowsChannelClosedException()
    {
        var channel = VlkTask.Channel.CreateUnbounded<int>();
        channel.Writer.Complete();

        var readTask = channel.Reader.ReadAsync();
        Assert.IsTrue(readTask.IsCompleted);
        Assert.Throws<ChannelClosedException>(() => readTask.GetAwaiter().GetResult());
    }
}
```

---

## Ejemplo: Probar VlkTask.Delay Con Avance de Fotogramas

Este ejemplo demuestra cómo probar un método que espera un retraso configurable, verificando que el avance parcial no complete la tarea prematuramente.

```csharp
[TestFixture]
public class DelayFrameTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = VlkTaskTestHelper.Setup(defaultDeltaTime: 1f / 60f);

    [TearDown]
    public void TearDown() => VlkTaskTestHelper.Teardown();

    [Test]
    public void Delay_500ms_RequiresTenFramesAt50ms()
    {
        clock.SetDeltaTime(0.05f);  // 50 ms por fotograma
        var task = VlkTask.Delay(500);

        clock.AdvanceFrames(9);
        Assert.IsFalse(task.IsCompleted, "450 ms transcurridos — no debería estar listo");

        clock.AdvanceFrame();
        Assert.IsTrue(task.IsCompleted, "500 ms transcurridos — debería estar listo");
    }

    [Test]
    public void Delay_Realtime_UsesTimestampNotDeltaTime()
    {
        // El retraso en tiempo real ignora DeltaTime; usa el timestamp del Stopwatch
        clock.SetDeltaTime(0f);  // DeltaTime congelado
        var task = VlkTask.Delay(200, DelayType.Realtime);

        // Advance solo mueve el timestamp mediante TimeSpan
        clock.Advance(TimeSpan.FromMilliseconds(199));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(task.IsCompleted);
    }
}
```

---

## Integración con NUnit

El conjunto de pruebas usa **NUnit 3.x** (fijado en 3.x en `TestRunner.csproj`). NUnit 4 eliminó la API clásica `Assert.IsTrue` / `Assert.AreEqual` que usan las pruebas; no actualices a NUnit 4 a menos que las aserciones se actualicen a la API basada en restricciones.

### Unity Test Framework

En el Editor de Unity, las pruebas en `Tests/Editor/` son descubiertas y ejecutadas por el **Unity Test Framework** (UTF), que está basado en NUnit. La integración del runner de pruebas funciona así:

- UTF ejecuta cada `[Test]` en el hilo principal.
- `VlkTaskTestHelper.Setup()` instala el `TestClock` antes de cada prueba; el atributo `[SetUp]` de UTF lo llama.
- `VlkTaskTestHelper.Teardown()` elimina el reloj en `[TearDown]`.
- No se necesita corrutina ni `[UnityTest]`: todas las pruebas de Valkarn Tasks usan métodos NUnit `[Test]` sincrónicos. El `TestClock` controla el tiempo manualmente, por lo que no hay necesidad de avance de fotogramas real.

---

## El Proyecto _TestRunner~

El directorio `_TestRunner~/` contiene un proyecto .NET 8 independiente que compila y ejecuta el conjunto de pruebas completo **fuera de Unity**, usando el SDK de .NET y `dotnet test`. Se usa para CI y para el desarrollo local de colaboradores.

### Estructura

```
_TestRunner~/
  TestRunner.csproj   — archivo de proyecto .NET 8
  bin~/               — salida de compilación (ignorada por git)
  obj~/               — salida intermedia (ignorada por git)
```

### Cómo funciona

`TestRunner.csproj` compila cuatro conjuntos de fuentes en un único ensamblado:

| Conjunto de fuentes | Ruta | Notas |
|---------------------|------|-------|
| Runtime | `../Runtime/**/*.cs` | Todos los archivos de runtime |
| Testing | `../Testing/**/*.cs` | `VlkTaskTestHelper`, `TestClock` |
| Tests | `../Tests/Editor/**/*.cs` | Todos los archivos `.cs` de pruebas |
| Exclusiones | `UnityTimeProvider.cs`, `Bridge/`, `Burst/`, `ECS/` | Requiere APIs reales de Unity — excluido |

El proyecto **no** define `UNITY_5_3_OR_NEWER`, `VTASKS_HAS_BURST`, `VTASKS_HAS_COLLECTIONS` ni `VTASKS_HAS_ENTITIES`, por lo que todas las ramas `#if` específicas de Unity se compilan fuera. Esto significa que las pruebas ejercitan las rutas de código puras de C#.

Los DLLs del analizador se cargan como elementos `<Analyzer>`, por lo que las mismas reglas que se activan en el IDE también se activan durante `dotnet build` del proyecto de pruebas.

### Ejecutar las pruebas

```bash
cd _TestRunner~
dotnet test
```

O desde la raíz del repositorio:

```bash
dotnet test _TestRunner~/TestRunner.csproj
```

### Archivos de prueba excluidos

Tres subdirectorios de pruebas están excluidos del proyecto `_TestRunner~` porque requieren APIs reales del runtime de Unity:

| Directorio | Razón |
|------------|-------|
| `Tests/Editor/Bridge/` | Pruebas para el puente del Sistema de Jobs; requiere `Unity.Jobs` |
| `Tests/Editor/Burst/` | Pruebas para el programador Burst; requiere `Unity.Burst` |
| `Tests/Editor/ECS/` | Pruebas para utilidades ECS; requiere `Unity.Entities` |

Estas pruebas solo se ejecutan dentro del Editor de Unity mediante el Unity Test Framework.

### Añadir una nueva prueba

1. Añade un nuevo archivo `.cs` en `Tests/Editor/` (no en un subdirectorio de API de Unity).
2. Decora la clase con `[TestFixture]` y los métodos con `[Test]`.
3. Si la prueba necesita control de tiempo, usa `VlkTaskTestHelper.Setup()` / `Teardown()` en `[SetUp]` / `[TearDown]`.
4. Si la prueba solo necesita aserciones de tareas sincrónicas (sin retraso), usa `TestHelper.EnsureInitialized()` en `[SetUp]` — no se necesita teardown.
5. Ejecuta `dotnet test _TestRunner~/TestRunner.csproj` para verificar fuera de Unity.
6. Ejecuta el Unity Test Framework para verificar dentro de Unity.
