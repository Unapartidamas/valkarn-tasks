---
sidebar_position: 6
title: Por qué Valkarn Tasks
---

# Por qué Valkarn Tasks

> Async/await sin asignaciones para Unity. Más rápido que UniTask. Más inteligente que Awaitable.

---

## El problema: async en Unity está roto

Los desarrolladores de Unity necesitan operaciones asíncronas en todas partes: cargar escenas, descargar assets, esperar animaciones, retrasar spawns, comunicarse con servidores. Hoy, las opciones son:

| Opción | Problema |
|--------|---------|
| **Coroutines** | Sin valores de retorno, sin manejo de errores, sin cancelación, sin composición |
| **System.Threading.Tasks** | Asigna en cada llamada `async` (~144–232 bytes), activa el GC, sin conciencia del ciclo de vida de Unity |
| **UniTask** | Buena opción — pero: colisión de tokens tras 18 minutos, sin diagnósticos en tiempo de compilación, reporte de errores dependiente de finalizadores |
| **Unity Awaitable** (2023+) | Basada en clases (asigna), sin combinadores, sin canales, sin soporte de pruebas |

Valkarn Tasks resuelve todos estos problemas en un único paquete generado por código fuente y sin asignaciones.

---

## Sin asignaciones — por qué cada byte importa

### Qué significa asignar memoria en juegos

Cada `new object()`, `new List<T>()` o llamada `async Task` asigna en el **heap administrado**, rastreado por el Recolector de Basura. Unity usa el GC Boehm, que tiene dos problemas críticos:

1. **Pausas stop-the-world** — cuando el GC se ejecuta, el juego se congela. Una pausa de 2 ms a 60 fps consume el 12% del presupuesto de frame; a 120 fps (VR) consume el 24%.
2. **Temporización impredecible** — el GC puede activarse durante un combate de jefe, una cinemática o una partida competitiva.

### Qué hace diferente a Valkarn Tasks

| Escenario | `System.Task` | UniTask | **Valkarn Tasks** |
|----------|--------------|---------|------------------|
| `async Method()` — completa síncronamente | 144 bytes | **0 bytes** | **0 bytes** |
| `async Method()` — suspende una vez | 232+ bytes | 0 bytes (pooled) | **0 bytes (pooled)** |
| `WhenAll(a, b)` | 232 bytes | 0 bytes (pooled) | **0 bytes (source-gen pooled)** |
| `WhenAny(a, b)` | 144 bytes | 72 bytes | **0 bytes** |
| Promise (completado manual) | 144 bytes | 104 bytes | **88 bytes** |
| Promise en pool (reutilizable) | 144 bytes | 0 bytes | **0 bytes** |

En un frame de juego típico con 50–100 operaciones async, `System.Task` genera 7–23 KB de basura. Valkarn Tasks genera **cero**.

### Qué significa esto para tu juego

- **Sin stuttering por el GC** — framerate estable sin interrupciones causadas por operaciones async
- **Seguro para VR** — objetivos de 90/120 fps sin picos del GC
- **Optimizado para móviles** — menor presión de memoria en dispositivos con RAM limitada
- **Certificado para consolas** — el comportamiento de memoria predecible ayuda a superar los requisitos de certificación

---

## Rendimiento — comparado con los mejores

Benchmarks: BenchmarkDotNet v0.14.0, .NET 9.0, Intel Core i7-10875H.

### Async/await básico — 2× más rápido que ValueTask

| Benchmark | ValueTask | UniTask | **Valkarn Tasks** | vs ValueTask |
|-----------|-----------|---------|------------------|--------------|
| 100 tareas en bloque | 956 ns | 508 ns | **489 ns** | **1.95×** |
| 1.000 tareas en bloque | 9.697 ns | 5.016 ns | **4.728 ns** | **2.05×** |
| Con CancellationToken | 38,8 ns | 36,2 ns | **29,6 ns** | **1.31×** |
| Manejo de excepciones | 10.399 ns | 8.247 ns | **9.248 ns** | **1.12×** |

Todos los caminos: **0 bytes asignados**.

### Combinadores — hasta 9,6× más rápido que Task

| Benchmark | Task | UniTask | **Valkarn Tasks** | vs Task |
|-----------|------|---------|------------------|---------|
| WhenAll (2 tareas) | 117 ns / 232B | 13,6 ns / 0B | **12,1 ns / 0B** | **9.6×** |
| WhenAll (5 tareas) | 156 ns / 272B | 25,1 ns / 0B | **25,3 ns / 0B** | **6.2×** |
| WhenAny (2 tareas) | 39,0 ns / 144B | 60,0 ns / 72B | **11,6 ns / 0B** | **3.4×** |
| Promise en pool | 59,5 ns / 144B | 53,6 ns / 0B | **38,3 ns / 0B** | **1.55×** |

WhenAny es **5,2× más rápido que UniTask** y no asigna ningún byte.

### Pool de objetos — 4,3 nanosegundos

| Operación | Tiempo | Asignación |
|-----------|------|------------|
| Alquiler + devolución en slot rápido del hilo principal | **4,3 ns** | 0 bytes |
| Pila Treiber entre hilos | ~15 ns | 0 bytes |

Sin operaciones atómicas en el hilo principal — crítico para IL2CPP, donde `Volatile.Read` es 9,2× más lento.

### Impacto en el mundo real (50 operaciones async/frame a 60 fps)

| Biblioteca | Tiempo/frame | Presupuesto de frame | GC/segundo |
|---------|-----------|-------------|-----------|
| System.Task | ~48 µs | 0,29% | ~430 KB/s |
| UniTask | ~25 µs | 0,15% | ~3,6 KB/s |
| **Valkarn Tasks** | **~24 µs** | **0,14%** | **0 KB/s** |

En 10 minutos, `System.Task` genera **~258 MB** de basura async. Valkarn Tasks genera **cero**.

---

## Características que ninguna otra biblioteca tiene

### Cancelación automática de ciclo de vida

```csharp
public partial class EnemySpawner : MonoBehaviour
{
    async ValkarnTask Start()
    {
        while (true)
        {
            await ValkarnTask.Delay(3000);
            SpawnWave();
        }
        // Cancelado automáticamente cuando este GameObject se destruye.
        // Sin CancellationToken. Sin fugas de memoria. Sin tareas zombie.
    }
}
```

No más `MissingReferenceException` por métodos async que se ejecutan después de la destrucción. Sin limpieza manual en `OnDestroy`. Sin `CancellationTokenSource` olvidados.

### Secciones críticas

```csharp
async ValkarnTask SaveProgress()
{
    var data = await LoadPlayerData();       // cancelable

    await using (ValkarnTask.Critical())
    {
        await db.Insert(data);              // se completa aunque el GO sea destruido
        await db.Commit();
    }                                       // la cancelación pendiente se aplica ahora

    await SendNotification();               // cancelable de nuevo
}
```

Las escrituras en base de datos, peticiones de red y guardados de archivos se completan aunque el jugador salga o una escena se descargue. Sin guardados corruptos. Sin analíticas a medias. Sin recibos perdidos.

### Diagnósticos en tiempo de compilación

| Diagnóstico | Qué detecta |
|------------|----------------|
| TT001 | Doble await sobre un ValkarnTask (bug de uso tras liberación) |
| TT002 | Olvidar awaitar una tarea (fallo silencioso) |
| TT012 | Bucles async sin comprobación de cancelación (bucles zombie) |
| TT013 | Tarea devuelta pero no esperada (bug de fire-and-forget) |
| TT016 | Método async sin await (sobrecarga innecesaria) |
| TT017 | `[FireAndForget]` en `ValkarnTask<T>` (descarta un resultado) |

Errores detectados en el IDE como líneas rojas — no como crashes en tiempo de ejecución 20 minutos después de empezar las pruebas.

### Result\<T\> — manejo de errores sin try/catch

```csharp
var result = await loadTask.AsResult();

if (result.Succeeded)       Use(result.Value);
else if (result.IsCanceled) ShowRetryDialog();
else                         Debug.LogError(result.Error);
```

Cada camino de error es explícito. Sin excepciones silenciadas. Sin manejadores omitidos.

### Canales con contrapresión

```csharp
var channel = ValkarnTask.Channel.CreateBounded<EnemySpawnRequest>(capacity: 10);

// Productor (lógica de juego)
await channel.Writer.WriteAsync(new EnemySpawnRequest { type = EnemyType.Dragon });

// Consumidor (sistema de spawn)
await foreach (var request in channel.Reader.ReadAllAsync())
    await SpawnEnemy(request);
```

Desacopla sistemas de forma limpia. Limita la tasa de spawning. Encola mensajes de red. Almacena eventos de entrada en buffer. El productor se ralentiza cuando el consumidor no puede seguir el ritmo — evitando picos de memoria.

### Pruebas deterministas con TestClock

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

Prueba la lógica dependiente del tiempo al instante. Sin `yield return new WaitForSeconds(3)` en pruebas. Sin temporización de CI inestable.

### Seguridad de tokens generacionales

UniTask usa un token `short` (16 bits). Tras 65.536 ciclos de pool (~18 minutos de trabajo async activo), una referencia obsoleta lee silenciosamente el resultado de otra tarea — un bug de uso tras liberación prácticamente imposible de reproducir.

Valkarn Tasks usa un contador de generación `uint` por slot de pool: **4.294.967.296 ciclos por slot** antes de colisión. Imposible en cualquier escenario realista.

---

## Migración en minutos, no semanas

### Desde UniTask

```
Paso 1: Instala Valkarn Tasks
Paso 2: Aparecen bombillas amarillas en los usos de UniTask en el IDE
Paso 3: Clic derecho → "Corregir todas las ocurrencias en la solución" (Ctrl+.)
Paso 4: Elimina la referencia al paquete UniTask
```

**15 diagnósticos de migración** (MIG001–MIG015) cubren automáticamente toda la API de UniTask. Un proyecto típico con 500–2.000 métodos async migra en menos de 5 minutos. 95%+ completamente automatizado.

### Desde Unity Awaitable

La misma migración con un clic:
- `async Awaitable` → `async ValkarnTask`
- `Awaitable.NextFrameAsync()` → `ValkarnTask.NextFrame()`
- `Awaitable.BackgroundThreadAsync()` → `ValkarnTask.SwitchToThreadPool()`
- `Awaitable.MainThreadAsync()` → eliminado (Valkarn se ejecuta en el hilo principal por defecto)

---

## Matriz de comparación

| Característica | System.Task | UniTask | Awaitable | **Valkarn Tasks** |
|---------|------------|---------|-----------|------------------|
| Ruta síncrona sin asignación | No | Sí | No | **Sí** |
| Combinadores sin asignación | No | No | No | **Sí (source gen)** |
| Basado en struct | No | Sí | No | **Sí** |
| Cancelación automática de ciclo de vida | No | Manual | Parcial | **Automático** |
| Sin cancelación de hermanos | No | No | No | **Sí** |
| Secciones críticas | No | No | No | **Sí** |
| Result\<T\> (sin lanzar) | No | Parcial | No | **Sí** |
| TestClock | No | No | No | **Sí** |
| Puente con Job System | No | No | No | **Sí** |
| Diagnósticos en tiempo de compilación | No | No | No | **Sí (17 reglas)** |
| Pools acotados + recorte | No | No | N/A | **Sí** |
| Reporte de errores determinista | No | No (finalizador) | Parcial | **Sí (retorno al pool)** |
| Canales completos | Sí (.NET) | Mínimo | No | **Sí** |
| Puente con Awaitable | N/A | Mínimo | Nativo | **Transparente** |
| Pool optimizado para IL2CPP | No | No (Volatile en cada op) | N/A | **Sí (sin atómicos)** |
| Seguridad contra colisión de tokens | N/A | 18 min (short) | N/A | **Nunca (uint gen)** |
| Migración automática desde UniTask | N/A | N/A | N/A | **Sí (15 correcciones)** |
| Migración automática desde Awaitable | N/A | N/A | N/A | **Sí (8 correcciones)** |

---

**Tu juego merece un async que no se entrecorte.**
