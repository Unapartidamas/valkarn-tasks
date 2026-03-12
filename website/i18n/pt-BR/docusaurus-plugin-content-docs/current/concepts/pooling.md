---
sidebar_position: 2
title: Object Pooling
---

# Object Pooling

O Valkarn Tasks elimina alocações de GC em caminhos assíncronos comuns fazendo pooling dos objetos que dão suporte a cada `ValkarnTask`. Esta página explica a arquitetura do pool — como os objetos são armazenados, como são adquiridos e retornados, e quais garantias de ciclo de vida o sistema oferece.

---

## Visão geral

Quando um método `async` suspende, a biblioteca precisa de um lugar para armazenar a máquina de estados gerada pelo compilador e um mecanismo de conclusão ao qual o awaiter possa se inscrever. Em `System.Threading.Tasks`, esse é o próprio objeto `Task` — uma alocação no heap por chamada. No Valkarn Tasks, esse papel é desempenhado por objetos em pool que implementam `ValkarnTask.ISource`.

O design do pool tem três objetivos:

1. **Zero operações atômicas no caminho quente da thread principal.** O loop de jogo do Unity é single-threaded por convenção. Alugar e retornar da thread principal deve ser leituras e escritas simples.
2. **Acesso seguro entre threads.** Tasks de background usando `ValkarnTask.Run` operam em threads de thread-pool. O pool deve lidar com alugar/retornar concorrentes corretamente.
3. **Crescimento limitado com trimming adaptativo.** Os pools não devem crescer sem limite após um pico de tráfego, mas não devem encolher tão agressivamente que realoquem constantemente.

---

## `ValkarnTaskPool<T>`

`ValkarnTaskPool<T>` é a classe de pool principal. É `internal sealed` — você não interage com ela diretamente, mas entendê-la explica onde vão suas alocações.

```
ValkarnTaskPool<T>
  |
  +-- fastItem: T            (slot de cache único, apenas thread principal, leitura/escrita simples)
  |
  +-- stackHead: T           (cabeça da pilha Treiber, baseada em CAS para segurança entre threads)
  +-- stackSize: int
  |
  +-- maxSize: int           (limitado por ValkarnTask.DefaultMaxPoolSize)
  +-- totalCreated: int      (rastreia alocações vitalícias para proporção de trimming)
```

### Slot rápido (thread principal)

O campo `fastItem` é um slot reservado único para o objeto retornado mais recentemente. Na thread principal, alugar e retornar são uma leitura e escrita simples — sem operações atômicas, sem spinning. Isso cobre a grande maioria das operações do loop de jogo Unity.

```
Alugar (thread principal):
  fastItem != null  →  tomar (fastItem = null), retornar   [zero atômicos]
  fastItem == null  →  cair para pilha Treiber

Retornar (thread principal):
  fastItem == null  →  fastItem = item                     [zero atômicos]
  fastItem != null  →  cair para pilha Treiber
```

### Pilha Treiber (overflow / threads de background)

Quando o slot rápido está ocupado (ou quando a thread chamadora não é a thread principal), o pool usa uma pilha Treiber sem bloqueio — uma lista encadeada intrusiva clássica usando compare-and-swap (CAS):

```
Alugar (qualquer thread):
  while (true):
    head = Volatile.Read(stackHead)
    if head == null: retornar null (pool vazio)
    next = head.NextNode
    if CAS(stackHead, next, head) == head: retornar head  // ganhou a corrida
    spinner.SpinOnce()                                    // perdeu, tentar novamente

Retornar (qualquer thread):
  if stackSize >= maxSize: retornar false (pool cheio, descartar item)
  while (true):
    head = Volatile.Read(stackHead)
    item.NextNode = head
    if CAS(stackHead, item, head) == head: stackSize++; retornar true
    spinner.SpinOnce()
```

A pilha é intrusiva: cada objeto em pool armazena seu próprio ponteiro `NextNode`, portanto nenhum nó wrapper externo é necessário. Isso é imposto pela interface `IPoolNode<T>`.

### Roteamento de thread

```csharp
internal static class ValkarnTaskPoolShared
{
    internal static volatile int MainThreadId;
}
```

Todas as instâncias de pool compartilham um único `MainThreadId`. As operações de alugar/retornar verificam `Thread.CurrentThread.ManagedThreadId == MainThreadId` para rotear para o caminho correto. O campo `volatile` garante visibilidade entre threads após o ID ser publicado na inicialização.

---

## `IPoolNode<T>`

Qualquer tipo que participa do pool deve implementar esta interface:

```csharp
internal interface IPoolNode<T> where T : class
{
    ref T NextNode { get; }
}
```

`ref T NextNode` retorna uma referência ao campo dentro do objeto que armazena o próximo ponteiro. O pool escreve diretamente neste campo via ref, eliminando qualquer nó wrapper separado. Todos os tipos em pool na biblioteca — runners, promises, combinadores — implementam esta interface declarando um campo privado e o expondo:

```csharp
// Exemplo de PooledPromise<T>
PooledPromise<T> nextNode;
public ref PooledPromise<T> NextNode => ref nextNode;
```

---

## Ciclo de vida do pool: adquirir, usar, retornar

O ciclo de vida completo de um objeto em pool é:

```
Chamador invoca método async
  |
  +--> AsyncValkarnTaskMethodBuilder.AwaitUnsafeOnCompleted()
        |
        +--> runner == null? SIM --> Pool.TryRent() ?? Pool.TrackNew(new Runner())
              |
              +--> runner armazenado no builder; máquina de estados copiada para runner
              |
              +--> awaiter.UnsafeOnCompleted(runner.MoveNextAction)
              |
              [... trabalho assíncrono prossegue ...]
              |
              +--> runner.SetResult() / runner.SetException()
                    |
                    +--> ValkarnTaskCompletionCore.TrySetResult() --> continuação invocada
                          |
                          +--> awaiter do chamador chama GetResult(token)
                                |
                                +--> core.GetResult(token) -- lê resultado ou relança
                                |
                                +--> TryReturn():
                                      stateMachine = default
                                      core.Reset()           // incrementa geração
                                      Pool.TryReturn(this)
```

O método `TryReturn` sempre limpa a máquina de estados antes de chamar `core.Reset()`. Essa ordenação é importante: `Reset()` incrementa o contador de geração, tornando o slot visível como disponível para renters concorrentes. Se a máquina de estados fosse limpa após `Reset()`, um renter em outra thread poderia obter o slot e ter sua máquina de estados sobrescrita.

---

## `ValkarnTaskCompletionCore<TResult>`

`ValkarnTaskCompletionCore<TResult>` é uma `internal struct` embutida em cada objeto em pool. É a máquina de estados real para a promise — rastreando estado de conclusão, armazenando resultados e erros, e resolvendo a corrida entre `OnCompleted` (registrando uma continuação) e `TrySetResult` (sinalizando conclusão).

```
Campos:
  result:          TResult     -- o valor de sucesso
  error:           object      -- ExceptionDispatchInfo ou OperationCanceledException
  errorKind:       byte        -- 0=nenhum, 1=faulted, 2=cancelado(OCE), 3=cancelado(EDI)
  generation:      int         -- monotonicamente crescente; convertido para uint para comparação de token
  completedCount:  int         -- 0=pendente, 1=reivindicado, 2=concluído (publicação em duas fases)
  continuation:    Action<object>
  continuationState: object
  hasUnhandledError: bool
```

### Protocolo de conclusão em duas fases

A conclusão usa um CAS em duas fases para ser segura em ARM64 onde pares store-release / load-acquire são necessários:

```
TrySetResult(value):
  Fase 1: CAS(completedCount, 0 -> 1)   -- reivindicar propriedade exclusiva
  Fase 2: escrever resultado
  Fase 3: Volatile.Write(completedCount, 2)  -- publicar com semântica de release
  Fase 4: InvokeContinuation()
```

Os leitores usam `Volatile.Read(completedCount)` (semântica de acquire) antes de ler o resultado, garantindo que vejam o valor escrito na Fase 2.

### Resolução de corrida entre `OnCompleted` e `TrySetResult`

Três padrões podem ocorrer:

```
Padrão A — OnCompleted primeiro:
  OnCompleted armazena continuação via CAS(continuation, null -> cont)
  TrySetResult lê continuação não-nula -> a invoca

Padrão B — TrySetResult primeiro (caminho rápido síncrono):
  TrySetResult coloca ContinuationSentinel via CAS(continuation, null -> sentinel)
  OnCompleted lê sentinel -> invoca continuação inline imediatamente

Padrão C (corrida concorrente):
  C.1: OnCompleted ganha CAS -> TrySetResult o lê -> invoca
  C.2: TrySetResult ganha CAS (coloca sentinel) -> OnCompleted detecta sentinel -> invoca inline
```

O sentinel é um objeto `Action<object>` estático usado puramente como um valor marcador — ele nunca é realmente invocado como delegate.

### Validação de token e segurança ABA

Toda chamada a `GetStatus`, `GetResult` e `OnCompleted` valida o `uint token` contra a `generation` atual. Quando `Reset()` chama `Interlocked.Increment(ref generation)`, qualquer struct `ValkarnTask` que mantém o token antigo receberá uma `InvalidOperationException` em vez de silenciosamente operar em estado reciclado. Um contador de geração de 32 bits dando a volta (exigindo ~4 bilhões de reutilizações de um único slot) é considerado efetivamente impossível na prática.

### Reset e relatório de erros não observados

`Reset()` é chamado no momento de retorno ao pool. Antes de incrementar a geração, verifica se um erro foi armazenado mas nunca observado (isto é, `GetResult` nunca foi chamado após uma falha). Se sim, publica a exceção por meio de `ValkarnTask.UnobservedException`. Erros de cancelamento são relatados apenas se `LogUnobservedCancellations` estiver habilitado em `ValkarnTaskSettings`, pois o cancelamento muitas vezes é intencional.

Para objetos `Promise` e `Promise<T>` não em pool, o relatório de erros não observados ocorre a partir do finalizador via `ReportUnobservedIfNeeded()`, que segue a mesma lógica sem limpar o estado.

---

## Configuração do pool

Três configurações controlam o dimensionamento do pool. Em builds Unity, elas são lidas de um asset `ValkarnTaskSettings` ScriptableObject (com padrões de fallback) e podem ser substituídas em runtime via propriedades estáticas:

```csharp
// Máximo de objetos por tipo de pool (por TStateMachine ou por tipo de promise)
ValkarnTask.DefaultMaxPoolSize = 256;   // padrão: 256

// Quantos frames entre verificações de trimming (frames do Unity PlayerLoop)
ValkarnTask.TrimCheckInterval = 300;    // padrão: 300 (~5 segundos a 60fps)

// Mínimo de objetos para manter após uma passagem de trim
ValkarnTask.MinPoolSize = 8;            // padrão: 8
```

`DefaultMaxPoolSize` é o teto aplicado no momento da construção do pool. É imposto por instância de pool, não globalmente — um pool para `AsyncValkarnTaskRunner<LoadSceneStateMachine>` e um pool para `AsyncValkarnTaskRunner<FetchDataStateMachine>` têm cada um seu próprio teto.

### Trimming do pool

O `PlayerLoopHelper` invoca `PoolRegistry.TrimAll(minPoolSize)` a cada `TrimCheckInterval` frames na thread principal. Cada pool usa uma estratégia de histerese:

```
Cada verificação de trim:
  currentSize = fastItem.count + stackSize
  if currentSize <= MinPoolSize: resetar contador consecutivo, pular

  ratio = currentSize / totalCreated
  if ratio > 0.5 (pool mantém > 50% de todos os objetos já criados):
    trimConsecutiveCount++
    if trimConsecutiveCount >= hysteresisThreshold:
      liberar alguma fração (releaseRatio) dos objetos excedentes da pilha
      (fastItem é preservado — é o slot mais amigável ao cache)
  else:
    resetar trimConsecutiveCount
```

A histerese evita que um breve pico de tráfego cause imediatamente que todos os objetos sejam alocados e depois imediatamente liberados. O slot rápido é sempre preservado durante o trimming porque representa o item mais recentemente usado e, portanto, é mais provável de ser necessário novamente.

---

## `PoolRegistry` e monitoramento

Cada `ValkarnTaskPool<T>` se registra no `PoolRegistry` global no momento da construção. O registry mantém uma lista de referências `IPoolInfo`, que expõem:

```csharp
internal interface IPoolInfo
{
    Type PooledType { get; }
    int Size { get; }
    int MaxSize { get; }
    bool IsAlive { get; }
    int Trim(int minPoolSize);
}
```

Você pode enumerar todos os pools ativos em runtime usando a API pública:

```csharp
foreach (var (type, size, maxSize) in ValkarnTask.GetPoolInfo())
{
    Debug.Log($"{type.Name}: {size}/{maxSize}");
}
```

Estes são os mesmos dados exibidos pela janela **Task Tracker** no Unity Editor. A janela consulta `GetPoolInfo()` e exibe uma tabela ao vivo de ocupação de pool, permitindo que você veja se os pools estão aquecidos, se algum tipo está consistentemente atingindo seu teto e se o trimming está funcionando como esperado.

Entradas de pool mortas (onde `IsAlive` retorna false) são removidas de forma lazy da lista do registry durante chamadas `GetAll()` e `TrimAll()`, evitando que o registry cresça indefinidamente se instâncias de pool forem coletadas pelo GC.

---

## `PooledPromise` e `PooledPromise<T>`

Estas são as sources de conclusão em pool destinadas ao uso em padrões async personalizados — por exemplo, envolvendo uma API baseada em callback ou um canal produtor/consumidor repetitivo.

```csharp
// Adquirir uma promise pendente do pool
var promise = ValkarnTask.PooledPromise<string>.Create(out uint token);
ValkarnTask<string> task = promise.Task;

// Entregar a task a um consumidor
// ... mais tarde, de qualquer thread ...
promise.TrySetResult("hello");

// Quando o consumidor aguarda a task e GetResult é chamado,
// a promise é redefinida e retorna ao pool automaticamente.
```

Características principais:

- `Create(out uint token)` aluga do pool ou aloca uma nova instância rastreada pelo pool.
- `CreateCompleted(T result, out uint token)` faz o mesmo mas sinaliza imediatamente o resultado, para que a task já esteja concluída quando retornada.
- Após `GetResult` ser chamado na task de apoio, `TryReturn()` dispara: a promise chama `core.Reset()` e retorna a si mesma ao pool.
- Uma proteção contra double-return (`Interlocked.Exchange(ref returned, 1)`) evita corrupção do pool se `GetResult` for chamado duas vezes.

**Alternativa sem pool: `Promise` e `Promise<T>`.** Estas são classes alocadas no heap que não são retornadas a um pool. Use-as para operações de longa duração onde o tempo de vida é imprevisível ou onde a mesma source deve sobreviver a múltiplos ciclos de await. Elas dependem de um finalizador para relatar exceções não observadas.

---

## Pools de combinadores

Os combinadores `WhenAll` e `WhenAny` também usam pools. Cada combinação de aridade e tipo tem seu próprio pool:

| Combinador | Tipo de pool |
|-----------|-----------|
| `WhenAll(task1, task2)` (tipado) | `ValkarnTaskPool<WhenAllPromise<T1, T2>>` |
| `WhenAll(IEnumerable<ValkarnTask<T>>)` | `ValkarnTaskPool<WhenAllArrayPromise<T>>` |
| `WhenAll(task1, task2)` (void) | `ValkarnTaskPool<WhenAllVoidPromise2>` |
| `WhenAny(task1, task2)` (tipado) | `ValkarnTaskPool<WhenAnyPromise2<T>>` |
| `WhenAny(IEnumerable<ValkarnTask<T>>)` | `ValkarnTaskPool<WhenAnyArrayPromise<T>>` |

Combinadores baseados em array (`WhenAll<T>(IEnumerable<...>)` e `WhenAny<T>(IEnumerable<...>)`) usam `System.Buffers.ArrayPool<T>.Shared` para seus arrays internos de source/token, para que esses arrays também sejam reciclados em vez de alocados por chamada.

Todos os combinadores aplicam o mesmo curto-circuito de zero alocação: se todas as entradas estiverem sincronamente concluídas no ponto em que `WhenAll` ou `WhenAny` é chamado, um novo objeto em pool nunca é criado.

```csharp
// Zero alocação — ambas as tasks estão concluídas de forma síncrona
var t1 = ValkarnTask.FromResult(1);
var t2 = ValkarnTask.FromResult(2);
ValkarnTask<(int, int)> combined = ValkarnTask.WhenAll(t1, t2);
// combined.source == null; resultado é (1, 2) armazenado inline
```
