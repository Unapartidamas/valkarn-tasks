---
sidebar_position: 2
title: Bridges para Job & Awaitable
---

# Bridges para Job & Awaitable

O Valkarn Tasks fornece um conjunto de tipos bridge que conectam o Sistema de Jobs do Unity e a API `Awaitable` ao pipeline `VlkTask`. Cada bridge é uma camada fina que minimiza alocações; não há mágica oculta.

Todos os tipos descritos aqui estão no namespace `UnaPartidaMas.Valkarn.Tasks.Bridge` e são protegidos por `#if UNITY_5_3_OR_NEWER` (ou `#if UNITY_2023_1_OR_NEWER` para suporte ao Awaitable).

---

## JobHandleExtensions — aguardar um único JobHandle

A bridge mais simples. Chame `.ToVlkTask()` em qualquer `JobHandle` para obter de volta um `VlkTask` que completa quando o job termina.

```csharp
public static VlkTask ToVlkTask(
    this JobHandle handle,
    PlayerLoopTiming timing          = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

### Como funciona

1. **Caminho rápido.** Se `handle.IsCompleted` já é verdadeiro, `handle.Complete()` é chamado imediatamente e `VlkTask.CompletedTask` é retornado — zero alocação, sem registro no PlayerLoop.
2. **Caminho normal.** Um `JobHandlePromise` em pool é alugado, registrado no PlayerLoop no `timing` dado e retornado envolvido em um `VlkTask`. A cada frame `MoveNext()` chama `JobHandle.ScheduleBatchedJobs()` (para liberar a fila de jobs no modo editor e modo batch) e então verifica `handle.IsCompleted`. Quando o handle está concluído, a promise conclui a task e retorna a si mesma ao pool.
3. **Cancelamento.** Se o `CancellationToken` disparar, o handle é forçosamente concluído (`handle.Complete()` é sempre chamado para evitar vazamentos do sistema de jobs) e a task transita para o estado cancelado.

### Uso básico

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Jobs;

// Agendar um job e aguardá-lo imediatamente.
var handle = myJob.Schedule();
await handle.ToVlkTask();

// Com timing não padrão e cancelamento.
await handle.ToVlkTask(PlayerLoopTiming.PreUpdate, destroyCancellationToken);
```

---

## JobHandleWhenAll — aguardar múltiplos JobHandles em paralelo

Quando você precisa agendar vários jobs independentes e retomar apenas após todos eles serem concluídos, use `JobHandleExtensions.WhenAll`.

```csharp
// Sobrecarga mais simples: aguarda todos os handles no timing Update.
public static VlkTask WhenAll(params JobHandle[] handles)

// Sobrecarga completa: timing e cancelamento configuráveis.
public static VlkTask WhenAll(
    JobHandle[]       handles,
    PlayerLoopTiming  timing,
    CancellationToken cancellationToken = default)

// Alias de método de extensão para a sobrecarga completa.
public static VlkTask ToVlkTask(
    this JobHandle[]  handles,
    PlayerLoopTiming  timing              = PlayerLoopTiming.Update,
    CancellationToken cancellationToken   = default)
```

### Como funciona

- **Caminho rápido.** Se cada handle no array já estiver concluído, todos os handles são finalizados e `VlkTask.CompletedTask` é retornado imediatamente.
- **Array vazio.** Retorna `VlkTask.CompletedTask`.
- **Caminho normal.** Um `JobHandleArrayPromise` em pool é criado. Internamente ele aluga um `JobHandle[]` de `ArrayPool<JobHandle>.Shared` (evitando alocação no heap por chamada), copia os handles de entrada e registra no PlayerLoop. A cada frame itera apenas os handles ainda pendentes usando um loop de swap-e-redução compacto e chama `JobHandle.ScheduleBatchedJobs()` para manter os workers executando.
- **Cancelamento.** Todos os handles restantes são forçosamente concluídos e a task é cancelada.

### Uso

```csharp
var handleA = jobA.Schedule();
var handleB = jobB.Schedule();
var handleC = jobC.Schedule();

// Aguardar os três.
await JobHandleExtensions.WhenAll(handleA, handleB, handleC);

// Ou usando o método de extensão em um array.
JobHandle[] handles = { handleA, handleB, handleC };
await handles.ToVlkTask();
```

---

## TempNativeArrayScope — tempo de vida de NativeArray entre pontos de await

### O problema

`NativeArray<T>` alocado com `Allocator.TempJob` tem um tempo de vida curto. Se você alocar um, agendar um job, `await` o handle do job e então esquecer de descartar o array, o sistema de segurança do Unity reportará um vazamento de memória. Usar um `try`/`finally` simples funciona, mas é fácil de errar em um método async longo.

`TempNativeArrayScope<T>` é uma struct que envolve um `NativeArray<T>` e o descarta quando o escopo termina, usando a declaração `using` — o padrão RAII aplicado à memória nativa.

### API

```csharp
public struct TempNativeArrayScope<T> : IDisposable where T : struct
{
    // Acessa o array envolvido. Lança ObjectDisposedException se já descartado.
    public NativeArray<T> Array { get; }

    // True se o escopo não foi descartado e o array foi criado.
    public bool IsCreated { get; }

    // Aloca um novo NativeArray<T> com Allocator.TempJob e assume a propriedade.
    public static TempNativeArrayScope<T> Create(int length);

    // Assume a propriedade de um NativeArray<T> já alocado.
    public static TempNativeArrayScope<T> Wrap(NativeArray<T> existing);

    // Descarta o array. Idempotente: seguro para chamar múltiplas vezes.
    public void Dispose();
}

// Helper não-genérico (conveniência de inferência de tipo).
public static class TempNativeArrayScope
{
    public static TempNativeArrayScope<T> Create<T>(int length) where T : struct;
    public static TempNativeArrayScope<T> Wrap<T>(NativeArray<T> existing) where T : struct;
}
```

`Dispose` usa uma flag `int` simples em vez de `Interlocked` porque o escopo é projetado para uso single-threaded na thread principal via `using var`.

### Uso

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Collections;
using Unity.Jobs;

async VlkTask ProcessDataAsync(int count, CancellationToken ct)
{
    // A declaração using garante que Dispose() seja chamado quando o escopo sair,
    // seja por conclusão normal, exceção ou cancelamento.
    using var inputScope  = TempNativeArrayScope.Create<float>(count);
    using var outputScope = TempNativeArrayScope.Create<float>(count);

    NativeArray<float> input  = inputScope.Array;
    NativeArray<float> output = outputScope.Array;

    // Preencher input, agendar o job.
    var job = new MyProcessingJob { Input = input, Output = output };
    var handle = job.Schedule(count, 64);

    // Aguardar sem bloquear a thread principal.
    // Os NativeArrays permanecem válidos — o job ainda está executando.
    await handle.ToVlkTask(cancellationToken: ct);

    // O job está concluído. Ler resultados aqui.
    float total = 0f;
    for (int i = 0; i < count; i++)
        total += output[i];

    UnityEngine.Debug.Log($"Soma: {total}");

    // inputScope.Dispose() e outputScope.Dispose() executam automaticamente aqui.
}
```

Você também pode envolver um array que já alocou:

```csharp
var existing = new NativeArray<int>(1024, Allocator.TempJob);
using var scope = TempNativeArrayScope.Wrap(existing);
// scope é dono de existing e irá descartá-lo.
```

### Armadilha comum: tempo de vida de NativeArray sem um escopo

Este padrão está errado e causará um erro do sistema de segurança:

```csharp
// ERRADO: o array pode sobreviver ao job ou ser vazado se uma exceção ocorrer.
var array = new NativeArray<float>(1024, Allocator.TempJob);
var handle = job.Schedule(1024, 64);
await handle.ToVlkTask(); // ponto de suspensão — o array deve permanecer vivo
array.Dispose();          // nunca alcançado se uma exceção disparar acima
```

Use `TempNativeArrayScope` ou um `try`/`finally` para garantir o descarte em todos os caminhos de código.

---

## AwaitableBridge — convertendo Unity Awaitable para VlkTask

`AwaitableBridge` fornece métodos de extensão para converter os tipos `Awaitable` e `Awaitable<T>` do Unity (disponíveis desde Unity 2023.1) em awaiters compatíveis com `VlkTask`.

**Nota:** `Awaitable` também tem seu próprio `GetAwaiter()`. Como a resolução de sobrecarga do C# sempre prefere métodos de instância a métodos de extensão, escrever `await myAwaitable` dentro de um método `async VlkTask` já funciona corretamente — o awaiter do Unity implementa `ICriticalNotifyCompletion` e o builder `VlkTask` o aceita. Os métodos de extensão `.AsVlkTask()` são necessários apenas quando você quer passar um `Awaitable` para um combinador (`VlkTask.WhenAll`, `VlkTask.WhenAny`) ou armazená-lo como uma variável `VlkTask`.

Este arquivo é protegido por `#if UNITY_2023_1_OR_NEWER`.

### API

```csharp
// Converter Awaitable para um awaiter compatível com VlkTask.
public static AwaitableVlkTaskAwaiter   AsVlkTask(this Awaitable awaitable)

// Converter Awaitable<T> para um awaiter compatível com VlkTask.
public static AwaitableVlkTaskAwaiter<T> AsVlkTask<T>(this Awaitable<T> awaitable)
```

Ambos os awaiters implementam `ICriticalNotifyCompletion`, que ignora a captura de `ExecutionContext`. Eles delegam `IsCompleted`, `GetResult` e `OnCompleted` diretamente ao awaiter Unity envolvido.

### Uso

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnityEngine;

// Await direto — funciona sem conversão em um método async VlkTask.
async VlkTask DirectExample()
{
    await Awaitable.NextFrameAsync();       // Sem conversão necessária.
    await Awaitable.WaitForSecondsAsync(1f);
}

// Conversão explícita — necessária para combinadores e armazenamento.
async VlkTask CombinatorExample()
{
    VlkTask a = Awaitable.NextFrameAsync().AsVlkTask();
    VlkTask b = Awaitable.WaitForSecondsAsync(2f).AsVlkTask();
    await VlkTask.WhenAll(a, b);
}

// Versão genérica com um tipo de resultado.
async VlkTask<Texture2D> LoadTextureExample(string path)
{
    Awaitable<Texture2D> loadOp = LoadTextureAsync(path);
    return await loadOp.AsVlkTask();
}
```

---

## JobBridge — o wrapper gerado por código-fonte

`JobBridge.cs` define `JobPromise<TJob>`, o tipo de promise em pool genérico usado pelo gerador de código-fonte. É um detalhe de implementação; você normalmente não o instancia diretamente.

```csharp
// Faz polling de um JobHandle a cada frame. Usado pelos métodos ScheduleAsync gerados por código-fonte.
public sealed class JobPromise<TJob> : VlkTask.ISource, IPlayerLoopItem, IPoolNode<JobPromise<TJob>>
    where TJob : struct
{
    public static JobPromise<TJob> Create(JobHandle handle, CancellationToken ct, out uint token);
}
```

O comportamento é idêntico ao `JobHandlePromise` (veja `JobHandleExtensions`), exceto que é genérico sobre o tipo de job para isolamento de pool — cada tipo de job tem seu próprio pool.

---

## Gerador de código-fonte: JobBridgeGenerator

O `JobBridgeGenerator` é um gerador de código-fonte Roslyn incremental (classe `UnaPartidaMas.Valkarn.Tasks.SourceGen.Generators.JobBridgeGenerator`) que produz automaticamente métodos de extensão `ScheduleAsync` para seus tipos de job.

### O que ele detecta

O gerador varre todas as structs **públicas** na compilação que implementam uma das seguintes:
- `Unity.Jobs.IJob`
- `Unity.Jobs.IJobParallelFor`
- `Unity.Jobs.IJobFor`

Structs de job privadas e internas são ignoradas. Se uma struct está aninhada dentro de um tipo não-público, também é ignorada.

O gerador não faz nada se `UnaPartidaMas.Valkarn.Tasks.ValkarnTask` não for encontrado na compilação, portanto é seguro em assemblies que não referenciam o Valkarn Tasks.

### O que ele gera

O arquivo de saída é `ValkarnTask.JobBridge.Generated.g.cs`. Para cada tipo de job detectado, emite uma `public static class __<TypeName>_AsyncExt` contendo:

| Interface de job | Assinatura do método gerado |
|---|---|
| `IJob` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, CancellationToken ct = default)` |
| `IJobParallelFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleParallelAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |

Cada método gerado agenda o job usando os métodos de extensão Unity padrão, então envolve o `JobHandle` resultante em um `JobPromise<TJob>` e retorna um `ValkarnTask`.

Para tipos aninhados (ex.: uma struct de job dentro de uma classe externa), o nome da classe gerada usa sublinhados: `__Outer_Inner_AsyncExt`.

### Uso dos métodos gerados

```csharp
// Exemplo IJob
public struct MyCalculationJob : IJob
{
    public NativeArray<float> Data;
    public void Execute() { /* ... */ }
}

// O gerador produz:
//   public static ValkarnTask ScheduleAsync(this ref MyCalculationJob job, CancellationToken ct = default)

async VlkTask RunCalculation(CancellationToken ct)
{
    using var scope = TempNativeArrayScope.Create<float>(1024);
    var job = new MyCalculationJob { Data = scope.Array };
    await job.ScheduleAsync(ct);  // método de extensão gerado
    // Ler resultados de scope.Array aqui.
}

// Exemplo IJobParallelFor
public struct MyParallelJob : IJobParallelFor
{
    public NativeArray<float> Input;
    public NativeArray<float> Output;
    public void Execute(int index) { Output[index] = Input[index] * 2f; }
}

async VlkTask RunParallel(int length, CancellationToken ct)
{
    using var inputScope  = TempNativeArrayScope.Create<float>(length);
    using var outputScope = TempNativeArrayScope.Create<float>(length);
    var job = new MyParallelJob { Input = inputScope.Array, Output = outputScope.Array };
    await job.ScheduleAsync(length, innerLoopBatchCount: 64, ct);
}
```

---

## Gerador de código-fonte: AwaitableBridgeGenerator

O `AwaitableBridgeGenerator` detecta se `UnityEngine.Awaitable` e `UnityEngine.Awaitable<T>` estão presentes na compilação e emite métodos de extensão `AsValkarnTask()` quando estão.

O arquivo de saída é `ValkarnTask.AwaitableBridge.Generated.g.cs`. O código gerado vive em `namespace UnaPartidaMas.Valkarn.Tasks.Bridge` sob a classe `AwaitableBridgeExtensions`.

Métodos gerados:

```csharp
// Emitido quando UnityEngine.Awaitable é encontrado:
public static async ValkarnTask AsValkarnTask(this Awaitable awaitable)
{
    await awaitable;
}

// Emitido quando UnityEngine.Awaitable<T> é encontrado:
public static async ValkarnTask<T> AsValkarnTask<T>(this Awaitable<T> awaitable)
{
    return await awaitable;
}
```

Estes são métodos `async ValkarnTask`, portanto passam pelo builder async em pool do Valkarn Tasks — em um pool aquecido são de zero alocação.

O gerador é protegido: se `ValkarnTask` não estiver na compilação, nenhum código é emitido. Isso evita erros CS0246 em assemblies que referenciam o Unity mas não o Valkarn Tasks.

---

## Exemplo completo funcional: bridge de job em um sistema ECS

O seguinte é de `Samples~/ECS/JobBridgeExample.cs`. Mostra o padrão completo para agendar um job paralelo Burst de um `ISystem`, aguardá-lo sem bloquear e escrever resultados de volta.

```csharp
#if UNITY_5_3_OR_NEWER && VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnaPartidaMas.Valkarn.Tasks.ECS;

public partial struct JobBridgeExample : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<HealthData>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var worldCt = state.World.GetWorldCancellationToken();

        // Extrair todos os dados de entidade de forma síncrona dentro de OnUpdate.
        // Métodos async não podem ter parâmetros ref (CS1988), portanto os dados
        // devem ser copiados aqui e passados por valor para o método async.
        var query       = SystemAPI.QueryBuilder().WithAll<HealthData>().Build();
        var entityCount = query.CalculateEntityCount();
        if (entityCount == 0) return;

        var entities    = query.ToEntityArray(Allocator.TempJob);
        var healthArray = query.ToComponentDataArray<HealthData>(Allocator.TempJob);
        var results     = new NativeArray<float>(entityCount, Allocator.TempJob);

        // O método async assume a propriedade dos NativeArrays e os descarta.
        ProcessHealthAsync(state.EntityManager, entities, healthArray, results, worldCt).Forget();
        state.Enabled = false;
    }

    public void OnDestroy(ref SystemState state) { }

    static async VlkTask ProcessHealthAsync(
        EntityManager entityManager,
        NativeArray<Entity> entities,
        NativeArray<HealthData> healthArray,
        NativeArray<float> results,
        CancellationToken ct)
    {
        try
        {
            // Fase 1: Agendar o job Burst.
            var job = new HealthProcessingJob
            {
                HealthInputs     = healthArray,
                ProcessedOutputs = results,
            };
            var handle = job.Schedule(entities.Length, batchSize: 64);

            // Fase 2: Aguardar conclusão sem bloquear a thread principal.
            await handle.ToVlkTask(cancellationToken: ct);

            // Fase 3: Aplicar resultados. Estamos de volta na thread principal.
            ct.ThrowIfCancellationRequested();

            for (int i = 0; i < entities.Length; i++)
            {
                // A entidade pode ter sido destruída enquanto o job estava executando.
                if (!entityManager.SafeEntityExists(entities[i]))
                    continue;

                entityManager.SetComponentData(entities[i], new HealthData
                {
                    CurrentHealth = results[i],
                });
            }
        }
        finally
        {
            // Sempre descartar NativeArrays — executado no sucesso, exceção ou cancelamento.
            if (entities.IsCreated)    entities.Dispose();
            if (healthArray.IsCreated) healthArray.Dispose();
            if (results.IsCreated)     results.Dispose();
        }
    }

    [BurstCompile]
    struct HealthProcessingJob : IJobParallelFor
    {
        [ReadOnly]  public NativeArray<HealthData> HealthInputs;
        [WriteOnly] public NativeArray<float>      ProcessedOutputs;

        public void Execute(int index)
        {
            var h = HealthInputs[index];
            var newHealth = h.CurrentHealth + h.RegenRate;
            if (newHealth > h.MaxHealth) newHealth = h.MaxHealth;
            ProcessedOutputs[index] = newHealth;
        }
    }

    struct HealthData : IComponentData
    {
        public float CurrentHealth;
        public float MaxHealth;
        public float RegenRate;
    }
}
#endif
```

---

## Resumo dos tipos bridge

| Tipo | Finalidade | Alocação |
|---|---|---|
| `JobHandleExtensions.ToVlkTask()` | Aguardar um único `JobHandle` | Zero no caminho rápido; promise em pool caso contrário |
| `JobHandleExtensions.WhenAll()` | Aguardar múltiplos `JobHandle` em paralelo | Zero no caminho rápido; promise em pool + aluguel de `ArrayPool` caso contrário |
| `TempNativeArrayScope<T>` | Gerenciamento de tempo de vida RAII para `NativeArray` | Nenhuma (struct) |
| `AwaitableBridge.AsVlkTask()` | Converter `Awaitable`/`Awaitable<T>` para VlkTask | Nenhuma (awaiter struct) |
| `ScheduleAsync()` gerado | Aguardar um job tipado diretamente | `JobPromise<TJob>` em pool |
