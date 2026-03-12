---
sidebar_position: 5
title: Arquitetura
---

# Arquitetura

Visão geral técnica dos internos do Valkarn Tasks.

---

## Estrutura de alto nível

```
┌─────────────────────────────────────────────────────────────────┐
│                    TEMPO DE COMPILAÇÃO                            │
│                                                                   │
│  ┌──────────────┐  ┌──────────────────┐  ┌───────────────────┐  │
│  │  Lifecycle    │  │  Awaitable       │  │  Diagnostics      │  │
│  │  Analyzer     │  │  Bridge Gen      │  │  (TT001–TT017)    │  │
│  └──────────────┘  └──────────────────┘  └───────────────────┘  │
│                                                                   │
│  ┌──────────────┐  ┌──────────────────┐                          │
│  │  Job Bridge   │  │  Combinator      │                          │
│  │  Gen          │  │  Gen             │                          │
│  └──────────────┘  └──────────────────┘                          │
├─────────────────────────────────────────────────────────────────┤
│                    TEMPO DE EXECUÇÃO                              │
│                                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐  │
│  │ ValkarnTask │  │ Result<T>  │  │ ValkarnPool│  │ Completion│  │
│  │  struct    │  │  struct    │  │  bounded   │  │  Core<T>  │  │
│  └────────────┘  └────────────┘  └────────────┘  └───────────┘  │
│                                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐  │
│  │ PlayerLoop │  │Continuation│  │  Channels  │  │ TestClock │  │
│  │  Helper    │  │   Queue    │  │            │  │           │  │
│  └────────────┘  └────────────┘  └────────────┘  └───────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### Layout de assemblies

```
ValkarnTask.Runtime      — embarcado com o jogo
ValkarnTask.SourceGen    — somente em tempo de compilação (gerador de código-fonte)
ValkarnTask.Analyzer     — somente em tempo de compilação (diagnósticos + correções de código)
ValkarnTask.Testing      — TestClock + utilitários de teste
```

---

## Struct ValkarnTask

```csharp
[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask
{
    internal readonly ValkarnTask.ISource source;
    internal readonly ulong token; // empacotado: 32 bits altos = geração, 32 baixos = índice de slot
}

[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
{
    internal readonly ValkarnTask.ISource<T> source;
    internal readonly T result;  // inline no caminho rápido síncrono
    internal readonly ulong token;
}
```

**Invariante principal:** `source == null` significa que a tarefa completou sincronamente sem erro — nenhum objeto de heap envolvido. `ValkarnTask.CompletedTask` é `default(ValkarnTask)`; `ValkarnTask.FromResult(value)` armazena o resultado inline.

### Token geracional

```csharp
// Empacotar
ulong token = ((ulong)generation << 32) | slotIndex;

// Desempacotar
uint slotIndex  = (uint)(token & 0xFFFFFFFF);
uint generation = (uint)(token >> 32);
```

Validado em cada chamada ISource: `slots[slotIndex].generation == expectedGeneration`. Uma referência obsoleta a um slot de pool reciclado lança imediatamente `InvalidOperationException`. 4 bilhões de gerações por slot — colisão é impossível na prática. (UniTask usa `short` — colisão após ~18 minutos.)

---

## Contrato ISource

```csharp
public interface ISource
{
    Status GetStatus(ulong token);
    void   GetResult(ulong token);
    void   OnCompleted(Action<object> continuation, object state, ulong token);
    Status UnsafeGetStatus();
}

public interface ISource<out T> : ISource
{
    new T GetResult(ulong token);
}
```

Qualquer objeto que implemente `ISource` pode sustentar um `ValkarnTask`. Implementações embutidas:

| Tipo | Finalidade |
|------|-----------|
| `AsyncValkarnRunner<TStateMachine>` | Sustenta cada método `async ValkarnTask` |
| `AsyncValkarnRunner<TStateMachine, T>` | Sustenta cada método `async ValkarnTask<T>` |
| `ValkarnTask.PooledPromise[<T>]` | Conclusão manual, retorno automático ao pool |
| `ValkarnTask.Promise[<T>]` | Conclusão manual, sem pool (longa duração) |
| `ExceptionSource` | Sustenta `FromException` |
| `CanceledSource` | Sustenta `FromCanceled` |
| `NeverSource` | Singleton — nunca transita de Pending |

---

## Builder de método async

O compilador C# aciona o protocolo de builder para tipos de retorno async personalizados:

```
Create()
  └─ retorna builder struct (zero alloc)

Start(ref stateMachine)
  └─ executa a máquina de estados sincronamente
     ├─ completa sem suspender → SetResult(), runner permanece null
     │  └─ Task retorna default(ValkarnTask)  ← zero alocação
     └─ atinge await incompleto → AwaitUnsafeOnCompleted()
           └─ aluga AsyncValkarnRunner do pool
              copia a máquina de estados para o runner (por valor, sem boxing)
              registra continuação no awaitable
              └─ Task encapsula runner como ISource  ← caminho async
```

O builder em si é uma `struct` — nenhuma alocação apenas para o builder. O `runner` é alocado **preguiçosamente**: se um método completar sincronamente, nenhum aluguel de pool ocorre.

---

## Runner de máquina de estados e pool

`AsyncValkarnRunner<TStateMachine>` mantém a máquina de estados gerada pelo compilador **por valor** (sem boxing) e atua como o `ISource`. É alugado de `ValkarnPool<T>` na primeira suspensão e retornado em `GetResult`.

Como `TStateMachine` é um tipo único por método async (por instanciação genérica fechada), cada método async obtém seu próprio pool automaticamente via especialização genérica do C#.

### ValkarnPool\<T\>

| Contexto | Estrutura | Razão |
|---------|-----------|-------|
| Thread principal do Unity | Stack de thread única | Sem sincronização — máxima velocidade possível |
| Threads de fundo | Treiber lock-free stack | Operações CAS, sem locks |

Contexto de thread detectado via `Thread.CurrentThread.IsBackground`. Forma do pool (capacidade, taxa de trim) configurada via `ValkarnTaskSettings`.

---

## ValkarnCompletionCore\<T\>

Estado compartilhado dentro de cada implementação de `ISource`:

- `Status` atual (Pending / Succeeded / Faulted / Canceled)
- Valor do resultado (fontes genéricas)
- Exceção ou `OperationCanceledException` (caminhos de erro)
- Delegate de continuação registrado + estado

Transições de status usam `Interlocked.CompareExchange` — sem lock, thread-safe. Um **guarda de conclusão dupla** garante que apenas a primeira chamada `TrySet*` vença; chamadas subsequentes são no-ops silenciosos.

---

## Integração com PlayerLoop

`PlayerLoopHelper` insere callbacks leves de runner no PlayerLoop do Unity na inicialização (`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]`).

Cada valor de `PlayerLoopTiming` corresponde a uma fase. Quando `await ValkarnTask.Yield(timing)` é chamado, a continuação é enfileirada no runner dessa fase e despachada na próxima vez que o Unity a atingir.

```
Initialization  →  EarlyUpdate  →  FixedUpdate  →  PreUpdate
→  Update  →  PreLateUpdate  →  PostLateUpdate  →  TimeUpdate
  (+ variantes Last* para cada fase)
```

---

## Gerador de código-fonte

O gerador de código-fonte Roslyn é executado em tempo de compilação. Para cada classe `partial` que estende `MonoBehaviour` com métodos `async ValkarnTask`, ele gera um arquivo de classe parcial que:

1. Declara o campo `_valkarnCancelToken`
2. Atribui a ele o valor de `destroyCancellationToken` em `Awake`
3. Envolve cada método async para propagar o token automaticamente

O arquivo gerado nunca é exibido no depurador e nunca modifica o código-fonte do usuário.

O gerador também produz:

- **Adaptadores bridge para Awaitable** — quando `Awaitable` é aguardado dentro de `async ValkarnTask`
- **Wrappers async para Job** — quando tipos `IJob` / `IJobParallelFor` são detectados
- **Pools de combinadores** — fontes `WhenAll`/`WhenAny` tipadas para tuplas de aridade 2–8

---

## Analisadores Roslyn

17 regras `DiagnosticAnalyzer` são fornecidas em `Analyzers/netstandard2.0/`. Elas são executadas durante o passo do compilador C# no Unity Editor e em CI:

- Todas usam `SemanticModel` para resolução de tipos (não correspondência de strings)
- O utilitário compartilhado `ValkarnTypeHelper` detecta qualquer variante de `ValkarnTask`
- O analisador de loop zumbi ignora corretamente funções locais e lambdas aninhadas
- Analisadores de migração (MIG001–MIG015) ativam automaticamente apenas quando UniTask / Awaitable são referenciados — inertes caso contrário

---

## Camada Burst & ECS

Três módulos opcionais, cada um protegido por verificações de define `#if`:

| Módulo | Requer | Finalidade |
|--------|--------|-----------|
| `JobBridge` | `Unity.Jobs` | Encapsula `JobHandle` como awaitable; verifica `handle.IsCompleted` a cada tick do PlayerLoop |
| `AsyncSystemBase` | `Unity.Entities` | Classe base de sistema ECS com suporte async |
| `BurstScheduler` | `Unity.Burst` + `Unity.Collections` | Agenda jobs Burst a partir de contexto async; gerencia `NativeTimerHeap` |

`NativeTimerHeap` é um min-heap compatível com Burst para temporizadores de alta precisão que evita totalmente a alocação de heap gerenciado.

---

## Integração com o Editor

O **Valkarn Hub** (`Tools → Valkarn → Hub`) usa `TypeCache.GetTypesDerivedFrom<IValkarnEditorPanel>()` para descobrir todos os pacotes Valkarn instalados automaticamente. Sem registro manual necessário.

O `TasksTrackerPanel` assina `EditorApplication.update` para atualizar os diagnósticos de pool a cada 0,5 s (configurável) e expõe a referência ao asset `ValkarnTaskSettings` para acesso rápido.

---

## Considerações sobre IL2CPP

- Máquinas de estados são armazenadas **por valor** dentro dos runners — sem boxing, o IL2CPP trata corretamente
- O runner de cada método async é uma especialização genérica separada — type-safe, sem contaminação cruzada
- Structs de awaiter implementam `ICriticalNotifyCompletion` — o compilador chama `UnsafeOnCompleted`, ignorando a captura de `ExecutionContext` (sem overhead na configuração padrão do Unity)
- Se o stripping agressivo estiver habilitado, preserve o assembly de runtime:

```xml
<!-- link.xml -->
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```
