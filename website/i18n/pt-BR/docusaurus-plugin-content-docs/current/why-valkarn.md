---
sidebar_position: 6
title: Por que Valkarn Tasks
---

# Por que Valkarn Tasks

> Async/await sem alocações para Unity. Mais rápido que UniTask. Mais inteligente que Awaitable.

---

## O problema: async no Unity está quebrado

Desenvolvedores Unity precisam de operações assíncronas em toda parte: carregamento de cenas, download de assets, espera por animações, atraso de spawns, comunicação com servidores. Hoje, as opções são:

| Opção | Problema |
|-------|----------|
| **Coroutines** | Sem valores de retorno, sem tratamento de erros, sem cancelamento, sem composição |
| **System.Threading.Tasks** | Aloca em cada chamada `async` (~144–232 bytes), aciona o GC, sem consciência do ciclo de vida do Unity |
| **UniTask** | Bom — mas: colisão de token após 18 minutos, sem diagnósticos em tempo de compilação, relatório de erros dependente de finalizer |
| **Unity Awaitable** (2023+) | Baseado em classe (aloca), sem combinadores, sem canais, sem suporte a testes |

Valkarn Tasks resolve todos esses problemas em um único pacote, gerado por código-fonte, sem alocações.

---

## Zero alocações — por que cada byte importa

### O que alocação significa para jogos

Cada `new object()`, `new List<T>()` ou chamada `async Task` aloca no **heap gerenciado**, rastreado pelo Garbage Collector. O Unity usa o Boehm GC, que tem dois problemas críticos:

1. **Pausas stop-the-world** — quando o GC é executado, seu jogo congela. Uma pausa de 2 ms a 60 fps consome 12% do seu orçamento de frame; a 120 fps (VR) consome 24%.
2. **Temporização imprevisível** — o GC pode ser acionado durante uma batalha de chefe, uma cena cinemática ou uma partida competitiva.

### O que Valkarn Tasks faz de diferente

| Cenário | `System.Task` | UniTask | **Valkarn Tasks** |
|---------|--------------|---------|------------------|
| `async Method()` — completa sincronamente | 144 bytes | **0 bytes** | **0 bytes** |
| `async Method()` — suspende uma vez | 232+ bytes | 0 bytes (pooled) | **0 bytes (pooled)** |
| `WhenAll(a, b)` | 232 bytes | 0 bytes (pooled) | **0 bytes (source-gen pooled)** |
| `WhenAny(a, b)` | 144 bytes | 72 bytes | **0 bytes** |
| Promise (conclusão manual) | 144 bytes | 104 bytes | **88 bytes** |
| Pooled Promise (reutilizável) | 144 bytes | 0 bytes | **0 bytes** |

Em um frame típico de jogo com 50–100 operações async, `System.Task` gera 7–23 KB de lixo. Valkarn Tasks gera **zero**.

### O que isso significa para seu jogo

- **Sem travamentos do GC** — framerate travado sem solavancos causados por operações async
- **Seguro para VR** — alvos de 90/120 fps sem picos do GC
- **Amigável para mobile** — menos pressão de memória em dispositivos com RAM limitada
- **Certificado para console** — comportamento de memória previsível ajuda a passar nos requisitos de certificação

---

## Desempenho — comparado com os melhores

Benchmarks: BenchmarkDotNet v0.14.0, .NET 9.0, Intel Core i7-10875H.

### Core async/await — 2× mais rápido que ValueTask

| Benchmark | ValueTask | UniTask | **Valkarn Tasks** | vs ValueTask |
|-----------|-----------|---------|------------------|--------------|
| 100 tarefas em lote | 956 ns | 508 ns | **489 ns** | **1,95×** |
| 1.000 tarefas em lote | 9.697 ns | 5.016 ns | **4.728 ns** | **2,05×** |
| Com CancellationToken | 38,8 ns | 36,2 ns | **29,6 ns** | **1,31×** |
| Tratamento de exceções | 10.399 ns | 8.247 ns | **9.248 ns** | **1,12×** |

Todos os caminhos: **0 bytes alocados**.

### Combinadores — até 9,6× mais rápido que Task

| Benchmark | Task | UniTask | **Valkarn Tasks** | vs Task |
|-----------|------|---------|------------------|---------|
| WhenAll (2 tarefas) | 117 ns / 232B | 13,6 ns / 0B | **12,1 ns / 0B** | **9,6×** |
| WhenAll (5 tarefas) | 156 ns / 272B | 25,1 ns / 0B | **25,3 ns / 0B** | **6,2×** |
| WhenAny (2 tarefas) | 39,0 ns / 144B | 60,0 ns / 72B | **11,6 ns / 0B** | **3,4×** |
| Pooled Promise | 59,5 ns / 144B | 53,6 ns / 0B | **38,3 ns / 0B** | **1,55×** |

WhenAny é **5,2× mais rápido que UniTask** e aloca zero bytes.

### Pool de objetos — 4,3 nanossegundos

| Operação | Tempo | Alocação |
|----------|-------|---------|
| Rent + return no slot rápido da thread principal | **4,3 ns** | 0 bytes |
| Treiber stack entre threads | ~15 ns | 0 bytes |

Zero operações atômicas na thread principal — crítico para IL2CPP onde `Volatile.Read` é 9,2× mais lento.

### Impacto no mundo real (50 ops async/frame a 60 fps)

| Biblioteca | Tempo/frame | Orçamento de frame | GC/segundo |
|------------|------------|-------------------|------------|
| System.Task | ~48 µs | 0,29% | ~430 KB/s |
| UniTask | ~25 µs | 0,15% | ~3,6 KB/s |
| **Valkarn Tasks** | **~24 µs** | **0,14%** | **0 KB/s** |

Em 10 minutos, `System.Task` gera **~258 MB** de lixo async. Valkarn Tasks gera **zero**.

---

## Funcionalidades que nenhuma outra biblioteca possui

### Cancelamento automático de ciclo de vida

```csharp
public partial class EnemySpawner : MonoBehaviour
{
    async ValkarnTask Start()
    {
        while (true)
        {
            await ValkarnTask.Delay(3000);
            SpawnWave();
        }
        // Cancelado automaticamente quando este GameObject é destruído.
        // Sem CancellationToken. Sem vazamentos de memória. Sem tarefas zumbi.
    }
}
```

Chega de `MissingReferenceException` de métodos async executando após a destruição. Sem limpeza manual em `OnDestroy`. Sem descarte esquecido de `CancellationTokenSource`.

### Seções críticas

```csharp
async ValkarnTask SaveProgress()
{
    var data = await LoadPlayerData();       // cancelável

    await using (ValkarnTask.Critical())
    {
        await db.Insert(data);              // completa mesmo se o GO for destruído
        await db.Commit();
    }                                       // cancelamento pendente é aplicado agora

    await SendNotification();               // cancelável novamente
}
```

Gravações no banco de dados, requisições de rede e salvamentos de arquivo são concluídos mesmo quando o jogador sai ou uma cena é descarregada. Sem salvamentos corrompidos. Sem analytics parcialmente gravadas. Sem recibos perdidos.

### Diagnósticos em tempo de compilação

| Diagnóstico | O que detecta |
|-------------|--------------|
| TT001 | Double-await em um ValkarnTask (bug de use-after-free) |
| TT002 | Esquecer de aguardar uma tarefa (falha silenciosa) |
| TT012 | Loops async sem verificação de cancelamento (loops zumbi) |
| TT013 | Tarefa retornada mas não aguardada (bug de fire-and-forget) |
| TT016 | Método async sem await (overhead desnecessário) |
| TT017 | `[FireAndForget]` em `ValkarnTask<T>` (descartando um resultado) |

Bugs capturados na IDE como sublinhados vermelhos — não como crashes em tempo de execução 20 minutos após o início dos testes.

### Result\<T\> — tratamento de erros sem try/catch

```csharp
var result = await loadTask.AsResult();

if (result.Succeeded)       Use(result.Value);
else if (result.IsCanceled) ShowRetryDialog();
else                         Debug.LogError(result.Error);
```

Cada caminho de erro é explícito. Sem exceções engolidas. Sem handlers ausentes.

### Canais com backpressure

```csharp
var channel = ValkarnTask.Channel.CreateBounded<EnemySpawnRequest>(capacity: 10);

// Produtor (lógica do jogo)
await channel.Writer.WriteAsync(new EnemySpawnRequest { type = EnemyType.Dragon });

// Consumidor (sistema de spawn)
await foreach (var request in channel.Reader.ReadAllAsync())
    await SpawnEnemy(request);
```

Desacople sistemas de forma limpa. Limite a taxa de spawn. Enfileire mensagens de rede. Armazene eventos de entrada em buffer. O produtor desacelera quando o consumidor não consegue acompanhar — prevenindo picos de memória.

### Testes determinísticos com TestClock

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

Teste lógica dependente de tempo instantaneamente. Sem `yield return new WaitForSeconds(3)` em testes. Sem temporização instável em CI.

### Segurança de token geracional

O UniTask usa um token `short` (16 bits). Após 65.536 ciclos de pool (~18 minutos de trabalho async ativo), uma referência obsoleta lê silenciosamente o resultado de outra tarefa — um bug de use-after-free virtualmente impossível de reproduzir.

Valkarn Tasks usa um contador de geração `uint` por slot de pool: **4.294.967.296 ciclos por slot** antes de colisão. Impossível em qualquer cenário realista.

---

## Migração em minutos, não semanas

### A partir do UniTask

```
Passo 1: Instale Valkarn Tasks
Passo 2: Lâmpadas amarelas aparecem nos usos de UniTask na IDE
Passo 3: Clique com o botão direito → "Fix all occurrences in Solution" (Ctrl+.)
Passo 4: Remova a referência ao pacote UniTask
```

**15 diagnósticos de migração** (MIG001–MIG015) cobrem toda a API do UniTask automaticamente. Um projeto típico com 500–2.000 métodos async migra em menos de 5 minutos. Mais de 95% totalmente automatizado.

### A partir do Unity Awaitable

A mesma migração com um clique:
- `async Awaitable` → `async ValkarnTask`
- `Awaitable.NextFrameAsync()` → `ValkarnTask.NextFrame()`
- `Awaitable.BackgroundThreadAsync()` → `ValkarnTask.SwitchToThreadPool()`
- `Awaitable.MainThreadAsync()` → removido (Valkarn executa na thread principal por padrão)

---

## Matriz de comparação

| Funcionalidade | System.Task | UniTask | Awaitable | **Valkarn Tasks** |
|----------------|------------|---------|-----------|------------------|
| Caminho síncrono sem alocação | Não | Sim | Não | **Sim** |
| Combinadores sem alocação | Não | Não | Não | **Sim (source gen)** |
| Baseado em struct | Não | Sim | Não | **Sim** |
| Auto-cancelamento de ciclo de vida | Não | Manual | Parcial | **Automático** |
| Sem cancelamento de siblings | Não | Não | Não | **Sim** |
| Seções críticas | Não | Não | Não | **Sim** |
| Result\<T\> (sem throw) | Não | Parcial | Não | **Sim** |
| TestClock | Não | Não | Não | **Sim** |
| Bridge para Job System | Não | Não | Não | **Sim** |
| Diagnósticos em tempo de compilação | Não | Não | Não | **Sim (17 regras)** |
| Pools limitados + trim | Não | Não | N/A | **Sim** |
| Relatório de erro determinístico | Não | Não (finalizer) | Parcial | **Sim (pool return)** |
| Canais completos | Sim (.NET) | Mínimo | Não | **Sim** |
| Bridge para Awaitable | N/A | Mínimo | Nativo | **Transparente** |
| Pooling otimizado para IL2CPP | Não | Não (Volatile em cada op) | N/A | **Sim (zero atômicos)** |
| Segurança contra colisão de token | N/A | 18 min (short) | N/A | **Nunca (uint gen)** |
| Auto-migração a partir do UniTask | N/A | N/A | N/A | **Sim (15 correções)** |
| Auto-migração a partir do Awaitable | N/A | N/A | N/A | **Sim (8 correções)** |

---

**Seu jogo merece async sem travamentos.**
