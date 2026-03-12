---
sidebar_position: 3
title: Fontes de Conclusão
---

# VlkTaskCompletionSource

`VlkTaskCompletionSource<T>` e o `VlkTaskCompletionSource` não-genérico dão a você controle manual sobre um `VlkTask`. São o equivalente assíncrono de escrever o resultado você mesmo — você mantém um objeto source, distribui seu `.Task` para chamadores e então resolve, falha ou cancela a partir de um local de chamada separado.

Este é o equivalente do Valkarn Tasks de `TaskCompletionSource<T>` da BCL, mas respaldado por `VlkTask.Promise<T>` para manter o modelo de alocação consistente com o resto da biblioteca.

---

## VlkTaskCompletionSource&lt;T&gt;

```csharp
public class VlkTaskCompletionSource<T>
{
    public VlkTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled();
}
```

### Task

```csharp
public VlkTask<T> Task { get; }
```

A task que o código aguardante irá observar. Distribua-a para todos os chamadores que precisam esperar pelo resultado. Múltiplos chamadores podem fazer `await` na mesma task concorrentemente.

### TrySetResult

```csharp
public bool TrySetResult(T result);
```

Conclui a task com sucesso com o valor fornecido. Todas as continuações aguardantes são retomadas. Retorna `true` se a conclusão foi aceita; retorna `false` se a task já estava concluída (por qualquer chamada `TrySet*` anterior). Nunca lança exceção.

### TrySetException

```csharp
public bool TrySetException(Exception ex);
```

Causa falha na task com a exceção fornecida. O código aguardante recebe a exceção ao chamar `await`. Lança `ArgumentNullException` se `ex` é `null`. Retorna `true` se aceita, `false` se já concluída.

### TrySetCanceled

```csharp
public bool TrySetCanceled();
```

Cancela a task. O código aguardante recebe uma `OperationCanceledException`. Retorna `true` se aceita, `false` se já concluída.

---

## VlkTaskCompletionSource não-genérico

Não há uma classe `VlkTaskCompletionSource` não-genérica separada na API pública. Para tasks manuais de retorno void, use `VlkTask.Promise` diretamente:

```csharp
var promise = new VlkTask.Promise();
VlkTask task = promise.Task;

promise.TrySetResult();    // concluir
promise.TrySetException(ex);
promise.TrySetCanceled();
```

`VlkTask.Promise` expõe a mesma superfície `TrySet*` e a mesma proteção contra dupla conclusão, mas produz um `VlkTask` não-genérico em vez de um `VlkTask<T>`.

---

## Proteção contra Dupla Conclusão

Todos os métodos `TrySet*` são seguros para chamar de qualquer thread a qualquer momento, incluindo concorrentemente. A primeira chamada que vence um compare-and-swap na máquina de estados interna tem sucesso; toda chamada subsequente retorna `false` e não tem efeito. Isso significa:

- Chamar `TrySetResult` duas vezes não faz nada na segunda chamada.
- Chamar `TrySetResult` após `TrySetException` não faz nada.
- Duas threads competindo para concluir a mesma source simultaneamente é seguro — uma vence, a outra é silenciosamente ignorada.

Se você precisar saber qual chamador "venceu", verifique o valor de retorno. Se não se importar (sinal fire-and-forget), pode ignorá-lo com segurança.

```csharp
// Corrida segura — apenas um destes irá realmente concluir a task
_ = source.TrySetResult(value);
_ = source.TrySetCanceled();
```

---

## Padrões comuns

### Fazendo bridge de uma API de callback

Muitas APIs Unity e de plataforma entregam resultados por callbacks em vez de async/await. `VlkTaskCompletionSource<T>` permite envolvê-las de forma limpa.

```csharp
public VlkTask<Texture2D> LoadTextureAsync(string url)
{
    var tcs = new VlkTaskCompletionSource<Texture2D>();

    StartCoroutine(LoadCoroutine(url, tcs));

    return tcs.Task;
}

IEnumerator LoadCoroutine(string url, VlkTaskCompletionSource<Texture2D> tcs)
{
    var request = UnityWebRequestTexture.GetTexture(url);
    yield return request.SendWebRequest();

    if (request.result == UnityWebRequest.Result.Success)
        tcs.TrySetResult(DownloadHandlerTexture.GetContent(request));
    else
        tcs.TrySetException(new Exception(request.error));
}
```

Os chamadores agora podem simplesmente fazer `await` na task retornada:

```csharp
Texture2D tex = await LoadTextureAsync("https://example.com/image.png");
```

### Sinal único (portão async)

Use `VlkTask.Promise` quando precisar de um sinal que dispara uma vez e desbloqueia qualquer número de waiters. Isso é semelhante a `ManualResetEventSlim`, mas nativo ao async.

```csharp
public class AsyncGate
{
    readonly VlkTask.Promise _promise = new();

    // Qualquer número de chamadores pode aguardar isso
    public VlkTask WaitAsync() => _promise.Task;

    // Chamar uma vez para desbloquear todos
    public void Open() => _promise.TrySetResult();
}

// Uso
var gate = new AsyncGate();

// Vários sistemas aguardam o portão independentemente
async VlkTask SystemAAsync()
{
    await gate.WaitAsync();
    // prosseguir após o portão abrir
}

async VlkTask SystemBAsync()
{
    await gate.WaitAsync();
    // prossegue ao mesmo tempo que SystemA
}

// Em algum outro lugar — abrir o portão
gate.Open();
```

Uma vez que `TrySetResult()` é chamado, todas as chamadas `await` atuais e futuras na task são concluídas imediatamente (de forma síncrona se a task já estiver concluída quando elas são executadas).

### Envolvendo uma operação async de terceiros com suporte a cancelamento

```csharp
public VlkTask<Result> RunWithTimeoutAsync(
    Func<VlkTask<Result>> operation,
    float timeoutSeconds,
    CancellationToken ct)
{
    var tcs = new VlkTaskCompletionSource<Result>();

    RunCoreAsync(tcs, operation, timeoutSeconds, ct).Forget();

    return tcs.Task;
}

async VlkTask RunCoreAsync(
    VlkTaskCompletionSource<Result> tcs,
    Func<VlkTask<Result>> operation,
    float timeout,
    CancellationToken ct)
{
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
    linked.CancelAfter(TimeSpan.FromSeconds(timeout));

    try
    {
        var result = await operation();
        tcs.TrySetResult(result);
    }
    catch (OperationCanceledException)
    {
        tcs.TrySetCanceled();
    }
    catch (Exception ex)
    {
        tcs.TrySetException(ex);
    }
}
```

### Portão de inicialização adiada

Um padrão comum do Unity é expor uma task "pronta" que componentes podem aguardar independentemente de a inicialização ter começado ou não.

```csharp
public class ServiceBus : MonoBehaviour
{
    readonly VlkTask.Promise _readyPromise = new();

    // Qualquer pessoa pode aguardar isso em qualquer ponto — antes ou depois de Initialize()
    public VlkTask Ready => _readyPromise.Task;

    async void Start()
    {
        await LoadConfigAsync();
        await ConnectAsync();
        _readyPromise.TrySetResult();  // desbloqueia todos os waiters
    }
}

// Em qualquer outro componente
async VlkTask OnEnableAsync()
{
    await ServiceBus.Instance.Ready;  // aguarda se não estiver pronto, retorna instantaneamente se já estiver
    DoWork();
}
```

---

## Relação com VlkTask.Promise

`VlkTaskCompletionSource<T>` é um wrapper público fino em torno de `VlkTask.Promise<T>`. Ambos fornecem a mesma funcionalidade. A diferença é a convenção de nomenclatura:

| Tipo | Retorna | Uso comum |
|------|---------|------------|
| `VlkTaskCompletionSource<T>` | `VlkTask<T>` | API pública, espelha o estilo BCL `TaskCompletionSource<T>` |
| `VlkTask.Promise<T>` | `VlkTask<T>` | Uso interno, ligeiramente mais direto |
| `VlkTask.Promise` | `VlkTask` | Sinais void (portões, eventos) |

Todos os três respaldam sua task com `VlkTaskCompletionCore<T>`, a máquina de estados baseada em struct interna que lida com segurança de thread e a corrida de continuação/resultado usando um protocolo em duas fases baseado em CAS.
