---
sidebar_position: 1
title: Задачи на структурах
---

# Задачи на структурах

`VlkTask` и `VlkTask<T>` — это основные асинхронные типы возврата в Valkarn Tasks. В отличие от `System.Threading.Tasks.Task`, который является ссылочным типом и всегда выделяет память в куче, оба типа задач Valkarn являются значениями `readonly struct`. На этой странице объясняется, что это означает на практике, как работает быстрый путь без аллокаций, и как компилятор интегрируется с механизмом async/await.

---

## Почему `readonly struct`?

Задача на основе класса, такая как `Task<T>`, должна выделяться в куче каждый раз при вызове async-метода, даже для методов, завершающихся синхронно. В игровом цикле Unity, работающем на 60 fps, сотни небольших асинхронных операций каждый кадр могут создавать ощутимое давление на GC.

`VlkTask` и `VlkTask<T>` объявлены как `readonly partial struct`:

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

Будучи структурой, значение задачи само по себе живёт на стеке (или встроенно в родительский объект), а не в куче. Модификатор `readonly` позволяет компилятору рассуждать о неизменяемости и предотвращает случайные ошибки копирования. `StructLayout.Auto` позволяет среде выполнения оптимизировать порядок полей для целевой платформы.

### Ключевой инвариант: `source == null`

Дизайн построен вокруг одного инварианта:

> Когда `source` равен `null`, задача синхронно завершена без ошибок. Объекты в куче не задействованы.

`VlkTask.CompletedTask` — это `default(VlkTask)`, у которого поле `source` равно null, поэтому это ничего не стоит. `VlkTask<T>` хранит результат встроенно в поле `result`, что также делает `VlkTask.FromResult(value)` вызовом без аллокаций:

```csharp
// Ноль аллокаций — source равен null, результат хранится встроенно
VlkTask<int> task = VlkTask.FromResult(42);

// Тоже ноль аллокаций — source равен null
VlkTask done = VlkTask.CompletedTask;
```

---

## Быстрый путь без аллокаций

Когда `async`-метод завершается, ни разу не приостанавливаясь (ни один `await` не ожидает незавершённой операции), весь метод выполняется синхронно в вызывающем потоке. Builder обнаруживает это и возвращает задачу с `source == null`.

Awaiter проверяет это немедленно:

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

Когда `IsCompleted` равен true до вызова `OnCompleted`, конечный автомат не регистрирует продолжение. `GetResult` вызывается немедленно, и для `VlkTask<T>` с `source == null` результат читается из встроенного поля `result` структуры:

```csharp
public T GetResult()
{
    var s = task.source;
    if (s == null)
        return task.result;   // встроенный, без вызова ISource
    return s.GetResult(task.token);
}
```

Никаких объектов не создаётся, никакой диспетчеризации через интерфейс, никаких делегатов продолжения. Весь await разрешается как прямое чтение значения.

### Когда нужен source

Если async-метод приостанавливается (ожидает что-то, что ещё не завершилось), builder выделяет из пула объект `AsyncVlkTaskRunner<TStateMachine>` (или `AsyncVlkTaskRunner<TStateMachine, TResult>` для обобщённого варианта). Этот объект выполняет двойную роль: он хранит компилятор-сгенерированный конечный автомат по значению и реализует `VlkTask.ISource`, так что может использоваться непосредственно как источник задачи. Задача, возвращаемая вызывающим, оборачивает этот runner вместе с поколенческим токеном `uint`.

При завершении, когда вызывающий вызывает `GetResult` на awaiter'е, runner сбрасывает себя и возвращается в свой пул — таким образом, аллокация амортизируется по многим вызовам метода.

---

## Интерфейс `ISource`

Контракт между структурой `VlkTask` и её асинхронным объектом-источником — это `VlkTask.ISource`:

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

Любой объект, реализующий `ISource`, может быть источником `VlkTask`. Библиотека поставляется с несколькими реализациями:

| Тип | Назначение |
|-----|-----------|
| `AsyncVlkTaskRunner<TStateMachine>` | Источник каждого метода `async VlkTask` (внутренний) |
| `AsyncVlkTaskRunner<TStateMachine, TResult>` | Источник каждого метода `async VlkTask<T>` (внутренний) |
| `VlkTask.PooledPromise` | Источник ручного завершения с автоматическим возвратом в пул |
| `VlkTask.PooledPromise<T>` | Обобщённый вариант вышеуказанного |
| `VlkTask.Promise` | Источник ручного завершения без пулирования (долгоживущие операции) |
| `VlkTask.Promise<T>` | Обобщённый вариант вышеуказанного |

Параметр `uint token` является поколенческой защитой. Когда пулируемый источник сбрасывается для повторного использования, его счётчик поколений увеличивается. Любая структура `VlkTask`, держащая старый токен, немедленно получит `InvalidOperationException` вместо молчаливого чтения переработанного состояния.

---

## `VlkTask` vs `VlkTask<T>`

| Возможность | `VlkTask` | `VlkTask<T>` |
|-------------|-----------|-------------|
| Возвращаемое значение | Нет (эквивалент void) | `T` |
| Встроенное хранение результата | Нет поля `result` | Поле `result` (тип `T`) |
| Awaiter `GetResult` | `void` | Возвращает `T` |
| Тип builder'а | `AsyncVlkTaskMethodBuilder` | `AsyncVlkTaskMethodBuilder<TResult>` |
| Синхронно завершённое значение | `VlkTask.CompletedTask` | `VlkTask.FromResult(value)` |
| Преобразование в необобщённый | Не применимо | `.AsNonGeneric()` |

Используйте `VlkTask`, когда async-метод не имеет значимого возвращаемого значения, и `VlkTask<T>`, когда он возвращает результат. Вы всегда можете понизить `VlkTask<T>` до `VlkTask` через `AsNonGeneric()`, когда нужно смешивать типизированные и нетипизированные задачи в комбинаторах типа `WhenAll`.

---

## Как работает async method builder

Компилятор C# ищет тип, указанный в `[AsyncMethodBuilder(...)]` у возвращаемого типа. Для `VlkTask` это `AsyncVlkTaskMethodBuilder`. Для `VlkTask<T>` — `AsyncVlkTaskMethodBuilder<TResult>`.

Builder сам является структурой, чтобы избежать аллокации для объекта builder'а. У него два поля (три для обобщённого варианта):

```csharp
public struct AsyncVlkTaskMethodBuilder
{
    IStateMachineRunnerPromise runner;   // null до первой приостановки
    Exception syncException;            // устанавливается только при синхронной ошибке
}

public struct AsyncVlkTaskMethodBuilder<TResult>
{
    IStateMachineRunnerPromise<TResult> runner;
    Exception syncException;
    TResult result;                     // устанавливается только при синхронном успехе
}
```

### Жизненный цикл builder'а

Компилятор вызывает эти методы в следующем порядке:

**1. `Create()`** — возвращает builder по умолчанию (все поля null/default). Без аллокаций.

**2. `Start(ref stateMachine)`** — синхронно вызывает `stateMachine.MoveNext()`. Если метод завершается без незавершённого `await`, вызывается `SetResult`/`SetException` и `runner` остаётся null.

**3. `AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine)`** — вызывается, когда метод встречает незавершённый `await`. Если `runner` равен null (первая приостановка), берётся или создаётся `AsyncVlkTaskRunner`, и конечный автомат копируется в него. Затем вызывается `awaiter.UnsafeOnCompleted(runner.MoveNextAction)` для регистрации продолжения конечного автомата.

**4. `SetResult()` / `SetException(exception)`** — сигнализирует о завершении в `VlkTaskCompletionCore` runner'а, что пробуждает зарегистрированный awaiter.

**5. Свойство `Task`** — проверяется вызывающим для получения значения `VlkTask`. На пути синхронного успеха (`runner == null && syncException == null`) возвращает `default` (или `new VlkTask<T>(result)` для обобщённого варианта) — без аллокаций. На асинхронном пути оборачивает runner как источник.

Ключевая оптимизация состоит в том, что `runner` выделяется лениво. Если метод завершается синхронно (что типично для попаданий в кэш, проверок охраны, ранних возвратов), пулируемый объект никогда не берётся.

---

## Состояния `VlkTaskStatus`

Статус представлен перечислением размером с `byte`, вложенным в `VlkTask`:

```csharp
public enum Status : byte
{
    Pending   = 0,   // ещё не завершён
    Succeeded = 1,   // завершён нормально
    Faulted   = 2,   // завершён с необработанным исключением
    Canceled  = 3    // завершён через OperationCanceledException
}
```

Вы можете проверять статус напрямую:

```csharp
VlkTask task = SomeOperation();
VlkTask.Status status = task.GetStatus();

switch (status)
{
    case VlkTask.Status.Pending:
        // Ещё выполняется — нельзя вызывать GetResult
        break;
    case VlkTask.Status.Succeeded:
        // Завершён нормально
        break;
    case VlkTask.Status.Faulted:
        // Завершён с исключением — GetResult повторно бросит его
        break;
    case VlkTask.Status.Canceled:
        // Завершён с OperationCanceledException
        break;
}
```

Для синхронно завершённого быстрого пути (где `source == null`) `GetStatus()` возвращает `Succeeded` без каких-либо вызовов через интерфейс:

```csharp
public Status GetStatus()
{
    if (source == null) return Status.Succeeded;
    return source.GetStatus(token);
}
```

Свойство `IsCompleted` следует тому же паттерну и возвращает `true` для любого не-`Pending` состояния.

---

## Последствия для IL2CPP

IL2CPP компилирует C# в C++ перед сборкой в нативный код. Обобщённые типы-значения — включая структуры — полностью специализируются в сгенерированном коде, что имеет важные последствия для этой библиотеки.

**Специализация конечного автомата.** Компилятор генерирует уникальную структуру конечного автомата для каждого async-метода. `AsyncVlkTaskRunner<TStateMachine>` поэтому также уникален для каждого async-метода, а `VlkTaskPool<AsyncVlkTaskRunner<TStateMachine>>` — это отдельный пул для каждого метода. Это на самом деле выгодно: пул никогда не разделяется между несовместимыми типами, что устраняет любой риск путаницы типов.

**Без boxing конечного автомата.** Конечный автомат хранится по значению внутри объекта runner'а, а не в упакованном виде. IL2CPP обрабатывает это корректно, потому что runner — это `sealed class` с конкретным полем `TStateMachine`.

**Защита от вырезания.** Атрибут `[AsyncMethodBuilder]` сохраняет типы builder'ов живыми. Однако, если вы используете `VlkTask.ISource` через ссылку на интерфейс в IL2CPP с агрессивным вырезанием, добавьте запись `link.xml`, сохраняющую сборку `UnaPartidaMas.Valkarn.Tasks`:

```xml
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```

**`ICriticalNotifyCompletion`.** Структуры awaiter'ов реализуют `ICriticalNotifyCompletion`, что говорит компилятору вызывать `UnsafeOnCompleted` вместо `OnCompleted`. «Unsafe» вариант намеренно пропускает захват `ExecutionContext`. Это правильно для Unity — в конфигурации Unity по умолчанию нет `SynchronizationContext`, и его захват добавил бы накладные расходы без пользы. В IL2CPP это также позволяет избежать накладных расходов пути `ExecutionContext.Run`, который стандартный `Task` всегда платит.

---

## Практические примеры

### Ранний возврат без аллокаций

```csharp
// async VlkTask<int>, который синхронно завершается на горячем пути
async VlkTask<int> GetCachedValue(string key)
{
    if (_cache.TryGetValue(key, out var value))
        return value;            // компилятор вызывает SetResult(value); source остаётся null

    var result = await FetchFromDatabaseAsync(key);
    _cache[key] = result;
    return result;
}
```

Когда значение закэшировано, метод никогда не приостанавливается. Возвращённый `VlkTask<int>` имеет `source == null` и несёт результат встроенно. На этом пути аллокации в куче не происходит.

### Проверка IsCompleted перед await

```csharp
VlkTask<Texture2D> loadTask = LoadTextureAsync("sprites/hero.png");

if (loadTask.IsCompleted)
{
    // Уже готово — GetAwaiter().GetResult() читает встроенный результат без вызова ISource
    Texture2D tex = loadTask.GetAwaiter().GetResult();
    ApplyTexture(tex);
}
else
{
    // По-настоящему асинхронно — регистрируем продолжение
    ApplyTextureAsync(loadTask).Forget();
}
```

### Наблюдение за необработанными исключениями

Задачи с ошибками, которые никогда не ожидаются (паттерны fire-and-forget), сообщают о своих исключениях через событие `VlkTask.UnobservedException`. Это вызывается детерминированно во время возврата в пул для пулируемых источников, или из финализатора для задач, поддерживаемых `Promise`.

```csharp
VlkTask.UnobservedException += ex =>
{
    Debug.LogError($"[VlkTask] Unobserved: {ex}");
};
```

Событие потокобезопасно; обработчики могут добавляться или удаляться из любого потока с использованием lock-free цикла compare-exchange.
