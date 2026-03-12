---
sidebar_position: 2
title: Canais
---

# Canais

Os canais fornecem um pipeline thread-safe e capaz de async para passar dados entre produtores e consumidores. São particularmente adequados para sistemas de jogo onde o trabalho é gerado em um lado (threads de background, callbacks de eventos, conclusões de jobs) e precisa ser consumido em outro (thread principal, pools de workers).

## Criando um Canal

Os canais são criados por meio da classe factory estática `ValkarnTask.Channel`. Não há construtor público.

### Canal Ilimitado

```csharp
Channel<T> channel = ValkarnTask.Channel.CreateUnbounded<T>(bool multiConsumer = false);
```

Um canal ilimitado não tem limite de capacidade. `WriteAsync` e `TryWrite` sempre têm sucesso imediatamente enquanto o canal não tiver sido concluído. Os itens acumulam em uma `Queue<T>` interna até que um consumidor os leia.

**Parâmetros**

| Parâmetro | Padrão | Descrição |
|-----------|---------|-------------|
| `multiConsumer` | `false` | Quando `true`, múltiplas chamadas `ReadAsync` concorrentes são suportadas (consumidores concorrentes). Quando `false`, apenas um leitor pode aguardar `ReadAsync` por vez. |

### Canal Limitado

```csharp
Channel<T> channel = ValkarnTask.Channel.CreateBounded<T>(int capacity, bool multiConsumer = false);
```

Um canal limitado mantém no máximo `capacity` itens em um buffer de anel de tamanho fixo. Quando o buffer está cheio, `WriteAsync` suspende o código chamador de forma assíncrona até que espaço fique disponível (backpressure). `TryWrite` retorna `false` imediatamente quando cheio, em vez de esperar.

**Parâmetros**

| Parâmetro | Padrão | Descrição |
|-----------|---------|-------------|
| `capacity` | obrigatório | Número máximo de itens que o buffer pode conter. Deve ser maior que zero. |
| `multiConsumer` | `false` | Igual ao ilimitado — habilita múltiplos leitores concorrentes. |

**Escolhendo entre limitado e ilimitado**

Use `CreateBounded` quando precisar aplicar backpressure — ou seja, quando quiser que os produtores desacelerem automaticamente se os consumidores ficarem para trás. Use `CreateUnbounded` quando a taxa de produção é naturalmente limitada (como eventos de entrada) e o buffering ilimitado é aceitável, ou quando você já considerou o crescimento de memória.

---

## Channel&lt;T&gt;

`Channel<T>` é um container que expõe os dois lados do pipeline como objetos separados.

```csharp
public sealed class Channel<T>
{
    public ChannelReader<T> Reader { get; }
    public ChannelWriter<T> Writer { get; }
}
```

Mantenha a referência `Writer` no lado do produtor e a referência `Reader` no lado do consumidor. Não há requisito de que estejam na mesma thread.

---

## ChannelWriter&lt;T&gt;

`ChannelWriter<T>` é o lado de escrita do canal. Obtenha-o de `channel.Writer`.

### TryWrite

```csharp
public abstract bool TryWrite(T item);
```

Tenta escrever um item sem suspender. Retorna `true` se o item foi aceito; retorna `false` se o canal está cheio (limitado) ou foi concluído.

Use `TryWrite` em caminhos quentes onde você pode descartar itens, ou quando você faz polling em um loop e quer evitar alocação de máquinas de estados async.

```csharp
if (!channel.Writer.TryWrite(item))
{
    // canal está cheio ou fechado — lidar adequadamente
}
```

### WriteAsync

```csharp
public abstract ValkarnTask WriteAsync(T item);
```

Escreve um item no canal, suspendendo o chamador de forma assíncrona se necessário.

- **Canais ilimitados**: sempre completa de forma síncrona (caminho rápido de zero alocação) enquanto o canal estiver aberto.
- **Canais limitados**: completa de forma síncrona quando há espaço no buffer; suspende o chamador e enfileira um registro de escritor pendente quando o buffer está cheio. O chamador é retomado assim que um consumidor lê um item e libera um slot.

Lança `ChannelClosedException` se o canal foi concluído via `Complete()` antes ou durante a escrita.

```csharp
await channel.Writer.WriteAsync(item);
```

Múltiplos produtores podem chamar `WriteAsync` concorrentemente em um canal limitado. Cada escritor suspenso é enfileirado e desbloqueado em ordem FIFO à medida que o espaço fica disponível.

### Complete

```csharp
public abstract void Complete();
```

Sinaliza que nenhum item adicional será escrito. Após `Complete()` ser chamado:

- Quaisquer itens já escritos permanecem no buffer e ainda podem ser consumidos.
- Novas chamadas a `WriteAsync` ou `TryWrite` falharão com `ChannelClosedException`.
- Uma vez que o buffer seja totalmente drenado, `ChannelReader<T>.Completion` é concluído e quaisquer chamadas `ReadAsync` pendentes ou futuras lançam `ChannelClosedException`.

`Complete()` é idempotente em uma única chamada — chamá-la mais de uma vez é seguro (chamadas subsequentes são ignoradas).

```csharp
// Sinalizar fim do trabalho
channel.Writer.Complete();
```

---

## ChannelReader&lt;T&gt;

`ChannelReader<T>` é o lado de leitura do canal. Obtenha-o de `channel.Reader`.

### ReadAsync

```csharp
public abstract ValkarnTask<T> ReadAsync();
```

Lê o próximo item do canal. Se nenhum item estiver disponível atualmente, o chamador suspende de forma assíncrona até que um chegue. Quando o canal está concluído e totalmente drenado, lança `ChannelClosedException`.

```csharp
T item = await channel.Reader.ReadAsync();
```

**Modo de consumidor único** (padrão): apenas um `ReadAsync` pode estar em andamento por vez. Tentar iniciar um segundo `ReadAsync` concorrente lança imediatamente. Essa restrição habilita uma otimização interna de zero alocação — o core do leitor é embutido diretamente na implementação do canal em vez de ser alocado de um pool por chamada.

**Modo de múltiplos consumidores** (`multiConsumer: true`): qualquer número de chamadas `ReadAsync` pode estar pendente simultaneamente. Cada chamador pendente é enfileirado e resolvido em ordem FIFO à medida que os itens ficam disponíveis.

### TryRead

```csharp
public abstract bool TryRead(out T item);
```

Tenta ler um item sem suspender. Retorna `true` e popula `item` se um item estava disponível; retorna `false` se o canal está vazio (define `item` como `default`).

`TryRead` não distingue entre um canal vazio-mas-aberto e um canal vazio-e-fechado. Use `Completion` para detectar o estado fechado ao usar `TryRead` em um loop de polling.

### ReadAllAsync

```csharp
public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default);
```

Retorna um `IAsyncEnumerable<T>` que itera sobre todos os itens até que o canal seja concluído e drenado. A enumeração termina de forma limpa sem propagar `ChannelClosedException` — simplesmente para.

```csharp
await foreach (var item in channel.Reader.ReadAllAsync(ct))
{
    Process(item);
}
// Chegou aqui quando o canal está completo e vazio
```

O token de cancelamento passado para `ReadAllAsync` é usado como fallback. Se `GetAsyncEnumerator` também receber um token (como `await foreach` faz via `WithCancellation`), o token do lado do `foreach` tem precedência.

### Completion

```csharp
public abstract ValkarnTask Completion { get; }
```

Um `ValkarnTask` que completa quando o canal está totalmente drenado após `Complete()` ser chamado. Especificamente:

- Se `Complete()` for chamado em um canal já vazio, `Completion` resolve imediatamente.
- Se `Complete()` for chamado enquanto itens permanecem no buffer, `Completion` resolve apenas após o último item ser consumido.

Aguardar `Completion` é a forma canônica de esperar que um pipeline termine.

```csharp
channel.Writer.Complete();
await channel.Reader.Completion;
// Todos os itens foram consumidos
```

---

## ChannelClosedException

```csharp
public sealed class ChannelClosedException : InvalidOperationException
```

Lançada em duas circunstâncias:

1. **Leitura de um canal concluído e drenado** — `ReadAsync()` lança quando o canal foi marcado como completo e não restam itens.
2. **Escrita em um canal concluído** — `WriteAsync()` lança quando `Complete()` foi chamado antes da escrita.

`ChannelClosedException` herda de `InvalidOperationException`. Não é lançada por `TryRead` ou `TryWrite`, que retornam `false` em vez disso.

Construtores:

```csharp
new ChannelClosedException()
new ChannelClosedException(string message)
new ChannelClosedException(Exception innerException)
```

---

## Canal Limitado: Backpressure em Detalhes

Quando o buffer de um canal limitado está cheio e `WriteAsync` é chamado, o escritor é suspenso e um registro de escritor pendente é enfileirado internamente. O escritor mantém seu item. Quando um consumidor chama `ReadAsync` ou `TryRead` e desenfileira um item:

1. O slot liberado é imediatamente reivindicado pelo escritor pendente mais antigo.
2. O item desse escritor é colocado no buffer.
3. O código aguardante do escritor é retomado.

Isso significa que um canal limitado cheio nunca perde itens e nunca desperdiça capacidade de buffer — há sempre uma correspondência de um para um entre um slot ficando livre e um escritor bloqueado sendo retomado. Em casos em que um leitor chega enquanto escritores pendentes estão esperando, mas o buffer está vazio, o item é transferido diretamente sem tocar no buffer.

---

## Padrões

### Produtor/consumidor básico

```csharp
var channel = ValkarnTask.Channel.CreateUnbounded<WorkItem>();

// Produtor (ex.: executa em uma thread de background ou de callbacks)
async ValkarnTask ProduceAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var work = await FetchNextWorkItemAsync(ct);
        await channel.Writer.WriteAsync(work);
    }
    channel.Writer.Complete();
}

// Consumidor (executa onde você escolher chamá-lo)
async ValkarnTask ConsumeAsync()
{
    await foreach (var item in channel.Reader.ReadAllAsync())
    {
        await ProcessAsync(item);
    }
}
```

### Múltiplos produtores, consumidor único

Múltiplos produtores podem manter cada um uma referência a `channel.Writer` e chamar `WriteAsync` concorrentemente. Todas as operações de canal são protegidas por um lock interno, portanto isso é seguro.

```csharp
var channel = ValkarnTask.Channel.CreateBounded<Event>(capacity: 64);

// Vários produtores escrevendo concorrentemente
ValkarnTask ProducerA() => ProduceFrom(sourceA, channel.Writer);
ValkarnTask ProducerB() => ProduceFrom(sourceB, channel.Writer);
ValkarnTask ProducerC() => ProduceFrom(sourceC, channel.Writer);

// Consumidor único (padrão — nenhuma flag multiConsumer necessária)
async ValkarnTask ConsumerAsync()
{
    await foreach (var ev in channel.Reader.ReadAllAsync())
        HandleEvent(ev);
}
```

Ao usar múltiplos produtores com um canal limitado, coordene `Complete()` cuidadosamente — chame-o apenas após todos os produtores terminarem de escrever, caso contrário alguns escritores podem receber `ChannelClosedException`.

### Múltiplos produtores, múltiplos consumidores

```csharp
// multiConsumer: true habilita ReadAsync concorrente de vários consumidores
var channel = ValkarnTask.Channel.CreateUnbounded<Job>(multiConsumer: true);

async ValkarnTask WorkerAsync(int id, CancellationToken ct)
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
        // Canal encerrado — sair graciosamente
    }
}
```

Cada item é entregue a exatamente um consumidor. Os consumidores competem por itens em ordem FIFO (o consumidor que está esperando há mais tempo recebe o próximo item disponível).

### Desligamento gracioso

A sequência de desligamento recomendada é:

1. Sinalizar que todos os produtores parem (ex.: cancelar seu `CancellationToken`).
2. Chamar `channel.Writer.Complete()` após todos os produtores pararem de escrever.
3. Aguardar `channel.Reader.Completion` para confirmar que todos os itens foram consumidos.

```csharp
cts.Cancel();                        // parar produtores
await allProducersTask;              // aguardá-los sair
channel.Writer.Complete();           // selar o canal
await channel.Reader.Completion;     // drenar itens restantes
```

Se você estiver usando `ReadAllAsync`, a etapa 3 acontece automaticamente — o loop `await foreach` sai quando o canal está completo e vazio.

---

## Comparação com System.Threading.Channels

| Recurso | Canais Valkarn | System.Threading.Channels |
|---------|-----------------|---------------------------|
| Tipo de retorno | `ValkarnTask` / `ValkarnTask<T>` | `ValueTask` / `ValueTask<T>` |
| Alocação (caminho quente) | Zero (ilimitado de consumidor único) | Próximo de zero |
| `WaitToReadAsync` | Não presente — use `ReadAsync` ou `ReadAllAsync` | Presente |
| `TryComplete(Exception)` | Não presente — use `Complete()` | Presente |
| `Count` / `CanCount` | Não exposto | Presente em alguns tipos de canal |
| Política de descarte quando cheio | Não suportado — `WriteAsync` bloqueia | `DropWrite`, `DropNewest`, `DropOldest`, `Wait` |
| Enumerável assíncrono | `ReadAllAsync()` | `ReadAllAsync()` |
| Thread safety | Completo (baseado em lock) | Completo (baseado em lock) |

A diferença principal é que os Canais Valkarn se integram nativamente com `ValkarnTask` para await sem overhead em builds Unity, e o caminho de consumidor único evita um alugar/retornar de pool em cada chamada `ReadAsync` ao embutir o core de conclusão diretamente no canal.
