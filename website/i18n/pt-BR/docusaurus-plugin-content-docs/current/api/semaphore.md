---
sidebar_position: 7
title: ValkarnSemaphore
---

# ValkarnSemaphore

`ValkarnSemaphore` é um semáforo assíncrono de alocação zero construído sobre `ValkarnTask`. Funciona como `SemaphoreSlim`, mas se integra nativamente ao pipeline do Valkarn Tasks — sem alocações de `Task`, sem pressão no GC em estado estável.

```csharp
namespace UnaPartidaMas.Valkarn.Tasks
```

**Caminho rápido (sem contenção):** retorna `ValkarnTask.CompletedTask` imediatamente — alocação zero.
**Caminho lento (com contenção):** os nós de espera são alugados de um pool limitado e devolvidos ao concluir — GC zero em estado estável.

---

## Construtor

```csharp
public ValkarnSemaphore(int initialCount, int maxCount)
```

| Parâmetro | Descrição |
|-----------|-------------|
| `initialCount` | Número de slots disponíveis imediatamente. Deve estar em `[0, maxCount]`. |
| `maxCount` | Limite superior para a contagem de slots. Deve ser > 0. |

---

## Propriedades

```csharp
public int CurrentCount { get; } // Slots disponíveis agora (thread-safe)
```

---

## Métodos

### WaitAsync

```csharp
public ValkarnTask WaitAsync(CancellationToken ct = default)
```

Adquire um slot de forma assíncrona. Retorna um `ValkarnTask` concluído imediatamente se um slot estiver disponível (alocação zero). Suspende o chamador até que `Release()` seja chamado se não houver slot disponível. Os waiters são atendidos em ordem FIFO.

Lança `OperationCanceledException` se `ct` for acionado enquanto aguarda.

### Wait

```csharp
public void Wait(CancellationToken ct = default)
```

Variante síncrona. Bloqueia o thread chamador se não houver slot disponível. Use somente quando não for possível usar `await` (p. ex. pontos de entrada não assíncronos).

### Release

```csharp
public void Release(int releaseCount = 1)
```

Devolve `releaseCount` slots. Os waiters pendentes são concluídos em ordem FIFO, fora do lock interno — as continuações nunca são executadas com o lock mantido.

Lança `InvalidOperationException` se a liberação excedesse `maxCount`.

### Dispose

```csharp
public void Dispose()
```

Cancela todos os waiters pendentes e libera recursos. Idempotente.

---

## Uso

### Exclusão mútua (1 slot)

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

### Concorrência limitada (N slots)

```csharp
// Permite até 4 requisições de rede concorrentes.
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

### Portão produtor / consumidor

```csharp
// Começa com 0 slots — o consumidor aguarda até que o produtor sinalize.
var gate = new ValkarnSemaphore(initialCount: 0, maxCount: 1);

async ValkarnTask ProducerAsync(CancellationToken ct)
{
    await ProduceDataAsync(ct);
    gate.Release(); // sinalizar o consumidor
}

async ValkarnTask ConsumerAsync(CancellationToken ct)
{
    await gate.WaitAsync(ct); // aguardar o sinal
    await ConsumeDataAsync(ct);
}
```

---

## Segurança de threads

Todas as operações são thread-safe. `Release()` pode ser chamado de qualquer thread, incluindo threads em segundo plano. As conclusões são despachadas fora do lock interno — uma continuação que reentra no semáforo não causará deadlock.

---

## Comparação com SemaphoreSlim

| | `ValkarnSemaphore` | `SemaphoreSlim` |
|---|---|---|
| Alocação no caminho rápido | Zero | Zero |
| Alocação no caminho lento | Nó em pool, GC zero | Alocação de `Task` |
| Integração com ValkarnTask | Sim | Requer `.AsValkarnTask()` |
| `IDisposable` | Sim | Sim |
| Ordenação FIFO | Sim | Sim |
| Cancelamento | Sim | Sim |
