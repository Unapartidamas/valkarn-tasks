---
sidebar_position: 1
title: VlkTask
---

# VlkTask

`VlkTask` é o tipo awaitable principal. É uma `readonly struct` — sem alocação no heap no caminho feliz (quando a task é concluída de forma síncrona ou via pool).

```csharp
namespace UnaPartidaMas.Valkarn.Tasks;

public readonly struct VlkTask : IEquatable<VlkTask>
```

---

## Métodos factory estáticos

### Delay

```csharp
// Milissegundos (usa PlayerLoopTiming.Update por padrão)
VlkTask VlkTask.Delay(int millisecondsDelay)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(int millisecondsDelay, CancellationToken cancellationToken)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing, CancellationToken cancellationToken)

// Sobrecargas com TimeSpan
VlkTask VlkTask.Delay(TimeSpan delay)
VlkTask VlkTask.Delay(TimeSpan delay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(TimeSpan delay, CancellationToken cancellationToken)
```

### Yield

```csharp
// Ceder para o próximo frame (timing Update por padrão)
VlkTask VlkTask.Yield()
VlkTask VlkTask.Yield(PlayerLoopTiming timing)
VlkTask VlkTask.Yield(CancellationToken cancellationToken)
```

### WhenAll

```csharp
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2)
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2, VlkTask task3)
// ... até 8 parâmetros

// Com valores de retorno — desestruturação de tupla suportada
VlkTask<(T1, T2)>     VlkTask.WhenAll<T1, T2>(VlkTask<T1>, VlkTask<T2>)
VlkTask<(T1, T2, T3)> VlkTask.WhenAll<T1, T2, T3>(...)
// ... até 8

// Sobrecargas de coleção
VlkTask VlkTask.WhenAll(IEnumerable<VlkTask> tasks)
VlkTask<T[]> VlkTask.WhenAll<T>(IEnumerable<VlkTask<T>> tasks)
```

### WhenAny

```csharp
VlkTask<int> VlkTask.WhenAny(VlkTask task1, VlkTask task2)   // retorna índice da primeira concluída
VlkTask<T>   VlkTask.WhenAny<T>(VlkTask<T> task1, VlkTask<T> task2) // retorna valor da primeira
```

### Troca de thread

```csharp
VlkTask VlkTask.SwitchToMainThread()
VlkTask VlkTask.SwitchToThreadPool()
VlkTask VlkTask.SwitchToSynchronizationContext(SynchronizationContext context)
```

### Concluída / Nunca

```csharp
VlkTask VlkTask.CompletedTask      // pré-concluída, zero alocação
VlkTask<T> VlkTask.FromResult<T>(T value)
VlkTask VlkTask.FromCanceled(CancellationToken ct)
VlkTask<T> VlkTask.FromCanceled<T>(CancellationToken ct)
VlkTask VlkTask.FromException(Exception ex)
VlkTask<T> VlkTask.FromException<T>(Exception ex)
VlkTask VlkTask.Never                // nunca conclui
```

### Run

```csharp
// Executa delegate no thread pool
VlkTask VlkTask.Run(Action action)
VlkTask VlkTask.Run(Func<VlkTask> factory)
VlkTask<T> VlkTask.Run<T>(Func<T> func)
VlkTask<T> VlkTask.Run<T>(Func<VlkTask<T>> factory)
```

### Forget

```csharp
// Fire-and-forget: suprimir aviso CS4014 com segurança
void VlkTask.Forget(VlkTask task)
void VlkTask.Forget(VlkTask task, Action<Exception> exceptionHandler)
```

---

## Membros de instância

```csharp
VlkTaskStatus Status { get; }
bool IsCompleted { get; }
bool IsCompletedSuccessfully { get; }
bool IsFaulted { get; }
bool IsCanceled { get; }

// Obter resultado (lança se faulted/cancelado)
void GetResult()

// Awaiter
VlkTaskAwaiter GetAwaiter()

// Converter para ValueTask
ValueTask AsValueTask()
```

---

## Condições de espera

```csharp
// Aguardar até que uma condição seja verdadeira
VlkTask VlkTask.WaitUntil(Func<bool> condition)
VlkTask VlkTask.WaitUntil(Func<bool> condition, PlayerLoopTiming timing)

// Aguardar enquanto uma condição for verdadeira
VlkTask VlkTask.WaitWhile(Func<bool> condition)

// Aguardar um número fixo de frames
VlkTask VlkTask.WaitForFrames(int frameCount)
VlkTask VlkTask.NextFrame()
```

---

## Diagnósticos de pool

```csharp
// Retorna (Type type, int currentSize, int maxSize) por pool
IEnumerable<(Type, int, int)> VlkTask.GetPoolInfo()
```

Disponível na janela **Window → Valkarn Tasks → Task Tracker**.
