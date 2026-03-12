---
sidebar_position: 1
title: Struct Tasks
---

# Struct Tasks

`VlkTask` e `VlkTask<T>` são os tipos de retorno assíncronos principais do Valkarn Tasks. Diferente de `System.Threading.Tasks.Task`, que é um tipo de referência que sempre aloca no heap, ambos os tipos de task do Valkarn são valores `readonly struct`. Esta página explica o que isso significa na prática, como funciona o caminho feliz de zero alocação e como o compilador se integra com a maquinaria do async/await.

---

## Por que um `readonly struct`?

Uma task baseada em classe como `Task<T>` deve ser alocada no heap toda vez que um método async é chamado, mesmo para métodos que completam de forma síncrona. Em um loop de jogo Unity a 60 fps, centenas de pequenas operações assíncronas por frame podem acumular uma pressão de GC mensurável.

`VlkTask` e `VlkTask<T>` são declarados como `readonly partial struct`:

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncVlkTaskMethodBuilder))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct VlkTask
{
    internal readonly ISource source;
    internal readonly uint token;
}
```

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncVlkTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct VlkTask<T>
{
    internal readonly VlkTask.ISource<T> source;
    internal readonly T result;
    internal readonly uint token;
}
```

Ser uma struct significa que o valor da task em si vive na pilha (ou inline em seu objeto pai) em vez de no heap. O modificador `readonly` garante que o compilador possa raciocinar sobre imutabilidade e evita bugs acidentais de cópia. `StructLayout.Auto` permite que o runtime otimize a ordenação dos campos para a plataforma de destino.

### O invariante principal: `source == null`

O design é construído em torno de um único invariante:

> Quando `source` é `null`, a task é concluída de forma síncrona sem erro. Nenhum objeto no heap está envolvido.

`VlkTask.CompletedTask` é `default(VlkTask)` — seu campo `source` é null, portanto não tem custo algum. `VlkTask<T>` carrega seu resultado inline no campo `result`, tornando `VlkTask.FromResult(value)` também uma chamada de zero alocação:

```csharp
// Zero alocação — source é null, resultado armazenado inline
VlkTask<int> task = VlkTask.FromResult(42);

// Também zero alocação — source é null
VlkTask done = VlkTask.CompletedTask;
```

---

## O caminho feliz de zero alocação

Quando um método `async` é concluído sem nunca suspender (sem `await` que yield para uma operação incompleta), todo o método é executado de forma síncrona na thread chamadora. O builder detecta isso e retorna uma task com `source == null`.

O awaiter verifica isso imediatamente:

```csharp
public bool IsCompleted
{
    get
    {
        var s = task.source;
        return s == null || s.GetStatus(task.token).IsCompleted();
    }
}
```

Quando `IsCompleted` é verdadeiro antes que `OnCompleted` seja chamado, a máquina de estados não registra uma continuação. `GetResult` é chamado imediatamente e, para `VlkTask<T>` com `source == null`, o resultado é lido do campo `result` inline da struct:

```csharp
public T GetResult()
{
    var s = task.source;
    if (s == null)
        return task.result;   // inline, sem chamada ISource
    return s.GetResult(task.token);
}
```

Nenhum objeto é criado, nenhum despacho de interface ocorre e nenhum delegate de continuação é alocado. O await completo se resolve como uma leitura direta de valor.

### Quando uma source é necessária

Se um método async suspende (aguarda algo que ainda não está completo), o builder aloca um objeto `AsyncVlkTaskRunner<TStateMachine>` em pool (ou `AsyncVlkTaskRunner<TStateMachine, TResult>` para a variante genérica). Este objeto tem dupla função: mantém a máquina de estados gerada pelo compilador por valor e implementa `VlkTask.ISource`, para que possa ser usado diretamente como a source de apoio da task. A task retornada aos chamadores envolve este runner junto com um token `uint` geracional.

Na conclusão, quando o chamador chama `GetResult` no awaiter, o runner se redefine e retorna ao seu pool — portanto, a alocação é amortizada em muitas invocações de método.

---

## A interface `ISource`

O contrato entre uma struct `VlkTask` e seu objeto de apoio assíncrono é `VlkTask.ISource`:

```csharp
public interface ISource
{
    VlkTask.Status GetStatus(uint token);
    void GetResult(uint token);
    void OnCompleted(Action<object> continuation, object state, uint token);
    VlkTask.Status UnsafeGetStatus();
}

public interface ISource<out T> : ISource
{
    new T GetResult(uint token);
}
```

Qualquer objeto que implementa `ISource` pode dar suporte a um `VlkTask`. A biblioteca inclui várias implementações:

| Tipo | Finalidade |
|------|---------|
| `AsyncVlkTaskRunner<TStateMachine>` | Dá suporte a todo método `async VlkTask` (interno) |
| `AsyncVlkTaskRunner<TStateMachine, TResult>` | Dá suporte a todo método `async VlkTask<T>` (interno) |
| `VlkTask.PooledPromise` | Source de conclusão manual com retorno automático ao pool |
| `VlkTask.PooledPromise<T>` | Variante genérica do acima |
| `VlkTask.Promise` | Source de conclusão manual sem pooling (operações de longa duração) |
| `VlkTask.Promise<T>` | Variante genérica do acima |

O parâmetro `uint token` é uma proteção geracional. Quando uma source em pool é redefinida para reutilização, seu contador de geração é incrementado. Qualquer struct `VlkTask` que mantém o token antigo receberá imediatamente uma `InvalidOperationException` em vez de silenciosamente ler estado reciclado.

---

## `VlkTask` vs `VlkTask<T>`

| Recurso | `VlkTask` | `VlkTask<T>` |
|---------|-----------|-------------|
| Valor de retorno | Nenhum (equivalente a void) | `T` |
| Armazenamento de resultado inline | Sem campo `result` | Campo `result` (tipo `T`) |
| Awaiter `GetResult` | `void` | Retorna `T` |
| Tipo de builder | `AsyncVlkTaskMethodBuilder` | `AsyncVlkTaskMethodBuilder<TResult>` |
| Valor concluído de forma síncrona | `VlkTask.CompletedTask` | `VlkTask.FromResult(value)` |
| Converter para não-genérico | Não aplicável | `.AsNonGeneric()` |

Use `VlkTask` quando um método async não tem valor de retorno significativo, e `VlkTask<T>` quando produz um resultado. Você sempre pode fazer downcast de um `VlkTask<T>` para um `VlkTask` via `AsNonGeneric()` quando precisar misturar tasks tipadas e não tipadas em combinadores como `WhenAll`.

---

## Como o builder de método async funciona

O compilador C# procura o tipo nomeado em `[AsyncMethodBuilder(...)]` no tipo de retorno. Para `VlkTask`, esse é `AsyncVlkTaskMethodBuilder`. Para `VlkTask<T>`, é `AsyncVlkTaskMethodBuilder<TResult>`.

O builder é em si uma struct para evitar uma alocação no heap apenas para o objeto builder. Ele tem dois campos (três para a variante genérica):

```csharp
public struct AsyncVlkTaskMethodBuilder
{
    IStateMachineRunnerPromise runner;   // null até a primeira suspensão
    Exception syncException;            // definido apenas no caminho de falha síncrona
}

public struct AsyncVlkTaskMethodBuilder<TResult>
{
    IStateMachineRunnerPromise<TResult> runner;
    Exception syncException;
    TResult result;                     // definido apenas no caminho de sucesso síncrono
}
```

### Ciclo de vida do builder

O compilador chama estes métodos em ordem:

**1. `Create()`** — retorna um builder padrão (todos os campos null/default). Sem alocação.

**2. `Start(ref stateMachine)`** — chama `stateMachine.MoveNext()` de forma síncrona. Se o método for concluído sem atingir um `await` incompleto, `SetResult`/`SetException` é chamado e `runner` permanece null.

**3. `AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine)`** — chamado quando o método encontra um `await` incompleto. Se `runner` é null (primeira suspensão), ele aluga ou cria um `AsyncVlkTaskRunner` e copia a máquina de estados para ele. Então chama `awaiter.UnsafeOnCompleted(runner.MoveNextAction)` para registrar a continuação da máquina de estados.

**4. `SetResult()` / `SetException(exception)`** — sinaliza conclusão no `VlkTaskCompletionCore` do runner, que acorda qualquer awaiter registrado.

**5. Propriedade `Task`** — verificada pelo chamador para obter o valor `VlkTask`. No caminho de sucesso síncrono (`runner == null && syncException == null`), retorna `default` (ou `new VlkTask<T>(result)` para a variante genérica) — zero alocação. No caminho assíncrono, envolve o runner como a source.

A otimização crítica é que `runner` é alocado de forma lazy. Se um método é concluído de forma síncrona (o caso comum para hits de cache, guards, retornos antecipados), nenhum objeto em pool é alugado.

---

## Estados de `VlkTaskStatus`

O status é representado por um enum do tamanho de um `byte` aninhado dentro de `VlkTask`:

```csharp
public enum Status : byte
{
    Pending   = 0,   // ainda não concluído
    Succeeded = 1,   // concluído normalmente
    Faulted   = 2,   // concluído com uma exceção não tratada
    Canceled  = 3    // concluído via OperationCanceledException
}
```

Você pode verificar o status diretamente:

```csharp
VlkTask task = SomeOperation();
VlkTask.Status status = task.GetStatus();

switch (status)
{
    case VlkTask.Status.Pending:
        // Ainda em execução — não é possível chamar GetResult
        break;
    case VlkTask.Status.Succeeded:
        // Concluído normalmente
        break;
    case VlkTask.Status.Faulted:
        // Concluído com exceção — GetResult irá relançar
        break;
    case VlkTask.Status.Canceled:
        // Concluído com OperationCanceledException
        break;
}
```

Para o caminho rápido de conclusão síncrona (onde `source == null`), `GetStatus()` retorna `Succeeded` sem nenhuma chamada de interface:

```csharp
public Status GetStatus()
{
    if (source == null) return Status.Succeeded;
    return source.GetStatus(token);
}
```

A propriedade `IsCompleted` segue o mesmo padrão e retorna `true` para qualquer estado não `Pending`.

---

## Implicações do IL2CPP

O IL2CPP compila C# para C++ antes de construir para código nativo. Tipos de valor genéricos — incluindo structs — são totalmente especializados no código gerado, o que tem consequências importantes para esta biblioteca.

**Especialização da máquina de estados.** O compilador gera uma struct de máquina de estados única por método async. `AsyncVlkTaskRunner<TStateMachine>` é, portanto, também único por método async, e `VlkTaskPool<AsyncVlkTaskRunner<TStateMachine>>` é um pool separado por método. Isso é realmente benéfico: o pool nunca é compartilhado entre tipos incompatíveis, eliminando qualquer risco de confusão de tipos.

**Sem boxing da máquina de estados.** A máquina de estados é armazenada por valor dentro do objeto runner, sem boxing. O IL2CPP lida com isso corretamente porque o runner é uma `sealed class` com um campo `TStateMachine` concreto.

**Proteção contra stripping.** O atributo `[AsyncMethodBuilder]` mantém os tipos de builder ativos. No entanto, se você usa `VlkTask.ISource` por meio de uma referência de interface em IL2CPP com stripping agressivo, adicione uma entrada `link.xml` preservando o assembly `UnaPartidaMas.Valkarn.Tasks`:

```xml
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```

**`ICriticalNotifyCompletion`.** As structs awaiter implementam `ICriticalNotifyCompletion`, que diz ao compilador para chamar `UnsafeOnCompleted` em vez de `OnCompleted`. A variante "unsafe" ignora intencionalmente a captura de `ExecutionContext`. Isso é correto para Unity — não há `SynchronizationContext` na configuração padrão do Unity, e capturar um adicionaria overhead sem benefício. Sob IL2CPP, isso também evita o overhead do caminho `ExecutionContext.Run` que o `Task` padrão sempre paga.

---

## Exemplos práticos

### Retorno antecipado sem alocação

```csharp
// async VlkTask<int> que completa de forma síncrona no caminho quente
async VlkTask<int> GetCachedValue(string key)
{
    if (_cache.TryGetValue(key, out var value))
        return value;            // o compilador chama SetResult(value); source fica null

    var result = await FetchFromDatabaseAsync(key);
    _cache[key] = result;
    return result;
}
```

Quando o valor está em cache, o método nunca suspende. O `VlkTask<int>` retornado tem `source == null` e carrega o resultado inline. Nenhuma alocação no heap ocorre neste caminho.

### Verificando IsCompleted antes de aguardar

```csharp
VlkTask<Texture2D> loadTask = LoadTextureAsync("sprites/hero.png");

if (loadTask.IsCompleted)
{
    // Já concluído — GetAwaiter().GetResult() lê resultado inline sem chamada ISource
    Texture2D tex = loadTask.GetAwaiter().GetResult();
    ApplyTexture(tex);
}
else
{
    // Genuinamente assíncrono — registrar continuação
    ApplyTextureAsync(loadTask).Forget();
}
```

### Observando exceções não tratadas

Tasks com falha que nunca são aguardadas (padrões fire-and-forget) relatam suas exceções por meio do evento `VlkTask.UnobservedException`. Isso é disparado deterministicamente no momento de retorno ao pool para sources em pool, ou a partir do finalizador para tasks respaldadas por `Promise`.

```csharp
VlkTask.UnobservedException += ex =>
{
    Debug.LogError($"[VlkTask] Não observado: {ex}");
};
```

O evento é thread-safe; handlers podem ser adicionados ou removidos de qualquer thread usando um loop de compare-exchange sem bloqueio.
