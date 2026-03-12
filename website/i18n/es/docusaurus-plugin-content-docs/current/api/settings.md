---
sidebar_position: 4
title: Configuración y Result
---

# VlkTaskSettings

`VlkTaskSettings` es un `ScriptableObject` de Unity que controla el comportamiento en tiempo de ejecución de Valkarn Tasks — principalmente el grupo de objetos que recicla las máquinas de estados async y los objetos de promesa para evitar basura durante el juego.

---

## Crear el Activo de Configuración

1. En la ventana Project, haz clic derecho en una carpeta `Resources` (crea una si no tienes).
2. Selecciona **Assets > Create > Valkarn Tasks > Task Settings**.
3. Nombra el archivo `VlkTaskSettings` y colócalo dentro de la carpeta `Resources`.

El archivo debe llamarse exactamente `VlkTaskSettings` y debe residir en una carpeta llamada `Resources` en cualquier lugar de tu proyecto. El activo se carga en tiempo de ejecución con `Resources.Load<VlkTaskSettings>("VlkTaskSettings")`.

Si no se encuentra ningún activo, todas las configuraciones vuelven a sus valores predeterminados integrados. La biblioteca funciona correctamente sin el activo — crearlo solo es necesario cuando quieres cambiar los valores predeterminados.

---

## Acceder a la Configuración en Tiempo de Ejecución

En compilaciones de Unity, la configuración se lee desde el activo a través de un singleton en caché:

```csharp
VlkTaskSettings settings = VlkTaskSettings.Instance;
```

Los parámetros de grupo más necesarios también se exponen directamente en `VlkTask` por conveniencia:

```csharp
int max      = VlkTask.DefaultMaxPoolSize;   // lee de VlkTaskSettings.Instance
int min      = VlkTask.MinPoolSize;
int interval = VlkTask.TrimCheckInterval;
```

En compilaciones no Unity (pruebas, .NET standalone), `VlkTaskSettings` es una clase estática con propiedades mutables en lugar de un `ScriptableObject`. Se aplican los mismos nombres de propiedad y pueden escribirse directamente:

```csharp
// Solo para compilaciones no Unity / pruebas
VlkTask.DefaultMaxPoolSize = 512;
VlkTask.TrimCheckInterval  = 600;
VlkTask.MinPoolSize        = 16;
```

---

## Propiedades Configurables

### Configuración del Grupo

#### DefaultMaxPoolSize

| | |
|-|-|
| Tipo | `int` |
| Predeterminado | `256` |
| Rango válido | `8` – `1024` |
| Tooltip del Inspector | "Máximo de elementos por tipo de grupo. Los elementos en exceso se recortan." |

El número máximo de objetos retenidos en cada grupo por tipo. Cada instanciación genérica distinta (por ejemplo, `PooledPromise<int>`, `PooledPromise<string>`) tiene su propio grupo limitado a este valor.

Cuando una tarea se completa y su objeto interno se devuelve al grupo, si el grupo ya contiene `DefaultMaxPoolSize` elementos, el objeto devuelto se descarta (elegible para GC). Esto evita el crecimiento ilimitado de memoria después de una ráfaga de actividad async.

Aumenta este valor si el perfilado muestra asignaciones frecuentes de GC durante cargas de trabajo async sostenidas de alto rendimiento. Disminúyelo si la presión de memoria es una preocupación y las tareas no se reutilizan con frecuencia.

#### MinPoolSize

| | |
|-|-|
| Tipo | `int` |
| Predeterminado | `8` |
| Rango válido | `1` – `64` |
| Tooltip del Inspector | "Tamaño mínimo del grupo — nunca reducir por debajo de esto." |

El pase de recorte del grupo nunca reducirá ningún grupo por debajo de este conteo. Esto garantiza que una línea base de grupo caliente siempre esté disponible, evitando picos de asignación después de un período tranquilo donde el pase de recorte podría haber liberado todo.

#### TrimCheckInterval

| | |
|-|-|
| Tipo | `int` |
| Predeterminado | `300` |
| Rango válido | `30` – `1000` |
| Tooltip del Inspector | "Fotogramas entre verificaciones de recorte. A 60fps, 300 ≈ 5 segundos." |

Cuántos fotogramas transcurren entre pases de recorte del grupo. El pase de recorte recorre todos los grupos registrados y libera objetos en exceso (los que están sobre `MinPoolSize`) si el grupo ha estado consistentemente sobredimensionado.

A 60 fps el valor predeterminado de 300 equivale a aproximadamente 5 segundos entre verificaciones. Reduce este valor si quieres un recorte más agresivo y frecuente (a costa de más trabajo de recorte frecuente). Auméntalo si los pases de recorte aparecen como picos en el perfilado.

#### TrimHysteresisCount

| | |
|-|-|
| Tipo | `int` |
| Predeterminado | `2` |
| Rango válido | `1` – `10` |
| Tooltip del Inspector | "Número de verificaciones consecutivas por encima del umbral antes de recortar." |

Un grupo solo se recorta después de que ha sido observado como sobredimensionado durante este número de ciclos de recorte consecutivos. Esto evita el thrashing — si el juego tiene un breve pico seguido de un período tranquilo, un conteo de histéresis de 2 significa que el grupo sobrevive un ciclo tranquilo antes de comenzar a liberar objetos.

#### TrimReleaseRatio

| | |
|-|-|
| Tipo | `float` |
| Predeterminado | `0.25` |
| Rango válido | `0.1` – `1.0` |
| Tooltip del Inspector | "Fracción del exceso a liberar por ciclo de recorte (0.25 = 25%)." |

Cuando se recorta un grupo, esta fracción de la capacidad en exceso (elementos por encima de `MinPoolSize`) se libera por ciclo en lugar de todo a la vez. Un valor de `0.25` significa que cada pase de recorte elimina el 25% del exceso. Esta liberación gradual evita una caída repentina en el tamaño del grupo que podría causar una ráfaga de asignación si la carga aumenta nuevamente.

Establece esto en `1.0` si quieres que todo el exceso se libere inmediatamente en cada ciclo.

---

### Ciclo de Vida

#### EnableAutoCancel

| | |
|-|-|
| Tipo | `bool` |
| Predeterminado | `true` |
| Tooltip del Inspector | "Auto-vincular tareas MonoBehaviour a destroyCancellationToken." |

Cuando está habilitado, las tareas iniciadas desde un `MonoBehaviour` se vinculan automáticamente al `destroyCancellationToken` de ese `MonoBehaviour`. Si el `MonoBehaviour` se destruye mientras una tarea está en ejecución, la tarea se cancela en lugar de continuar ejecutándose contra un objeto destruido.

Deshabilita esto solo si estás gestionando la cancelación manualmente y no quieres vinculación automática.

---

### Manejo de Errores

#### LogUnobservedCancellations

| | |
|-|-|
| Tipo | `bool` |
| Predeterminado | `false` |
| Tooltip del Inspector | "Registrar cancelaciones no observadas como advertencias." |

Por defecto, una tarea que se cancela pero nunca se espera (cancelación no observada) se ignora silenciosamente. Habilita esto para registrar una advertencia cuando eso ocurra. Útil durante el desarrollo para encontrar tareas fire-and-forget que se cancelan silenciosamente.

Los fallos no observados siempre se reportan a través del evento `VlkTask.UnobservedException` independientemente de esta configuración.

#### MaxExceptionLogsPerFrame

| | |
|-|-|
| Tipo | `int` |
| Predeterminado | `10` |
| Rango válido | `1` – `100` |
| Tooltip del Inspector | "Máximo de entradas de registro de excepciones por fotograma para evitar spam." |

Limita el número de entradas de registro de excepciones no observadas emitidas en un único fotograma. Si muchas tareas fallan en el mismo fotograma (por ejemplo, después de un fallo de red), esto evita que la consola se inunde con cientos de trazas de pila idénticas.

---

## Cómo Funciona el Recorte del Grupo en Tiempo de Ejecución

En cada actualización del bucle del jugador de Unity, `PlayerLoopHelper` incrementa un contador de fotogramas. Cuando el contador alcanza `TrimCheckInterval`, llama a `PoolRegistry.TrimAll(MinPoolSize)`. Cada grupo que ha sido registrado verifica si ha estado por encima de la capacidad durante al menos `TrimHysteresisCount` verificaciones consecutivas. Si es así, libera `TrimReleaseRatio` de su exceso.

Los grupos se registran automáticamente en el primer uso. El método `VlkTask.GetPoolInfo()` devuelve una instantánea de todos los grupos actualmente registrados con su tipo, tamaño actual y tamaño máximo — esto es lo que muestra la ventana Task Tracker.

```
Fotograma 0 ──────────────────────────────────────────────────────────
  El bucle del jugador se ejecuta
  Grupo A: tamaño 200, máx 256 — normal, no se necesita recorte

Fotograma 300 ────────────────────────────────────────────────────────
  TrimAll se dispara
  Grupo A: tamaño 18, máx 256 — por encima de MinPoolSize(8), pero primera verificación de exceso
  hysteresisCount para A = 1 (aún no en el umbral de 2)

Fotograma 600 ────────────────────────────────────────────────────────
  TrimAll se dispara
  Grupo A: tamaño 18, máx 256 — por encima de MinPoolSize(8), segunda verificación de exceso
  hysteresisCount para A = 2 — ¡umbral alcanzado!
  Exceso = 18 - 8 = 10; liberar 10 * 0.25 = 2 objetos
  Grupo A: tamaño 16
```

---

## Ventana Task Tracker

Abre vía **Window > Valkarn Tasks > Task Tracker**.

El Task Tracker es una ventana solo de Editor que muestra el estado del grupo en vivo mientras estás en Modo de Juego. Se actualiza en un intervalo configurable (predeterminado 0.5 segundos, ajustable de 0.1 a 5 segundos vía el control deslizante en la barra de herramientas).

### Pestaña Grupos

Lista cada tipo de grupo que ha estado activo desde la última recarga de dominio, ordenados por tamaño actual (el más grande primero). Cada fila muestra:

| Columna | Descripción |
|---------|-------------|
| Tipo | El nombre del tipo del objeto agrupado, con argumentos genéricos expandidos |
| Tamaño | Número actual de objetos en el grupo |
| Máx | El techo `DefaultMaxPoolSize` para este grupo |
| Uso | Una barra de progreso que muestra `Tamaño / Máx` como porcentaje |

Si no se han usado grupos aún, un mensaje dice "Sin grupos activos. Los grupos se crean en el primer uso."

### Pestaña Config

Muestra los tres parámetros de grupo en vivo leídos de `VlkTask.DefaultMaxPoolSize`, `VlkTask.TrimCheckInterval` y `VlkTask.MinPoolSize`. Los valores solo se muestran en Modo de Juego — en Modo de Edición una nota indica que entres al Modo de Juego para ver los valores en vivo.

La pestaña Config también muestra una referencia al activo `VlkTaskSettings` (si existe en `Resources`), para que puedas hacer clic e inspeccionarlo o modificarlo. Si no se encuentra ningún activo, una advertencia te dirige a crear uno.

---

## Result&lt;T&gt;

`Result<T>` es un struct de unión discriminada para representar el resultado de una operación sin lanzar. Es el tipo de retorno usado por los combinadores `WhenAll` para reportar resultados por tarea, y también está disponible como patrón de resultado de propósito general.

```csharp
public readonly struct Result<T>
{
    public bool IsSuccess  { get; }
    public bool IsFailure  { get; }   // verdadero si falló o se canceló
    public bool IsFaulted  { get; }
    public bool IsCanceled { get; }

    public VlkTask.Status Status { get; }
    public T Value    { get; }        // lanza si no tuvo éxito
    public Exception Error { get; }   // null si no falló

    public static Result<T> Success(T value);
    public static Result<T> Failure(string error);    // envuelve en InvalidOperationException
    public static Result<T> Faulted(Exception error);
    public static Result<T> Canceled(OperationCanceledException oce = null);

    public static implicit operator bool(Result<T> r);  // verdadero si tuvo éxito
}
```

Existe un `Result` no genérico para tareas void con la misma forma pero sin `Value`.

### Métodos de extensión AsResult

Convierte cualquier `VlkTask` o `VlkTask<T>` en un `Result` sin un `try/catch` en tu propio código:

```csharp
public static VlkTask<Result<T>> AsResult<T>(this VlkTask<T> task);
public static VlkTask<Result>    AsResult(this VlkTask task);
```

Si la tarea subyacente ya está completada sincrónicamente (el camino rápido de cero asignaciones), `AsResult` devuelve inmediatamente sin crear una máquina de estados async. De lo contrario, envuelve en un método async que captura `OperationCanceledException` y todas las demás excepciones, traduciéndolas a la variante `Result` apropiada.

### Cuándo usar Result&lt;T&gt;

Usa `Result<T>` en lugar de capturar excepciones cuando:

- Llamas a múltiples tareas en paralelo y quieres resultados por tarea sin cortocircuitar todo el lote.
- Quieres expresar operaciones fallibles de forma segura en tipos sin flujo de control de excepciones.
- Devuelves desde un método que el llamador puede no querer envolver en `try/catch`.

```csharp
// Disparar múltiples tareas, obtener todos los resultados independientemente de los fallos individuales
Result<int>[] results = await VlkTask.WhenAll(
    FetchScoreAsync(playerA).AsResult(),
    FetchScoreAsync(playerB).AsResult(),
    FetchScoreAsync(playerC).AsResult()
);

foreach (var r in results)
{
    if (r.IsSuccess)
        Debug.Log($"Puntuación: {r.Value}");
    else if (r.IsFaulted)
        Debug.LogError($"Fallo: {r.Error.Message}");
    else
        Debug.Log("Cancelado");
}
```

Verificar `IsSuccess` o usar el operador `bool` implícito son las formas preferidas de ramificar en un resultado:

```csharp
var result = await SomeOperationAsync().AsResult();

if (result)
{
    Use(result.Value);
}
```

Acceder a `result.Value` cuando `IsSuccess` es `false` lanza una `InvalidOperationException`.

### Métodos de fábrica

| Método | Estado establecido | Error establecido |
|--------|-------------------|-------------------|
| `Result<T>.Success(value)` | `Succeeded` | ninguno |
| `Result<T>.Failure(message)` | `Faulted` | `new InvalidOperationException(message)` |
| `Result<T>.Faulted(exception)` | `Faulted` | la excepción proporcionada |
| `Result<T>.Canceled(oce?)` | `Canceled` | la `OperationCanceledException` proporcionada, o `null` |

La propiedad `Succeeded` existe tanto en `Result` como en `Result<T>` pero está marcada como `[Obsolete]` — usa `IsSuccess` en su lugar para consistencia con `IsFailure`, `IsFaulted` e `IsCanceled`.
