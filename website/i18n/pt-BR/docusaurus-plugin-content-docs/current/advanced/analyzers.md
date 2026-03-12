---
sidebar_position: 1
title: Regras do Analisador
---

# Regras do Analisador

O Valkarn Tasks inclui dois pacotes de analisador Roslyn que se ativam automaticamente quando o pacote é importado:

- **`UnaPartidaMas.Valkarn.Tasks.SourceGen.dll`** — Regras específicas à correção do ValkarnTask e ao ciclo de vida do Unity. Os IDs de regra começam com `TT`.
- **`UnaPartidaMas.Valkarn.Tasks.Analyzer.dll`** — Regras de migração para bases de código que estão migrando do UniTask. Os IDs de regra começam com `MIG`. Essas regras disparam **apenas quando `Cysharp.Threading.Tasks.UniTask` está presente** na compilação, portanto são silenciosas em novos projetos.

Ambos os pacotes são DLLs pré-compiladas localizadas na pasta `Analyzers/` do pacote. O Unity os carrega como analisadores Roslyn via a referência `.asmdef`; o projeto `_TestRunner~` os carrega via itens `<Analyzer>` em `TestRunner.csproj`.

---

## Regras de Correção (TT)

Essas regras detectam bugs relacionados à natureza de consumo único do `ValkarnTask` e uso indevido de fire-and-forget.

---

### TT001 — ValkarnTask já aguardado

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Aviso |
| Categoria | ValkarnTask |
| Correção de código | Não |

`ValkarnTask` é de consumo único: uma vez aguardado, o token interno fica obsoleto. Um segundo `await` na mesma variável encontrará esse token obsoleto e lançará exceção. O analisador detecta quando a mesma variável local, parâmetro ou campo do tipo `ValkarnTask`/`ValkarnTask<T>` é aguardado mais de uma vez dentro do mesmo método.

**Dispara em:**
```csharp
async ValkarnTask Bad()
{
    ValkarnTask work = DoWorkAsync();
    await work;   // primeiro await — ok
    await work;   // TT001: já aguardado
}
```

**Correção:** Se precisar ramificar no resultado, capture-o com `.AsResult()` antes do primeiro await, ou reestruture para que a task seja aguardada exatamente uma vez.
```csharp
async ValkarnTask Good()
{
    var result = await DoWorkAsync().AsResult();
    // usar resultado em ambas as ramificações
}
```

---

### TT002 — ValkarnTask não aguardado nem descartado

| Propriedade | Valor |
|------------|------------------------|
| Severidade | **Erro** |
| Categoria | ValkarnTask |
| Correção de código | Não |

Uma chamada de método que retorna `ValkarnTask` usada como declaração de expressão — não aguardada, não atribuída e não seguida de `.Forget()` — é um bug silencioso. Exceções lançadas dentro da task nunca são observadas, e a máquina de estados em pool da task nunca é retornada ao pool.

O analisador verifica declarações de expressão onde o tipo da expressão resolve para `ValkarnTask` ou `ValkarnTask<T>`. Ele ignora:
- Expressões de atribuição (`tasks[i] = DoWork()`)
- Cadeias terminando em `.Forget()` (fire-and-forget intencional)

**Dispara em:**
```csharp
void Bad()
{
    LoadDataAsync();      // TT002: não aguardado nem descartado
    ProcessItemAsync();   // TT002
}
```

**Correção — aguardá-la:**
```csharp
async ValkarnTask Good()
{
    await LoadDataAsync();
    await ProcessItemAsync();
}
```

**Correção — fire-and-forget explícito:**
```csharp
void GoodFireAndForget()
{
    LoadDataAsync().Forget();
}
```

---

### TT013 — ValkarnTask retornado mas nunca consumido

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Aviso |
| Categoria | ValkarnTask |
| Correção de código | Não |

Este ID de regra é **reservado para análise de fluxo de dados futura**. Pretende detectar o padrão atribuído-mas-nunca-aguardado — armazenar um `ValkarnTask` em uma variável e então nunca aguardá-lo ou descartá-lo — que o TT002 não cobre porque o TT002 só examina declarações de expressão puras.

A implementação atual não registra nenhuma ação de sintaxe. Quando a análise de fluxo de dados for implementada, o TT013 complementará o TT002 cobrindo:
```csharp
async ValkarnTask Bad()
{
    ValkarnTask task = DoWorkAsync();  // atribuído mas nunca aguardado
    DoOtherThing();
    // task é abandonada — TT013 (futuro)
}
```

Métodos decorados com `[FireAndForget]` serão isentos.

---

## Regras de Ciclo de Vida (TT)

Essas regras se relacionam ao gerenciamento de tempo de vida do MonoBehaviour e cancelamento.

---

### TT010 — Auto-cancel ativo para método async do MonoBehaviour

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Info |
| Categoria | ValkarnTask |
| Correção de código | Não |

Uma dica informacional. Qualquer método `async ValkarnTask` (ou `async ValkarnTask<T>`) dentro de uma classe que herda de `UnityEngine.MonoBehaviour` será automaticamente cancelado quando o objeto for destruído, **a menos** que o método seja decorado com `[NoAutoCancel]`. Este diagnóstico torna esse comportamento visível no IDE sem exigir que o desenvolvedor se lembre disso.

**Dispara em:**
```csharp
public class MyBehaviour : MonoBehaviour
{
    async ValkarnTask LoadLevel()  // TT010: auto-cancel ativo
    {
        await ValkarnTask.Delay(2000);
    }
}
```

**Para desativar** (e suprimir TT010 para aquele método), adicione `[NoAutoCancel]`:
```csharp
[NoAutoCancel]
async ValkarnTask LoadLevel(CancellationToken ct)
{
    await ValkarnTask.Delay(2000, ct);
}
```

---

### TT011 — WhenAll mistura escopos de lifetime diferentes

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Aviso |
| Categoria | ValkarnTask |
| Correção de código | Não |

`ValkarnTask.WhenAll` chamado com tasks de diferentes escopos de lifetime pode produzir comportamento surpresa de cancelamento parcial. Se uma task está vinculada a um `MonoBehaviour` (retornada por um método de instância de uma classe que herda `MonoBehaviour`) e outra não está vinculada (uma task estática, ou de uma classe não-MonoBehaviour), destruir o objeto irá cancelar uma task mas não a outra, deixando o combinador em um estado indeterminado.

**Dispara em:**
```csharp
// Assumindo EnemyAI : MonoBehaviour
await ValkarnTask.WhenAll(
    enemyAI.PatrolAsync(),   // vinculado: cancelado automaticamente ao Destroy
    GlobalMusic.FadeAsync()  // não vinculado: vive indefinidamente
);
// TT011: WhenAll mistura lifetimes — PatrolAsync() vs GlobalMusic.FadeAsync()
```

**Correção — dar a ambas as tasks um token de cancelamento compartilhado:**
```csharp
using var cts = new CancellationTokenSource();
await ValkarnTask.WhenAll(
    enemyAI.PatrolAsync(cts.Token),
    GlobalMusic.FadeAsync(cts.Token)
);
```

---

### TT012 — Loop async sem verificação de cancelamento

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Aviso |
| Categoria | ValkarnTask |
| Correção de código | Não |

Um método `async ValkarnTask` contendo um loop `for`, `while`, `foreach` ou `do`-`while` cujo corpo não tem verificação de cancelamento é um "loop zumbi": se o cancelamento de ciclo de vida disparar (ex.: um MonoBehaviour é destruído), o sinal de auto-cancel não pode quebrar o loop. O loop continua executando mesmo que o objeto proprietário tenha ido embora.

O analisador considera que o corpo de um loop tem uma verificação de cancelamento se contiver qualquer um dos seguintes:
- Uma expressão `await` (a operação aguardada pode observar o token e lançar `OperationCanceledException`)
- `ThrowIfCancellationRequested` (como identificador ou acesso a membro)
- `IsCancellationRequested` (como identificador ou acesso a membro)

A verificação **não** desce para lambdas aninhadas ou funções locais.

**Dispara em:**
```csharp
async ValkarnTask PollForever(CancellationToken ct)
{
    while (true)                     // TT012: sem verificação de cancelamento no corpo
    {
        ProcessNextItem();
        Thread.Sleep(16);            // síncrono — não é um await
    }
}
```

**Correção — adicionar um await ou uma verificação explícita:**
```csharp
async ValkarnTask PollForever(CancellationToken ct)
{
    while (true)
    {
        ct.ThrowIfCancellationRequested();
        ProcessNextItem();
        await ValkarnTask.Yield();       // também satisfaz a verificação
    }
}
```

---

### TT014 — [NoAutoCancel] sem parâmetro CancellationToken

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Aviso |
| Categoria | ValkarnTask |
| Correção de código | Não |

`[NoAutoCancel]` em um método `async ValkarnTask` em um `MonoBehaviour` opta o método para fora do cancelamento automático de ciclo de vida. Mas se o método não tem parâmetro `CancellationToken`, não tem mecanismo para observar o cancelamento, tornando o atributo inútil e quase certamente indicando um parâmetro esquecido.

**Dispara em:**
```csharp
public class Enemy : MonoBehaviour
{
    [NoAutoCancel]
    async ValkarnTask Chase()      // TT014: [NoAutoCancel] mas sem parâmetro CancellationToken
    {
        await ValkarnTask.Delay(1000);
    }
}
```

**Correção — adicionar um parâmetro `CancellationToken`:**
```csharp
[NoAutoCancel]
async ValkarnTask Chase(CancellationToken ct)
{
    await ValkarnTask.Delay(1000, ct);
}
```

---

## Regras Informacionais (TT)

---

### TT015 — Adaptador bridge do Awaitable gerado

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Info |
| Categoria | ValkarnTask |
| Correção de código | Não |

Quando você faz `await` em um `Awaitable` do Unity (de `UnityEngine`) dentro de um método `async ValkarnTask`, o Valkarn Tasks gera automaticamente um adaptador bridge via `AwaitableBridge`. Este diagnóstico é informacional: confirma que a bridge está em vigor. Nenhuma ação é necessária.

**Dispara em:**
```csharp
async ValkarnTask LoadScene()
{
    await SceneManager.LoadSceneAsync("Main");  // TT015: adaptador bridge gerado
}
```

Nenhuma alteração é necessária. A bridge lida com a conversão de forma transparente.

---

## Regras de Qualidade de Código (TT)

---

### TT016 — Método async sem await

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Aviso |
| Categoria | ValkarnTask |
| Correção de código | Não |

Um método `async ValkarnTask` (ou `async ValkarnTask<T>`) que não contém expressões `await` — incluindo `await foreach`, `await using` e `await` dentro de declarações `using` — incorre no overhead completo de alocação de máquina de estados sem benefício. O compilador ainda gera uma máquina de estados, mas como não há ponto de suspensão, o método sempre completa de forma síncrona.

O analisador verifica: `AwaitExpressionSyntax`, `foreach` com palavra-chave `await`, declarações `using` com palavra-chave `await` e declarações `using` com palavra-chave `await`.

**Dispara em:**
```csharp
async ValkarnTask<int> ComputeTotal()  // TT016: sem await no corpo
{
    return items.Sum(x => x.Value);
}
```

**Correção — remover `async` e retornar uma task concluída:**
```csharp
ValkarnTask<int> ComputeTotal()
{
    return ValkarnTask.FromResult(items.Sum(x => x.Value));
}
```

Ou para retorno void:
```csharp
ValkarnTask DoSetup()
{
    Initialize();
    return ValkarnTask.CompletedTask;
}
```

---

### TT017 — [FireAndForget] em ValkarnTask\<T\>

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Aviso |
| Categoria | ValkarnTask |
| Correção de código | Não |

`[FireAndForget]` sinaliza que um método é intencionalmente executado sem aguardar seu resultado. Aplicá-lo a um método que retorna `ValkarnTask<T>` (resultado tipado) sempre descarta `T`, tornando o retorno tipado sem sentido. O método deve retornar `ValkarnTask` (void) em vez disso.

**Dispara em:**
```csharp
[FireAndForget]
async ValkarnTask<int> SendReport()   // TT017: valor de retorno é descartado
{
    await UploadAsync();
    return 42;                     // este 42 nunca é visto
}
```

**Correção — alterar o tipo de retorno para `ValkarnTask`:**
```csharp
[FireAndForget]
async ValkarnTask SendReport()
{
    await UploadAsync();
}
```

---

## Regras de Migração (MIG)

O analisador de migração se ativa **apenas quando o tipo `Cysharp.Threading.Tasks.UniTask` está presente** na compilação. As regras são informacionais ou avisos para guiar a transição do UniTask para o Valkarn Tasks. Nenhuma dessas regras tem correção automática; as descrições abaixo explicam a alteração manual.

---

### MIG001 — Tipo UniTask detectado

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Info |
| Categoria | ValkarnTaskMigration |

Um uso de um tipo `UniTask` (a struct em si ou `UniTask<T>`) foi detectado. Substitua pelo equivalente `ValkarnTask` ou `ValkarnTask<T>`.

```csharp
// Antes
UniTask<Sprite> LoadSprite(string path) { ... }

// Depois
ValkarnTask<Sprite> LoadSprite(string path) { ... }
```

---

### MIG002 — Parâmetro cancelImmediately é desnecessário

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Info |
| Categoria | ValkarnTaskMigration |

Os métodos `Delay` e outros baseados em tempo do UniTask aceitam um parâmetro `cancelImmediately` para optar por cancelamento rápido. O Valkarn Tasks cancela imediatamente por padrão — não há parâmetro `cancelImmediately`. Remova o argumento.

```csharp
// Antes
await UniTask.Delay(1000, cancelImmediately: true, cancellationToken: ct);

// Depois
await ValkarnTask.Delay(1000, ct);
```

---

### MIG003 — SuppressCancellationThrow() detectado

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Aviso |
| Categoria | ValkarnTaskMigration |

`.SuppressCancellationThrow()` do UniTask converte uma task cancelada em uma tupla `(bool isCancelled, T result)` sem lançar. O Valkarn Tasks usa `.AsResult()` para o mesmo propósito, que retorna uma struct `Result<T>`.

```csharp
// Antes
var (isCancelled, value) = await myUniTask.SuppressCancellationThrow();

// Depois
var result = await myValkarnTask.AsResult();
if (result.IsCanceled) { ... }
T value = result.Value;
```

---

### MIG004 — Tipo de retorno Awaitable detectado

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Info |
| Categoria | ValkarnTaskMigration |

Um método retorna o tipo `Awaitable` do Unity. Considere substituí-lo por `ValkarnTask`, que se integra nativamente com o runtime do Valkarn Tasks e suporta cancelamento automático, pooling e o conjunto completo de combinadores.

---

### MIG005 — Canal SingleConsumerUnbounded detectado

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Aviso |
| Categoria | ValkarnTaskMigration |

`Channel.CreateSingleConsumerUnbounded<T>()` do UniTask cria um canal sem backpressure. Considere substituí-lo por `ValkarnTask.Channel.CreateBounded<T>(capacity)` para adicionar backpressure e evitar crescimento ilimitado de memória sob carga.

```csharp
// Antes
var ch = Channel.CreateSingleConsumerUnbounded<Event>();

// Depois (com backpressure)
var ch = ValkarnTask.Channel.CreateBounded<Event>(capacity: 256);

// Ou se ilimitado é intencional
var ch = ValkarnTask.Channel.CreateUnbounded<Event>();
```

---

### MIG006 — PlayerLoopTiming do UniTask detectado

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Info |
| Categoria | ValkarnTaskMigration |

Um valor de enum `PlayerLoopTiming` do namespace `Cysharp.Threading.Tasks` foi detectado. Altere a diretiva `using` para `UnaPartidaMas.Valkarn.Tasks`; os nomes de valor do enum são idênticos.

```csharp
// Antes
using Cysharp.Threading.Tasks;
timing = PlayerLoopTiming.Update;

// Depois
using UnaPartidaMas.Valkarn.Tasks;
timing = PlayerLoopTiming.Update;  // mesmo nome, namespace diferente
```

---

### MIG007 — async UniTaskVoid detectado

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Aviso |
| Categoria | ValkarnTaskMigration |

`async UniTaskVoid` era o padrão de método fire-and-forget do UniTask. O Valkarn Tasks substitui com duas opções:

```csharp
// Antes
async UniTaskVoid StartLoadAsync() { ... }

// Depois — opção 1: atributo [FireAndForget]
[FireAndForget]
async ValkarnTask StartLoadAsync() { ... }

// Depois — opção 2: ValkarnTask padrão + .Forget() no local de chamada
async ValkarnTask StartLoadAsync() { ... }
// Chamado como:
StartLoadAsync().Forget();
```

---

### MIG008 — Awaitable MainThreadAsync() detectado

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Info |
| Categoria | ValkarnTaskMigration |

`Awaitable.MainThreadAsync()` era usado na API `Awaitable` do Unity para voltar para a thread principal. O Valkarn Tasks executa continuações na thread principal por padrão via integração com o PlayerLoop, portanto chamadas explícitas a `MainThreadAsync()` geralmente são desnecessárias e podem ser removidas.

---

### MIG009 — UniTask.RunOnThreadPool detectado

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Aviso |
| Categoria | ValkarnTaskMigration |

Substitua por `ValkarnTask.RunOnThreadPool`. A API é a mesma.

```csharp
// Antes
await UniTask.RunOnThreadPool(() => HeavyWork());

// Depois
await ValkarnTask.RunOnThreadPool(() => HeavyWork());
```

---

### MIG010 — .ToCoroutine() detectado

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Aviso |
| Categoria | ValkarnTaskMigration |

`.ToCoroutine()` era uma bridge UniTask para chamadores de coroutine legados. Reescreva o código consumidor como um método `async ValkarnTask` em vez disso.

```csharp
// Antes
IEnumerator LegacyCaller() { yield return MyUniTask().ToCoroutine(); }

// Depois
async ValkarnTask ModernCaller() { await MyValkarnTask(); }
```

---

### MIG011 — UniTask.Create() detectado

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Aviso |
| Categoria | ValkarnTaskMigration |

`UniTask.Create(Func<UniTask>)` envolve um delegate factory. Substitua pelo padrão `ValkarnTask.Promise<T>` para conclusão controlada manualmente.

```csharp
// Antes
var task = UniTask.Create(async () => { await DoWork(); return 42; });

// Depois
var promise = new ValkarnTaskCompletionSource<int>();
DoWorkThenComplete(promise);
var task = promise.Task;
```

---

### MIG012 — UniTask.Lazy/Defer detectado

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Info |
| Categoria | ValkarnTaskMigration |

`UniTask.Lazy<T>` e `UniTask.Defer` existiam para evitar alocação quando uma task poderia completar de forma síncrona. O Valkarn Tasks tem um caminho rápido síncrono de zero alocação integrado: retornar `ValkarnTask.CompletedTask` ou `ValkarnTask.FromResult(value)` nunca aloca. Remova os wrappers `Lazy`/`Defer`.

---

### MIG013 — .ToUniTask()/.AsUniTask() detectado

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Info |
| Categoria | ValkarnTaskMigration |

Chamadas de conversão do `Awaitable` do Unity para UniTask. Remova-as; o Valkarn Tasks faz bridge com `Awaitable` nativamente (veja TT015).

---

### MIG014 — UniTaskAsyncEnumerable detectado

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Aviso |
| Categoria | ValkarnTaskMigration |

O UniTask incluía seu próprio `IUniTaskAsyncEnumerable<T>` e utilitários `UniTaskAsyncEnumerable`. Use `IAsyncEnumerable<T>` da BCL com `System.Linq.Async` em vez disso. `IAsyncEnumerable<T>` é suportado nativamente pelo C# `await foreach`.

```csharp
// Antes
IUniTaskAsyncEnumerable<int> GetItems() { ... }

// Depois
IAsyncEnumerable<int> GetItems() { ... }
```

---

### MIG015 — TimeoutController detectado

| Propriedade | Valor |
|------------|------------------------|
| Severidade | Info |
| Categoria | ValkarnTaskMigration |

O `TimeoutController` do UniTask era um helper para timeouts reutilizáveis. Substitua por um `CancellationTokenSource` padrão construído com um `TimeSpan`, que a BCL suporta diretamente.

```csharp
// Antes
var controller = new TimeoutController();
var ct = controller.Timeout(TimeSpan.FromSeconds(5));

// Depois
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var ct = cts.Token;
```

---

## Suprimindo Regras

Os mecanismos de supressão padrão do Roslyn funcionam para todas as regras:

```csharp
// Suprimir uma única ocorrência inline
#pragma warning disable TT012
while (true) { DoSomething(); }
#pragma warning restore TT012
```

Ou via `.editorconfig` para suprimir em todo o projeto:
```ini
[*.cs]
dotnet_diagnostic.TT012.severity = none
```

As regras de migração (`MIG*`) podem ser suprimidas da mesma forma, ou desabilitadas globalmente uma vez que a migração esteja completa definindo a severidade como `none` em `.editorconfig`.
