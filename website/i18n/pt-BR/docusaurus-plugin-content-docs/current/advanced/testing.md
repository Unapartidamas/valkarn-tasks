---
sidebar_position: 3
title: Testes
---

# Testes

O Valkarn Tasks inclui uma infraestrutura de testes dedicada que permite escrever testes unitários rápidos e determinísticos para código async — sem timers reais, sem esperas de frame, sem necessidade do Unity Editor para a suíte de testes principal.

---

## Visão geral

O suporte a testes reside na assembly `Testing/` (`UnaPartidaMas.Valkarn.Tasks.Testing`), visível ao runtime via `InternalsVisibleTo`. Ele expõe dois tipos públicos:

| Tipo | Finalidade |
|------|-----------|
| `ValkarnTaskTestHelper` | Inicializar e encerrar o runtime do Valkarn Tasks para uma sessão de testes |
| `TestClock` | Provedor de tempo e frames determinístico; substitui o `TimeProvider` do Unity durante os testes |

---

## ValkarnTaskTestHelper

`ValkarnTaskTestHelper` é uma classe utilitária estática no namespace `UnaPartidaMas.Valkarn.Tasks.Testing`.

### O que faz

`Setup()` realiza três coisas:
1. Cria um `TestClock` e o instala como `TimeProvider.Current`, substituindo qualquer provedor de tempo real do Unity.
2. Chama `PlayerLoopHelper.InitializeForTest()`, que aloca as arrays `ContinuationQueue` e `PlayerLoopRunner` para todos os 16 timings do PlayerLoop e marca o runtime como inicializado.
3. Retorna o `TestClock` para que seu teste possa controlar o tempo.

`Teardown()` reverte a configuração:
1. Instala um novo `TestClock` vazio como `TimeProvider.Current` para evitar que o tempo residual vaze entre fixtures de teste.
2. Chama `PlayerLoopHelper.ShutdownForTest()`, que desmonta as filas e runners.

### Padrão de uso

```csharp
using NUnit.Framework;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Testing;

[TestFixture]
public class MyAsyncTests
{
    TestClock clock;

    [SetUp]
    public void SetUp()
    {
        clock = ValkarnTaskTestHelper.Setup();
    }

    [TearDown]
    public void TearDown()
    {
        ValkarnTaskTestHelper.Teardown();
    }

    // Testes aqui
}
```

Chame `Setup` uma vez por teste em `[SetUp]` e `Teardown` uma vez por teste em `[TearDown]`. O padrão garante que cada teste comece com um estado de runtime limpo e não vaze para o próximo teste.

### Delta time padrão

`Setup` aceita um parâmetro opcional `defaultDeltaTime` (padrão: `1/60` segundos, ou seja, 60 fps):

```csharp
clock = ValkarnTaskTestHelper.Setup(defaultDeltaTime: 1f / 30f);  // simulação a 30 fps
```

Esse valor é usado por `TestClock.AdvanceFrame()` e `TestClock.AdvanceFrames(n)`.

---

## TestClock

`TestClock` implementa `ITimeProvider` e dá aos testes controle total sobre:

- Tempo simulado (via `GetTimestamp()`, respaldado por ticks `Stopwatch.Frequency`)
- `DeltaTime` e `UnscaledDeltaTime` para o frame atual
- `FrameCount`

### Avançando o tempo

#### `Advance(TimeSpan duration)`

Avança o tempo pela duração especificada em um único passo. A duração inteira é aplicada como `DeltaTime` para aquele frame, `FrameCount` incrementa em 1, e todos os timings do PlayerLoop são processados. Após o processamento, `DeltaTime` é restaurado ao valor que tinha antes da chamada.

```csharp
var task = ValkarnTask.Delay(3000);  // delay de 3 segundos

clock.Advance(TimeSpan.FromSeconds(2));
Assert.IsFalse(task.IsCompleted);   // ainda não

clock.Advance(TimeSpan.FromSeconds(1));
Assert.IsTrue(task.IsCompleted);    // exatamente em 3 segundos
```

Use isso quando quiser pular para um ponto específico no tempo sem simular frames individuais.

#### `AdvanceFrame()`

Avança um frame usando o `DeltaTime` atual. `FrameCount` incrementa em 1, todos os timings do PlayerLoop são processados, e um `Thread.Sleep` de 1 ms é inserido antes do processamento. O sleep existe porque threads de worker em background (usadas por `RunOnThreadPool` e integração com o Unity Job System) precisam de uma pequena janela para concluir entre ticks de frame — sem ele, os testes executam frames consecutivamente sem intervalo, causando condições de corrida que não ocorrem em frames reais do Unity.

```csharp
clock.AdvanceFrame();
```

#### `AdvanceFrames(int count)`

Chama `AdvanceFrame()` `count` vezes.

```csharp
clock.AdvanceFrames(10);  // simular 10 frames
```

#### `ProcessTick(PlayerLoopTiming timing)`

Processa uma única fase de timing do PlayerLoop sem avançar o tempo ou `FrameCount`. Útil ao testar código agendado em um timing específico (ex.: `PlayerLoopTiming.FixedUpdate`) e você não quer processar todos os outros timings.

```csharp
clock.ProcessTick(PlayerLoopTiming.FixedUpdate);
```

### Controlando o delta time

#### `SetDeltaTime(float deltaTime)`

Define `DeltaTime` e `UnscaledDeltaTime` para o mesmo valor para todos os frames subsequentes.

#### `SetDeltaTime(float deltaTime, float unscaledDeltaTime)`

Define-os independentemente para simular `Time.timeScale` diferente de 1. Por exemplo, para testar código que usa `DelayType.UnscaledDeltaTime` enquanto o tempo está pausado:

```csharp
clock.SetDeltaTime(deltaTime: 0f, unscaledDeltaTime: 0.05f);

var scaledTask = ValkarnTask.Delay(500, DelayType.DeltaTime);
var unscaledTask = ValkarnTask.Delay(500, DelayType.UnscaledDeltaTime);

clock.AdvanceFrames(9);  // 450 ms não escalado, 0 ms escalado
Assert.IsFalse(scaledTask.IsCompleted);    // tempo de jogo pausado nunca chega a 500ms
Assert.IsFalse(unscaledTask.IsCompleted); // 450ms < 500ms

clock.AdvanceFrame();  // 500 ms não escalado
Assert.IsTrue(unscaledTask.IsCompleted);  // delay não escalado concluído
Assert.IsFalse(scaledTask.IsCompleted);  // escalado ainda nunca se move
```

---

## Escrevendo testes unitários para métodos async ValkarnTask

### Testes que completam sincronamente

Algumas operações `ValkarnTask` completam sem suspender — por exemplo, aguardar `ValkarnTask.CompletedTask`, `ValkarnTask.FromResult(value)`, ou um `ValkarnTaskCompletionSource` que foi completado antes de ser aguardado. Esses não requerem um clock e podem usar a classe interna `TestHelper` diretamente (interna à assembly de testes):

```csharp
[TestFixture]
public class SyncTests
{
    [SetUp]
    public void SetUp()
    {
        // Define apenas o ID da thread principal — sem clock ou PlayerLoop necessário
        TestHelper.EnsureInitialized();
    }

    [Test]
    public void FromResult_ReturnsValue()
    {
        var task = ValkarnTask.FromResult(42);
        Assert.IsTrue(task.IsCompleted);
        Assert.AreEqual(42, task.GetAwaiter().GetResult());
    }
}
```

`TestHelper` (em `Tests/Editor/TestHelper.cs`) é um helper interno usado pela própria suíte de testes:

- `TestHelper.EnsureInitialized()` define `ValkarnTaskPoolShared.MainThreadId` para a thread atual. Isso é necessário para que as operações de pool sejam roteadas para o caminho rápido correto (não-atômico).
- `TestHelper.RunSync(ValkarnTask task)` afirma que a task já está concluída e chama `GetResult()` — útil para testar caminhos de código que devem completar sincronamente.
- `TestHelper.RunSync<T>(ValkarnTask<T> task)` faz o mesmo e retorna o valor do resultado.
- `TestHelper.UnobservedExceptionCollector` assina `ValkarnTask.UnobservedException` pelo tempo de sua existência, coletando quaisquer exceções não observadas para asserção.

### Testes que requerem tempo

Qualquer teste envolvendo `ValkarnTask.Delay`, `ValkarnTask.Yield`, ou código agendado em um timing do PlayerLoop requer `ValkarnTaskTestHelper.Setup()` e o `TestClock` retornado.

**Exemplo: testando um método baseado em delay**

Suponha que você tem:

```csharp
public static async ValkarnTask WaitAndLog(int ms)
{
    await ValkarnTask.Delay(ms);
    Log("done");
}
```

Teste-o sem espera real:

```csharp
[TestFixture]
public class DelayTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = ValkarnTaskTestHelper.Setup();

    [TearDown]
    public void TearDown() => ValkarnTaskTestHelper.Teardown();

    [Test]
    public void WaitAndLog_CompletesAfterDelay()
    {
        var task = WaitAndLog(3000);
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(task.IsCompleted);

        // Recuperar resultado (lança se faulted)
        task.GetAwaiter().GetResult();
    }

    [Test]
    public void WaitAndLog_ZeroDelay_CompletesImmediately()
    {
        var task = WaitAndLog(0);
        // ValkarnTask.Delay(0) retorna CompletedTask imediatamente
        Assert.IsTrue(task.IsCompleted);
    }
}
```

### Testando cancelamento

```csharp
[Test]
public void Delay_CancelledMidway_ThrowsOCE()
{
    var cts = new CancellationTokenSource();
    var task = ValkarnTask.Delay(5000, cts.Token);
    Assert.IsFalse(task.IsCompleted);

    cts.Cancel();
    // O cancelamento se propaga no próximo tick
    clock.AdvanceFrame();

    Assert.IsTrue(task.IsCompleted);
    Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
}
```

### Coletando exceções não observadas

```csharp
[Test]
public void FaultedTask_PublishesUnobservedException()
{
    using var collector = new TestHelper.UnobservedExceptionCollector();

    var tcs = new ValkarnTaskCompletionSource();
    var task = tcs.Task;

    // Não aguardar — fire and forget
    task.Forget();

    // Completar com exceção — aciona o caminho não observado
    tcs.TrySetException(new InvalidOperationException("oops"));

    Assert.AreEqual(1, collector.Exceptions.Count);
    Assert.IsInstanceOf<InvalidOperationException>(collector.Exceptions[0]);
}
```

---

## Exemplo: Testando um pipeline Producer/Consumer com Channel

```csharp
[TestFixture]
public class ChannelPipelineTests
{
    [SetUp]
    public void SetUp() => TestHelper.EnsureInitialized();

    [Test]
    public void Producer_WritesItems_ConsumerReadsInOrder()
    {
        var channel = ValkarnTask.Channel.CreateUnbounded<int>();

        // Produtor: escrever sincronamente
        channel.Writer.TryWrite(1);
        channel.Writer.TryWrite(2);
        channel.Writer.TryWrite(3);
        channel.Writer.Complete();

        // Consumidor: ReadAsync em um channel com itens completa sincronamente
        Assert.AreEqual(1, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(2, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(3, TestHelper.RunSync(channel.Reader.ReadAsync()));
    }

    [Test]
    public void ReadAsync_BeforeWrite_PendsThenCompletesOnWrite()
    {
        var channel = ValkarnTask.Channel.CreateUnbounded<string>();

        // Leitura emitida antes de qualquer escrita
        var readTask = channel.Reader.ReadAsync();
        Assert.IsFalse(readTask.IsCompleted);

        // Escrita completa a leitura pendente sincronamente
        channel.Writer.TryWrite("hello");
        Assert.IsTrue(readTask.IsCompleted);
        Assert.AreEqual("hello", readTask.GetAwaiter().GetResult());
    }

    [Test]
    public void Complete_DrainedChannel_CompletionTaskCompletes()
    {
        var channel = ValkarnTask.Channel.CreateUnbounded<int>();
        channel.Writer.TryWrite(42);

        var completion = channel.Reader.Completion;
        Assert.IsFalse(completion.IsCompleted);

        channel.Writer.Complete();
        Assert.IsFalse(completion.IsCompleted);  // item ainda não lido

        channel.Reader.TryRead(out _);
        Assert.IsTrue(completion.IsCompleted);   // drenado — completion dispara
    }

    [Test]
    public void ClosedChannel_Read_ThrowsChannelClosedException()
    {
        var channel = ValkarnTask.Channel.CreateUnbounded<int>();
        channel.Writer.Complete();

        var readTask = channel.Reader.ReadAsync();
        Assert.IsTrue(readTask.IsCompleted);
        Assert.Throws<ChannelClosedException>(() => readTask.GetAwaiter().GetResult());
    }
}
```

---

## Exemplo: Testando ValkarnTask.Delay com avanço de frames

Este exemplo demonstra como testar um método que aguarda um delay configurável, verificando que o avanço parcial não completa a task prematuramente.

```csharp
[TestFixture]
public class DelayFrameTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = ValkarnTaskTestHelper.Setup(defaultDeltaTime: 1f / 60f);

    [TearDown]
    public void TearDown() => ValkarnTaskTestHelper.Teardown();

    [Test]
    public void Delay_500ms_RequiresTenFramesAt50ms()
    {
        clock.SetDeltaTime(0.05f);  // 50 ms por frame
        var task = ValkarnTask.Delay(500);

        clock.AdvanceFrames(9);
        Assert.IsFalse(task.IsCompleted, "450 ms decorridos — não deveria estar pronto");

        clock.AdvanceFrame();
        Assert.IsTrue(task.IsCompleted, "500 ms decorridos — deveria estar pronto");
    }

    [Test]
    public void Delay_Realtime_UsesTimestampNotDeltaTime()
    {
        // Delay realtime ignora DeltaTime; usa o timestamp do Stopwatch
        clock.SetDeltaTime(0f);  // DeltaTime congelado
        var task = ValkarnTask.Delay(200, DelayType.Realtime);

        // Advance move apenas o timestamp via TimeSpan
        clock.Advance(TimeSpan.FromMilliseconds(199));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(task.IsCompleted);
    }
}
```

---

## Integração com NUnit

A suíte de testes usa **NUnit 3.x** (fixado na versão 3.x em `TestRunner.csproj`). O NUnit 4 removeu a API clássica `Assert.IsTrue` / `Assert.AreEqual` que os testes utilizam; não atualize para o NUnit 4 a menos que as asserções sejam atualizadas para a API baseada em constraints.

### Unity Test Framework

No Unity Editor, os testes em `Tests/Editor/` são descobertos e executados pelo **Unity Test Framework** (UTF), que é baseado em NUnit. A integração com o test runner funciona da seguinte forma:

- O UTF executa cada `[Test]` na thread principal.
- `ValkarnTaskTestHelper.Setup()` instala o `TestClock` antes de cada teste; o atributo `[SetUp]` do UTF o chama.
- `ValkarnTaskTestHelper.Teardown()` remove o clock em `[TearDown]`.
- Nenhuma coroutine ou `[UnityTest]` é necessário: todos os testes do Valkarn Tasks usam métodos NUnit `[Test]` síncronos. O `TestClock` controla o tempo manualmente, então não há necessidade de avanço real de frames.

---

## O projeto _TestRunner~

O diretório `_TestRunner~/` contém um projeto .NET 8 standalone que compila e executa toda a suíte de testes **fora do Unity**, usando o SDK .NET e `dotnet test`. Isso é usado para CI e desenvolvimento local de colaboradores.

### Estrutura

```
_TestRunner~/
  TestRunner.csproj   — arquivo de projeto .NET 8
  bin~/               — saída de build (gitignored)
  obj~/               — saída intermediária (gitignored)
```

### Como funciona

`TestRunner.csproj` compila quatro conjuntos de fontes em uma única assembly:

| Conjunto de fontes | Caminho | Observações |
|--------------------|---------|-------------|
| Runtime | `../Runtime/**/*.cs` | Todos os arquivos de runtime |
| Testing | `../Testing/**/*.cs` | `ValkarnTaskTestHelper`, `TestClock` |
| Tests | `../Tests/Editor/**/*.cs` | Todos os arquivos de teste `.cs` |
| Exclusões | `UnityTimeProvider.cs`, `Bridge/`, `Burst/`, `ECS/` | Requer APIs Unity reais — excluídos |

O projeto **não** define `UNITY_5_3_OR_NEWER`, `VTASKS_HAS_BURST`, `VTASKS_HAS_COLLECTIONS` ou `VTASKS_HAS_ENTITIES`, então todos os branches `#if` específicos do Unity são compilados fora. Isso significa que os testes exercitam os caminhos de código C# puro.

As DLLs de analisador são carregadas como itens `<Analyzer>`, então as mesmas regras que disparam na IDE também disparam durante o `dotnet build` do projeto de teste.

### Executando os testes

```bash
cd _TestRunner~
dotnet test
```

Ou da raiz do repositório:

```bash
dotnet test _TestRunner~/TestRunner.csproj
```

### Arquivos de teste excluídos

Três subdiretórios de teste são excluídos do projeto `_TestRunner~` porque requerem APIs de runtime reais do Unity:

| Diretório | Motivo |
|-----------|--------|
| `Tests/Editor/Bridge/` | Testes para bridge do Job System; requer `Unity.Jobs` |
| `Tests/Editor/Burst/` | Testes para scheduler Burst; requer `Unity.Burst` |
| `Tests/Editor/ECS/` | Testes para utilitários ECS; requer `Unity.Entities` |

Esses testes são executados apenas dentro do Unity Editor via Unity Test Framework.

### Adicionando um novo teste

1. Adicione um novo arquivo `.cs` em `Tests/Editor/` (não em um subdiretório de API Unity).
2. Decore a classe com `[TestFixture]` e os métodos com `[Test]`.
3. Se o teste precisa de controle de tempo, use `ValkarnTaskTestHelper.Setup()` / `Teardown()` em `[SetUp]` / `[TearDown]`.
4. Se o teste precisa apenas de asserções síncronas de tasks (sem delay), use `TestHelper.EnsureInitialized()` em `[SetUp]` — nenhum teardown é necessário.
5. Execute `dotnet test _TestRunner~/TestRunner.csproj` para verificar fora do Unity.
6. Execute o Unity Test Framework para verificar dentro do Unity.
