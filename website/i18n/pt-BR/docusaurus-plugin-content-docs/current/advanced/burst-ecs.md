---
sidebar_position: 1
title: Integração Burst & ECS
---

# Integração Burst & ECS

O Valkarn Tasks inclui integração opcional com o compilador Burst do Unity, Unity Collections e o pacote Entities (ECS). Toda essa funcionalidade é compilada condicionalmente — ela só é ativa quando os pacotes necessários estão presentes e os símbolos de definição de script correspondentes estão configurados.

## Requisitos

| Funcionalidade | Pacote necessário | Define de script |
|---|---|---|
| `NativeTimerHeap`, `NativeScheduler`, `BurstSchedulerRunner` | Unity Burst 1.8+, Unity Collections 2.0+ | `VTASKS_HAS_BURST` e `VTASKS_HAS_COLLECTIONS` |
| `AsyncSystemUtilities` | Unity Entities 1.0+ | `VTASKS_HAS_ENTITIES` |

Todos os arquivos de código-fonte Burst/ECS são envolvidos em guards `#if` correspondendo a esses defines. Nada nesses arquivos compila ou linka a menos que os defines estejam presentes.

### Configuração

1. Instale os pacotes necessários via Unity Package Manager:
   - `com.unity.burst` 1.8 ou superior
   - `com.unity.collections` 2.0 ou superior
   - `com.unity.entities` 1.0 ou superior (apenas para utilitários ECS)

2. Adicione os símbolos de definição de script em **Project Settings > Player > Scripting Define Symbols**:
   ```
   VTASKS_HAS_BURST;VTASKS_HAS_COLLECTIONS;VTASKS_HAS_ENTITIES
   ```
   Você só precisa adicionar os defines para os pacotes que instalou.

---

## NativeTimerHeap

**Namespace:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Guard:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeTimerHeap` é um min-heap binário compatível com Burst para agendar timers. Armazena valores `TimerEntry` ordenados por prazo, oferecendo inserção O(log n) e remoção O(log n) por timer expirado.

### Tipos principais

```csharp
// Entrada armazenada no heap. Ordenada por Deadline.
public struct TimerEntry : IComparable<TimerEntry>
{
    public long Deadline;
    public int  Id;
}

public struct NativeTimerHeap : IDisposable
{
    public bool IsCreated { get; }
    public int  Count     { get; }

    // Cria o heap. Use Allocator.Persistent para heaps de longa duração.
    public NativeTimerHeap(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // Insere um novo timer. Retorna o ID do timer (usado para identificar o callback).
    // O prazo e o valor passado para DrainExpired devem usar a mesma unidade
    // (BurstSchedulerRunner usa ticks DateTime via Time.realtimeSinceStartupAsDouble).
    [BurstCompile]
    public int Schedule(long deadline);

    // Remove e anexa IDs de todos os timers cujo Deadline <= currentTimestamp.
    // Retorna o número de timers drenados.
    [BurstCompile]
    public int DrainExpired(long currentTimestamp, NativeList<int> expiredIds);

    public void Dispose();
}
```

`NativeTimerHeap` é uma struct não gerenciada. Não pode armazenar delegates gerenciados — IDs são correspondidos a callbacks gerenciados no dicionário de `BurstSchedulerRunner` na thread principal.

---

## NativeScheduler

**Namespace:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Guard:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeScheduler` é uma fila de trabalho compatível com Burst respaldada por `NativeQueue<ScheduledWork>`. Jobs compilados com Burst enfileiram itens de trabalho; a thread principal os drena a cada frame.

### Tipos principais

```csharp
public enum WorkType : byte
{
    TimerExpired  = 0,
    JobCompleted  = 1,
    Custom        = 2
}

public struct ScheduledWork
{
    public int      Id;
    public WorkType Type;
    public long     Payload;
}

public struct NativeScheduler : IDisposable
{
    public bool IsCreated { get; }

    public NativeScheduler(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // Enfileira um item de trabalho de um job compilado com Burst.
    [BurstCompile]
    public void Enqueue(ScheduledWork work);

    // Drena todo trabalho pendente em `results`. Chamar apenas da thread principal.
    // Retorna o número de itens drenados.
    public int Drain(NativeList<ScheduledWork> results);

    // Retorna um writer paralelo adequado para uso em jobs IJobParallelFor.
    public NativeQueue<ScheduledWork>.ParallelWriter AsParallelWriter();

    public void Dispose();
}
```

A fila é o ponto de cruzamento entre o mundo Burst e o mundo gerenciado. `Enqueue` é chamável por Burst; `Drain` é somente para a thread principal.

---

## BurstSchedulerRunner

**Namespace:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Guard:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`BurstSchedulerRunner` é a ponte gerenciada entre o scheduler/heap de timers nativos e o resto do seu jogo. Implementa `IPlayerLoopItem`, portanto o Unity chama `MoveNext()` uma vez por frame no timing registrado. A cada frame ele:

1. Drena `NativeScheduler` e dispara quaisquer callbacks gerenciados registrados correspondidos pelo ID de trabalho.
2. Drena `NativeTimerHeap` para timers expirados e dispara seus callbacks gerenciados.

Exceções lançadas por callbacks são encaminhadas para `VlkTask.PublishUnobservedException` em vez de se propagar pelo PlayerLoop.

### API

```csharp
public sealed class BurstSchedulerRunner : IPlayerLoopItem, IDisposable
{
    // Cria um runner e o registra no PlayerLoop. Retorna a instância.
    // Descarte o runner retornado quando não for mais necessário.
    public static BurstSchedulerRunner Create(
        PlayerLoopTiming timing       = PlayerLoopTiming.Update,
        int initialCapacity           = 64);

    // Acesso direto ao NativeScheduler para enfileiramento a partir de jobs Burst.
    public NativeScheduler Scheduler { get; }

    // Agenda um callback de timer gerenciado. Deve ser chamado da thread principal.
    // Retorna um ID de timer (apenas para identificação; não há API de cancelamento).
    public int ScheduleTimer(TimeSpan delay, Action callback);

    // Associa um callback gerenciado a um ID de trabalho enfileirado por um job Burst.
    // Deve ser chamado da thread principal, antes ou durante o frame em que o job é concluído.
    public void RegisterCallback(int workId, Action callback);

    // Descarta todos os containers nativos e cancela o registro do PlayerLoop.
    public void Dispose();
}
```

### Como BurstSchedulerRunner difere do scheduler padrão

O scheduler padrão do Valkarn Tasks integra diretamente com máquinas de estados `async/await` e gerencia o despacho de continuação via `PlayerLoopHelper`. `BurstSchedulerRunner` adiciona uma lane separada especificamente para sinalização a partir de jobs compilados com Burst:

| | Scheduler padrão | BurstSchedulerRunner |
|---|---|---|
| Tipo de continuação | `Action` gerenciado via `ISource` | `Action` gerenciado registrado por ID |
| Fonte de sinal | Padrão awaiter C# | `NativeScheduler.Enqueue` não gerenciado |
| Fonte de timer | `VlkTask.Delay` (gerenciado) | `NativeTimerHeap` (não gerenciado) |
| Thread safety | Continuações na thread principal | Enqueue é seguro para Burst; drain é somente thread principal |

### Padrão de uso

```csharp
// 1. Criar o runner uma vez (ex.: em um MonoBehaviour de bootstrap ou ISystem.OnCreate).
var runner = BurstSchedulerRunner.Create(PlayerLoopTiming.Update, initialCapacity: 128);

// 2. Agendar um callback de timer (thread principal).
runner.ScheduleTimer(TimeSpan.FromSeconds(2.0), () =>
{
    Debug.Log("Dois segundos decorridos (timer não gerenciado).");
});

// 3. De um job Burst, enfileirar um item de trabalho.
//    O NativeScheduler.ParallelWriter é seguro para usar de IJobParallelFor.
var writer = runner.Scheduler.AsParallelWriter();
// Dentro de Execute(int index):
writer.Enqueue(new ScheduledWork { Id = myWorkId, Type = WorkType.Custom });

// 4. Registrar o callback gerenciado na thread principal antes do job ser concluído.
runner.RegisterCallback(myWorkId, () =>
{
    Debug.Log($"Sinal de conclusão de job recebido para trabalho {myWorkId}.");
});

// 5. Descartar o runner quando concluído (ex.: OnDestroy ou domain reload).
runner.Dispose();
```

---

## AsyncSystemUtilities

**Namespace:** `UnaPartidaMas.Valkarn.Tasks.ECS`
**Guard:** `#if VTASKS_HAS_ENTITIES`

`AsyncSystemUtilities` fornece dois helpers de extensão para escrever sistemas ECS assíncronos.

### GetWorldCancellationToken

```csharp
public static CancellationToken GetWorldCancellationToken(
    this World world,
    PlayerLoopTiming timing = PlayerLoopTiming.Update)
```

Retorna um `CancellationToken` que é automaticamente cancelado quando o `World` dado é destruído. Internamente inicia um `VlkTask` fire-and-forget que chama `VlkTask.Yield(timing)` a cada frame enquanto `world.IsCreated` é verdadeiro, então cancela um `CancellationTokenSource` quando o loop sai.

Se o world já estiver destruído quando você chamar este método, retorna um token que já está no estado cancelado.

Passe este token para cada método async que você lança a partir de um sistema para que o trabalho em andamento seja automaticamente parado quando o world desaparecer.

### SafeEntityExists

```csharp
public static bool SafeEntityExists(this EntityManager entityManager, Entity entity)
```

Chama `entityManager.Exists(entity)` e retorna `false` se uma `ObjectDisposedException` for lançada. Isso pode acontecer quando o `EntityManager` é acessado após o world ter sido descartado, que é uma condição de corrida real em código async que sobrevive entre limites de frame.

Use isso após cada ponto de `await` antes de escrever de volta em uma entidade.

---

## Exemplo funcional: Sistema ECS assíncrono

O seguinte exemplo é de `Samples~/ECS/AsyncLoadSystem.cs`. Demonstra o padrão canônico para inicialização assíncrona one-shot a partir de um `ISystem`.

```csharp
#if VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Entities;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.ECS;

public partial struct AsyncLoadSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // Obter um token de cancelamento vinculado ao tempo de vida deste World.
        // Se o World for destruído, todo trabalho async lançado com este token
        // será automaticamente cancelado.
        var worldCt = state.World.GetWorldCancellationToken();

        // Lançar a inicialização async e esquecer a task.
        // Forget() roteia qualquer exceção não tratada para VlkTask.PublishUnobservedException.
        InitializeAsync(state.WorldUnmanaged, worldCt).Forget();
    }

    public void OnUpdate(ref SystemState state) { }
    public void OnDestroy(ref SystemState state) { }

    static async VlkTask InitializeAsync(WorldUnmanaged world, CancellationToken ct)
    {
        // Fase 1: Carregar dados em uma thread de background.
        // RunOnThreadPool muda para uma thread de trabalho, executa o delegate,
        // e retorna para a thread principal automaticamente.
        var configData = await VlkTask.RunOnThreadPool(
            () => LoadFromDisk(),
            cancellationToken: ct);

        // Fase 2: Aplicar resultados na thread principal.
        // Verificar cancelamento caso o World tenha sido destruído durante o carregamento.
        ct.ThrowIfCancellationRequested();
        ApplyConfiguration(world, configData);
    }

    static ConfigData LoadFromDisk()
    {
        // Apenas C# puro — nenhuma chamada de API Unity ou ECS é permitida aqui.
        return new ConfigData { MaxEnemies = 100, SpawnRadius = 50f };
    }

    static void ApplyConfiguration(WorldUnmanaged world, ConfigData data)
    {
        // Seguro: estamos na thread principal.
        UnityEngine.Debug.Log($"Config carregada: MaxEnemies={data.MaxEnemies}");
    }

    struct ConfigData
    {
        public int   MaxEnemies;
        public float SpawnRadius;
    }
}
#endif
```

O exemplo de throttling de IA (`Samples~/ECS/AISystemExample.cs`) se baseia neste padrão e adiciona `AsyncThrottle` para limitar quantas tasks assíncronas concorrentes estão em andamento. Consulte a documentação de Throttling para detalhes sobre esse padrão.

---

## Limitações

As seguintes restrições se aplicam a todo código async Burst/ECS. Violá-las produzirá erros no editor, violações de segurança de jobs ou corrupção silenciosa de dados.

### Dentro do código compilado por Burst

- **Sem tipos gerenciados.** O Burst não pode compilar código que aloca, acessa ou referencia objetos gerenciados (classes, delegates, arrays, strings, `List<T>`, etc.). Apenas structs blittable e containers nativos são permitidos.
- **Sem exceções.** O Burst não suporta `try`/`catch`/`throw`. Use códigos de retorno ou flags para comunicar erros.
- **Sem `async`/`await`.** As máquinas de estados async em C# são gerenciadas e não podem ser compiladas pelo Burst. `NativeScheduler` e `NativeTimerHeap` fornecem um canal lateral para sinalizar continuações gerenciadas, mas as continuações em si são executadas na thread principal.
- **Sem estado gerenciado mutável estático.** Jobs Burst podem ler campos estáticos readonly, mas não devem escrever em estáticos gerenciados.

### Entre pontos de await em sistemas ECS

- **Tempo de vida de entidade.** Entidades podem ser destruídas enquanto um método async está suspenso. Sempre chame `entityManager.SafeEntityExists(entity)` após cada ponto de `await` antes de escrever de volta.
- **Obsolescência de ComponentLookup.** `ComponentLookup`, `RefRW` e outros tipos de ponteiro de chunk se tornam inválidos após mudanças estruturais, que podem ocorrer em qualquer frame. Não armazene estes em cache entre pontos de `await`. Re-adquira do `SystemState` após retomar, ou use `EntityManager` diretamente.
- **Parâmetros `ref`.** Métodos async não podem ter parâmetros `ref`, `in` ou `out` (erro CS1988 do C#). Extraia todos os dados ECS de forma síncrona no método síncrono `OnUpdate` e passe-os para o método async por valor.
- **`SystemAPI` em métodos async.** `SystemAPI` é gerado por código e só funciona dentro de métodos `ISystem` parciais. Não está disponível em métodos `async`. Realize todas as consultas `SystemAPI` antes do primeiro `await`.
- **Thread safety.** `EntityManager`, `ComponentLookup` e mudanças estruturais são apenas para a thread principal. Use `VlkTask.RunOnThreadPool` apenas para computação pura em C# sem chamadas de API Unity ou ECS.
