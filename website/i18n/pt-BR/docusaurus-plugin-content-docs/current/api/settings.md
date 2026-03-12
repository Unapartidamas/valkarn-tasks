---
sidebar_position: 4
title: Configurações & Result
---

# VlkTaskSettings

`VlkTaskSettings` é um `ScriptableObject` Unity que controla o comportamento em runtime do Valkarn Tasks — principalmente o pool de objetos que recicla máquinas de estados async e objetos promise para evitar garbage durante o jogo.

---

## Criando o Asset de Configurações

1. Na janela Project, clique com o botão direito em uma pasta `Resources` (crie uma se não tiver).
2. Selecione **Assets > Create > Valkarn Tasks > Task Settings**.
3. Nomeie o arquivo `VlkTaskSettings` e coloque-o dentro da pasta `Resources`.

O arquivo deve ser nomeado exatamente `VlkTaskSettings` e deve residir em uma pasta chamada `Resources` em qualquer lugar do seu projeto. O asset é carregado em runtime com `Resources.Load<VlkTaskSettings>("VlkTaskSettings")`.

Se nenhum asset for encontrado, todas as configurações voltam para seus padrões integrados. A biblioteca funciona corretamente sem o asset — criá-lo só é necessário quando você quer alterar os padrões.

---

## Acessando as Configurações em Runtime

Em builds Unity, as configurações são lidas do asset por meio de um singleton em cache:

```csharp
VlkTaskSettings settings = VlkTaskSettings.Instance;
```

Os parâmetros de pool mais comuns também são expostos diretamente em `VlkTask` por conveniência:

```csharp
int max      = VlkTask.DefaultMaxPoolSize;   // lê de VlkTaskSettings.Instance
int min      = VlkTask.MinPoolSize;
int interval = VlkTask.TrimCheckInterval;
```

Em builds não-Unity (testes, .NET standalone), `VlkTaskSettings` é uma classe estática com propriedades mutáveis em vez de um `ScriptableObject`. Os mesmos nomes de propriedade se aplicam e podem ser escritos diretamente:

```csharp
// Apenas builds não-Unity / de teste
VlkTask.DefaultMaxPoolSize = 512;
VlkTask.TrimCheckInterval  = 600;
VlkTask.MinPoolSize        = 16;
```

---

## Propriedades Configuráveis

### Configuração do Pool

#### DefaultMaxPoolSize

| | |
|-|-|
| Tipo | `int` |
| Padrão | `256` |
| Intervalo válido | `8` – `1024` |
| Tooltip do inspetor | "Máximo de itens por tipo de pool. Itens em excesso são descartados." |

O número máximo de objetos retidos em cada pool por tipo. Cada instanciação genérica distinta (ex.: `PooledPromise<int>`, `PooledPromise<string>`) tem seu próprio pool limitado a este valor.

Quando uma task é concluída e seu objeto interno é retornado ao pool, se o pool já mantém `DefaultMaxPoolSize` itens, o objeto retornado é descartado (elegível para GC). Isso evita crescimento ilimitado de memória após um pico de atividade assíncrona.

Aumente este valor se o profiling mostrar alocações frequentes de GC durante cargas de trabalho assíncronas de alta taxa sustentada. Diminua-o se a pressão de memória for uma preocupação e as tasks não forem reutilizadas com frequência.

#### MinPoolSize

| | |
|-|-|
| Tipo | `int` |
| Padrão | `8` |
| Intervalo válido | `1` – `64` |
| Tooltip do inspetor | "Tamanho mínimo do pool — nunca encolher abaixo disso." |

A passagem de trimming do pool nunca reduzirá nenhum pool abaixo desta contagem. Isso garante que uma linha de base de pool aquecido esteja sempre disponível, evitando picos de alocação após um período de silêncio onde a passagem de trimming poderia ter liberado tudo.

#### TrimCheckInterval

| | |
|-|-|
| Tipo | `int` |
| Padrão | `300` |
| Intervalo válido | `30` – `1000` |
| Tooltip do inspetor | "Frames entre verificações de trimming. A 60fps, 300 ≈ 5 segundos." |

Quantos frames decorrem entre as passagens de trimming do pool. A passagem de trimming percorre todos os pools registrados e libera objetos em excesso (aqueles acima de `MinPoolSize`) se o pool tiver sido consistentemente superdimensionado.

A 60 fps, o valor padrão de 300 equivale a aproximadamente 5 segundos entre verificações. Diminua este valor se quiser trimming mais agressivo e frequente (ao custo de mais trabalho de trimming frequente). Aumente-o se as passagens de trimming estiverem aparecendo como picos no profiling.

#### TrimHysteresisCount

| | |
|-|-|
| Tipo | `int` |
| Padrão | `2` |
| Intervalo válido | `1` – `10` |
| Tooltip do inspetor | "Número de verificações consecutivas acima do limiar antes do trimming." |

Um pool só é trimado após ter sido observado como superdimensionado por este número de ciclos de trimming consecutivos. Isso evita instabilidade — se o jogo tem um breve pico seguido de um período de silêncio, uma contagem de histerese de 2 significa que o pool sobrevive a um ciclo de silêncio antes de começar a liberar objetos.

#### TrimReleaseRatio

| | |
|-|-|
| Tipo | `float` |
| Padrão | `0.25` |
| Intervalo válido | `0.1` – `1.0` |
| Tooltip do inspetor | "Fração do excesso a liberar por ciclo de trimming (0.25 = 25%)." |

Quando um pool é trimado, esta fração da capacidade excedente (itens acima de `MinPoolSize`) é liberada por ciclo em vez de tudo de uma vez. Um valor de `0.25` significa que cada passagem de trimming remove 25% do excedente. Esta liberação gradual evita uma queda repentina no tamanho do pool que poderia causar um pico de alocação se a carga aumentar novamente.

Defina como `1.0` se quiser todo o excesso liberado imediatamente em cada ciclo.

---

### Ciclo de Vida

#### EnableAutoCancel

| | |
|-|-|
| Tipo | `bool` |
| Padrão | `true` |
| Tooltip do inspetor | "Vincular automaticamente tasks de MonoBehaviour ao destroyCancellationToken." |

Quando habilitado, tasks iniciadas a partir de um `MonoBehaviour` são automaticamente vinculadas ao `destroyCancellationToken` daquele `MonoBehaviour`. Se o `MonoBehaviour` for destruído enquanto uma task está em execução, a task é cancelada em vez de continuar a executar contra um objeto destruído.

Desabilite isso apenas se você estiver gerenciando o cancelamento manualmente e não quiser vínculo automático.

---

### Tratamento de Erros

#### LogUnobservedCancellations

| | |
|-|-|
| Tipo | `bool` |
| Padrão | `false` |
| Tooltip do inspetor | "Registrar cancelamentos não observados como avisos." |

Por padrão, uma task que é cancelada mas nunca aguardada (cancelamento não observado) é silenciosamente ignorada. Habilite isso para registrar um aviso quando isso acontecer. Útil durante o desenvolvimento para encontrar tasks fire-and-forget que cancelam silenciosamente.

Falhas não observadas são sempre relatadas via o evento `VlkTask.UnobservedException` independentemente desta configuração.

#### MaxExceptionLogsPerFrame

| | |
|-|-|
| Tipo | `int` |
| Padrão | `10` |
| Intervalo válido | `1` – `100` |
| Tooltip do inspetor | "Máximo de logs de exceção por frame para evitar spam." |

Limita o número de entradas de log de exceção não observada emitidas em um único frame. Se muitas tasks falharem no mesmo frame (por exemplo, após uma falha de rede), isso evita que o console seja inundado com centenas de rastreamentos de pilha idênticos.

---

## Como o Trimming do Pool Funciona em Runtime

Em cada atualização do player loop do Unity, `PlayerLoopHelper` incrementa um contador de frames. Quando o contador atinge `TrimCheckInterval`, chama `PoolRegistry.TrimAll(MinPoolSize)`. Todo pool que foi registrado verifica se tem sido superdimensionado por pelo menos `TrimHysteresisCount` verificações consecutivas. Se sim, libera `TrimReleaseRatio` de seu excesso.

Os pools se registram automaticamente no primeiro uso. O método `VlkTask.GetPoolInfo()` retorna um snapshot de todos os pools atualmente registrados com seu tipo, tamanho atual e tamanho máximo — isso é o que a janela Task Tracker exibe.

```
Frame 0 ──────────────────────────────────────────────────────────
  Player loop executa
  Pool A: tamanho 200, máx 256 — normal, sem trim necessário

Frame 300 ────────────────────────────────────────────────────────
  TrimAll dispara
  Pool A: tamanho 18, máx 256 — acima de MinPoolSize(8), mas primeira verificação de excesso
  hysteresisCount para A = 1 (ainda não no limiar de 2)

Frame 600 ────────────────────────────────────────────────────────
  TrimAll dispara
  Pool A: tamanho 18, máx 256 — acima de MinPoolSize(8), segunda verificação de excesso
  hysteresisCount para A = 2 — limiar atingido!
  Excesso = 18 - 8 = 10; liberar 10 * 0.25 = 2 objetos
  Pool A: tamanho 16
```

---

## Janela Task Tracker

Abra via **Window > Valkarn Tasks > Task Tracker**.

O Task Tracker é uma janela somente do Editor que mostra o estado ao vivo do pool enquanto você está no Modo de Jogo. Atualiza em um intervalo configurável (padrão de 0,5 segundos, ajustável de 0,1 a 5 segundos via o slider na barra de ferramentas).

### Aba Pools

Lista todos os tipos de pool que estiveram ativos desde o último domain reload, classificados por tamanho atual (do maior para o menor). Cada linha mostra:

| Coluna | Descrição |
|--------|-------------|
| Tipo | O nome do tipo do objeto em pool, com argumentos genéricos expandidos |
| Tamanho | Número atual de objetos no pool |
| Máx | O teto `DefaultMaxPoolSize` para este pool |
| Uso | Uma barra de progresso mostrando `Tamanho / Máx` como porcentagem |

Se nenhum pool tiver sido usado ainda, uma mensagem diz "Nenhum pool ativo. Os pools são criados no primeiro uso."

### Aba Config

Mostra os três parâmetros de pool ao vivo conforme lidos de `VlkTask.DefaultMaxPoolSize`, `VlkTask.TrimCheckInterval` e `VlkTask.MinPoolSize`. Os valores só são mostrados enquanto no Modo de Jogo — no Modo de Edição uma nota instrui você a entrar no Modo de Jogo para ver os valores ao vivo.

A aba Config também exibe uma referência ao asset `VlkTaskSettings` (se um existir em `Resources`), para que você possa clicar para inspecioná-lo ou modificá-lo. Se nenhum asset for encontrado, um aviso direciona você a criar um.

---

## Result&lt;T&gt;

`Result<T>` é uma union discriminada em struct para representar o resultado de uma operação sem lançar exceção. É o tipo de retorno usado pelos combinadores `WhenAll` para relatar resultados por task, e também está disponível como um padrão de resultado de uso geral.

```csharp
public readonly struct Result<T>
{
    public bool IsSuccess  { get; }
    public bool IsFailure  { get; }   // true se faulted ou cancelado
    public bool IsFaulted  { get; }
    public bool IsCanceled { get; }

    public VlkTask.Status Status { get; }
    public T Value    { get; }        // lança se não foi bem-sucedido
    public Exception Error { get; }   // null se não faulted

    public static Result<T> Success(T value);
    public static Result<T> Failure(string error);    // envolve em InvalidOperationException
    public static Result<T> Faulted(Exception error);
    public static Result<T> Canceled(OperationCanceledException oce = null);

    public static implicit operator bool(Result<T> r);  // true se foi bem-sucedido
}
```

Um `Result` não-genérico existe para tasks void com a mesma forma mas sem `Value`.

### Métodos de extensão AsResult

Converta qualquer `VlkTask` ou `VlkTask<T>` em um `Result` sem um `try/catch` em seu próprio código:

```csharp
public static VlkTask<Result<T>> AsResult<T>(this VlkTask<T> task);
public static VlkTask<Result>    AsResult(this VlkTask task);
```

Se a task subjacente já estiver sincronamente concluída (o caminho rápido de zero alocação), `AsResult` retorna imediatamente sem criar uma máquina de estados async. Caso contrário, envolve em um método async que captura `OperationCanceledException` e todas as outras exceções, traduzindo-as na variante `Result` apropriada.

### Quando usar Result&lt;T&gt;

Use `Result<T>` em vez de capturar exceções quando:

- Você está chamando múltiplas tasks em paralelo e quer resultados por task sem curto-circuitar o lote inteiro.
- Quer expressar operações falíveis de forma type-safe sem fluxo de controle por exceção.
- Está retornando de um método que o chamador pode não querer envolver em `try/catch`.

```csharp
// Disparar múltiplas tasks, obter todos os resultados independentemente de falhas individuais
Result<int>[] results = await VlkTask.WhenAll(
    FetchScoreAsync(playerA).AsResult(),
    FetchScoreAsync(playerB).AsResult(),
    FetchScoreAsync(playerC).AsResult()
);

foreach (var r in results)
{
    if (r.IsSuccess)
        Debug.Log($"Pontuação: {r.Value}");
    else if (r.IsFaulted)
        Debug.LogError($"Falhou: {r.Error.Message}");
    else
        Debug.Log("Cancelado");
}
```

Verificar `IsSuccess` ou usar o operador implícito `bool` são as formas preferidas de ramificar em um resultado:

```csharp
var result = await SomeOperationAsync().AsResult();

if (result)
{
    Use(result.Value);
}
```

Acessar `result.Value` quando `IsSuccess` é `false` lança `InvalidOperationException`.

### Métodos factory

| Método | Status definido | Erro definido |
|--------|-----------|-----------|
| `Result<T>.Success(value)` | `Succeeded` | nenhum |
| `Result<T>.Failure(message)` | `Faulted` | `new InvalidOperationException(message)` |
| `Result<T>.Faulted(exception)` | `Faulted` | a exceção fornecida |
| `Result<T>.Canceled(oce?)` | `Canceled` | a `OperationCanceledException` fornecida, ou `null` |

A propriedade `Succeeded` existe em `Result` e `Result<T>`, mas está marcada como `[Obsolete]` — use `IsSuccess` em vez disso, por consistência com `IsFailure`, `IsFaulted` e `IsCanceled`.
