---
sidebar_position: 1
title: Integración con el PlayerLoop de Unity
---

# Integración con el PlayerLoop de Unity

ValkarnTasks no usa hilos ni programadores en segundo plano para reanudar tus métodos `async`. Todo se ejecuta en el hilo principal de Unity, impulsado por el propio sistema PlayerLoop de Unity. Entender cómo funciona esto te ayudará a elegir el timing correcto para tu caso de uso y razonar exactamente cuándo tu código se reanuda después de un `await`.

## Qué es el PlayerLoop de Unity

El PlayerLoop de Unity es el bucle interno del motor que impulsa cada fotograma. No es una única llamada a `Update()` — es una secuencia jerárquica de fases que se ejecutan en un orden definido cada fotograma:

```
Initialization
EarlyUpdate
FixedUpdate      (repetido si la física avanza)
PreUpdate
Update
PreLateUpdate
PostLateUpdate
TimeUpdate
```

Cada una de estas fases de nivel superior contiene subsistemas que Unity (y paquetes de terceros) insertan para ejecutar su lógica en puntos específicos. `MonoBehaviour.Update()`, por ejemplo, se ejecuta dentro de la fase `Update`. `MonoBehaviour.LateUpdate()` se ejecuta dentro de `PreLateUpdate`.

Dado que ValkarnTasks se conecta directamente a este bucle, `await ValkarnTask.Yield()` no bloquea un hilo — registra un callback que Unity llama en el siguiente tick de la fase elegida, y luego retorna inmediatamente.

## Los 16 Timings del PlayerLoop

ValkarnTasks se inyecta en 16 puntos: uno al **inicio** y uno al **final** de cada una de las 8 fases del PlayerLoop de Unity. Las variantes `Last` se añaden al final de la lista de subsistemas de su fase padre; las variantes simples se anteponen al inicio.

| Valor | Entero enum | Fase padre | Posición en padre |
|---|---|---|---|
| `Initialization` | 0 | `UnityEngine.PlayerLoop.Initialization` | Primera |
| `LastInitialization` | 1 | `UnityEngine.PlayerLoop.Initialization` | Última |
| `EarlyUpdate` | 2 | `UnityEngine.PlayerLoop.EarlyUpdate` | Primera |
| `LastEarlyUpdate` | 3 | `UnityEngine.PlayerLoop.EarlyUpdate` | Última |
| `FixedUpdate` | 4 | `UnityEngine.PlayerLoop.FixedUpdate` | Primera |
| `LastFixedUpdate` | 5 | `UnityEngine.PlayerLoop.FixedUpdate` | Última |
| `PreUpdate` | 6 | `UnityEngine.PlayerLoop.PreUpdate` | Primera |
| `LastPreUpdate` | 7 | `UnityEngine.PlayerLoop.PreUpdate` | Última |
| `Update` | 8 | `UnityEngine.PlayerLoop.Update` | Primera |
| `LastUpdate` | 9 | `UnityEngine.PlayerLoop.Update` | Última |
| `PreLateUpdate` | 10 | `UnityEngine.PlayerLoop.PreLateUpdate` | Primera |
| `LastPreLateUpdate` | 11 | `UnityEngine.PlayerLoop.PreLateUpdate` | Última |
| `PostLateUpdate` | 12 | `UnityEngine.PlayerLoop.PostLateUpdate` | Primera |
| `LastPostLateUpdate` | 13 | `UnityEngine.PlayerLoop.PostLateUpdate` | Última |
| `TimeUpdate` | 14 | `UnityEngine.PlayerLoop.TimeUpdate` | Primera |
| `LastTimeUpdate` | 15 | `UnityEngine.PlayerLoop.TimeUpdate` | Última |

`Update` (valor 8) es el **predeterminado** para todas las operaciones de ValkarnTasks — `Yield()`, `Delay()`, `WaitUntil()`, `WaitWhile()`, `NextFrame()` y `DelayFrame()`.

## Structs marcadores e inyección idempotente

Para inyectar en el PlayerLoop de Unity, ValkarnTasks crea un `PlayerLoopSystem` por timing. Cada sistema está identificado por un struct marcador único definido dentro de `PlayerLoopHelper`:

```csharp
struct ValkarnTaskInitialization     { }
struct ValkarnTaskLastInitialization { }
struct ValkarnTaskEarlyUpdate        { }
struct ValkarnTaskLastEarlyUpdate    { }
struct ValkarnTaskFixedUpdate        { }
struct ValkarnTaskLastFixedUpdate    { }
struct ValkarnTaskPreUpdate          { }
struct ValkarnTaskLastPreUpdate      { }
struct ValkarnTaskUpdate             { }
struct ValkarnTaskLastUpdate         { }
struct ValkarnTaskPreLateUpdate      { }
struct ValkarnTaskLastPreLateUpdate  { }
struct ValkarnTaskPostLateUpdate     { }
struct ValkarnTaskLastPostLateUpdate { }
struct ValkarnTaskTimeUpdate         { }
struct ValkarnTaskLastTimeUpdate     { }
```

Antes de inyectar, `PlayerLoopHelper` verifica si `ValkarnTaskUpdate` ya aparece en algún lugar del árbol PlayerLoop actual. Si lo hace, la inyección se omite. Esto hace que la inyección sea **idempotente** — llamar a `Init()` múltiples veces (lo que puede ocurrir en el editor) nunca resulta en que se registren sistemas duplicados.

La inyección siempre lee el PlayerLoop **actual** con `PlayerLoop.GetCurrentPlayerLoop()`, nunca `GetDefaultPlayerLoop()`. Esto significa que cualquier sistema instalado previamente por otros paquetes se preserva.

## ContinuationQueue — Callbacks de una sola vez

Cuando haces `await ValkarnTask.Yield()` (o cualquier cosa que se suspenda por exactamente un tick), la máquina de estados generada por el compilador llama a `OnCompleted` en el awaiter. El awaiter llama a `PlayerLoopHelper.AddContinuation(timing, action, state)`, que encola el callback en la `ContinuationQueue` para ese timing.

`ContinuationQueue` usa un diseño de **doble búfer**:

1. **Búfer activo** (`actionList`): contiene las continuaciones encoladas antes y durante el drenaje actual.
2. **Búfer de espera** (`waitingList`): captura las continuaciones encoladas _mientras_ se drena el búfer activo (es decir, encolas re-entrantes desde dentro de una continuación en sí).
3. **Pila Treiber entre hilos** (`crossThreadHead`): las continuaciones publicadas desde hilos en segundo plano aterrizan aquí a través de una comparación e intercambio sin bloqueo. Al inicio de cada drenaje, toda la pila se reclama atómicamente y se mueve al búfer activo.

Cada tick del PlayerLoop, `ContinuationQueue.Run()` ejecuta esta secuencia:

```
1. Drenar crossThreadHead → actionList     (toma atómica sin bloqueo, luego copiar bajo SpinLock)
2. Tomar snapshot del conteo, establecer isDraining = true  (bajo SpinLock)
3. Ejecutar todos los callbacks                  (fuera del bloqueo, refs borradas para GC)
4. Intercambiar actionList ↔ waitingList          (bajo SpinLock, establecer isDraining = false)
```

Después del calentamiento de aproximadamente uno o dos fotogramas, los arrays internos se estabilizan en su marca de agua alta y la cola funciona con **cero asignaciones**.

Los nodos entre hilos se agrupan en un grupo sin bloqueo acotado (máx. 1.024 nodos) para evitar asignar en cada encole de hilo en segundo plano.

## PlayerLoopRunner — Elementos recurrentes

Algunas operaciones deben verificarse en cada tick hasta que se completen: `Delay()`, `DelayFrame()`, `WaitUntil()`, `WaitWhile()` y `NextFrame()`. Estos implementan la interfaz `IPlayerLoopItem`:

```csharp
internal interface IPlayerLoopItem
{
    // Devolver true para seguir ejecutándose; devolver false para eliminar del runner.
    bool MoveNext();
}
```

Los elementos se añaden a `PlayerLoopRunner` vía `PlayerLoopHelper.AddAction(timing, item)`. Cada tick, `PlayerLoopRunner.Run()` itera todos los elementos registrados y llama a `MoveNext()` en cada uno. Los elementos que devuelven `false` se eliminan mediante **compactación en sitio** — el array se compacta en un único pase, preservando el orden de inserción. Los elementos añadidos durante una llamada a `Run()` se añaden de forma segura y serán procesados en el siguiente tick.

```
Tick N:
  ContinuationQueue.Run()   → reanudar todos los awaiters de una sola vez
  PlayerLoopRunner.Run()    → avanzar todos los elementos recurrentes (Delay, WaitUntil, ...)
```

Ambos se ejecutan para cada uno de los 16 timings, en el orden en que el motor los llama.

El timing `Update` también rastrea el conteo global de fotogramas y activa periódicamente el recorte del grupo de objetos (cada `ValkarnTask.TrimCheckInterval` fotogramas).

## Inicialización — RuntimeInitializeOnLoadMethod

ValkarnTasks se inicializa en `RuntimeInitializeLoadType.SubsystemRegistration`, el hook más temprano que Unity proporciona, que se dispara antes de `Awake()` en cualquier objeto de escena:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void Init()
{
    // 1. Capturar el ID del hilo principal para verificaciones de seguridad de hilos
    // 2. Crear ContinuationQueue y PlayerLoopRunner para los 16 timings
    // 3. Inyectar en el PlayerLoop (idempotente)
    // 4. Registrar callback de cambio de estado de modo de juego (solo editor)
}
```

El ID del hilo principal se captura en este punto y se comparte con todos los subsistemas de grupo y cola para que puedan distinguir los encoles del hilo principal (camino SpinLock) de los encoles entre hilos (camino de pila Treiber).

## Manejo de recarga de dominio (Editor)

En el Editor de Unity, entrar o salir del modo de juego activa una recarga de dominio. El estado estático de la sesión de juego anterior persistiría de lo contrario y causaría referencias obsoletas.

ValkarnTasks maneja esto con un callback `EditorApplication.playModeStateChanged` registrado durante `Init()`. Cuando el estado transiciona a `ExitingPlayMode` o `EnteredEditMode`, se ejecuta la siguiente limpieza:

```csharp
// Reiniciar todas las colas y runners
for (int i = 0; i < 16; i++)
{
    s_continuationQueues[i] = null;
    s_playerLoopRunners[i] = null;
}

// Reiniciar otros subsistemas
ValkarnTask.ResetStatics();
TimeProvider.ResetToDefault();   // revierte a UnityTimeProvider
PoolRegistry.Clear();
ContinuationQueue.ContinuationNode.ResetPool();
```

Cuando el modo de juego se vuelve a entrar, `Init()` se dispara vía `RuntimeInitializeOnLoadMethod` y se asignan nuevas colas y runners. La verificación de inyección del PlayerLoop (`HasValkarnTaskSystems`) evita que los sistemas marcadores se inserten una segunda vez si el dominio no se descargó completamente.

## Elegir el timing correcto

La mayoría del código debería usar el timing `Update` predeterminado. Solo recurre a otros cuando tienes una razón específica.

| Timing | Cuándo usarlo |
|---|---|
| `Initialization` / `LastInitialization` | Configuración muy temprana del fotograma; raramente necesario en el código de juego |
| `EarlyUpdate` / `LastEarlyUpdate` | Muestreo de entrada; se ejecuta antes de la física y antes de `Update` |
| `FixedUpdate` / `LastFixedUpdate` | Lógica sincronizada con la física; coincide con la cadencia de `MonoBehaviour.FixedUpdate` |
| `PreUpdate` / `LastPreUpdate` | Antes del despacho de actualización principal de Unity; útil para el pre-pase del programador personalizado |
| **`Update`** (predeterminado) | **Lógica de juego estándar; coincide con `MonoBehaviour.Update`** |
| `LastUpdate` | Lógica que debe ejecutarse después de todas las llamadas a `MonoBehaviour.Update` |
| `PreLateUpdate` / `LastPreLateUpdate` | Coincide con `MonoBehaviour.LateUpdate`; seguimiento de cámara y transformación |
| `PostLateUpdate` / `LastPostLateUpdate` | Después del envío de renderizado; finalización de UI, captura de pantalla |
| `TimeUpdate` / `LastTimeUpdate` | Sincronización de valores de tiempo; raramente necesario |

```csharp
// Reanudar en el siguiente tick de Update (predeterminado)
await ValkarnTask.Yield();

// Reanudar al inicio del próximo FixedUpdate (seguro para física)
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// Esperar 2 segundos, avanzando con tiempo sin escala, verificado en LateUpdate
await ValkarnTask.Delay(
    TimeSpan.FromSeconds(2),
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.PreLateUpdate);

// Esperar hasta que una condición sea verdadera, verificada después de todas las llamadas a LateUpdate
await ValkarnTask.WaitUntil(() => _ready, PlayerLoopTiming.LastPreLateUpdate);
```
