---
sidebar_position: 7
title: ValkarnSemaphore
---

# ValkarnSemaphore

`ValkarnSemaphore` es un semáforo asíncrono de asignación cero construido sobre `ValkarnTask`. Funciona como `SemaphoreSlim` pero se integra de forma nativa con el pipeline de Valkarn Tasks — sin asignaciones de `Task`, sin presión sobre el GC en estado estable.

```csharp
namespace UnaPartidaMas.Valkarn.Tasks
```

**Ruta rápida (sin contención):** devuelve `ValkarnTask.CompletedTask` inmediatamente — asignación cero.
**Ruta lenta (con contención):** los nodos de espera se toman prestados de un pool acotado y se devuelven al completarse — GC cero en estado estable.

---

## Constructor

```csharp
public ValkarnSemaphore(int initialCount, int maxCount)
```

| Parámetro | Descripción |
|-----------|-------------|
| `initialCount` | Número de ranuras disponibles de inmediato. Debe estar en `[0, maxCount]`. |
| `maxCount` | Límite superior para el recuento de ranuras. Debe ser > 0. |

---

## Propiedades

```csharp
public int CurrentCount { get; } // Ranuras disponibles ahora mismo (thread-safe)
```

---

## Métodos

### WaitAsync

```csharp
public ValkarnTask WaitAsync(CancellationToken ct = default)
```

Adquiere una ranura de forma asíncrona. Devuelve un `ValkarnTask` completado inmediatamente si hay una ranura disponible (asignación cero). Suspende al llamante hasta que se llame a `Release()` si no hay ranuras disponibles. Los waiters se atienden en orden FIFO.

Lanza `OperationCanceledException` si `ct` se activa mientras se espera.

### Wait

```csharp
public void Wait(CancellationToken ct = default)
```

Variante síncrona. Bloquea el hilo llamante si no hay ninguna ranura disponible. Úsela solo cuando no pueda usar `await` (p. ej. puntos de entrada no asíncronos).

### Release

```csharp
public void Release(int releaseCount = 1)
```

Devuelve `releaseCount` ranuras. Los waiters pendientes se completan en orden FIFO, fuera del lock interno — las continuaciones nunca se ejecutan con el lock tomado.

Lanza `InvalidOperationException` si la liberación excedería `maxCount`.

### Dispose

```csharp
public void Dispose()
```

Cancela todos los waiters pendientes y libera recursos. Es idempotente.

---

## Uso

### Exclusión mutua (1 ranura)

```csharp
var sem = new ValkarnSemaphore(initialCount: 1, maxCount: 1);

async ValkarnTask WriteFileAsync(string path, string content, CancellationToken ct)
{
    await sem.WaitAsync(ct);
    try
    {
        await File.WriteAllTextAsync(path, content, ct);
    }
    finally
    {
        sem.Release();
    }
}
```

### Concurrencia acotada (N ranuras)

```csharp
// Permite hasta 4 solicitudes de red concurrentes.
var sem = new ValkarnSemaphore(initialCount: 4, maxCount: 4);

async ValkarnTask FetchAsync(string url, CancellationToken ct)
{
    await sem.WaitAsync(ct);
    try
    {
        await DownloadAsync(url, ct);
    }
    finally
    {
        sem.Release();
    }
}
```

### Compuerta productor / consumidor

```csharp
// Comienza con 0 ranuras — el consumidor espera hasta que el productor señalice.
var gate = new ValkarnSemaphore(initialCount: 0, maxCount: 1);

async ValkarnTask ProducerAsync(CancellationToken ct)
{
    await ProduceDataAsync(ct);
    gate.Release(); // señalizar al consumidor
}

async ValkarnTask ConsumerAsync(CancellationToken ct)
{
    await gate.WaitAsync(ct); // esperar la señal
    await ConsumeDataAsync(ct);
}
```

---

## Seguridad de hilos

Todas las operaciones son thread-safe. `Release()` puede llamarse desde cualquier hilo, incluidos los hilos en segundo plano. Las completaciones se despachan fuera del lock interno — una continuación que reingresa al semáforo no producirá un deadlock.

---

## Comparación con SemaphoreSlim

| | `ValkarnSemaphore` | `SemaphoreSlim` |
|---|---|---|
| Asignación en ruta rápida | Cero | Cero |
| Asignación en ruta lenta | Nodo en pool, GC cero | Asignación de `Task` |
| Se integra con ValkarnTask | Sí | Requiere `.AsValkarnTask()` |
| `IDisposable` | Sí | Sí |
| Orden FIFO | Sí | Sí |
| Cancelación | Sí | Sí |
