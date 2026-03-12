---
sidebar_position: 4
title: Características
---

# Características

Referencia completa de características de Valkarn Tasks.

---

## Primitiva async principal

### Struct ValkarnTask

Un tipo de retorno async basado en struct y sin asignaciones que reemplaza tanto `UniTask` como `Awaitable` de Unity.

```csharp
async ValkarnTask LoadLevel() { ... }
async ValkarnTask<int> CountEnemies() { ... }
```

- **Ruta rápida síncrona sin asignación** — si el método se completa sin suspenderse, no ocurren asignaciones en el heap.
- **Ruta async con pool** — si el método se suspende, el runner de la máquina de estados se toma de un pool acotado y reducible. Sin boxing, pools acotados con recorte automático.
- **Pool optimizado para IL2CPP** — las operaciones de pool en el hilo principal usan cero atómicos. IL2CPP penaliza `Volatile` en 9,2× e `Interlocked` en 2,9× respecto a Mono; Valkarn evita ambos en la ruta crítica.
- **Seguridad de tokens generacionales** — un contador de generación `uint` por slot de pool. 4.294.967.296 ciclos por slot antes de colisión — imposible en la práctica. (UniTask usa un token `short`: colisión tras ~18 minutos de trabajo async activo.)

### Result\<T\> — manejo de errores sin excepciones

```csharp
var result = await loadTask.AsResult();

if (result.Succeeded)    Use(result.Value);
else if (result.IsCanceled) ShowRetry();
else                         Debug.LogError(result.Error);
```

`Result<T>` y `Result` son valores `readonly struct` que representan el resultado de una tarea sin lanzar excepciones. Ambos soportan conversión implícita a `bool` (verdadero cuando se completa con éxito).

### Fuentes de completado manual

```csharp
var promise = new ValkarnTask.Promise<string>();
promise.TrySetResult("done");
string value = await promise.Task;
```

Soporta `TrySetResult`, `TrySetException` y `TrySetCanceled`. El reporte de excepciones no observadas basado en finalizadores garantiza que los errores nunca se pierdan silenciosamente.

### Fuentes de completado en pool

Variantes en pool con auto-reset para patrones repetitivos (usadas internamente por canales y combinadores):

```csharp
var source = ValkarnTask.PooledPromise<int>.Create(out uint token);
source.TrySetResult(42);
int value = await source.Task; // la fuente vuelve al pool automáticamente
```

---

## Puente con Awaitable

Interoperabilidad transparente con `Awaitable` de Unity — sin conversión manual:

```csharp
async ValkarnTask LoadGame()
{
    await SceneManager.LoadSceneAsync("Level2"); // Unity Awaitable — funciona directamente
    await Resources.LoadAsync<Texture2D>("hero"); // Unity Awaitable — funciona directamente
    await ValkarnTask.Delay(1000);                // nativo de Valkarn
}
```

El generador de código fuente detecta los awaits de `Awaitable` y genera el adaptador automáticamente.

---

## Cancelación de ciclo de vida

### Automática (generada por código fuente)

Marca la clase como `partial` — el generador de código fuente hace el resto:

```csharp
public partial class EnemySpawner : MonoBehaviour
{
    async ValkarnTask Start()
    {
        while (true)
        {
            await ValkarnTask.Delay(3000);
            SpawnWave();
            // Cancelado automáticamente cuando este GameObject es destruido.
        }
    }
}
```

- **MonoBehaviour** — vinculado a `destroyCancellationToken`
- **ScriptableObject** — vinculado al tiempo de vida de la aplicación
- **Clase simple** — sin vinculación automática (se requiere token manual)

### Exclusión voluntaria

```csharp
[NoAutoCancel]
async ValkarnTask BackgroundSync()
{
    await SyncToServer(); // no se cancela automáticamente al destruir
}
```

### Override manual de CancellationToken

Pasar un `CancellationToken` explícito reemplaza el token de ciclo de vida inyectado automáticamente:

```csharp
async ValkarnTask DoWork(CancellationToken ct)
{
    await ValkarnTask.Delay(1000, ct);
}
```

### Sin cancelación de tareas hermanas

Las tareas nunca cancelan tareas hermanas. `WhenAll` espera a TODAS las tareas; `WhenAny` devuelve el primer resultado pero las tareas perdedoras siguen ejecutándose. Esto previene la corrupción de datos cuando las tareas tienen efectos secundarios.

---

## Secciones críticas

Para operaciones que no deben ser interrumpidas por la cancelación del ciclo de vida:

```csharp
async ValkarnTask SaveProgress()
{
    var data = await LoadPlayerData();        // cancelable

    await using (ValkarnTask.Critical())
    {
        await db.Insert(data);               // NO se cancela aunque el GO sea destruido
        await db.Commit();
    }                                        // la cancelación pendiente se aplica aquí

    await SendNotification();                // cancelable de nuevo
}
```

Dentro de una sección crítica, la cancelación se **difiere** — no se ignora. Cuando la sección termina, se aplica la cancelación pendiente.

---

## Combinadores

### WhenAll (tipado)

```csharp
// Directo — lanza si alguna tarea falla
var (enemies, map) = await ValkarnTask.WhenAll(LoadEnemies(), LoadMap());

// Seguro — envuelve con AsResult() para comportamiento sin lanzamiento
var (a, b) = await ValkarnTask.WhenAll(
    LoadEnemies().AsResult(), LoadMap().AsResult());
```

Ruta rápida síncrona sin asignación cuando todas las tareas ya están completadas. La sobrecarga `IEnumerable<ValkarnTask<T>>` usa `ArrayPool<T>` para arrays internos.

### WhenAll (void)

```csharp
await ValkarnTask.WhenAll(SaveA(), SaveB(), SaveC());
await ValkarnTask.WhenAll(taskList); // IEnumerable<ValkarnTask>
```

### WhenAny

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(DownloadFromA(), DownloadFromB());
```

Devuelve el primer resultado completado. Las tareas perdedoras siguen ejecutándose con normalidad.

### Fire-and-forget

```csharp
SendAnalytics("event").Forget();

[FireAndForget]
async ValkarnTask SendAnalytics(string eventName) { ... }
// Los llamadores no necesitan .Forget() — no se genera ninguna advertencia
```

`.Forget()` enruta los errores a `ValkarnTask.UnobservedException`. Nunca se silencian.

### AsNonGeneric

```csharp
ValkarnTask voidTask = typedTask.AsNonGeneric();
```

---

## Tiempo y retraso

```csharp
await ValkarnTask.Delay(1000);                                    // milisegundos
await ValkarnTask.Delay(TimeSpan.FromSeconds(2));                  // TimeSpan
await ValkarnTask.Delay(1000, DelayType.UnscaledDeltaTime);       // ignora la escala de tiempo
await ValkarnTask.Delay(1000, DelayType.Realtime);                 // basado en Stopwatch

await ValkarnTask.Yield();                                         // siguiente tick del PlayerLoop
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);             // temporización específica
await ValkarnTask.NextFrame();                                      // garantiza el siguiente frame
await ValkarnTask.DelayFrame(5);                                    // N frames

await ValkarnTask.WaitUntil(() => player.IsReady);
await ValkarnTask.WaitWhile(() => isLoading);
```

---

## Cambio de hilo

```csharp
async ValkarnTask ProcessData()
{
    var raw = await DownloadData();

    await ValkarnTask.SwitchToThreadPool();
    var processed = HeavyComputation(raw);  // hilo en segundo plano

    await ValkarnTask.SwitchToMainThread();
    ApplyToGameObject(processed);           // hilo principal
}
```

---

## 16 temporizaciones del PlayerLoop

| Grupo | Temporizaciones |
|-------|---------|
| Initialization | `Initialization`, `LastInitialization` |
| EarlyUpdate | `EarlyUpdate`, `LastEarlyUpdate` |
| FixedUpdate | `FixedUpdate`, `LastFixedUpdate` |
| PreUpdate | `PreUpdate`, `LastPreUpdate` |
| Update | `Update`, `LastUpdate` |
| PreLateUpdate | `PreLateUpdate`, `LastPreLateUpdate` |
| PostLateUpdate | `PostLateUpdate`, `LastPostLateUpdate` |
| TimeUpdate | `TimeUpdate`, `LastTimeUpdate` |

Todas las operaciones usan `PlayerLoopTiming.Update` por defecto salvo que se especifique lo contrario.

---

## Canales

```csharp
// Sin límite
var channel = ValkarnTask.Channel.CreateUnbounded<EnemySpawnRequest>();

// Acotado — contrapresión cuando está lleno
var channel = ValkarnTask.Channel.CreateBounded<LogEntry>(capacity: 100);

// Multi-consumidor
var channel = ValkarnTask.Channel.CreateUnbounded<WorkItem>(multiConsumer: true);

// Productor
await channel.Writer.WriteAsync(entry);
bool accepted = channel.Writer.TryWrite(entry);

// Consumidor
await foreach (var item in channel.Reader.ReadAllAsync())
    Process(item);

// Completado
channel.Writer.Complete();
await channel.Reader.Completion;
```

---

## Pruebas deterministas (TestClock)

```csharp
[Test]
public void Respawn_WaitsThreeSeconds()
{
    var clock = new TestClock();
    var task = spawner.RespawnEnemy();

    clock.Advance(TimeSpan.FromSeconds(2));
    Assert.IsFalse(task.IsCompleted);

    clock.Advance(TimeSpan.FromSeconds(1));
    Assert.IsTrue(task.IsCompleted);
}
```

Todas las operaciones dependientes del tiempo leen desde `TimeProvider.Current`. En pruebas, sustitúyelo por `TestClock`. `AdvanceFrame()` simula un tick del player loop.

---

## Puente con el Job System

```csharp
var job = new PathfindingJob { start = a, end = b, results = results };
await job.ScheduleAsync();
// el NativeArray de resultados es legible inmediatamente tras el await

await job.ScheduleParallelAsync(dataCount, batchSize); // IJobParallelFor

// Cancelación — completa el job handle antes de reportar la cancelación (sin fuga de jobs)
await job.ScheduleAsync(cancellationToken);
```

---

## Diagnósticos en tiempo de compilación

| Código | Severidad | Descripción |
|------|----------|-------------|
| TT001 | Advertencia | Doble await / uso tras liberación en un `ValkarnTask` |
| TT002 | Error | Resultado de `async ValkarnTask` usado como sentencia de expresión — debe ser esperado o con `.Forget()` |
| TT010 | Info | Método async en MonoBehaviour cancelado automáticamente al destruir |
| TT011 | Advertencia | `WhenAll` contiene tareas con tiempos de vida distintos |
| TT012 | Advertencia | Bucle async sin comprobación de cancelación (posible bucle zombie) |
| TT013 | Advertencia | `ValkarnTask` devuelto pero nunca esperado ni descartado explícitamente |
| TT014 | Advertencia | `[NoAutoCancel]` sin parámetro `CancellationToken` manual |
| TT015 | Info | Await de `Awaitable` dentro de `ValkarnTask` — adaptador de puente generado |
| TT016 | Advertencia | Método async sin expresión await |
| TT017 | Advertencia | `[FireAndForget]` en `ValkarnTask<T>` — descarta el valor de retorno |

---

## Gestión del pool

Cada runner de método async se almacena en pool mediante `ValkarnPool<T>`:

- **Hilo principal** — pila libre de locks, sin atómicos
- **Hilos en segundo plano** — pila Treiber libre de locks con operaciones CAS
- **Recorte basado en frames** — cada 300 frames (~5s a 60fps), los objetos sobrantes se liberan gradualmente
- **Nunca se reduce** por debajo del mínimo configurable (por defecto: 8)

Monitorea en tiempo de ejecución:

```csharp
foreach (var (type, size, maxSize) in ValkarnTask.GetPoolInfo())
    Debug.Log($"{type}: {size}/{maxSize}");
```

---

## ValkarnTaskSettings

Configura mediante `ScriptableObject` (`Assets > Create > Valkarn > Tasks > Task Settings`, colócalo en `Resources/`):

| Ajuste | Por defecto | Descripción |
|---------|---------|-------------|
| `DefaultMaxPoolSize` | 256 | Máximo de elementos por tipo de pool |
| `MinPoolSize` | 8 | Nunca recortar por debajo de este valor |
| `TrimCheckInterval` | 300 | Frames entre comprobaciones de recorte |
| `TrimHysteresisCount` | 2 | Comprobaciones consecutivas antes de recortar |
| `TrimReleaseRatio` | 0,25 | Fracción del exceso liberada por ciclo |
| `EnableAutoCancel` | true | Vincula automáticamente las tareas de MonoBehaviour a destroyCancellationToken |
| `LogUnobservedCancellations` | false | Registra las cancelaciones no observadas como advertencias |
| `MaxExceptionLogsPerFrame` | 10 | Límite de logs de excepción por frame |

---

## Manejo de errores

```csharp
// Excepciones no observadas — disparadas determinísticamente al devolver al pool
ValkarnTask.UnobservedException += ex =>
    Debug.LogError($"Unobserved: {ex}");
```

- `WhenAll` — lanza la primera excepción, enruta las adicionales a `UnobservedException`
- `WhenAny` — la excepción del ganador se lanza; los fallos de los perdedores van a `UnobservedException`
- Cancelación de ciclo de vida — `OperationCanceledException` suprimida por defecto (configurable)

### Métodos de fábrica

```csharp
ValkarnTask.CompletedTask          // void, sin asignación
ValkarnTask.FromResult<T>(value)   // tipado, sin asignación
ValkarnTask.FromException(ex)      // con fallo
ValkarnTask.FromException<T>(ex)
ValkarnTask.FromCanceled()         // cancelado
ValkarnTask.FromCanceled<T>()
ValkarnTask.Never                  // nunca se completa (sentinel para WhenAny)
```
