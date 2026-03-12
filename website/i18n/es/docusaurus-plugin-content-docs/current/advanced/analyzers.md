---
sidebar_position: 1
title: Reglas del Analizador
---

# Reglas del Analizador

Valkarn Tasks incluye dos paquetes de analizador de Roslyn que se activan automáticamente cuando se importa el paquete:

- **`UnaPartidaMas.Valkarn.Tasks.SourceGen.dll`** — Reglas específicas para la corrección de ValkarnTask y el ciclo de vida de Unity. Los IDs de regla comienzan con `TT`.
- **`UnaPartidaMas.Valkarn.Tasks.Analyzer.dll`** — Reglas de migración para bases de código que se mueven desde UniTask. Los IDs de regla comienzan con `MIG`. Estas reglas se activan **solo cuando `Cysharp.Threading.Tasks.UniTask` está presente** en la compilación, por lo que están silenciosas en proyectos nuevos.

Ambos paquetes son DLLs precompiladas ubicadas en la carpeta `Analyzers/` del paquete. Unity las carga como analizadores de Roslyn vía la referencia `.asmdef`; el proyecto `_TestRunner~` las carga vía elementos `<Analyzer>` en `TestRunner.csproj`.

---

## Reglas de Corrección (TT)

Estas reglas detectan errores relacionados con la naturaleza de consumo único de `ValkarnTask` y el uso incorrecto de fire-and-forget.

---

### TT001 — ValkarnTask ya esperado

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Advertencia            |
| Categoría  | ValkarnTask            |
| Corrección | No                     |

`ValkarnTask` es de consumo único: una vez esperado, el token interno se vuelve obsoleto. Un segundo `await` en la misma variable encontrará ese token obsoleto y lanzará. El analizador detecta cuando la misma variable local, parámetro o campo de tipo `ValkarnTask`/`ValkarnTask<T>` se espera más de una vez dentro del mismo método.

**Se activa en:**
```csharp
async ValkarnTask Bad()
{
    ValkarnTask work = DoWorkAsync();
    await work;   // primera espera — correcto
    await work;   // TT001: ya esperado
}
```

**Corrección:** Si necesitas ramificar en el resultado, captura con `.AsResult()` antes de la primera espera, o reestructura para que la tarea se espere exactamente una vez.
```csharp
async ValkarnTask Good()
{
    var result = await DoWorkAsync().AsResult();
    // usar result en ambas ramas
}
```

---

### TT002 — ValkarnTask no esperado ni descartado

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | **Error**              |
| Categoría  | ValkarnTask            |
| Corrección | No                     |

Una llamada a método que devuelve `ValkarnTask` usada como declaración de expresión — no esperada, no asignada y no seguida de `.Forget()` — es un error silencioso. Las excepciones lanzadas dentro de la tarea nunca se observan, y la máquina de estados agrupada de la tarea nunca se devuelve al grupo.

El analizador verifica las declaraciones de expresión donde el tipo de expresión se resuelve a `ValkarnTask` o `ValkarnTask<T>`. Omite:
- Expresiones de asignación (`tasks[i] = DoWork()`)
- Cadenas que terminan en `.Forget()` (fire-and-forget intencional)

**Se activa en:**
```csharp
void Bad()
{
    LoadDataAsync();      // TT002: no esperado ni descartado
    ProcessItemAsync();   // TT002
}
```

**Corrección — esperarlo:**
```csharp
async ValkarnTask Good()
{
    await LoadDataAsync();
    await ProcessItemAsync();
}
```

**Corrección — fire-and-forget explícito:**
```csharp
void GoodFireAndForget()
{
    LoadDataAsync().Forget();
}
```

---

### TT013 — ValkarnTask devuelto pero nunca consumido

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Advertencia            |
| Categoría  | ValkarnTask            |
| Corrección | No                     |

Este ID de regla está **reservado para análisis de flujo de datos futuro**. Está destinado a capturar el patrón asignado pero nunca esperado — almacenar un `ValkarnTask` en una variable y luego nunca esperarlo ni descartarlo — lo cual TT002 no cubre porque TT002 solo examina declaraciones de expresión simples.

La implementación actual no registra acciones de sintaxis. Cuando se implemente el análisis de flujo de datos, TT013 complementará a TT002 al cubrir:
```csharp
async ValkarnTask Bad()
{
    ValkarnTask task = DoWorkAsync();  // asignado pero nunca esperado
    DoOtherThing();
    // task es abandonado — TT013 (futuro)
}
```

Los métodos decorados con `[FireAndForget]` estarán exentos.

---

## Reglas de Ciclo de Vida (TT)

Estas reglas se relacionan con la gestión del tiempo de vida de MonoBehaviour y la cancelación.

---

### TT010 — Auto-cancel activo para método async de MonoBehaviour

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Info                   |
| Categoría  | ValkarnTask            |
| Corrección | No                     |

Una pista informativa. Cualquier método `async ValkarnTask` (o `async ValkarnTask<T>`) dentro de una clase que hereda de `UnityEngine.MonoBehaviour` será cancelado automáticamente cuando el objeto sea destruido, **a menos que** el método esté decorado con `[NoAutoCancel]`. Este diagnóstico hace visible ese comportamiento en el IDE sin requerir que el desarrollador lo recuerde.

**Se activa en:**
```csharp
public class MyBehaviour : MonoBehaviour
{
    async ValkarnTask LoadLevel()  // TT010: auto-cancel activo
    {
        await ValkarnTask.Delay(2000);
    }
}
```

**Para optar por no participar** (y suprimir TT010 para ese método), añade `[NoAutoCancel]`:
```csharp
[NoAutoCancel]
async ValkarnTask LoadLevel(CancellationToken ct)
{
    await ValkarnTask.Delay(2000, ct);
}
```

---

### TT011 — WhenAll mezcla diferentes scopes de tiempo de vida

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Advertencia            |
| Categoría  | ValkarnTask            |
| Corrección | No                     |

`ValkarnTask.WhenAll` llamado con tareas de diferentes scopes de tiempo de vida puede producir comportamiento de cancelación parcial sorprendente. Si una tarea está vinculada a un `MonoBehaviour` (devuelta por un método de instancia de una clase que hereda `MonoBehaviour`) y otra no está vinculada (una tarea estática, o de una clase que no es MonoBehaviour), destruir el objeto cancelará una tarea pero no la otra, dejando al combinador en un estado indeterminado.

**Se activa en:**
```csharp
// Suponiendo EnemyAI : MonoBehaviour
await ValkarnTask.WhenAll(
    enemyAI.PatrolAsync(),   // vinculada: auto-cancelada al Destroy
    GlobalMusic.FadeAsync()  // no vinculada: vive indefinidamente
);
// TT011: WhenAll mezcla tiempos de vida — PatrolAsync() vs GlobalMusic.FadeAsync()
```

**Corrección — dar a ambas tareas un token de cancelación compartido:**
```csharp
using var cts = new CancellationTokenSource();
await ValkarnTask.WhenAll(
    enemyAI.PatrolAsync(cts.Token),
    GlobalMusic.FadeAsync(cts.Token)
);
```

---

### TT012 — Bucle async sin verificación de cancelación

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Advertencia            |
| Categoría  | ValkarnTask            |
| Corrección | No                     |

Un método `async ValkarnTask` que contiene un bucle `for`, `while`, `foreach` o `do`-`while` cuyo cuerpo no tiene verificación de cancelación es un "bucle zombie": si se activa la cancelación del ciclo de vida (por ejemplo, un MonoBehaviour es destruido), la señal de auto-cancel no puede romper el bucle. El bucle sigue ejecutándose aunque el objeto propietario haya desaparecido.

El analizador considera que el cuerpo de un bucle tiene una verificación de cancelación si contiene alguno de:
- Una expresión `await` (la operación esperada puede observar el token y lanzar `OperationCanceledException`)
- `ThrowIfCancellationRequested` (como identificador o acceso a miembro)
- `IsCancellationRequested` (como identificador o acceso a miembro)

La verificación **no** desciende en lambdas anidadas o funciones locales.

**Se activa en:**
```csharp
async ValkarnTask PollForever(CancellationToken ct)
{
    while (true)                     // TT012: sin verificación de cancelación en el cuerpo
    {
        ProcessNextItem();
        Thread.Sleep(16);            // síncrono — no es un await
    }
}
```

**Corrección — añadir un await o una verificación explícita:**
```csharp
async ValkarnTask PollForever(CancellationToken ct)
{
    while (true)
    {
        ct.ThrowIfCancellationRequested();
        ProcessNextItem();
        await ValkarnTask.Yield();       // también satisface la verificación
    }
}
```

---

### TT014 — [NoAutoCancel] sin parámetro CancellationToken

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Advertencia            |
| Categoría  | ValkarnTask            |
| Corrección | No                     |

`[NoAutoCancel]` en un método `async ValkarnTask` en un `MonoBehaviour` opta al método por no participar en la cancelación automática del ciclo de vida. Pero si el método no tiene parámetro `CancellationToken`, no tiene ningún mecanismo para observar la cancelación en absoluto, lo que hace que el atributo sea inútil y casi ciertamente indica un parámetro olvidado.

**Se activa en:**
```csharp
public class Enemy : MonoBehaviour
{
    [NoAutoCancel]
    async ValkarnTask Chase()      // TT014: [NoAutoCancel] pero sin parámetro CancellationToken
    {
        await ValkarnTask.Delay(1000);
    }
}
```

**Corrección — añadir un parámetro `CancellationToken`:**
```csharp
[NoAutoCancel]
async ValkarnTask Chase(CancellationToken ct)
{
    await ValkarnTask.Delay(1000, ct);
}
```

---

## Reglas Informativas (TT)

---

### TT015 — Adaptador de puente Awaitable generado

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Info                   |
| Categoría  | ValkarnTask            |
| Corrección | No                     |

Cuando haces `await` en un `Awaitable` de Unity (de `UnityEngine`) dentro de un método `async ValkarnTask`, Valkarn Tasks genera un adaptador de puente automáticamente vía `AwaitableBridge`. Este diagnóstico es informativo: confirma que el puente está en efecto. No se requiere ninguna acción.

**Se activa en:**
```csharp
async ValkarnTask LoadScene()
{
    await SceneManager.LoadSceneAsync("Main");  // TT015: adaptador de puente generado
}
```

No se necesita ningún cambio. El puente maneja la conversión de forma transparente.

---

## Reglas de Calidad de Código (TT)

---

### TT016 — Método async sin await

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Advertencia            |
| Categoría  | ValkarnTask            |
| Corrección | No                     |

Un método `async ValkarnTask` (o `async ValkarnTask<T>`) que no contiene expresiones `await` — incluyendo `await foreach`, `await using` y `await` dentro de declaraciones `using` — incurre en la sobrecarga completa de asignación de máquina de estados sin beneficio. El compilador aún genera una máquina de estados, pero dado que no hay punto de suspensión, el método siempre se completa sincrónicamente.

**Se activa en:**
```csharp
async ValkarnTask<int> ComputeTotal()  // TT016: sin await en el cuerpo
{
    return items.Sum(x => x.Value);
}
```

**Corrección — eliminar `async` y devolver una tarea completada:**
```csharp
ValkarnTask<int> ComputeTotal()
{
    return ValkarnTask.FromResult(items.Sum(x => x.Value));
}
```

O para retorno void:
```csharp
ValkarnTask DoSetup()
{
    Initialize();
    return ValkarnTask.CompletedTask;
}
```

---

### TT017 — [FireAndForget] en ValkarnTask\<T\>

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Advertencia            |
| Categoría  | ValkarnTask            |
| Corrección | No                     |

`[FireAndForget]` señala que un método se ejecuta intencionalmente sin esperar su resultado. Aplicarlo a un método que devuelve `ValkarnTask<T>` (resultado tipado) siempre descarta `T`, haciendo que el retorno tipado sea insignificante. El método debería devolver `ValkarnTask` (void) en su lugar.

**Se activa en:**
```csharp
[FireAndForget]
async ValkarnTask<int> SendReport()   // TT017: el valor de retorno se descarta
{
    await UploadAsync();
    return 42;                     // este 42 nunca se ve
}
```

**Corrección — cambiar el tipo de retorno a `ValkarnTask`:**
```csharp
[FireAndForget]
async ValkarnTask SendReport()
{
    await UploadAsync();
}
```

---

## Reglas de Migración (MIG)

El analizador de migración se activa **solo cuando el tipo `Cysharp.Threading.Tasks.UniTask` está presente** en la compilación. Las reglas son informativas o advertencias para guiar la transición desde UniTask a Valkarn Tasks. Ninguna de estas reglas tiene correcciones automáticas; las descripciones a continuación explican el cambio manual.

---

### MIG001 — Tipo UniTask detectado

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Info                   |
| Categoría  | ValkarnTaskMigration   |

Se detectó el uso de un tipo `UniTask` (el struct en sí o `UniTask<T>`). Reemplázalo con el equivalente `ValkarnTask` o `ValkarnTask<T>`.

```csharp
// Antes
UniTask<Sprite> LoadSprite(string path) { ... }

// Después
ValkarnTask<Sprite> LoadSprite(string path) { ... }
```

---

### MIG002 — El parámetro cancelImmediately es innecesario

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Info                   |
| Categoría  | ValkarnTaskMigration   |

Los métodos `Delay` y otros métodos basados en tiempo de UniTask aceptan un parámetro `cancelImmediately` para optar por cancelación rápida. Valkarn Tasks cancela inmediatamente por defecto — no hay parámetro `cancelImmediately`. Elimina el argumento.

```csharp
// Antes
await UniTask.Delay(1000, cancelImmediately: true, cancellationToken: ct);

// Después
await ValkarnTask.Delay(1000, ct);
```

---

### MIG003 — SuppressCancellationThrow() detectado

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Advertencia            |
| Categoría  | ValkarnTaskMigration   |

`.SuppressCancellationThrow()` de UniTask convierte una tarea cancelada en una tupla `(bool isCancelled, T result)` sin lanzar. Valkarn Tasks usa `.AsResult()` para el mismo propósito, que devuelve un struct `Result<T>`.

```csharp
// Antes
var (isCancelled, value) = await myUniTask.SuppressCancellationThrow();

// Después
var result = await myValkarnTask.AsResult();
if (result.IsCanceled) { ... }
T value = result.Value;
```

---

### MIG004 — Tipo de retorno Awaitable detectado

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Info                   |
| Categoría  | ValkarnTaskMigration   |

Un método devuelve el tipo `Awaitable` de Unity. Considera reemplazarlo con `ValkarnTask`, que se integra nativamente con el runtime de Valkarn Tasks y admite auto-cancelación, agrupamiento y el conjunto completo de combinadores.

---

### MIG005 — Canal SingleConsumerUnbounded detectado

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Advertencia            |
| Categoría  | ValkarnTaskMigration   |

`Channel.CreateSingleConsumerUnbounded<T>()` de UniTask crea un canal sin contrapresión. Considera reemplazarlo con `ValkarnTask.Channel.CreateBounded<T>(capacity)` para añadir contrapresión y evitar el crecimiento ilimitado de memoria bajo carga.

```csharp
// Antes
var ch = Channel.CreateSingleConsumerUnbounded<Event>();

// Después (con contrapresión)
var ch = ValkarnTask.Channel.CreateBounded<Event>(capacity: 256);

// O si no acotado es intencional
var ch = ValkarnTask.Channel.CreateUnbounded<Event>();
```

---

### MIG006 — PlayerLoopTiming de UniTask detectado

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Info                   |
| Categoría  | ValkarnTaskMigration   |

Se detectó un valor del enum `PlayerLoopTiming` del espacio de nombres `Cysharp.Threading.Tasks`. Cambia la directiva `using` a `UnaPartidaMas.Valkarn.Tasks`; los nombres de los valores del enum son idénticos.

```csharp
// Antes
using Cysharp.Threading.Tasks;
timing = PlayerLoopTiming.Update;

// Después
using UnaPartidaMas.Valkarn.Tasks;
timing = PlayerLoopTiming.Update;  // mismo nombre, diferente espacio de nombres
```

---

### MIG007 — async UniTaskVoid detectado

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Advertencia            |
| Categoría  | ValkarnTaskMigration   |

`async UniTaskVoid` era el patrón de método fire-and-forget de UniTask. Valkarn Tasks lo reemplaza con dos opciones:

```csharp
// Antes
async UniTaskVoid StartLoadAsync() { ... }

// Después — opción 1: atributo [FireAndForget]
[FireAndForget]
async ValkarnTask StartLoadAsync() { ... }

// Después — opción 2: ValkarnTask estándar + .Forget() en el sitio de llamada
async ValkarnTask StartLoadAsync() { ... }
// Llamado como:
StartLoadAsync().Forget();
```

---

### MIG008 — Awaitable MainThreadAsync() detectado

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Info                   |
| Categoría  | ValkarnTaskMigration   |

`Awaitable.MainThreadAsync()` se usaba en la API `Awaitable` de Unity para cambiar de vuelta al hilo principal. Valkarn Tasks ejecuta las continuaciones en el hilo principal por defecto vía la integración de PlayerLoop, por lo que las llamadas explícitas a `MainThreadAsync()` suelen ser innecesarias y pueden eliminarse.

---

### MIG009 — UniTask.RunOnThreadPool detectado

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Advertencia            |
| Categoría  | ValkarnTaskMigration   |

Reemplaza con `ValkarnTask.RunOnThreadPool`. La API es la misma.

```csharp
// Antes
await UniTask.RunOnThreadPool(() => HeavyWork());

// Después
await ValkarnTask.RunOnThreadPool(() => HeavyWork());
```

---

### MIG010 — .ToCoroutine() detectado

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Advertencia            |
| Categoría  | ValkarnTaskMigration   |

`.ToCoroutine()` era un puente de UniTask para los llamadores de coroutine heredados. Reescribe el código consumidor como un método `async ValkarnTask` en su lugar.

```csharp
// Antes
IEnumerator LegacyCaller() { yield return MyUniTask().ToCoroutine(); }

// Después
async ValkarnTask ModernCaller() { await MyValkarnTask(); }
```

---

### MIG011 — UniTask.Create() detectado

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Advertencia            |
| Categoría  | ValkarnTaskMigration   |

`UniTask.Create(Func<UniTask>)` envuelve un delegado de fábrica. Reemplaza con el patrón `ValkarnTask.Promise<T>` para completado controlado manualmente.

```csharp
// Antes
var task = UniTask.Create(async () => { await DoWork(); return 42; });

// Después
var promise = new ValkarnTaskCompletionSource<int>();
DoWorkThenComplete(promise);
var task = promise.Task;
```

---

### MIG012 — UniTask.Lazy/Defer detectado

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Info                   |
| Categoría  | ValkarnTaskMigration   |

`UniTask.Lazy<T>` y `UniTask.Defer` existían para evitar asignaciones cuando una tarea podría completarse sincrónicamente. Valkarn Tasks tiene un camino rápido síncrono de cero asignaciones integrado: devolver `ValkarnTask.CompletedTask` o `ValkarnTask.FromResult(value)` nunca asigna. Elimina los envoltorios `Lazy`/`Defer`.

---

### MIG013 — .ToUniTask()/.AsUniTask() detectado

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Info                   |
| Categoría  | ValkarnTaskMigration   |

Llamadas de conversión desde `Awaitable` de Unity a UniTask. Elimínalas; Valkarn Tasks conecta `Awaitable` de forma nativa (ver TT015).

---

### MIG014 — UniTaskAsyncEnumerable detectado

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Advertencia            |
| Categoría  | ValkarnTaskMigration   |

UniTask incluía su propio `IUniTaskAsyncEnumerable<T>` y utilidades `UniTaskAsyncEnumerable`. Usa `IAsyncEnumerable<T>` de la BCL con `System.Linq.Async` en su lugar. `IAsyncEnumerable<T>` es compatible nativamente con `await foreach` de C#.

```csharp
// Antes
IUniTaskAsyncEnumerable<int> GetItems() { ... }

// Después
IAsyncEnumerable<int> GetItems() { ... }
```

---

### MIG015 — TimeoutController detectado

| Propiedad  | Valor                  |
|------------|------------------------|
| Severidad  | Info                   |
| Categoría  | ValkarnTaskMigration   |

El `TimeoutController` de UniTask era un ayudante para tiempos de espera reutilizables. Reemplázalo con un `CancellationTokenSource` estándar construido con un `TimeSpan`, que la BCL admite directamente.

```csharp
// Antes
var controller = new TimeoutController();
var ct = controller.Timeout(TimeSpan.FromSeconds(5));

// Después
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var ct = cts.Token;
```

---

## Suprimir Reglas

Los mecanismos de supresión estándar de Roslyn funcionan para todas las reglas:

```csharp
// Suprimir una única ocurrencia en línea
#pragma warning disable TT012
while (true) { DoSomething(); }
#pragma warning restore TT012
```

O vía `.editorconfig` para suprimir en todo el proyecto:
```ini
[*.cs]
dotnet_diagnostic.TT012.severity = none
```

Las reglas de migración (`MIG*`) pueden suprimirse de la misma manera, o deshabilitarse globalmente una vez que la migración esté completa estableciendo la severidad en `none` en `.editorconfig`.
