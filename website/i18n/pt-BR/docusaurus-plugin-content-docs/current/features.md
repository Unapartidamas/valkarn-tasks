---
sidebar_position: 4
title: Funcionalidades
---

# Funcionalidades

Referência completa de funcionalidades para Valkarn Tasks.

---

## Primitivo async principal

### Struct ValkarnTask

Um tipo de retorno async baseado em struct, sem alocações, que substitui tanto `UniTask` quanto o `Awaitable` do Unity.

```csharp
async ValkarnTask LoadLevel() { ... }
async ValkarnTask<int> CountEnemies() { ... }
```

- **Caminho rápido síncrono sem alocação** — se o método completar sem nunca suspender, zero alocações de heap ocorrem.
- **Caminho async com pool** — se o método suspender, o runner da máquina de estados é retirado de um pool limitado e redimensionável. Zero boxing, pools limitados com trim automático.
- **Pooling IL2CPP-first** — operações de pool na thread principal usam zero atômicos. O IL2CPP penaliza `Volatile` em 9,2× e `Interlocked` em 2,9× vs Mono; Valkarn evita ambos no caminho quente.
- **Segurança de token geracional** — um contador de geração `uint` por slot de pool. 4.294.967.296 ciclos por slot antes de colisão — impossível na prática. (UniTask usa um token `short`: colisão após ~18 minutos de trabalho async ativo.)

### Result\<T\> — tratamento de erros sem exceções

```csharp
var result = await loadTask.AsResult();

if (result.Succeeded)    Use(result.Value);
else if (result.IsCanceled) ShowRetry();
else                         Debug.LogError(result.Error);
```

`Result<T>` e `Result` são valores `readonly struct` que representam o resultado de uma tarefa sem lançar exceções. Ambos suportam conversão implícita para `bool` (verdadeiro quando bem-sucedido).

### Fontes de conclusão manual

```csharp
var promise = new ValkarnTask.Promise<string>();
promise.TrySetResult("done");
string value = await promise.Task;
```

Suporta `TrySetResult`, `TrySetException` e `TrySetCanceled`. O relatório de exceções não observadas baseado em finalizer garante que os erros nunca sejam perdidos silenciosamente.

### Fontes de conclusão com pool

Variantes com pool de auto-reset para padrões repetitivos (usadas internamente por canais e combinadores):

```csharp
var source = ValkarnTask.PooledPromise<int>.Create(out uint token);
source.TrySetResult(42);
int value = await source.Task; // source retorna ao pool automaticamente
```

---

## Bridge para Awaitable

Interoperabilidade transparente com o `Awaitable` do Unity — sem conversão manual:

```csharp
async ValkarnTask LoadGame()
{
    await SceneManager.LoadSceneAsync("Level2"); // Unity Awaitable — funciona diretamente
    await Resources.LoadAsync<Texture2D>("hero"); // Unity Awaitable — funciona diretamente
    await ValkarnTask.Delay(1000);                // nativo do Valkarn
}
```

O gerador de código-fonte detecta awaits de `Awaitable` e gera o adaptador automaticamente.

---

## Cancelamento de ciclo de vida

### Automático (gerado por código-fonte)

Marque a classe como `partial` — o gerador de código-fonte faz o resto:

```csharp
public partial class EnemySpawner : MonoBehaviour
{
    async ValkarnTask Start()
    {
        while (true)
        {
            await ValkarnTask.Delay(3000);
            SpawnWave();
            // Cancelado automaticamente quando este GameObject é destruído.
        }
    }
}
```

- **MonoBehaviour** — vinculado ao `destroyCancellationToken`
- **ScriptableObject** — vinculado ao tempo de vida da aplicação
- **Classe simples** — sem vinculação automática (token manual necessário)

### Desativar

```csharp
[NoAutoCancel]
async ValkarnTask BackgroundSync()
{
    await SyncToServer(); // não é auto-cancelado na destruição
}
```

### Override manual de CancellationToken

Passar um `CancellationToken` explícito substitui o token de ciclo de vida injetado automaticamente:

```csharp
async ValkarnTask DoWork(CancellationToken ct)
{
    await ValkarnTask.Delay(1000, ct);
}
```

### Sem cancelamento de siblings

Tarefas nunca cancelam tarefas irmãs. `WhenAll` aguarda TODAS as tarefas; `WhenAny` retorna o primeiro resultado, mas as tarefas perdedoras continuam executando. Isso previne corrupção de dados quando as tarefas têm efeitos colaterais.

---

## Seções críticas

Para operações que não devem ser interrompidas pelo cancelamento de ciclo de vida:

```csharp
async ValkarnTask SaveProgress()
{
    var data = await LoadPlayerData();        // cancelável

    await using (ValkarnTask.Critical())
    {
        await db.Insert(data);               // NÃO cancelado mesmo se o GO for destruído
        await db.Commit();
    }                                        // cancelamento pendente é aplicado aqui

    await SendNotification();                // cancelável novamente
}
```

Dentro de uma seção crítica, o cancelamento é **adiado** — não ignorado. Quando a seção termina, o cancelamento pendente é aplicado.

---

## Combinadores

### WhenAll (tipado)

```csharp
// Direto — lança exceção se alguma tarefa falhar
var (enemies, map) = await ValkarnTask.WhenAll(LoadEnemies(), LoadMap());

// Seguro — encapsule com AsResult() para comportamento sem lançamento
var (a, b) = await ValkarnTask.WhenAll(
    LoadEnemies().AsResult(), LoadMap().AsResult());
```

Caminho rápido síncrono sem alocação quando todas as tarefas já estão concluídas. A sobrecarga `IEnumerable<ValkarnTask<T>>` usa `ArrayPool<T>` para arrays internos.

### WhenAll (void)

```csharp
await ValkarnTask.WhenAll(SaveA(), SaveB(), SaveC());
await ValkarnTask.WhenAll(taskList); // IEnumerable<ValkarnTask>
```

### WhenAny

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(DownloadFromA(), DownloadFromB());
```

Retorna o primeiro resultado concluído. As tarefas perdedoras continuam executando naturalmente.

### Fire-and-forget

```csharp
SendAnalytics("event").Forget();

[FireAndForget]
async ValkarnTask SendAnalytics(string eventName) { ... }
// Chamadores não precisam de .Forget() — nenhum aviso gerado
```

`.Forget()` encaminha erros para `ValkarnTask.UnobservedException`. Nunca engolido silenciosamente.

### AsNonGeneric

```csharp
ValkarnTask voidTask = typedTask.AsNonGeneric();
```

---

## Tempo e delay

```csharp
await ValkarnTask.Delay(1000);                                    // milissegundos
await ValkarnTask.Delay(TimeSpan.FromSeconds(2));                  // TimeSpan
await ValkarnTask.Delay(1000, DelayType.UnscaledDeltaTime);       // ignorar timescale
await ValkarnTask.Delay(1000, DelayType.Realtime);                 // baseado em Stopwatch

await ValkarnTask.Yield();                                         // próximo tick do PlayerLoop
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);             // temporização específica
await ValkarnTask.NextFrame();                                      // garantidamente no próximo frame
await ValkarnTask.DelayFrame(5);                                    // N frames

await ValkarnTask.WaitUntil(() => player.IsReady);
await ValkarnTask.WaitWhile(() => isLoading);
```

---

## Troca de thread

```csharp
async ValkarnTask ProcessData()
{
    var raw = await DownloadData();

    await ValkarnTask.SwitchToThreadPool();
    var processed = HeavyComputation(raw);  // thread de fundo

    await ValkarnTask.SwitchToMainThread();
    ApplyToGameObject(processed);           // thread principal
}
```

---

## 16 timings do PlayerLoop

| Grupo | Timings |
|-------|---------|
| Initialization | `Initialization`, `LastInitialization` |
| EarlyUpdate | `EarlyUpdate`, `LastEarlyUpdate` |
| FixedUpdate | `FixedUpdate`, `LastFixedUpdate` |
| PreUpdate | `PreUpdate`, `LastPreUpdate` |
| Update | `Update`, `LastUpdate` |
| PreLateUpdate | `PreLateUpdate`, `LastPreLateUpdate` |
| PostLateUpdate | `PostLateUpdate`, `LastPostLateUpdate` |
| TimeUpdate | `TimeUpdate`, `LastTimeUpdate` |

Todas as operações usam `PlayerLoopTiming.Update` por padrão, salvo indicação em contrário.

---

## Canais

```csharp
// Ilimitado
var channel = ValkarnTask.Channel.CreateUnbounded<EnemySpawnRequest>();

// Limitado — backpressure quando cheio
var channel = ValkarnTask.Channel.CreateBounded<LogEntry>(capacity: 100);

// Multi-consumidor
var channel = ValkarnTask.Channel.CreateUnbounded<WorkItem>(multiConsumer: true);

// Produtor
await channel.Writer.WriteAsync(entry);
bool accepted = channel.Writer.TryWrite(entry);

// Consumidor
await foreach (var item in channel.Reader.ReadAllAsync())
    Process(item);

// Conclusão
channel.Writer.Complete();
await channel.Reader.Completion;
```

---

## Testes determinísticos (TestClock)

```csharp
[Test]
public void Respawn_WaitsThreeSeconds()
{
    var clock = new TestClock();
    var task = spawner.RespawnEnemy();

    clock.Advance(TimeSpan.FromSeconds(2));
    Assert.IsFalse(task.IsCompleted);

    clock.Advance(TimeSpan.FromSeconds(1));
    Assert.IsTrue(task.IsCompleted);
}
```

Todas as operações dependentes de tempo leem de `TimeProvider.Current`. Em testes, substitua pelo `TestClock`. `AdvanceFrame()` simula um único tick do player loop.

---

## Bridge para Job System

```csharp
var job = new PathfindingJob { start = a, end = b, results = results };
await job.ScheduleAsync();
// NativeArray de results é legível imediatamente após o await

await job.ScheduleParallelAsync(dataCount, batchSize); // IJobParallelFor

// Cancelamento — completa o job handle antes de reportar o cancelamento (sem vazamento de job)
await job.ScheduleAsync(cancellationToken);
```

---

## Diagnósticos em tempo de compilação

| Código | Severidade | Descrição |
|--------|------------|-----------|
| TT001 | Aviso | Double-await / use-after-free em um `ValkarnTask` |
| TT002 | Erro | Resultado de `async ValkarnTask` usado como declaração de expressão — deve ser aguardado ou `.Forget()` |
| TT010 | Info | Método async em MonoBehaviour auto-cancelado no Destroy |
| TT011 | Aviso | `WhenAll` contém tarefas com ciclos de vida diferentes |
| TT012 | Aviso | Loop async sem verificação de cancelamento (possível loop zumbi) |
| TT013 | Aviso | `ValkarnTask` retornado mas nunca aguardado e não explicitamente descartado |
| TT014 | Aviso | `[NoAutoCancel]` sem parâmetro manual `CancellationToken` |
| TT015 | Info | Aguardando `Awaitable` dentro de `ValkarnTask` — adaptador bridge gerado |
| TT016 | Aviso | Método async sem expressão await |
| TT017 | Aviso | `[FireAndForget]` em `ValkarnTask<T>` — descarta valor de retorno |

---

## Gerenciamento de pool

Todo runner de método async é colocado em pool via `ValkarnPool<T>`:

- **Thread principal** — stack sem lock, zero atômicos
- **Threads de fundo** — Treiber lock-free stack com operações CAS
- **Trim baseado em frame** — a cada 300 frames (~5s a 60fps), objetos em excesso são liberados gradualmente
- **Nunca reduz** abaixo do mínimo configurável (padrão: 8)

Monitore em tempo de execução:

```csharp
foreach (var (type, size, maxSize) in ValkarnTask.GetPoolInfo())
    Debug.Log($"{type}: {size}/{maxSize}");
```

---

## ValkarnTaskSettings

Configure via `ScriptableObject` (`Assets > Create > Valkarn > Tasks > Task Settings`, coloque em `Resources/`):

| Configuração | Padrão | Descrição |
|--------------|--------|-----------|
| `DefaultMaxPoolSize` | 256 | Máximo de itens por tipo de pool |
| `MinPoolSize` | 8 | Nunca reduzir abaixo deste valor |
| `TrimCheckInterval` | 300 | Frames entre verificações de trim |
| `TrimHysteresisCount` | 2 | Verificações consecutivas antes do trim |
| `TrimReleaseRatio` | 0,25 | Fração do excesso liberada por ciclo |
| `EnableAutoCancel` | true | Auto-vincular tarefas MonoBehaviour ao destroyCancellationToken |
| `LogUnobservedCancellations` | false | Registrar cancelamentos não observados como avisos |
| `MaxExceptionLogsPerFrame` | 10 | Limite de logs de exceção por frame |

---

## Tratamento de erros

```csharp
// Exceções não observadas — disparadas deterministicamente no retorno ao pool
ValkarnTask.UnobservedException += ex =>
    Debug.LogError($"Unobserved: {ex}");
```

- `WhenAll` — lança a primeira exceção, encaminha as adicionais para `UnobservedException`
- `WhenAny` — a exceção do vencedor é lançada; as falhas dos perdedores vão para `UnobservedException`
- Cancelamento de ciclo de vida — `OperationCanceledException` suprimida por padrão (configurável)

### Métodos de fábrica

```csharp
ValkarnTask.CompletedTask          // void, zero-alloc
ValkarnTask.FromResult<T>(value)   // tipado, zero-alloc
ValkarnTask.FromException(ex)      // com falha
ValkarnTask.FromException<T>(ex)
ValkarnTask.FromCanceled()         // cancelado
ValkarnTask.FromCanceled<T>()
ValkarnTask.Never                  // nunca completa (sentinela para WhenAny)
```
