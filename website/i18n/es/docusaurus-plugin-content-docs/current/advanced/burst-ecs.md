---
sidebar_position: 1
title: Integración con Burst y ECS
---

# Integración con Burst y ECS

Valkarn Tasks incluye integración opcional con el compilador Burst de Unity, Unity Collections y el paquete Entities (ECS). Toda esta funcionalidad se compila condicionalmente — solo está activa cuando los paquetes requeridos están presentes y los símbolos de definición de scripts correspondientes están configurados.

## Requisitos

| Característica | Paquete requerido | Definición de scripts |
|---|---|---|
| `NativeTimerHeap`, `NativeScheduler`, `BurstSchedulerRunner` | Unity Burst 1.8+, Unity Collections 2.0+ | `VTASKS_HAS_BURST` y `VTASKS_HAS_COLLECTIONS` |
| `AsyncSystemUtilities` | Unity Entities 1.0+ | `VTASKS_HAS_ENTITIES` |

Todos los archivos de código fuente de Burst/ECS están envueltos en guardas `#if` que coinciden con estas definiciones. Nada en estos archivos se compila o enlaza a menos que las definiciones estén presentes.

### Configuración

1. Instala los paquetes requeridos vía el Unity Package Manager:
   - `com.unity.burst` 1.8 o posterior
   - `com.unity.collections` 2.0 o posterior
   - `com.unity.entities` 1.0 o posterior (solo para utilidades ECS)

2. Añade los símbolos de definición de scripts en **Project Settings > Player > Scripting Define Symbols**:
   ```
   VTASKS_HAS_BURST;VTASKS_HAS_COLLECTIONS;VTASKS_HAS_ENTITIES
   ```
   Solo necesitas añadir las definiciones para los paquetes que hayas instalado.

---

## NativeTimerHeap

**Espacio de nombres:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Guarda:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeTimerHeap` es un montón mínimo binario compatible con Burst para programar temporizadores. Almacena valores `TimerEntry` ordenados por fecha límite, dando inserción O(log n) y eliminación O(log n) por temporizador expirado.

### Tipos clave

```csharp
// Entrada almacenada en el montón. Ordenada por Deadline.
public struct TimerEntry : IComparable<TimerEntry>
{
    public long Deadline;
    public int  Id;
}

public struct NativeTimerHeap : IDisposable
{
    public bool IsCreated { get; }
    public int  Count     { get; }

    // Crea el montón. Usa Allocator.Persistent para montones de larga duración.
    public NativeTimerHeap(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // Inserta un nuevo temporizador. Devuelve el ID del temporizador (usado para identificar el callback).
    // La fecha límite y el valor pasado a DrainExpired deben usar la misma unidad
    // (BurstSchedulerRunner usa ticks de DateTime vía Time.realtimeSinceStartupAsDouble).
    [BurstCompile]
    public int Schedule(long deadline);

    // Elimina y añade los IDs de todos los temporizadores cuyo Deadline <= currentTimestamp.
    // Devuelve el número de temporizadores drenados.
    [BurstCompile]
    public int DrainExpired(long currentTimestamp, NativeList<int> expiredIds);

    public void Dispose();
}
```

`NativeTimerHeap` es un struct no gestionado. No puede almacenar delegados gestionados — los IDs se corresponden con callbacks gestionados en el diccionario de `BurstSchedulerRunner` en el hilo principal.

---

## NativeScheduler

**Espacio de nombres:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Guarda:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeScheduler` es una cola de trabajo compatible con Burst respaldada por `NativeQueue<ScheduledWork>`. Los trabajos compilados con Burst encolan elementos de trabajo; el hilo principal los drena cada fotograma.

### Tipos clave

```csharp
public enum WorkType : byte
{
    TimerExpired  = 0,
    JobCompleted  = 1,
    Custom        = 2
}

public struct ScheduledWork
{
    public int      Id;
    public WorkType Type;
    public long     Payload;
}

public struct NativeScheduler : IDisposable
{
    public bool IsCreated { get; }

    public NativeScheduler(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // Encola un elemento de trabajo desde un trabajo compilado con Burst.
    [BurstCompile]
    public void Enqueue(ScheduledWork work);

    // Drena todo el trabajo pendiente en `results`. Llamar solo desde el hilo principal.
    // Devuelve el número de elementos drenados.
    public int Drain(NativeList<ScheduledWork> results);

    // Devuelve un escritor paralelo adecuado para usar en trabajos IJobParallelFor.
    public NativeQueue<ScheduledWork>.ParallelWriter AsParallelWriter();

    public void Dispose();
}
```

La cola es el punto de cruce entre el mundo Burst y el mundo gestionado. `Enqueue` es llamable desde Burst; `Drain` es solo para el hilo principal.

---

## BurstSchedulerRunner

**Espacio de nombres:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Guarda:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`BurstSchedulerRunner` es el puente gestionado entre el programador nativo/montón de temporizadores y el resto de tu juego. Implementa `IPlayerLoopItem`, por lo que Unity llama a `MoveNext()` una vez por fotograma en el timing registrado. Cada fotograma:

1. Drena `NativeScheduler` y dispara cualquier callback gestionado registrado que coincida con el ID de trabajo.
2. Drena `NativeTimerHeap` por temporizadores expirados y dispara sus callbacks gestionados.

Las excepciones lanzadas por los callbacks se reenvían a `VlkTask.PublishUnobservedException` en lugar de propagarse hacia arriba a través del PlayerLoop.

### API

```csharp
public sealed class BurstSchedulerRunner : IPlayerLoopItem, IDisposable
{
    // Crea un runner y lo registra en el PlayerLoop. Devuelve la instancia.
    // Desecha el runner devuelto cuando ya no se necesite.
    public static BurstSchedulerRunner Create(
        PlayerLoopTiming timing       = PlayerLoopTiming.Update,
        int initialCapacity           = 64);

    // Acceso directo al NativeScheduler para encolar desde trabajos Burst.
    public NativeScheduler Scheduler { get; }

    // Programa un callback de temporizador gestionado. Debe llamarse desde el hilo principal.
    // Devuelve un ID de temporizador (solo para identificación; no hay API de cancelación).
    public int ScheduleTimer(TimeSpan delay, Action callback);

    // Asocia un callback gestionado con un ID de trabajo encolado por un trabajo Burst.
    // Debe llamarse desde el hilo principal, antes o durante el fotograma en que el trabajo se completa.
    public void RegisterCallback(int workId, Action callback);

    // Desecha todos los contenedores nativos y cancela el registro en el PlayerLoop.
    public void Dispose();
}
```

### Cómo BurstSchedulerRunner difiere del programador predeterminado

El programador predeterminado de Valkarn Tasks se integra directamente con las máquinas de estados async/await y gestiona el despacho de continuaciones vía `PlayerLoopHelper`. `BurstSchedulerRunner` añade un carril separado específicamente para señalización desde trabajos compilados con Burst:

| | Programador predeterminado | BurstSchedulerRunner |
|---|---|---|
| Tipo de continuación | `Action` gestionada vía `ISource` | `Action` gestionada registrada por ID |
| Fuente de señal | Patrón de awaiter de C# | `NativeScheduler.Enqueue` no gestionado |
| Fuente de temporizador | `VlkTask.Delay` (gestionado) | `NativeTimerHeap` (no gestionado) |
| Seguridad de hilos | Continuaciones del hilo principal | Enqueue es seguro para Burst; drain es solo del hilo principal |

### Patrón de uso

```csharp
// 1. Crear el runner una vez (por ejemplo, en un MonoBehaviour bootstrap o ISystem.OnCreate).
var runner = BurstSchedulerRunner.Create(PlayerLoopTiming.Update, initialCapacity: 128);

// 2. Programar un callback de temporizador (hilo principal).
runner.ScheduleTimer(TimeSpan.FromSeconds(2.0), () =>
{
    Debug.Log("Dos segundos transcurridos (temporizador no gestionado).");
});

// 3. Desde un trabajo Burst, encolar un elemento de trabajo.
//    NativeScheduler.ParallelWriter es seguro para usar desde IJobParallelFor.
var writer = runner.Scheduler.AsParallelWriter();
// Dentro de Execute(int index):
writer.Enqueue(new ScheduledWork { Id = myWorkId, Type = WorkType.Custom });

// 4. Registrar el callback gestionado en el hilo principal antes de que el trabajo se complete.
runner.RegisterCallback(myWorkId, () =>
{
    Debug.Log($"Señal de trabajo completado recibida para trabajo {myWorkId}.");
});

// 5. Desechar el runner cuando termine (por ejemplo, OnDestroy o recarga de dominio).
runner.Dispose();
```

---

## AsyncSystemUtilities

**Espacio de nombres:** `UnaPartidaMas.Valkarn.Tasks.ECS`
**Guarda:** `#if VTASKS_HAS_ENTITIES`

`AsyncSystemUtilities` proporciona dos ayudantes de extensión para escribir sistemas ECS asíncronos.

### GetWorldCancellationToken

```csharp
public static CancellationToken GetWorldCancellationToken(
    this World world,
    PlayerLoopTiming timing = PlayerLoopTiming.Update)
```

Devuelve un `CancellationToken` que se cancela automáticamente cuando se destruye el `World` dado. Internamente inicia una `VlkTask` fire-and-forget que llama a `VlkTask.Yield(timing)` cada fotograma mientras `world.IsCreated` es verdadero, luego cancela un `CancellationTokenSource` cuando el bucle sale.

Si el world ya está destruido cuando llamas a este método, devuelve un token que ya está en el estado cancelado.

Pasa este token a cada método async que lances desde un sistema para que el trabajo en vuelo se detenga automáticamente cuando el world desaparezca.

### SafeEntityExists

```csharp
public static bool SafeEntityExists(this EntityManager entityManager, Entity entity)
```

Llama a `entityManager.Exists(entity)` y devuelve `false` si se lanza una `ObjectDisposedException`. Esto puede ocurrir cuando se accede al `EntityManager` después de que el world ha sido desechado, lo cual es una condición de carrera real en código async que sobrevive a los límites de fotograma.

Usa esto después de cada punto `await` antes de escribir de vuelta a una entidad.

---

## Ejemplo funcional: Sistema ECS asíncrono

El siguiente ejemplo es de `Samples~/ECS/AsyncLoadSystem.cs`. Demuestra el patrón canónico para la inicialización asíncrona de un solo disparo desde un `ISystem`.

```csharp
#if VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Entities;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.ECS;

public partial struct AsyncLoadSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // Obtener un token de cancelación vinculado al tiempo de vida de este World.
        // Si el World se destruye, todo el trabajo asíncrono lanzado con este token
        // se cancelará automáticamente.
        var worldCt = state.World.GetWorldCancellationToken();

        // Lanzar la inicialización asíncrona y olvidar la tarea.
        // Forget() enruta cualquier excepción no manejada a VlkTask.PublishUnobservedException.
        InitializeAsync(state.WorldUnmanaged, worldCt).Forget();
    }

    public void OnUpdate(ref SystemState state) { }
    public void OnDestroy(ref SystemState state) { }

    static async VlkTask InitializeAsync(WorldUnmanaged world, CancellationToken ct)
    {
        // Fase 1: Cargar datos en un hilo en segundo plano.
        // RunOnThreadPool cambia a un hilo de trabajo, ejecuta el delegado,
        // y vuelve al hilo principal automáticamente.
        var configData = await VlkTask.RunOnThreadPool(
            () => LoadFromDisk(),
            cancellationToken: ct);

        // Fase 2: Aplicar resultados en el hilo principal.
        // Verificar cancelación en caso de que el World fuera destruido durante la carga.
        ct.ThrowIfCancellationRequested();
        ApplyConfiguration(world, configData);
    }

    static ConfigData LoadFromDisk()
    {
        // Solo C# puro — no se permiten llamadas a Unity o ECS API aquí.
        return new ConfigData { MaxEnemies = 100, SpawnRadius = 50f };
    }

    static void ApplyConfiguration(WorldUnmanaged world, ConfigData data)
    {
        // Seguro: estamos en el hilo principal.
        UnityEngine.Debug.Log($"Config cargada: MaxEnemies={data.MaxEnemies}");
    }

    struct ConfigData
    {
        public int   MaxEnemies;
        public float SpawnRadius;
    }
}
#endif
```

El ejemplo de throttling de IA (`Samples~/ECS/AISystemExample.cs`) se basa en este patrón y añade `AsyncThrottle` para limitar cuántas tareas async concurrentes están en vuelo. Consulta la documentación de Throttling para detalles sobre ese patrón.

---

## Limitaciones

Las siguientes restricciones se aplican a todo el código async de Burst/ECS. Violarlas producirá errores del editor, violaciones de seguridad de trabajos o corrupción silenciosa de datos.

### Dentro del código compilado con Burst

- **Sin tipos gestionados.** Burst no puede compilar código que asigna, accede o hace referencia a objetos gestionados (clases, delegados, arrays, cadenas, `List<T>`, etc.). Solo se permiten structs blittables y contenedores nativos.
- **Sin excepciones.** Burst no admite `try`/`catch`/`throw`. Usa códigos de retorno o banderas para comunicar errores.
- **Sin `async`/`await`.** Las máquinas de estados async de C# son gestionadas y no pueden ser compiladas por Burst. El `NativeScheduler` y `NativeTimerHeap` proporcionan un canal lateral para señalizar continuaciones gestionadas, pero las continuaciones en sí se ejecutan en el hilo principal.
- **Sin estado gestionado mutable estático.** Los trabajos Burst pueden leer campos estáticos de solo lectura pero no deben escribir en estáticos gestionados.

### A través de puntos await en sistemas ECS

- **Tiempo de vida de entidades.** Las entidades pueden ser destruidas mientras un método async está suspendido. Siempre llama a `entityManager.SafeEntityExists(entity)` después de cada `await` antes de escribir de vuelta.
- **Obsolescencia de ComponentLookup.** `ComponentLookup`, `RefRW` y otros tipos de puntero de chunk se invalidan después de cambios estructurales, que pueden ocurrir en cualquier fotograma. No los almacenes en caché a través de puntos `await`. Vuelve a adquirirlos de `SystemState` después de reanudarte, o usa `EntityManager` directamente.
- **Parámetros `ref`.** Los métodos async no pueden tener parámetros `ref`, `in` o `out` (error de C# CS1988). Extrae todos los datos de ECS sincrónicamente en el método `OnUpdate` síncrono y pásalos al método async por valor.
- **`SystemAPI` en métodos async.** `SystemAPI` es generado por código fuente y solo funciona dentro de métodos `ISystem` parciales. No está disponible en métodos `async`. Realiza todas las consultas de `SystemAPI` antes del primer `await`.
- **Seguridad de hilos.** `EntityManager`, `ComponentLookup` y los cambios estructurales son solo del hilo principal. Usa `VlkTask.RunOnThreadPool` solo para cómputo puro de C# sin llamadas a APIs de Unity o ECS.
