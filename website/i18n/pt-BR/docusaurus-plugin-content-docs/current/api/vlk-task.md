---
sidebar_position: 1
title: ValkarnTask
---

# ValkarnTask

`ValkarnTask` é o tipo awaitable principal. É uma `readonly struct` — sem alocação no heap no caminho feliz (quando a task é concluída de forma síncrona ou via pool).

```csharp
namespace UnaPartidaMas.Valkarn.Tasks;

public readonly struct ValkarnTask : IEquatable<ValkarnTask>
```

---

## Métodos factory estáticos

### Delay

```csharp
// Milissegundos (usa PlayerLoopTiming.Update por padrão)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, CancellationToken cancellationToken)
ValkarnTask ValkarnTask.Delay(int millisecondsDelay, PlayerLoopTiming timing, CancellationToken cancellationToken)

// Sobrecargas com TimeSpan
ValkarnTask ValkarnTask.Delay(TimeSpan delay)
ValkarnTask ValkarnTask.Delay(TimeSpan delay, PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Delay(TimeSpan delay, CancellationToken cancellationToken)
```

### Yield

```csharp
// Ceder para o próximo frame (timing Update por padrão)
ValkarnTask ValkarnTask.Yield()
ValkarnTask ValkarnTask.Yield(PlayerLoopTiming timing)
ValkarnTask ValkarnTask.Yield(CancellationToken cancellationToken)
```

### WhenAll

```csharp
ValkarnTask ValkarnTask.WhenAll(ValkarnTask task1, ValkarnTask task2)
ValkarnTask ValkarnTask.WhenAll(ValkarnTask task1, ValkarnTask task2, ValkarnTask task3)
// ... até 8 parâmetros

// Com valores de retorno — desestruturação de tupla suportada
ValkarnTask<(T1, T2)>     ValkarnTask.WhenAll<T1, T2>(ValkarnTask<T1>, ValkarnTask<T2>)
ValkarnTask<(T1, T2, T3)> ValkarnTask.WhenAll<T1, T2, T3>(...)
// ... até 8

// Sobrecargas de coleção
ValkarnTask ValkarnTask.WhenAll(IEnumerable<ValkarnTask> tasks)
ValkarnTask<T[]> ValkarnTask.WhenAll<T>(IEnumerable<ValkarnTask<T>> tasks)
```

### WhenAny

```csharp
ValkarnTask<int> ValkarnTask.WhenAny(ValkarnTask task1, ValkarnTask task2)   // retorna índice da primeira concluída
ValkarnTask<T>   ValkarnTask.WhenAny<T>(ValkarnTask<T> task1, ValkarnTask<T> task2) // retorna valor da primeira
```

### Troca de thread

```csharp
ValkarnTask ValkarnTask.SwitchToMainThread()
ValkarnTask ValkarnTask.SwitchToThreadPool()
ValkarnTask ValkarnTask.SwitchToSynchronizationContext(SynchronizationContext context)
```

### Concluída / Nunca

```csharp
ValkarnTask ValkarnTask.CompletedTask      // pré-concluída, zero alocação
ValkarnTask<T> ValkarnTask.FromResult<T>(T value)
ValkarnTask ValkarnTask.FromCanceled(CancellationToken ct)
ValkarnTask<T> ValkarnTask.FromCanceled<T>(CancellationToken ct)
ValkarnTask ValkarnTask.FromException(Exception ex)
ValkarnTask<T> ValkarnTask.FromException<T>(Exception ex)
ValkarnTask ValkarnTask.Never                // nunca conclui
```

### Run

```csharp
// Executa delegate no thread pool
ValkarnTask ValkarnTask.Run(Action action)
ValkarnTask ValkarnTask.Run(Func<ValkarnTask> factory)
ValkarnTask<T> ValkarnTask.Run<T>(Func<T> func)
ValkarnTask<T> ValkarnTask.Run<T>(Func<ValkarnTask<T>> factory)
```

### Forget

```csharp
// Fire-and-forget: suprimir aviso CS4014 com segurança
void ValkarnTask.Forget(ValkarnTask task)
void ValkarnTask.Forget(ValkarnTask task, Action<Exception> exceptionHandler)
```

---

## Membros de instância

```csharp
ValkarnTaskStatus Status { get; }
bool IsCompleted { get; }
bool IsCompletedSuccessfully { get; }
bool IsFaulted { get; }
bool IsCanceled { get; }

// Obter resultado (lança se faulted/cancelado)
void GetResult()

// Awaiter
ValkarnTaskAwaiter GetAwaiter()

// Converter para ValueTask
ValueTask AsValueTask()
```

---

## Condições de espera

```csharp
// Aguardar até que uma condição seja verdadeira
ValkarnTask ValkarnTask.WaitUntil(Func<bool> condition)
ValkarnTask ValkarnTask.WaitUntil(Func<bool> condition, PlayerLoopTiming timing)

// Aguardar enquanto uma condição for verdadeira
ValkarnTask ValkarnTask.WaitWhile(Func<bool> condition)

// Aguardar um número fixo de frames
ValkarnTask ValkarnTask.WaitForFrames(int frameCount)
ValkarnTask ValkarnTask.NextFrame()
```

---

## Diagnósticos de pool

```csharp
// Retorna (Type type, int currentSize, int maxSize) por pool
IEnumerable<(Type, int, int)> ValkarnTask.GetPoolInfo()
```

Disponível na janela **Window → Valkarn Tasks → Task Tracker**.
