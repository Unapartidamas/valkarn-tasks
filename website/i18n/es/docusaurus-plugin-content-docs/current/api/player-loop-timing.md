---
sidebar_position: 2
title: PlayerLoopTiming
---

# PlayerLoopTiming

```csharp
public enum PlayerLoopTiming : byte
```

Espacio de nombres: `UnaPartidaMas.Valkarn.Tasks`

Especifica qué fase del PlayerLoop de Unity usa una operación de ValkarnTasks para reanudarse después de la suspensión. Pasar un valor `PlayerLoopTiming` controla **cuándo en el fotograma** se ejecuta tu continuación de `await`, y **cuándo** se verifican los elementos recurrentes como retrasos y condiciones de espera.

Los valores del enum coinciden exactamente con el enum `PlayerLoopTiming` de UniTask, lo que simplifica la migración desde UniTask.

---

## Predeterminado

`PlayerLoopTiming.Update` (valor `8`) es el predeterminado para todas las operaciones. Si no pasas un argumento de timing, se usa `Update`.

---

## Valores

### Initialization

```
Valor: 0
Fase padre: UnityEngine.PlayerLoop.Initialization
Posición: primer subsistema en la fase
```

Se ejecuta al comienzo de la fase Initialization, antes de los propios subsistemas de inicialización de Unity en esa fase. Esto se dispara una vez por fotograma, antes de `EarlyUpdate`. Raramente es útil para el código de juego pero puede ser relevante para sistemas que deben leer o configurar el estado antes de que cualquier otra cosa en el fotograma lo toque.

---

### LastInitialization

```
Valor: 1
Fase padre: UnityEngine.PlayerLoop.Initialization
Posición: último subsistema en la fase
```

Se ejecuta al final de la fase Initialization, después de los subsistemas de inicialización integrados de Unity. Prefiere esto sobre `Initialization` si necesitas timing de fase de inicialización pero quieres dejar que los propios sistemas de Unity se ejecuten primero.

---

### EarlyUpdate

```
Valor: 2
Fase padre: UnityEngine.PlayerLoop.EarlyUpdate
Posición: primer subsistema en la fase
```

Se ejecuta al inicio de EarlyUpdate, antes de que Unity procese los eventos de entrada y antes del paso de simulación de física. Este es el punto más temprano del fotograma donde los datos de entrada del fotograma anterior están disponibles. Útil para sistemas que deben muestrear la entrada antes de que se ejecute cualquier código de juego.

---

### LastEarlyUpdate

```
Valor: 3
Fase padre: UnityEngine.PlayerLoop.EarlyUpdate
Posición: último subsistema en la fase
```

Se ejecuta al final de EarlyUpdate, después de que los propios subsistemas de actualización temprana de Unity se han completado.

---

### FixedUpdate

```
Valor: 4
Fase padre: UnityEngine.PlayerLoop.FixedUpdate
Posición: primer subsistema en la fase
```

Se ejecuta al inicio de cada paso de FixedUpdate. Esto corresponde al timing de `MonoBehaviour.FixedUpdate()`. Unity puede ejecutar cero o más pasos de FixedUpdate por fotograma renderizado dependiendo de cómo el paso de tiempo de física se alinea con el tiempo delta del fotograma.

Usa este timing para cualquier cosa que deba permanecer sincronizada con la simulación de física: leer velocidades de `Rigidbody`, aplicar fuerzas, o esperar un número de pasos determinado por la física.

```csharp
// Esperar 10 pasos de física
await ValkarnTask.DelayFrame(10, PlayerLoopTiming.FixedUpdate, ct);

// Esperar hasta que una condición de física sea verdadera, verificada en cada paso de física
await ValkarnTask.WaitUntil(() => rb.velocity.magnitude < 0.1f,
    PlayerLoopTiming.FixedUpdate, ct);
```

---

### LastFixedUpdate

```
Valor: 5
Fase padre: UnityEngine.PlayerLoop.FixedUpdate
Posición: último subsistema en la fase
```

Se ejecuta al final de la fase FixedUpdate, después de que los subsistemas de física de Unity (incluyendo `Physics.Simulate`) se han completado. Usa esto para leer los resultados de física después de que la simulación ha avanzado.

---

### PreUpdate

```
Valor: 6
Fase padre: UnityEngine.PlayerLoop.PreUpdate
Posición: primer subsistema en la fase
```

Se ejecuta al inicio de la fase PreUpdate, que se ejecuta después de FixedUpdate y antes de Update. Unity usa PreUpdate para tareas como actualizaciones de zonas de viento y procesamiento de eventos de red.

---

### LastPreUpdate

```
Valor: 7
Fase padre: UnityEngine.PlayerLoop.PreUpdate
Posición: último subsistema en la fase
```

Se ejecuta al final de PreUpdate.

---

### Update

```
Valor: 8  (predeterminado)
Fase padre: UnityEngine.PlayerLoop.Update
Posición: primer subsistema en la fase
```

El **timing predeterminado** para todas las operaciones de ValkarnTasks. Se ejecuta al inicio de la fase Update, que es donde se dispara `MonoBehaviour.Update()`. Esta es la elección correcta para la gran mayoría del código de juego.

```csharp
// Todos estos usan Update por defecto
await ValkarnTask.Yield();
await ValkarnTask.Delay(1000, ct);
await ValkarnTask.WaitUntil(() => _ready, ct: ct);
await ValkarnTask.NextFrame(ct: ct);
await ValkarnTask.DelayFrame(3, ct: ct);

// Explícito — idéntico a lo anterior
await ValkarnTask.Yield(PlayerLoopTiming.Update);
```

---

### LastUpdate

```
Valor: 9
Fase padre: UnityEngine.PlayerLoop.Update
Posición: último subsistema en la fase
```

Se ejecuta al final de la fase Update, después de todas las llamadas a `MonoBehaviour.Update()` y cualquier otro subsistema de Update que se haya completado. Usa esto cuando tu continuación debe observar los resultados de los métodos `Update()` de otros scripts — por ejemplo, haciendo polling de un valor que otro script escribe durante `Update`.

```csharp
// Reanudarse después de todas las llamadas a MonoBehaviour.Update() en este fotograma
await ValkarnTask.Yield(PlayerLoopTiming.LastUpdate);
```

---

### PreLateUpdate

```
Valor: 10
Fase padre: UnityEngine.PlayerLoop.PreLateUpdate
Posición: primer subsistema en la fase
```

Se ejecuta al inicio de la fase PreLateUpdate, que es donde se dispara `MonoBehaviour.LateUpdate()`. Úsalo para seguimiento de cámara, ajustes de transformación y cualquier cosa que deba reaccionar a las posiciones finales establecidas durante `Update`.

```csharp
// Reanudarse en el mismo punto que MonoBehaviour.LateUpdate()
await ValkarnTask.Yield(PlayerLoopTiming.PreLateUpdate);
```

---

### LastPreLateUpdate

```
Valor: 11
Fase padre: UnityEngine.PlayerLoop.PreLateUpdate
Posición: último subsistema en la fase
```

Se ejecuta al final de la fase PreLateUpdate, después de todas las llamadas a `MonoBehaviour.LateUpdate()`. Usa esto cuando necesitas leer valores que los scripts establecen durante su `LateUpdate`.

---

### PostLateUpdate

```
Valor: 12
Fase padre: UnityEngine.PlayerLoop.PostLateUpdate
Posición: primer subsistema en la fase
```

Se ejecuta al inicio de PostLateUpdate. Esta fase se dispara después de que el renderizado ha sido enviado para el fotograma. Unity la usa para tareas como presentar el búfer de fotograma y limpiar el estado de renderizado. También es donde se disparan los callbacks `Camera.onPostRender`.

Usa este timing para operaciones de fin de fotograma: captura de pantalla, descargas de nivel en streaming, o cualquier trabajo que deba ocurrir después de que la escena ha sido completamente renderizada pero antes de que comience el próximo fotograma.

---

### LastPostLateUpdate

```
Valor: 13
Fase padre: UnityEngine.PlayerLoop.PostLateUpdate
Posición: último subsistema en la fase
```

El último punto en el bucle de fotograma estándar antes de que Unity restablezca el estado local del fotograma y comience el siguiente fotograma. Este es el final absoluto de un fotograma para propósitos de timing.

---

### TimeUpdate

```
Valor: 14
Fase padre: UnityEngine.PlayerLoop.TimeUpdate
Posición: primer subsistema en la fase
```

Se ejecuta al inicio de la fase TimeUpdate. Unity usa esta fase para avanzar el estado relacionado con el tiempo (como `Time.time`). Este timing se dispara antes de `EarlyUpdate` en versiones más recientes de Unity.

Este timing raramente se necesita en el código de juego. Existe principalmente para sistemas que deben interceptar u observar el avance del tiempo antes de que cualquier otro sistema lea los nuevos valores de tiempo.

---

### LastTimeUpdate

```
Valor: 15
Fase padre: UnityEngine.PlayerLoop.TimeUpdate
Posición: último subsistema en la fase
```

Se ejecuta al final de la fase TimeUpdate, después de que los subsistemas relacionados con el tiempo de Unity se han completado.

---

## Tabla Resumen

| Valor | Nombre | Fase Unity | Posición | Callback MonoBehaviour comparable |
|---|---|---|---|---|
| 0 | `Initialization` | `Initialization` | Primera | — |
| 1 | `LastInitialization` | `Initialization` | Última | — |
| 2 | `EarlyUpdate` | `EarlyUpdate` | Primera | — |
| 3 | `LastEarlyUpdate` | `EarlyUpdate` | Última | — |
| 4 | `FixedUpdate` | `FixedUpdate` | Primera | `FixedUpdate()` |
| 5 | `LastFixedUpdate` | `FixedUpdate` | Última | después de `FixedUpdate()` |
| 6 | `PreUpdate` | `PreUpdate` | Primera | — |
| 7 | `LastPreUpdate` | `PreUpdate` | Última | — |
| **8** | **`Update`** (predeterminado) | **`Update`** | **Primera** | **`Update()`** |
| 9 | `LastUpdate` | `Update` | Última | después de todos los `Update()` |
| 10 | `PreLateUpdate` | `PreLateUpdate` | Primera | `LateUpdate()` |
| 11 | `LastPreLateUpdate` | `PreLateUpdate` | Última | después de todos los `LateUpdate()` |
| 12 | `PostLateUpdate` | `PostLateUpdate` | Primera | después del renderizado |
| 13 | `LastPostLateUpdate` | `PostLateUpdate` | Última | fin de fotograma |
| 14 | `TimeUpdate` | `TimeUpdate` | Primera | — |
| 15 | `LastTimeUpdate` | `TimeUpdate` | Última | — |

---

## Qué APIs Aceptan PlayerLoopTiming

Todas las APIs de ValkarnTasks basadas en tiempo y condiciones aceptan un parámetro `PlayerLoopTiming` opcional. El predeterminado es siempre `Update`.

| Método | Firma |
|---|---|
| `ValkarnTask.Yield` | `Yield(PlayerLoopTiming timing = Update)` |
| `ValkarnTask.NextFrame` | `NextFrame(PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.DelayFrame` | `DelayFrame(int frameCount, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.Delay(int)` | `Delay(int ms, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.Delay(TimeSpan)` | `Delay(TimeSpan delay, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.WaitUntil` | `WaitUntil(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.WaitWhile` | `WaitWhile(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |

---

## Ejemplos de Código

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using System.Threading;

// Ceder al siguiente tick de Update (predeterminado)
await ValkarnTask.Yield();

// Ceder al siguiente tick de FixedUpdate — usar dentro de código impulsado por física
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// Esperar 500 ms usando deltaTime escalado, verificado en cada Update
await ValkarnTask.Delay(500, ct: destroyCancellationToken);

// Esperar 500 ms usando deltaTime sin escalar — no afectado por Time.timeScale
await ValkarnTask.Delay(
    500,
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// Esperar 500 ms usando tiempo real de reloj de pared (basado en Stopwatch)
await ValkarnTask.Delay(
    500,
    DelayType.Realtime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// Esperar hasta que una bandera esté establecida, verificada en LateUpdate
bool _isReady;
await ValkarnTask.WaitUntil(() => _isReady, PlayerLoopTiming.PreLateUpdate, destroyCancellationToken);

// Esperar mientras carga, verificado al final absoluto del fotograma
await ValkarnTask.WaitWhile(() => _loading, PlayerLoopTiming.LastPostLateUpdate, destroyCancellationToken);

// Saltar exactamente 5 pasos de física
await ValkarnTask.DelayFrame(5, PlayerLoopTiming.FixedUpdate, destroyCancellationToken);

// Avanzar un fotograma completo renderizado
await ValkarnTask.NextFrame(PlayerLoopTiming.Update, destroyCancellationToken);
```
