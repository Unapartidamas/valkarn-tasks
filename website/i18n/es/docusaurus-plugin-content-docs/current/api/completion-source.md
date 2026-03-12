---
sidebar_position: 3
title: Fuentes de Completado
---

# ValkarnTaskCompletionSource

`ValkarnTaskCompletionSource<T>` y el `ValkarnTaskCompletionSource` no genérico te dan control manual sobre un `ValkarnTask`. Son el equivalente async de escribir el resultado tú mismo — tienes un objeto fuente, entregas su `.Task` a los llamadores, y luego lo resuelves, fallas o cancelas desde un punto de llamada separado.

Este es el equivalente de Valkarn Tasks de `TaskCompletionSource<T>` de la BCL, pero respaldado por `ValkarnTask.Promise<T>` para mantener el modelo de asignación consistente con el resto de la biblioteca.

---

## ValkarnTaskCompletionSource&lt;T&gt;

```csharp
public class ValkarnTaskCompletionSource<T>
{
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled();
}
```

### Task

```csharp
public ValkarnTask<T> Task { get; }
```

La tarea que observará el código que espera. Distribúyela a todos los llamadores que necesiten esperar el resultado. Múltiples llamadores pueden hacer `await` en la misma tarea concurrentemente.

### TrySetResult

```csharp
public bool TrySetResult(T result);
```

Completa la tarea exitosamente con el valor dado. Todas las continuaciones en espera se reanudan. Devuelve `true` si el completado fue aceptado; devuelve `false` si la tarea ya estaba completada (por cualquier llamada `TrySet*` anterior). Nunca lanza.

### TrySetException

```csharp
public bool TrySetException(Exception ex);
```

Falla la tarea con la excepción proporcionada. El código en espera recibe la excepción cuando llama a `await`. Lanza `ArgumentNullException` si `ex` es `null`. Devuelve `true` si fue aceptado, `false` si ya estaba completado.

### TrySetCanceled

```csharp
public bool TrySetCanceled();
```

Cancela la tarea. El código en espera recibe una `OperationCanceledException`. Devuelve `true` si fue aceptado, `false` si ya estaba completado.

---

## ValkarnTaskCompletionSource no genérico

No hay una clase `ValkarnTaskCompletionSource` no genérica separada en la API pública. Para tareas manuales que devuelven void, usa `ValkarnTask.Promise` directamente:

```csharp
var promise = new ValkarnTask.Promise();
ValkarnTask task = promise.Task;

promise.TrySetResult();    // completar
promise.TrySetException(ex);
promise.TrySetCanceled();
```

`ValkarnTask.Promise` expone la misma superficie `TrySet*` y la misma protección de doble completado, pero produce un `ValkarnTask` no genérico en lugar de un `ValkarnTask<T>`.

---

## Protección contra Doble Completado

Todos los métodos `TrySet*` son seguros para llamar desde cualquier hilo en cualquier momento, incluyendo concurrentemente. La primera llamada que gana una comparación e intercambio en la máquina de estados interna tiene éxito; cada llamada posterior devuelve `false` y no tiene ningún efecto. Esto significa:

- Llamar a `TrySetResult` dos veces no hace nada en la segunda llamada.
- Llamar a `TrySetResult` después de `TrySetException` no hace nada.
- Dos hilos compitiendo para completar la misma fuente simultáneamente es seguro — uno gana, el otro se ignora silenciosamente.

Si necesitas saber qué llamador "ganó", verifica el valor de retorno. Si no te importa (señal fire-and-forget), puedes ignorarlo con seguridad.

```csharp
// Carrera segura — solo una de estas completará realmente la tarea
_ = source.TrySetResult(value);
_ = source.TrySetCanceled();
```

---

## Patrones Comunes

### Puentear una API de callbacks

Muchas APIs de Unity y de plataforma entregan resultados a través de callbacks en lugar de async/await. `ValkarnTaskCompletionSource<T>` te permite envolverlos de forma limpia.

```csharp
public ValkarnTask<Texture2D> LoadTextureAsync(string url)
{
    var tcs = new ValkarnTaskCompletionSource<Texture2D>();

    StartCoroutine(LoadCoroutine(url, tcs));

    return tcs.Task;
}

IEnumerator LoadCoroutine(string url, ValkarnTaskCompletionSource<Texture2D> tcs)
{
    var request = UnityWebRequestTexture.GetTexture(url);
    yield return request.SendWebRequest();

    if (request.result == UnityWebRequest.Result.Success)
        tcs.TrySetResult(DownloadHandlerTexture.GetContent(request));
    else
        tcs.TrySetException(new Exception(request.error));
}
```

Los llamadores ahora pueden simplemente hacer `await` en la tarea devuelta:

```csharp
Texture2D tex = await LoadTextureAsync("https://example.com/image.png");
```

### Señal de un solo disparo (puerta async)

Usa `ValkarnTask.Promise` cuando necesitas una señal que se dispare una vez y desbloquee cualquier número de esperadores. Esto es similar a un `ManualResetEventSlim` pero nativo de async.

```csharp
public class AsyncGate
{
    readonly ValkarnTask.Promise _promise = new();

    // Cualquier número de llamadores puede esperar esto
    public ValkarnTask WaitAsync() => _promise.Task;

    // Llamar una vez para desbloquear a todos
    public void Open() => _promise.TrySetResult();
}

// Uso
var gate = new AsyncGate();

// Varios sistemas esperan la puerta de forma independiente
async ValkarnTask SystemAAsync()
{
    await gate.WaitAsync();
    // proceder después de que la puerta se abra
}

async ValkarnTask SystemBAsync()
{
    await gate.WaitAsync();
    // procede al mismo tiempo que SystemA
}

// En algún otro lugar — abrir la puerta
gate.Open();
```

Una vez que se llama a `TrySetResult()`, todas las llamadas a `await` actuales y futuras en la tarea se completan inmediatamente (sincrónicamente si la tarea ya está hecha cuando se ejecutan).

### Envolver una operación async de terceros con soporte de cancelación

```csharp
public ValkarnTask<Result> RunWithTimeoutAsync(
    Func<ValkarnTask<Result>> operation,
    float timeoutSeconds,
    CancellationToken ct)
{
    var tcs = new ValkarnTaskCompletionSource<Result>();

    RunCoreAsync(tcs, operation, timeoutSeconds, ct).Forget();

    return tcs.Task;
}

async ValkarnTask RunCoreAsync(
    ValkarnTaskCompletionSource<Result> tcs,
    Func<ValkarnTask<Result>> operation,
    float timeout,
    CancellationToken ct)
{
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
    linked.CancelAfter(TimeSpan.FromSeconds(timeout));

    try
    {
        var result = await operation();
        tcs.TrySetResult(result);
    }
    catch (OperationCanceledException)
    {
        tcs.TrySetCanceled();
    }
    catch (Exception ex)
    {
        tcs.TrySetException(ex);
    }
}
```

### Puerta de inicialización diferida

Un patrón común de Unity es exponer una tarea "lista" que los componentes pueden esperar independientemente de si la inicialización ya ha comenzado.

```csharp
public class ServiceBus : MonoBehaviour
{
    readonly ValkarnTask.Promise _readyPromise = new();

    // Cualquiera puede esperar esto en cualquier momento — antes o después de Initialize()
    public ValkarnTask Ready => _readyPromise.Task;

    async void Start()
    {
        await LoadConfigAsync();
        await ConnectAsync();
        _readyPromise.TrySetResult();  // desbloquea todos los esperadores
    }
}

// En cualquier otro componente
async ValkarnTask OnEnableAsync()
{
    await ServiceBus.Instance.Ready;  // espera si no está listo, devuelve instantáneamente si ya está listo
    DoWork();
}
```

---

## Relación con ValkarnTask.Promise

`ValkarnTaskCompletionSource<T>` es un envoltorio público delgado alrededor de `ValkarnTask.Promise<T>`. Ambos proporcionan la misma funcionalidad. La diferencia es la convención de nombres:

| Tipo | Devuelve | Uso común |
|------|---------|-----------|
| `ValkarnTaskCompletionSource<T>` | `ValkarnTask<T>` | API pública, refleja el estilo BCL `TaskCompletionSource<T>` |
| `ValkarnTask.Promise<T>` | `ValkarnTask<T>` | Uso interno, ligeramente más directo |
| `ValkarnTask.Promise` | `ValkarnTask` | Señales void (puertas, eventos) |

Los tres respaldan su tarea con un `ValkarnTaskCompletionCore<T>`, la máquina de estados interna basada en struct que maneja la seguridad de hilos y la carrera continuación/resultado usando un protocolo de dos fases basado en CAS.
