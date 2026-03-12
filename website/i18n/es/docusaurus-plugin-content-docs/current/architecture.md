---
sidebar_position: 5
title: Arquitectura
---

# Arquitectura

Descripción técnica de los componentes internos de Valkarn Tasks.

---

## Estructura de alto nivel

```
┌─────────────────────────────────────────────────────────────────┐
│                    TIEMPO DE COMPILACIÓN                          │
│                                                                   │
│  ┌──────────────┐  ┌──────────────────┐  ┌───────────────────┐  │
│  │  Lifecycle    │  │  Awaitable       │  │  Diagnostics      │  │
│  │  Analyzer     │  │  Bridge Gen      │  │  (TT001–TT017)    │  │
│  └──────────────┘  └──────────────────┘  └───────────────────┘  │
│                                                                   │
│  ┌──────────────┐  ┌──────────────────┐                          │
│  │  Job Bridge   │  │  Combinator      │                          │
│  │  Gen          │  │  Gen             │                          │
│  └──────────────┘  └──────────────────┘                          │
├─────────────────────────────────────────────────────────────────┤
│                    TIEMPO DE EJECUCIÓN                            │
│                                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐  │
│  │ ValkarnTask │  │ Result<T>  │  │ ValkarnPool│  │ Completion│  │
│  │  struct    │  │  struct    │  │  bounded   │  │  Core<T>  │  │
│  └────────────┘  └────────────┘  └────────────┘  └───────────┘  │
│                                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐  │
│  │ PlayerLoop │  │Continuation│  │  Channels  │  │ TestClock │  │
│  │  Helper    │  │   Queue    │  │            │  │           │  │
│  └────────────┘  └────────────┘  └────────────┘  └───────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### Distribución de ensamblados

```
ValkarnTask.Runtime      — se distribuye con el juego
ValkarnTask.SourceGen    — solo en tiempo de compilación (generador de código fuente)
ValkarnTask.Analyzer     — solo en tiempo de compilación (diagnósticos + correcciones de código)
ValkarnTask.Testing      — TestClock + utilidades de prueba
```

---

## Struct ValkarnTask

```csharp
[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask
{
    internal readonly ValkarnTask.ISource source;
    internal readonly ulong token; // empaquetado: 32 bits altos = generación, 32 bajos = índice de slot
}

[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
{
    internal readonly ValkarnTask.ISource<T> source;
    internal readonly T result;  // en línea en la ruta rápida síncrona
    internal readonly ulong token;
}
```

**Invariante clave:** `source == null` significa que la tarea se completó síncronamente sin error — sin ningún objeto en el heap. `ValkarnTask.CompletedTask` es `default(ValkarnTask)`; `ValkarnTask.FromResult(value)` almacena el resultado en línea.

### Token generacional

```csharp
// Empaquetar
ulong token = ((ulong)generation << 32) | slotIndex;

// Desempaquetar
uint slotIndex  = (uint)(token & 0xFFFFFFFF);
uint generation = (uint)(token >> 32);
```

Validado en cada llamada a ISource: `slots[slotIndex].generation == expectedGeneration`. Una referencia obsoleta a un slot de pool reciclado lanza inmediatamente `InvalidOperationException`. 4 mil millones de generaciones por slot — la colisión es imposible en la práctica. (UniTask usa `short` — colisión tras ~18 minutos.)

---

## Contrato ISource

```csharp
public interface ISource
{
    Status GetStatus(ulong token);
    void   GetResult(ulong token);
    void   OnCompleted(Action<object> continuation, object state, ulong token);
    Status UnsafeGetStatus();
}

public interface ISource<out T> : ISource
{
    new T GetResult(ulong token);
}
```

Cualquier objeto que implemente `ISource` puede respaldar un `ValkarnTask`. Implementaciones integradas:

| Tipo | Propósito |
|------|---------|
| `AsyncValkarnRunner<TStateMachine>` | Respalda cada método `async ValkarnTask` |
| `AsyncValkarnRunner<TStateMachine, T>` | Respalda cada método `async ValkarnTask<T>` |
| `ValkarnTask.PooledPromise[<T>]` | Completado manual, retorno automático al pool |
| `ValkarnTask.Promise[<T>]` | Completado manual, sin pool (larga duración) |
| `ExceptionSource` | Respalda `FromException` |
| `CanceledSource` | Respalda `FromCanceled` |
| `NeverSource` | Singleton — nunca transiciona desde Pendiente |

---

## Constructor de métodos async

El compilador de C# gestiona el protocolo del constructor para tipos de retorno async personalizados:

```
Create()
  └─ devuelve struct builder (sin asignación)

Start(ref stateMachine)
  └─ ejecuta la máquina de estados síncronamente
     ├─ se completa sin suspender → SetResult(), el runner permanece nulo
     │  └─ Task devuelve default(ValkarnTask)  ← sin asignación
     └─ encuentra un await incompleto → AwaitUnsafeOnCompleted()
           └─ alquila AsyncValkarnRunner del pool
              copia la máquina de estados en el runner (por valor, sin boxing)
              registra la continuación en el awaitable
              └─ Task envuelve el runner como ISource  ← ruta async
```

El propio builder es un `struct` — sin asignación solo por el builder. El `runner` se asigna de forma **perezosa**: si un método se completa síncronamente, nunca ocurre ningún alquiler del pool.

---

## Runner de máquina de estados y pool

`AsyncValkarnRunner<TStateMachine>` almacena la máquina de estados generada por el compilador **por valor** (sin boxing) y actúa como `ISource`. Se alquila de `ValkarnPool<T>` en la primera suspensión y se devuelve en `GetResult`.

Dado que `TStateMachine` es un tipo único por método async (por instanciación genérica cerrada), cada método async obtiene su propio pool automáticamente mediante la especialización genérica de C#.

### ValkarnPool\<T\>

| Contexto | Estructura | Motivo |
|---------|-----------|--------|
| Hilo principal de Unity | Pila de un solo hilo | Sin sincronización — máxima velocidad posible |
| Hilos en segundo plano | Pila Treiber libre de locks | Operaciones CAS, sin locks |

El contexto del hilo se detecta mediante `Thread.CurrentThread.IsBackground`. La forma del pool (capacidad, tasa de recorte) se configura mediante `ValkarnTaskSettings`.

---

## ValkarnCompletionCore\<T\>

Estado compartido dentro de cada implementación de `ISource`:

- `Status` actual (Pendiente / Con éxito / Con fallo / Cancelado)
- Valor del resultado (fuentes genéricas)
- Excepción u `OperationCanceledException` (rutas de error)
- Delegado de continuación registrado + estado

Las transiciones de estado usan `Interlocked.CompareExchange` — sin locks, thread-safe. Una **guarda contra completado doble** garantiza que solo la primera llamada a `TrySet*` tiene efecto; las llamadas posteriores son no-ops silenciosos.

---

## Integración con PlayerLoop

`PlayerLoopHelper` inserta callbacks ligeros de runner en el PlayerLoop de Unity al inicio (`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]`).

Cada valor de `PlayerLoopTiming` corresponde a una fase. Cuando se llama `await ValkarnTask.Yield(timing)`, la continuación se encola en el runner de esa fase y se despacha la próxima vez que Unity la alcanza.

```
Initialization  →  EarlyUpdate  →  FixedUpdate  →  PreUpdate
→  Update  →  PreLateUpdate  →  PostLateUpdate  →  TimeUpdate
  (+ variantes Last* para cada fase)
```

---

## Generador de código fuente

El generador de código fuente de Roslyn se ejecuta en tiempo de compilación. Para cada clase `partial` que extiende `MonoBehaviour` con métodos `async ValkarnTask`, genera un archivo de clase parcial que:

1. Declara el campo `_valkarnCancelToken`
2. Lo asigna desde `destroyCancellationToken` en `Awake`
3. Envuelve cada método async para propagar el token automáticamente

El archivo generado nunca se muestra en el depurador y nunca modifica el código fuente del usuario.

El generador también produce:

- **Adaptadores de puente con Awaitable** — cuando se hace await de `Awaitable` dentro de `async ValkarnTask`
- **Wrappers async de jobs** — cuando se detectan tipos `IJob` / `IJobParallelFor`
- **Pools de combinadores** — fuentes `WhenAll`/`WhenAny` tipadas para tuplas de aridad 2–8

---

## Analizadores de Roslyn

17 reglas `DiagnosticAnalyzer` se incluyen en `Analyzers/netstandard2.0/`. Se ejecutan durante el paso del compilador de C# en el Editor de Unity y en CI:

- Todos usan `SemanticModel` para la resolución de tipos (no coincidencia de cadenas)
- La utilidad compartida `ValkarnTypeHelper` detecta cualquier variante de `ValkarnTask`
- El analizador de bucles zombie omite correctamente funciones locales y lambdas anidadas
- Los analizadores de migración (MIG001–MIG015) se activan automáticamente solo cuando se referencia UniTask / Awaitable — inactivos en caso contrario

---

## Capa de Burst y ECS

Tres módulos opcionales, cada uno protegido por comprobaciones de directiva `#if`:

| Módulo | Requiere | Propósito |
|--------|----------|---------|
| `JobBridge` | `Unity.Jobs` | Envuelve `JobHandle` como awaitable; sondea `handle.IsCompleted` en cada tick del PlayerLoop |
| `AsyncSystemBase` | `Unity.Entities` | Clase base de sistema ECS con soporte async |
| `BurstScheduler` | `Unity.Burst` + `Unity.Collections` | Programa jobs de Burst desde contexto async; gestiona `NativeTimerHeap` |

`NativeTimerHeap` es un min-heap compatible con Burst para temporizadores de alta precisión que evita completamente las asignaciones en el heap administrado.

---

## Integración con el Editor

El **Valkarn Hub** (`Tools → Valkarn → Hub`) usa `TypeCache.GetTypesDerivedFrom<IValkarnEditorPanel>()` para descubrir automáticamente todos los paquetes Valkarn instalados. No se requiere registro manual.

El `TasksTrackerPanel` se suscribe a `EditorApplication.update` para actualizar los diagnósticos del pool cada 0,5 s (configurable) y expone la referencia al asset `ValkarnTaskSettings` para acceso rápido.

---

## Consideraciones sobre IL2CPP

- Las máquinas de estados se almacenan **por valor** dentro de los runners — sin boxing, IL2CPP lo gestiona correctamente
- El runner de cada método async es una especialización genérica separada — type-safe, sin contaminación cruzada
- Los structs de awaiter implementan `ICriticalNotifyCompletion` — el compilador llama a `UnsafeOnCompleted`, omitiendo la captura de `ExecutionContext` (sin sobrecarga en la configuración predeterminada de Unity)
- Si el stripping agresivo está habilitado, preserva el ensamblado runtime:

```xml
<!-- link.xml -->
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```
