---
sidebar_position: 3
title: Тестирование
---

# Тестирование

Valkarn Tasks поставляется с выделенной инфраструктурой тестирования, позволяющей писать быстрые детерминированные юнит-тесты для асинхронного кода — без реальных таймеров, без ожидания кадров, без необходимости в Unity Editor для основного набора тестов.

---

## Обзор

Поддержка тестирования находится в сборке `Testing/` (`UnaPartidaMas.Valkarn.Tasks.Testing`), которая доступна среде выполнения через `InternalsVisibleTo`. Она предоставляет два публичных типа:

| Тип | Назначение |
|-----|-----------|
| `VlkTaskTestHelper` | Инициализация и завершение среды выполнения Valkarn Tasks для сессии тестирования |
| `TestClock` | Детерминированный провайдер времени и кадров; заменяет Unity `TimeProvider` во время тестов |

---

## VlkTaskTestHelper

`VlkTaskTestHelper` — это статический вспомогательный класс в пространстве имён `UnaPartidaMas.Valkarn.Tasks.Testing`.

### Что он делает

`Setup()` выполняет три действия:
1. Создаёт `TestClock` и устанавливает его как `TimeProvider.Current`, заменяя любой реальный провайдер времени Unity.
2. Вызывает `PlayerLoopHelper.InitializeForTest()`, который выделяет массивы `ContinuationQueue` и `PlayerLoopRunner` для всех 16 таймингов PlayerLoop и помечает среду выполнения как инициализированную.
3. Возвращает `TestClock`, чтобы тест мог управлять временем.

`Teardown()` отменяет настройку:
1. Устанавливает свежий пустой `TestClock` как `TimeProvider.Current`, чтобы предотвратить утечку устаревшего времени между фикстурами тестов.
2. Вызывает `PlayerLoopHelper.ShutdownForTest()`, который останавливает очереди и запускатели.

### Паттерн использования

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
        clock = VlkTaskTestHelper.Setup();
    }

    [TearDown]
    public void TearDown()
    {
        VlkTaskTestHelper.Teardown();
    }

    // Тесты здесь
}
```

Вызывайте `Setup` один раз за тест в `[SetUp]` и `Teardown` один раз за тест в `[TearDown]`. Паттерн гарантирует, что каждый тест начинается с чистым состоянием среды выполнения и не утекает в следующий тест.

### Дельта-время по умолчанию

`Setup` принимает необязательный параметр `defaultDeltaTime` (по умолчанию: `1/60` секунды, то есть 60 fps):

```csharp
clock = VlkTaskTestHelper.Setup(defaultDeltaTime: 1f / 30f);  // симуляция 30 fps
```

Это значение используется `TestClock.AdvanceFrame()` и `TestClock.AdvanceFrames(n)`.

---

## TestClock

`TestClock` реализует `ITimeProvider` и даёт тестам полный контроль над:

- Симулируемым временем (через `GetTimestamp()`, основанный на тиках `Stopwatch.Frequency`)
- `DeltaTime` и `UnscaledDeltaTime` для текущего кадра
- `FrameCount`

### Продвижение времени

#### `Advance(TimeSpan duration)`

Продвигает время на заданную длительность за один шаг. Вся длительность применяется как `DeltaTime` для этого кадра, `FrameCount` увеличивается на 1, и обрабатываются все тайминги PlayerLoop. После обработки `DeltaTime` восстанавливается до значения, которое было до вызова.

```csharp
var task = VlkTask.Delay(3000);  // задержка 3 секунды

clock.Advance(TimeSpan.FromSeconds(2));
Assert.IsFalse(task.IsCompleted);   // ещё нет

clock.Advance(TimeSpan.FromSeconds(1));
Assert.IsTrue(task.IsCompleted);    // ровно в 3 секунды
```

Используйте это, когда хотите перейти к конкретной точке во времени без симуляции отдельных кадров.

#### `AdvanceFrame()`

Продвигает один кадр, используя текущий `DeltaTime`. `FrameCount` увеличивается на 1, все тайминги PlayerLoop обрабатываются, и перед обработкой вставляется `Thread.Sleep` на 1 мс. Задержка существует потому, что фоновые рабочие потоки (используемые `RunOnThreadPool` и интеграцией с Unity Job System) нуждаются в небольшом окне для завершения между тиками кадров — без этого тесты выполняют кадры один за другим без пауз, что вызывает гонки состояний, которых не бывает в реальных кадрах Unity.

```csharp
clock.AdvanceFrame();
```

#### `AdvanceFrames(int count)`

Вызывает `AdvanceFrame()` `count` раз.

```csharp
clock.AdvanceFrames(10);  // симулировать 10 кадров
```

#### `ProcessTick(PlayerLoopTiming timing)`

Обрабатывает одну фазу тайминга PlayerLoop без продвижения времени или `FrameCount`. Полезно при тестировании кода, запланированного на конкретный тайминг (например, `PlayerLoopTiming.FixedUpdate`), когда не нужно обрабатывать все остальные тайминги.

```csharp
clock.ProcessTick(PlayerLoopTiming.FixedUpdate);
```

### Управление дельта-временем

#### `SetDeltaTime(float deltaTime)`

Устанавливает и `DeltaTime`, и `UnscaledDeltaTime` в одно значение для всех последующих кадров.

#### `SetDeltaTime(float deltaTime, float unscaledDeltaTime)`

Устанавливает их независимо для симуляции отличного от единицы `Time.timeScale`. Например, для тестирования кода, использующего `DelayType.UnscaledDeltaTime` при паузе времени:

```csharp
clock.SetDeltaTime(deltaTime: 0f, unscaledDeltaTime: 0.05f);

var scaledTask = VlkTask.Delay(500, DelayType.DeltaTime);
var unscaledTask = VlkTask.Delay(500, DelayType.UnscaledDeltaTime);

clock.AdvanceFrames(9);  // 450 мс без масштаба, 0 мс масштабируемых
Assert.IsFalse(scaledTask.IsCompleted);    // масштабируемое время игры никогда не достигает 500мс
Assert.IsFalse(unscaledTask.IsCompleted); // 450мс < 500мс

clock.AdvanceFrame();  // 500 мс без масштаба
Assert.IsTrue(unscaledTask.IsCompleted);  // немасштабируемая задержка выполнена
Assert.IsFalse(scaledTask.IsCompleted);  // масштабируемое по-прежнему не двигается
```

---

## Написание юнит-тестов для async VlkTask-методов

### Тесты, завершающиеся синхронно

Некоторые операции `VlkTask` завершаются без приостановки — например, ожидание `VlkTask.CompletedTask`, `VlkTask.FromResult(value)` или `VlkTaskCompletionSource`, завершённого до ожидания. Они вообще не требуют часов и могут использовать внутренний класс `TestHelper` напрямую (внутренний для тестовой сборки):

```csharp
[TestFixture]
public class SyncTests
{
    [SetUp]
    public void SetUp()
    {
        // Только устанавливает ID основного потока — часы или PlayerLoop не нужны
        TestHelper.EnsureInitialized();
    }

    [Test]
    public void FromResult_ReturnsValue()
    {
        var task = VlkTask.FromResult(42);
        Assert.IsTrue(task.IsCompleted);
        Assert.AreEqual(42, task.GetAwaiter().GetResult());
    }
}
```

`TestHelper` (в `Tests/Editor/TestHelper.cs`) — это внутренний вспомогательный класс, используемый самим набором тестов:

- `TestHelper.EnsureInitialized()` устанавливает `VlkTaskPoolShared.MainThreadId` в текущий поток. Это необходимо для правильной маршрутизации операций пула на корректный (неатомарный) быстрый путь.
- `TestHelper.RunSync(VlkTask task)` утверждает, что задача уже завершена, и вызывает `GetResult()` — полезно для тестирования путей кода, которые должны завершаться синхронно.
- `TestHelper.RunSync<T>(VlkTask<T> task)` делает то же самое и возвращает значение результата.
- `TestHelper.UnobservedExceptionCollector` подписывается на `VlkTask.UnobservedException` на время своей области видимости, собирая необнаруженные исключения для проверки.

### Тесты, требующие времени

Любой тест, включающий `VlkTask.Delay`, `VlkTask.Yield` или код, запланированный на тайминг PlayerLoop, требует `VlkTaskTestHelper.Setup()` и возвращённого `TestClock`.

**Пример: тестирование метода на основе задержки**

Предположим, у вас есть:

```csharp
public static async VlkTask WaitAndLog(int ms)
{
    await VlkTask.Delay(ms);
    Log("done");
}
```

Тестируйте без реального ожидания:

```csharp
[TestFixture]
public class DelayTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = VlkTaskTestHelper.Setup();

    [TearDown]
    public void TearDown() => VlkTaskTestHelper.Teardown();

    [Test]
    public void WaitAndLog_CompletesAfterDelay()
    {
        var task = WaitAndLog(3000);
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(task.IsCompleted);

        // Получить результат (выбрасывает при ошибке)
        task.GetAwaiter().GetResult();
    }

    [Test]
    public void WaitAndLog_ZeroDelay_CompletesImmediately()
    {
        var task = WaitAndLog(0);
        // VlkTask.Delay(0) возвращает CompletedTask немедленно
        Assert.IsTrue(task.IsCompleted);
    }
}
```

### Тестирование отмены

```csharp
[Test]
public void Delay_CancelledMidway_ThrowsOCE()
{
    var cts = new CancellationTokenSource();
    var task = VlkTask.Delay(5000, cts.Token);
    Assert.IsFalse(task.IsCompleted);

    cts.Cancel();
    // Отмена распространяется на следующем тике
    clock.AdvanceFrame();

    Assert.IsTrue(task.IsCompleted);
    Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
}
```

### Сбор необнаруженных исключений

```csharp
[Test]
public void FaultedTask_PublishesUnobservedException()
{
    using var collector = new TestHelper.UnobservedExceptionCollector();

    var tcs = new VlkTaskCompletionSource();
    var task = tcs.Task;

    // Не ожидаем — fire and forget
    task.Forget();

    // Завершаем с исключением — запускает путь необнаруженного исключения
    tcs.TrySetException(new InvalidOperationException("oops"));

    Assert.AreEqual(1, collector.Exceptions.Count);
    Assert.IsInstanceOf<InvalidOperationException>(collector.Exceptions[0]);
}
```

---

## Пример: тестирование канала producer/consumer

```csharp
[TestFixture]
public class ChannelPipelineTests
{
    [SetUp]
    public void SetUp() => TestHelper.EnsureInitialized();

    [Test]
    public void Producer_WritesItems_ConsumerReadsInOrder()
    {
        var channel = VlkTask.Channel.CreateUnbounded<int>();

        // Производитель: пишет синхронно
        channel.Writer.TryWrite(1);
        channel.Writer.TryWrite(2);
        channel.Writer.TryWrite(3);
        channel.Writer.Complete();

        // Потребитель: ReadAsync на канале с элементами завершается синхронно
        Assert.AreEqual(1, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(2, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(3, TestHelper.RunSync(channel.Reader.ReadAsync()));
    }

    [Test]
    public void ReadAsync_BeforeWrite_PendsThenCompletesOnWrite()
    {
        var channel = VlkTask.Channel.CreateUnbounded<string>();

        // Чтение выдано до любой записи
        var readTask = channel.Reader.ReadAsync();
        Assert.IsFalse(readTask.IsCompleted);

        // Запись завершает ожидающее чтение синхронно
        channel.Writer.TryWrite("hello");
        Assert.IsTrue(readTask.IsCompleted);
        Assert.AreEqual("hello", readTask.GetAwaiter().GetResult());
    }

    [Test]
    public void Complete_DrainedChannel_CompletionTaskCompletes()
    {
        var channel = VlkTask.Channel.CreateUnbounded<int>();
        channel.Writer.TryWrite(42);

        var completion = channel.Reader.Completion;
        Assert.IsFalse(completion.IsCompleted);

        channel.Writer.Complete();
        Assert.IsFalse(completion.IsCompleted);  // элемент ещё не прочитан

        channel.Reader.TryRead(out _);
        Assert.IsTrue(completion.IsCompleted);   // опустошён — completion срабатывает
    }

    [Test]
    public void ClosedChannel_Read_ThrowsChannelClosedException()
    {
        var channel = VlkTask.Channel.CreateUnbounded<int>();
        channel.Writer.Complete();

        var readTask = channel.Reader.ReadAsync();
        Assert.IsTrue(readTask.IsCompleted);
        Assert.Throws<ChannelClosedException>(() => readTask.GetAwaiter().GetResult());
    }
}
```

---

## Пример: тестирование VlkTask.Delay с продвижением кадров

Этот пример демонстрирует тестирование метода, ожидающего настраиваемую задержку, с проверкой того, что частичное продвижение не завершает задачу преждевременно.

```csharp
[TestFixture]
public class DelayFrameTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = VlkTaskTestHelper.Setup(defaultDeltaTime: 1f / 60f);

    [TearDown]
    public void TearDown() => VlkTaskTestHelper.Teardown();

    [Test]
    public void Delay_500ms_RequiresTenFramesAt50ms()
    {
        clock.SetDeltaTime(0.05f);  // 50 мс на кадр
        var task = VlkTask.Delay(500);

        clock.AdvanceFrames(9);
        Assert.IsFalse(task.IsCompleted, "Прошло 450 мс — не должно быть готово");

        clock.AdvanceFrame();
        Assert.IsTrue(task.IsCompleted, "Прошло 500 мс — должно быть готово");
    }

    [Test]
    public void Delay_Realtime_UsesTimestampNotDeltaTime()
    {
        // Задержка Realtime игнорирует DeltaTime; она использует метку Stopwatch
        clock.SetDeltaTime(0f);  // DeltaTime заморожен
        var task = VlkTask.Delay(200, DelayType.Realtime);

        // Advance перемещает только метку через TimeSpan
        clock.Advance(TimeSpan.FromMilliseconds(199));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(task.IsCompleted);
    }
}
```

---

## Интеграция с NUnit

Набор тестов использует **NUnit 3.x** (фиксировано на версии 3.x в `TestRunner.csproj`). NUnit 4 удалил классический API `Assert.IsTrue` / `Assert.AreEqual`, который использует набор тестов; не обновляйтесь до NUnit 4 без обновления утверждений до API на основе ограничений.

### Unity Test Framework

В Unity Editor тесты в `Tests/Editor/` обнаруживаются и запускаются **Unity Test Framework** (UTF), основанным на NUnit. Интеграция с запускателем тестов работает следующим образом:

- UTF запускает каждый `[Test]` в основном потоке.
- `VlkTaskTestHelper.Setup()` устанавливает `TestClock` перед каждым тестом; атрибут `[SetUp]` UTF вызывает его.
- `VlkTaskTestHelper.Teardown()` удаляет часы в `[TearDown]`.
- Корутина или `[UnityTest]` не нужны: все тесты Valkarn Tasks используют синхронные NUnit-методы `[Test]`. `TestClock` управляет временем вручную, поэтому нет необходимости в реальном продвижении кадров.

---

## Проект _TestRunner~

Директория `_TestRunner~/` содержит автономный проект .NET 8, который компилирует и запускает весь набор тестов **вне Unity**, используя .NET SDK и `dotnet test`. Это используется для CI и для локальной разработки участников.

### Структура

```
_TestRunner~/
  TestRunner.csproj   — файл проекта .NET 8
  bin~/               — вывод сборки (в .gitignore)
  obj~/               — промежуточный вывод (в .gitignore)
```

### Как это работает

`TestRunner.csproj` компилирует четыре набора исходных кодов в одну сборку:

| Набор исходников | Путь | Примечания |
|-----------------|------|-----------|
| Runtime | `../Runtime/**/*.cs` | Все файлы среды выполнения |
| Testing | `../Testing/**/*.cs` | `VlkTaskTestHelper`, `TestClock` |
| Tests | `../Tests/Editor/**/*.cs` | Все `.cs`-файлы тестов |
| Исключения | `UnityTimeProvider.cs`, `Bridge/`, `Burst/`, `ECS/` | Требуют реальных API Unity — исключены |

Проект **не** определяет `UNITY_5_3_OR_NEWER`, `VTASKS_HAS_BURST`, `VTASKS_HAS_COLLECTIONS` или `VTASKS_HAS_ENTITIES`, поэтому все Unity-специфичные ветви `#if` исключаются при компиляции. Это означает, что тесты проверяют пути кода на чистом C#.

DLL анализаторов загружаются как элементы `<Analyzer>`, поэтому те же правила, которые срабатывают в IDE, также срабатывают при `dotnet build` тестового проекта.

### Запуск тестов

```bash
cd _TestRunner~
dotnet test
```

Или из корня репозитория:

```bash
dotnet test _TestRunner~/TestRunner.csproj
```

### Исключённые тестовые файлы

Три поддиректории тестов исключены из проекта `_TestRunner~`, поскольку требуют реальных API среды выполнения Unity:

| Директория | Причина |
|-----------|---------|
| `Tests/Editor/Bridge/` | Тесты для моста Job System; требует `Unity.Jobs` |
| `Tests/Editor/Burst/` | Тесты для планировщика Burst; требует `Unity.Burst` |
| `Tests/Editor/ECS/` | Тесты для утилит ECS; требует `Unity.Entities` |

Эти тесты выполняются только внутри Unity Editor через Unity Test Framework.

### Добавление нового теста

1. Добавьте новый `.cs`-файл в `Tests/Editor/` (не в поддиректорию с Unity API).
2. Декорируйте класс `[TestFixture]`, а методы — `[Test]`.
3. Если тесту нужен контроль времени, используйте `VlkTaskTestHelper.Setup()` / `Teardown()` в `[SetUp]` / `[TearDown]`.
4. Если тесту нужны только синхронные утверждения задач (без задержки), используйте `TestHelper.EnsureInitialized()` в `[SetUp]` — teardown не нужен.
5. Запустите `dotnet test _TestRunner~/TestRunner.csproj` для проверки вне Unity.
6. Запустите Unity Test Framework для проверки внутри Unity.
