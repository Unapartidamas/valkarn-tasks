---
sidebar_position: 2
title: VlkTask<T>
---

# `VlkTask<T>`

`VlkTask<T>` é o tipo de task assíncrona com retorno de valor no Valkarn Tasks. É uma `readonly struct`, carregando um resultado inline (quando concluída de forma síncrona) ou uma referência a um objeto source em pool (quando concluída de forma assíncrona).

**Namespace:** `UnaPartidaMas.Valkarn.Tasks`

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncVlkTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct VlkTask<T>
```

Não há restrições genéricas em `T`. Qualquer tipo — tipo de valor, tipo de referência, struct ou class — é válido.

---

## Criando instâncias

### Tasks concluídas de forma síncrona

Esses métodos factory retornam um `VlkTask<T>` sem objeto source de apoio. Zero alocação.

#### `VlkTask.FromResult<T>(T value)`

Retorna um `VlkTask<T>` concluído carregando `value` inline. Declarado como método estático no tipo `VlkTask` não-genérico.

```csharp
public static VlkTask<T> FromResult<T>(T value)
```

```csharp
VlkTask<int> task = VlkTask.FromResult(42);
VlkTask<string> name = VlkTask.FromResult("Valkarn");
VlkTask<Vector3> pos = VlkTask.FromResult(transform.position);
```

A struct retornada tem `source == null`. Aguardá-la não incorre em alocação de continuação — o compilador vê `IsCompleted == true` imediatamente.

#### `VlkTask.FromException<T>(Exception exception)`

Retorna um `VlkTask<T>` com falha. Aguardá-la relança a exceção com seu rastreamento de pilha original preservado via `ExceptionDispatchInfo`.

```csharp
public static VlkTask<T> FromException<T>(Exception exception)
```

```csharp
VlkTask<Texture2D> LoadTexture(string path)
{
    if (string.IsNullOrEmpty(path))
        return VlkTask.FromException<Texture2D>(
            new ArgumentException("O caminho não deve estar vazio.", nameof(path)));

    return LoadTextureAsync(path);
}
```

#### `VlkTask.FromCanceled<T>(CancellationToken ct = default)`

Retorna um `VlkTask<T>` cancelado. Aguardá-lo lança `OperationCanceledException`.

```csharp
public static VlkTask<T> FromCanceled<T>(CancellationToken ct = default)
```

```csharp
VlkTask<byte[]> Download(string url, CancellationToken ct)
{
    if (ct.IsCancellationRequested)
        return VlkTask.FromCanceled<byte[]>(ct);

    return DownloadAsync(url, ct);
}
```

### Via métodos `async`

Qualquer método `async` declarado para retornar `VlkTask<T>` usa `AsyncVlkTaskMethodBuilder<TResult>` automaticamente:

```csharp
async VlkTask<int> ComputeAsync()
{
    await VlkTask.Yield();
    return 42;
}
```

O compilador gera uma máquina de estados. Se o método for concluído de forma síncrona (nunca suspende), `AsyncVlkTaskMethodBuilder<T>.Task` retorna `new VlkTask<T>(result)` com `source == null` — zero alocação.

---

## Executando trabalho no thread pool

Esses métodos executam um delegate no thread pool do .NET e retornam o resultado na thread principal (no `PlayerLoopTiming` especificado). São wrappers de conveniência sobre as variantes com nome mais longo `RunOnThreadPool`.

#### `VlkTask.Run<T>(Func<T> func, PlayerLoopTiming timing, CancellationToken ct)`

Executa um `Func<T>` síncrono no thread pool.

```csharp
public static VlkTask<T> Run<T>(
    Func<T> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
// Computar no thread pool, resultado chega de volta na thread principal no próximo Update
int hash = await VlkTask.Run(() => ComputeExpensiveHash(data));
```

#### `VlkTask.Run<T>(Func<VlkTask<T>> func, PlayerLoopTiming timing, CancellationToken ct)`

Executa um `Func<VlkTask<T>>` assíncrono no thread pool. Use quando o trabalho em si é assíncrono (ex.: I/O de arquivo).

```csharp
public static VlkTask<T> Run<T>(
    Func<VlkTask<T>> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
string json = await VlkTask.Run(async () =>
{
    using var reader = File.OpenText("data.json");
    return await reader.ReadToEndAsync();
});
```

Ambas as sobrecargas `Run` cancelam antecipadamente se o token já foi cancelado, e mudam de volta para a thread principal no `timing` após o trabalho ser concluído.

---

## Membros de instância

### `IsCompleted`

```csharp
public bool IsCompleted { get; }
```

Retorna `true` se a task foi concluída em qualquer estado terminal (Succeeded, Faulted ou Canceled). Para tasks concluídas de forma síncrona (`source == null`), sempre retorna `true` sem despacho de interface.

```csharp
var task = SomeLongOperation();
if (task.IsCompleted)
{
    int result = task.GetAwaiter().GetResult();
    Use(result);
}
```

### `GetStatus()`

```csharp
public VlkTask.Status GetStatus()
```

Retorna o `VlkTask.Status` atual. Valores possíveis: `Pending`, `Succeeded`, `Faulted`, `Canceled`. Para `source == null`, sempre retorna `Succeeded`.

### `GetAwaiter()`

```csharp
public Awaiter GetAwaiter()
```

Retorna uma struct `Awaiter`. Usado pelo compilador para implementar `await`. Você também pode chamá-lo diretamente para obter o resultado de forma síncrona (seguro apenas quando `IsCompleted` é true).

```csharp
VlkTask<int> task = VlkTask.FromResult(10);
int value = task.GetAwaiter().GetResult(); // seguro — concluído de forma síncrona
```

Chamar `GetResult()` em uma task pendente lança `InvalidOperationException`.

### `AsNonGeneric()`

```csharp
public VlkTask AsNonGeneric()
```

Converte este `VlkTask<T>` em um `VlkTask` não-genérico, descartando o tipo de resultado. A task resultante compartilha a mesma source subjacente e token, portanto completa ao mesmo tempo.

```csharp
VlkTask<int> typedTask = ComputeAsync();
VlkTask voidTask = typedTask.AsNonGeneric();
await voidTask;   // aguarda conclusão, ignora resultado
```

Isso é útil ao passar tasks de tipos mistos para combinadores ou quando você se importa apenas com o timing da conclusão, não com o valor.

---

## Combinadores retornando `VlkTask<T>`

### `WhenAll` — sobrecarga tipada de duas tasks

```csharp
public static VlkTask<(T1, T2)> WhenAll<T1, T2>(
    VlkTask<T1> task1, VlkTask<T2> task2)
```

Executa ambas as tasks concorrentemente e retorna uma tupla de resultados. Se qualquer task falhar ou for cancelada, a primeira exceção vence e o erro da outra é relatado por `VlkTask.UnobservedException`.

**Desestruturação de tupla** funciona naturalmente com a deconstrução do C#:

```csharp
var (profile, inventory) = await VlkTask.WhenAll(
    FetchProfileAsync(userId),
    FetchInventoryAsync(userId)
);
```

**Caminho rápido de zero alocação:** se ambas as tasks estiverem sincronamente concluídas no local de chamada, nenhum objeto em pool é criado e a tupla de resultado é retornada inline.

### `WhenAll` — sobrecarga tipada de coleção

```csharp
public static VlkTask<T[]> WhenAll<T>(IEnumerable<VlkTask<T>> tasks)
```

Aguarda todas as tasks na coleção concorrentemente e retorna um `T[]` em ordem de índice.

```csharp
var urls = new[] { "https://a.com", "https://b.com", "https://c.com" };
VlkTask<string>[] downloads = urls.Select(u => DownloadAsync(u)).ToArray();
string[] results = await VlkTask.WhenAll(downloads);
```

Se a coleção estiver vazia, retorna `VlkTask.FromResult(Array.Empty<T>())` — zero alocação. Se todas as tasks já estiverem sincronamente concluídas, um array de resultado é construído inline sem criar uma promise de combinador.

### `WhenAny` — sobrecarga tipada de duas tasks

```csharp
public static VlkTask<(int winnerIndex, T result)> WhenAny<T>(
    VlkTask<T> task1, VlkTask<T> task2)
```

Retorna assim que qualquer task for concluída. A tupla de resultado contém o índice baseado em 0 do vencedor e seu valor. As tasks perdedoras continuam executando; seus erros (se houver) são relatados por `VlkTask.UnobservedException`. Cancelamentos de perdedores não são relatados intencionalmente.

```csharp
var (winnerIndex, result) = await VlkTask.WhenAny(
    FetchFromCacheAsync(key),
    FetchFromNetworkAsync(key)
);

if (winnerIndex == 0)
    Debug.Log("Cache venceu");
```

### `WhenAny` — sobrecarga tipada de coleção

```csharp
public static VlkTask<(int winnerIndex, T result)> WhenAny<T>(
    IEnumerable<VlkTask<T>> tasks)
```

Mesma semântica da sobrecarga de duas tasks, estendida para qualquer número de tasks. Requer pelo menos uma task; lança `ArgumentException` para coleções vazias.

```csharp
var tasks = servers.Select(s => s.FetchAsync(query)).ToArray();
var (winnerIndex, data) = await VlkTask.WhenAny(tasks);
Debug.Log($"Servidor {winnerIndex} respondeu primeiro");
```

---

## Métodos factory de conveniência

### `VlkTask.Create<T>(Func<VlkTask<T>> factory)`

Invoca o delegate factory e aguarda a task resultante. Útil quando você quer adiar a construção de uma operação assíncrona.

```csharp
public static async VlkTask<T> Create<T>(Func<VlkTask<T>> factory)
```

```csharp
var result = await VlkTask.Create(() => LoadLevelDataAsync(levelId));
```

---

## Struct `Awaiter` (aninhada)

`VlkTask<T>.Awaiter` é o awaiter voltado ao compilador. É uma `readonly struct` implementando `ICriticalNotifyCompletion`. Normalmente você não interage com ela diretamente.

```csharp
public readonly struct Awaiter : ICriticalNotifyCompletion
{
    public bool IsCompleted { get; }
    public T GetResult();
    public void OnCompleted(Action continuation);
    public void UnsafeOnCompleted(Action continuation);
}
```

`UnsafeOnCompleted` é o caminho usado por `AsyncVlkTaskMethodBuilder<T>`. O rótulo "unsafe" significa que `ExecutionContext` não é capturado — isso é intencional para Unity onde nenhum `SynchronizationContext` está em vigor.

Quando `IsCompleted` é true, chamar `GetResult()` lê o campo `result` inline (para tasks síncronas) ou chama pela interface source (para tasks assíncronas). Ambos os caminhos são `[MethodImpl(MethodImplOptions.AggressiveInlining)]`.

---

## Métodos de extensão em `VlkTask<T>`

### `AsResult<T>()`

```csharp
public static VlkTask<Result<T>> AsResult<T>(this VlkTask<T> task)
```

Envolve um `VlkTask<T>` em um `VlkTask<Result<T>>`, capturando qualquer exceção ou cancelamento e codificando-o no valor `Result<T>`. Isso evita try/catch no local de chamada.

```csharp
Result<string> result = await FetchDataAsync(url).AsResult();

if (result.IsSuccess)
    Process(result.Value);
else if (result.IsCanceled)
    Debug.Log("Cancelado");
else
    Debug.LogError(result.Exception);
```

**Caminho rápido síncrono:** se a task source já estiver sincronamente concluída, `AsResult` retorna de forma síncrona sem nenhuma maquinaria async envolvida.

---

## `Promise<T>` — source de conclusão manual

`VlkTask.Promise<T>` é uma source de conclusão manual alocada no heap para casos em que você precisa controlar quando um `VlkTask<T>` é concluído e o tempo de vida não é limitado por uma única operação assíncrona.

```csharp
public class Promise<T>
{
    public VlkTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

```csharp
// Envolvendo uma API baseada em callback
var promise = new VlkTask.Promise<string>();

SomeCallbackApi.OnComplete += value => promise.TrySetResult(value);
SomeCallbackApi.OnError   += ex    => promise.TrySetException(ex);

string result = await promise.Task;
```

Diferente de `PooledPromise<T>`, `Promise<T>` não está em pool. Usa um finalizador para detectar e relatar exceções não observadas se a task falhar e o chamador nunca a aguardar.

Para padrões de alta frequência (loops produtor/consumidor, operações por frame), prefira `VlkTask.PooledPromise<T>`, que retorna automaticamente ao pool após `GetResult` ser chamado.

---

## `PooledPromise<T>` — source de conclusão manual em pool

```csharp
public sealed class PooledPromise<T> : VlkTask.ISource<T>, IPoolNode<PooledPromise<T>>
{
    public static PooledPromise<T> Create(out uint token);
    public static PooledPromise<T> CreateCompleted(T result, out uint token);
    public VlkTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

Após `GetResult` ser chamado na task de apoio, a promise redefine seu `VlkTaskCompletionCore<T>` e retorna a si mesma ao pool. Uma proteção contra double-return garante que isso aconteça no máximo uma vez mesmo se `GetResult` for chamado concorrentemente.

```csharp
// Padrão: produzir um VlkTask<T> que completa quando estiver pronto
var promise = VlkTask.PooledPromise<int>.Create(out uint token);
VlkTask<int> task = promise.Task;

// Despachar trabalho de forma assíncrona
ThreadPool.QueueUserWorkItem(_ =>
{
    int result = DoWork();
    promise.TrySetResult(result);
});

// Consumidor aguarda; na conclusão a promise retorna ao pool automaticamente
int value = await task;
```

---

## Resumo das formas de obter um `VlkTask<T>`

| Método | Quando usar |
|--------|------------|
| `return value` dentro de `async VlkTask<T>` | Métodos async normais |
| `VlkTask.FromResult(value)` | Retornos rápidos síncronos |
| `VlkTask.FromException<T>(ex)` | Tasks pré-falhadas |
| `VlkTask.FromCanceled<T>(ct)` | Tasks pré-canceladas |
| `VlkTask.Run<T>(Func<T>, ...)` | Offloading para thread pool |
| `VlkTask.Run<T>(Func<VlkTask<T>>, ...)` | Trabalho assíncrono no thread pool |
| `VlkTask.WhenAll<T1,T2>(t1, t2)` | Aguardar duas tasks tipadas, obter tupla |
| `VlkTask.WhenAll<T>(IEnumerable<...>)` | Aguardar N tasks tipadas, obter array |
| `VlkTask.WhenAny<T>(t1, t2)` | Primeira de duas tasks tipadas |
| `VlkTask.WhenAny<T>(IEnumerable<...>)` | Primeira de N tasks tipadas |
| `task.AsResult<T>()` | Wrapper seguro contra exceções |
| `new VlkTask.Promise<T>()` → `.Task` | Conclusão manual de longa duração |
| `VlkTask.PooledPromise<T>.Create(...)` → `.Task` | Conclusão manual de alta frequência |
