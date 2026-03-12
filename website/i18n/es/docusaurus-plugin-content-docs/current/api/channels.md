---
sidebar_position: 2
title: Canales
---

# Canales

Los canales proporcionan una tubería segura para hilos y compatible con async para pasar datos entre productores y consumidores. Son particularmente adecuados para sistemas de juego donde el trabajo se genera en un lado (hilos en segundo plano, callbacks de eventos, completados de trabajo) y necesita ser consumido en otro (hilo principal, grupos de trabajadores).

## Crear un Canal

Los canales se crean a través de la clase de fábrica estática `VlkTask.Channel`. No hay constructor público.

### Canal No Acotado

```csharp
Channel<T> channel = VlkTask.Channel.CreateUnbounded<T>(bool multiConsumer = false);
```

Un canal no acotado no tiene límite de capacidad. `WriteAsync` y `TryWrite` siempre tienen éxito inmediatamente siempre que el canal no se haya completado. Los elementos se acumulan en una `Queue<T>` interna hasta que un consumidor los lea.

**Parámetros**

| Parámetro | Predeterminado | Descripción |
|-----------|----------------|-------------|
| `multiConsumer` | `false` | Cuando es `true`, se admiten múltiples llamadas concurrentes a `ReadAsync` (consumidores competidores). Cuando es `false`, solo un lector puede esperar `ReadAsync` a la vez. |

### Canal Acotado

```csharp
Channel<T> channel = VlkTask.Channel.CreateBounded<T>(int capacity, bool multiConsumer = false);
```

Un canal acotado contiene como máximo `capacity` elementos en un búfer circular de tamaño fijo. Cuando el búfer está lleno, `WriteAsync` suspende el código que llama hasta que haya espacio disponible (contrapresión). `TryWrite` devuelve `false` inmediatamente cuando está lleno en lugar de esperar.

**Parámetros**

| Parámetro | Predeterminado | Descripción |
|-----------|----------------|-------------|
| `capacity` | requerido | Número máximo de elementos que puede contener el búfer. Debe ser mayor que cero. |
| `multiConsumer` | `false` | Igual que el no acotado — habilita múltiples lectores concurrentes. |

**Elegir entre acotado y no acotado**

Usa `CreateBounded` cuando necesites aplicar contrapresión — es decir, cuando quieras que los productores se ralenticen automáticamente si los consumidores se quedan atrás. Usa `CreateUnbounded` cuando la tasa del productor sea naturalmente acotada (como eventos de entrada) y el almacenamiento en búfer ilimitado sea aceptable, o cuando ya hayas tenido en cuenta el crecimiento de memoria.

---

## Channel&lt;T&gt;

`Channel<T>` es un contenedor que expone los dos lados de la tubería como objetos separados.

```csharp
public sealed class Channel<T>
{
    public ChannelReader<T> Reader { get; }
    public ChannelWriter<T> Writer { get; }
}
```

Mantén la referencia `Writer` en el lado del productor y la referencia `Reader` en el lado del consumidor. No hay requisito de que estén en el mismo hilo.

---

## ChannelWriter&lt;T&gt;

`ChannelWriter<T>` es el lado de escritura del canal. Obtenlo de `channel.Writer`.

### TryWrite

```csharp
public abstract bool TryWrite(T item);
```

Intenta escribir un elemento sin suspender. Devuelve `true` si el elemento fue aceptado; devuelve `false` si el canal está lleno (acotado) o ha sido completado.

Usa `TryWrite` en caminos calientes donde puedes permitirte descartar elementos, o cuando haces polling en un bucle y quieres evitar asignaciones de máquinas de estados async.

```csharp
if (!channel.Writer.TryWrite(item))
{
    // canal lleno o cerrado — manejar apropiadamente
}
```

### WriteAsync

```csharp
public abstract VlkTask WriteAsync(T item);
```

Escribe un elemento en el canal, suspendiendo al llamador asincrónicamente si es necesario.

- **Canales no acotados**: siempre se completa sincrónicamente (camino rápido de cero asignaciones) siempre que el canal esté abierto.
- **Canales acotados**: se completa sincrónicamente cuando hay espacio en el búfer; suspende al llamador y encola un registro de escritor pendiente cuando el búfer está lleno. El llamador se reanuda tan pronto como un consumidor lee un elemento y libera una ranura.

Lanza `ChannelClosedException` si el canal ha sido completado vía `Complete()` antes o durante la escritura.

```csharp
await channel.Writer.WriteAsync(item);
```

Múltiples productores pueden llamar a `WriteAsync` concurrentemente en un canal acotado. Cada escritor suspendido se encola y se desbloquea en orden FIFO a medida que el espacio se vuelve disponible.

### Complete

```csharp
public abstract void Complete();
```

Señala que no se escribirán más elementos. Después de que se llama a `Complete()`:

- Cualquier elemento ya escrito permanece en el búfer y aún puede consumirse.
- Las nuevas llamadas a `WriteAsync` o `TryWrite` fallarán con `ChannelClosedException`.
- Una vez que el búfer está completamente drenado, `ChannelReader<T>.Completion` se completa y cualquier llamada a `ReadAsync` pendiente o futura lanza `ChannelClosedException`.

`Complete()` es idempotente en una sola llamada — llamarlo más de una vez es seguro (las llamadas posteriores se ignoran).

```csharp
// Señalar fin de trabajo
channel.Writer.Complete();
```

---

## ChannelReader&lt;T&gt;

`ChannelReader<T>` es el lado de lectura del canal. Obtenlo de `channel.Reader`.

### ReadAsync

```csharp
public abstract VlkTask<T> ReadAsync();
```

Lee el siguiente elemento del canal. Si no hay ningún elemento disponible actualmente, el llamador se suspende asincrónicamente hasta que llegue uno. Cuando el canal está tanto completado como completamente drenado, lanza `ChannelClosedException`.

```csharp
T item = await channel.Reader.ReadAsync();
```

**Modo de consumidor único** (predeterminado): solo puede haber un `ReadAsync` en vuelo a la vez. Intentar iniciar un segundo `ReadAsync` concurrente lanza inmediatamente. Esta restricción habilita una optimización interna de cero asignaciones — el núcleo del lector está incrustado directamente en la implementación del canal en lugar de asignarse desde un grupo por llamada.

**Modo multi-consumidor** (`multiConsumer: true`): cualquier número de llamadas a `ReadAsync` pueden estar pendientes simultáneamente. Cada llamador pendiente se encola y se resuelve en orden FIFO a medida que los elementos se vuelven disponibles.

### TryRead

```csharp
public abstract bool TryRead(out T item);
```

Intenta leer un elemento sin suspender. Devuelve `true` y rellena `item` si había un elemento disponible; devuelve `false` si el canal está vacío (establece `item` a `default`).

`TryRead` no distingue entre un canal vacío pero abierto y un canal vacío y cerrado. Usa `Completion` para detectar el estado cerrado cuando uses `TryRead` en un bucle de polling.

### ReadAllAsync

```csharp
public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default);
```

Devuelve un `IAsyncEnumerable<T>` que itera sobre todos los elementos hasta que el canal esté completado y drenado. La enumeración termina limpiamente sin propagar `ChannelClosedException` — simplemente se detiene.

```csharp
await foreach (var item in channel.Reader.ReadAllAsync(ct))
{
    Process(item);
}
// Llegado aquí cuando el canal está completo y vacío
```

El token de cancelación pasado a `ReadAllAsync` se usa como reserva. Si a `GetAsyncEnumerator` también se le pasa un token (como hace `await foreach` vía `WithCancellation`), el token del lado `foreach` tiene prioridad.

### Completion

```csharp
public abstract VlkTask Completion { get; }
```

Un `VlkTask` que se completa cuando el canal está completamente drenado después de que se ha llamado a `Complete()`. Específicamente:

- Si se llama a `Complete()` en un canal ya vacío, `Completion` se resuelve inmediatamente.
- Si se llama a `Complete()` mientras quedan elementos en el búfer, `Completion` se resuelve solo después de que se consume el último elemento.

Esperar `Completion` es la forma canónica de esperar a que una tubería termine.

```csharp
channel.Writer.Complete();
await channel.Reader.Completion;
// Todos los elementos han sido consumidos
```

---

## ChannelClosedException

```csharp
public sealed class ChannelClosedException : InvalidOperationException
```

Se lanza en dos circunstancias:

1. **Leer desde un canal completado y drenado** — `ReadAsync()` lanza cuando el canal ha sido marcado como completo y no quedan elementos.
2. **Escribir en un canal completado** — `WriteAsync()` lanza cuando `Complete()` fue llamado antes de la escritura.

`ChannelClosedException` hereda de `InvalidOperationException`. No es lanzado por `TryRead` o `TryWrite`, que devuelven `false` en su lugar.

Constructores:

```csharp
new ChannelClosedException()
new ChannelClosedException(string message)
new ChannelClosedException(Exception innerException)
```

---

## Canal Acotado: Contrapresión en Detalle

Cuando el búfer de un canal acotado está lleno y se llama a `WriteAsync`, el escritor se suspende y se encola internamente un registro de escritor pendiente. El escritor retiene su elemento. Cuando un consumidor llama a `ReadAsync` o `TryRead` y desencola un elemento:

1. La ranura liberada es inmediatamente reclamada por el escritor pendiente más antiguo.
2. El elemento de ese escritor se coloca en el búfer.
3. El código en espera del escritor se reanuda.

Esto significa que un canal acotado lleno nunca pierde elementos y nunca desperdicia capacidad de búfer — siempre hay una correspondencia uno a uno entre una ranura que se libera y un escritor bloqueado que se reanuda. En los casos donde un lector llega mientras los escritores pendientes están esperando pero el búfer está vacío, el elemento se entrega directamente sin tocar el búfer en absoluto.

---

## Patrones

### Productor/consumidor básico

```csharp
var channel = VlkTask.Channel.CreateUnbounded<WorkItem>();

// Productor (por ejemplo, se ejecuta en un hilo en segundo plano o desde callbacks)
async VlkTask ProduceAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var work = await FetchNextWorkItemAsync(ct);
        await channel.Writer.WriteAsync(work);
    }
    channel.Writer.Complete();
}

// Consumidor (se ejecuta donde elijas llamarlo)
async VlkTask ConsumeAsync()
{
    await foreach (var item in channel.Reader.ReadAllAsync())
    {
        await ProcessAsync(item);
    }
}
```

### Múltiples productores, consumidor único

Múltiples productores pueden tener cada uno una referencia a `channel.Writer` y llamar a `WriteAsync` concurrentemente. Todas las operaciones del canal están protegidas por un bloqueo interno, por lo que esto es seguro.

```csharp
var channel = VlkTask.Channel.CreateBounded<Event>(capacity: 64);

// Varios productores escribiendo concurrentemente
VlkTask ProducerA() => ProduceFrom(sourceA, channel.Writer);
VlkTask ProducerB() => ProduceFrom(sourceB, channel.Writer);
VlkTask ProducerC() => ProduceFrom(sourceC, channel.Writer);

// Consumidor único (predeterminado — no se necesita la bandera multiConsumer)
async VlkTask ConsumerAsync()
{
    await foreach (var ev in channel.Reader.ReadAllAsync())
        HandleEvent(ev);
}
```

Cuando uses múltiples productores con un canal acotado, coordina `Complete()` cuidadosamente — solo llámalo después de que todos los productores hayan terminado de escribir, de lo contrario algunos escritores pueden recibir `ChannelClosedException`.

### Múltiples productores, múltiples consumidores

```csharp
// multiConsumer: true habilita ReadAsync concurrente desde varios consumidores
var channel = VlkTask.Channel.CreateUnbounded<Job>(multiConsumer: true);

async VlkTask WorkerAsync(int id, CancellationToken ct)
{
    try
    {
        while (true)
        {
            var job = await channel.Reader.ReadAsync();
            await ExecuteJobAsync(job, ct);
        }
    }
    catch (ChannelClosedException)
    {
        // El canal terminó — salir con gracia
    }
}
```

Cada elemento se entrega a exactamente un consumidor. Los consumidores compiten por elementos en orden FIFO (el consumidor que ha estado esperando más tiempo obtiene el siguiente elemento disponible).

### Apagado con gracia

La secuencia de apagado recomendada es:

1. Señalar a todos los productores que paren (por ejemplo, cancelar su `CancellationToken`).
2. Llamar a `channel.Writer.Complete()` después de que todos los productores hayan dejado de escribir.
3. Esperar `channel.Reader.Completion` para confirmar que todos los elementos han sido consumidos.

```csharp
cts.Cancel();                        // detener productores
await allProducersTask;              // esperar a que salgan
channel.Writer.Complete();           // sellar el canal
await channel.Reader.Completion;     // drenar los elementos restantes
```

Si estás usando `ReadAllAsync`, el paso 3 ocurre automáticamente — el bucle `await foreach` sale cuando el canal está completo y vacío.

---

## Comparación con System.Threading.Channels

| Característica | Canales Valkarn | System.Threading.Channels |
|----------------|-----------------|---------------------------|
| Tipo de retorno | `VlkTask` / `VlkTask<T>` | `ValueTask` / `ValueTask<T>` |
| Asignación (camino caliente) | Cero (no acotado de consumidor único) | Casi cero |
| `WaitToReadAsync` | No presente — usa `ReadAsync` o `ReadAllAsync` | Presente |
| `TryComplete(Exception)` | No presente — usa `Complete()` | Presente |
| `Count` / `CanCount` | No expuesto | Presente en algunos tipos de canal |
| Política de descarte cuando está lleno | No compatible — `WriteAsync` bloquea | `DropWrite`, `DropNewest`, `DropOldest`, `Wait` |
| Enumerable async | `ReadAllAsync()` | `ReadAllAsync()` |
| Seguridad de hilos | Completa (basada en bloqueo) | Completa (basada en bloqueo) |

La diferencia principal es que los Canales Valkarn se integran nativamente con `VlkTask` para espera de cero sobrecarga en compilaciones de Unity, y el camino de consumidor único evita un alquiler/retorno de grupo en cada llamada a `ReadAsync` al incrustar el núcleo de completado directamente en el canal.
