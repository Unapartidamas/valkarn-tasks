---
sidebar_position: 2
title: ValkarnTask<T>
---

# `ValkarnTask<T>`

`ValkarnTask<T>` — это асинхронный тип задачи с возвращаемым значением в Valkarn Tasks. Это `readonly struct`, несущий либо встроенный результат (при синхронном завершении), либо ссылку на пулируемый объект-источник (при асинхронном завершении).

**Пространство имён:** `UnaPartidaMas.Valkarn.Tasks`

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncValkarnTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
```

Для `T` нет обобщённых ограничений. Любой тип — тип-значение, ссылочный тип, структура или класс — допустим.

---

## Создание экземпляров

### Синхронно завершённые задачи

Эти фабричные методы возвращают `ValkarnTask<T>` без объекта-источника. Ноль аллокаций.

#### `ValkarnTask.FromResult<T>(T value)`

Возвращает завершённый `ValkarnTask<T>`, несущий `value` встроенно. Объявлен как статический метод на необобщённом типе `ValkarnTask`.

```csharp
public static ValkarnTask<T> FromResult<T>(T value)
```

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(42);
ValkarnTask<string> name = ValkarnTask.FromResult("Valkarn");
ValkarnTask<Vector3> pos = ValkarnTask.FromResult(transform.position);
```

Возвращаемая структура имеет `source == null`. Её ожидание не приводит к аллокации продолжения — компилятор немедленно видит `IsCompleted == true`.

#### `ValkarnTask.FromException<T>(Exception exception)`

Возвращает ошибочный `ValkarnTask<T>`. Его ожидание повторно бросит исключение с сохранённым оригинальным стеком вызовов через `ExceptionDispatchInfo`.

```csharp
public static ValkarnTask<T> FromException<T>(Exception exception)
```

```csharp
ValkarnTask<Texture2D> LoadTexture(string path)
{
    if (string.IsNullOrEmpty(path))
        return ValkarnTask.FromException<Texture2D>(
            new ArgumentException("Path must not be empty.", nameof(path)));

    return LoadTextureAsync(path);
}
```

#### `ValkarnTask.FromCanceled<T>(CancellationToken ct = default)`

Возвращает отменённый `ValkarnTask<T>`. Его ожидание бросит `OperationCanceledException`.

```csharp
public static ValkarnTask<T> FromCanceled<T>(CancellationToken ct = default)
```

```csharp
ValkarnTask<byte[]> Download(string url, CancellationToken ct)
{
    if (ct.IsCancellationRequested)
        return ValkarnTask.FromCanceled<byte[]>(ct);

    return DownloadAsync(url, ct);
}
```

### Через `async`-методы

Любой `async`-метод, объявленный для возврата `ValkarnTask<T>`, автоматически использует `AsyncValkarnTaskMethodBuilder<TResult>`:

```csharp
async ValkarnTask<int> ComputeAsync()
{
    await ValkarnTask.Yield();
    return 42;
}
```

Компилятор генерирует конечный автомат. Если метод завершается синхронно (никогда не приостанавливается), `AsyncValkarnTaskMethodBuilder<T>.Task` возвращает `new ValkarnTask<T>(result)` с `source == null` — ноль аллокаций.

---

## Выполнение работы в пуле потоков

Эти методы запускают делегат в пуле потоков .NET и возвращают результат в главный поток (при указанном `PlayerLoopTiming`). Это удобные обёртки над более длинными вариантами `RunOnThreadPool`.

#### `ValkarnTask.Run<T>(Func<T> func, PlayerLoopTiming timing, CancellationToken ct)`

Запускает синхронный `Func<T>` в пуле потоков.

```csharp
public static ValkarnTask<T> Run<T>(
    Func<T> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
// Вычислить в пуле потоков, результат возвращается в главный поток при следующем Update
int hash = await ValkarnTask.Run(() => ComputeExpensiveHash(data));
```

#### `ValkarnTask.Run<T>(Func<ValkarnTask<T>> func, PlayerLoopTiming timing, CancellationToken ct)`

Запускает асинхронный `Func<ValkarnTask<T>>` в пуле потоков. Используйте, когда работа сама является асинхронной (например, файловый ввод-вывод).

```csharp
public static ValkarnTask<T> Run<T>(
    Func<ValkarnTask<T>> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
string json = await ValkarnTask.Run(async () =>
{
    using var reader = File.OpenText("data.json");
    return await reader.ReadToEndAsync();
});
```

Оба варианта `Run` отменяются досрочно, если токен уже отменён, и переключаются обратно в главный поток при `timing` после завершения работы.

---

## Члены экземпляра

### `IsCompleted`

```csharp
public bool IsCompleted { get; }
```

Возвращает `true`, если задача завершена в любом конечном состоянии (Succeeded, Faulted или Canceled). Для синхронно завершённых задач (`source == null`) всегда возвращает `true` без диспетчеризации через интерфейс.

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
public ValkarnTask.Status GetStatus()
```

Возвращает текущий `ValkarnTask.Status`. Возможные значения: `Pending`, `Succeeded`, `Faulted`, `Canceled`. Для `source == null` всегда возвращает `Succeeded`.

### `GetAwaiter()`

```csharp
public Awaiter GetAwaiter()
```

Возвращает структуру `Awaiter`. Используется компилятором для реализации `await`. Вы также можете вызывать его напрямую для синхронного получения результата (безопасно только когда `IsCompleted` истинно).

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(10);
int value = task.GetAwaiter().GetResult(); // безопасно — синхронно завершено
```

Вызов `GetResult()` на ожидающей задаче бросает `InvalidOperationException`.

### `AsNonGeneric()`

```csharp
public ValkarnTask AsNonGeneric()
```

Преобразует этот `ValkarnTask<T>` в необобщённый `ValkarnTask`, отбрасывая тип результата. Результирующая задача разделяет тот же базовый источник и токен, поэтому завершается одновременно.

```csharp
ValkarnTask<int> typedTask = ComputeAsync();
ValkarnTask voidTask = typedTask.AsNonGeneric();
await voidTask;   // ждёт завершения, игнорирует результат
```

Полезно при передаче задач смешанных типов в комбинаторы или когда вас интересует только момент завершения, а не значение.

---

## Комбинаторы, возвращающие `ValkarnTask<T>`

### `WhenAll` — типизированная перегрузка с двумя задачами

```csharp
public static ValkarnTask<(T1, T2)> WhenAll<T1, T2>(
    ValkarnTask<T1> task1, ValkarnTask<T2> task2)
```

Запускает обе задачи параллельно и возвращает кортеж результатов. Если какая-либо задача даёт ошибку или отменяется, первое исключение побеждает, а ошибка другой сообщается через `ValkarnTask.UnobservedException`.

**Деструктуризация кортежа** естественно работает с C#-деструктуризацией:

```csharp
var (profile, inventory) = await ValkarnTask.WhenAll(
    FetchProfileAsync(userId),
    FetchInventoryAsync(userId)
);
```

**Быстрый путь без аллокаций:** если обе задачи синхронно завершены в момент вызова, пулируемый объект не создаётся и результирующий кортеж возвращается встроенно.

### `WhenAll` — типизированная перегрузка с коллекцией

```csharp
public static ValkarnTask<T[]> WhenAll<T>(IEnumerable<ValkarnTask<T>> tasks)
```

Ожидает все задачи в коллекции параллельно и возвращает `T[]` в порядке индексов.

```csharp
var urls = new[] { "https://a.com", "https://b.com", "https://c.com" };
ValkarnTask<string>[] downloads = urls.Select(u => DownloadAsync(u)).ToArray();
string[] results = await ValkarnTask.WhenAll(downloads);
```

Если коллекция пуста, возвращает `ValkarnTask.FromResult(Array.Empty<T>())` — ноль аллокаций. Если все задачи уже синхронно завершены, массив результатов строится встроенно без создания promise комбинатора.

### `WhenAny` — типизированная перегрузка с двумя задачами

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    ValkarnTask<T> task1, ValkarnTask<T> task2)
```

Возвращает, как только завершится любая задача. Результирующий кортеж содержит 0-основанный индекс победителя и его значение. Проигравшие задачи продолжают выполняться; их ошибки (если есть) сообщаются через `ValkarnTask.UnobservedException`. Отмены проигравших намеренно не сообщаются.

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(
    FetchFromCacheAsync(key),
    FetchFromNetworkAsync(key)
);

if (winnerIndex == 0)
    Debug.Log("Cache won");
```

### `WhenAny` — типизированная перегрузка с коллекцией

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    IEnumerable<ValkarnTask<T>> tasks)
```

Та же семантика, что и перегрузка с двумя задачами, расширенная до произвольного числа задач. Требует хотя бы одну задачу; бросает `ArgumentException` для пустых коллекций.

```csharp
var tasks = servers.Select(s => s.FetchAsync(query)).ToArray();
var (winnerIndex, data) = await ValkarnTask.WhenAny(tasks);
Debug.Log($"Server {winnerIndex} responded first");
```

---

## Вспомогательные фабричные методы

### `ValkarnTask.Create<T>(Func<ValkarnTask<T>> factory)`

Вызывает делегат-фабрику и ожидает результирующую задачу. Полезно, когда нужно отложить создание асинхронной операции.

```csharp
public static async ValkarnTask<T> Create<T>(Func<ValkarnTask<T>> factory)
```

```csharp
var result = await ValkarnTask.Create(() => LoadLevelDataAsync(levelId));
```

---

## Структура `Awaiter` (вложенная)

`ValkarnTask<T>.Awaiter` — это awaiter, обращённый к компилятору. Это `readonly struct`, реализующий `ICriticalNotifyCompletion`. Обычно вы не взаимодействуете с ним напрямую.

```csharp
public readonly struct Awaiter : ICriticalNotifyCompletion
{
    public bool IsCompleted { get; }
    public T GetResult();
    public void OnCompleted(Action continuation);
    public void UnsafeOnCompleted(Action continuation);
}
```

`UnsafeOnCompleted` — это путь, используемый `AsyncValkarnTaskMethodBuilder<T>`. Метка «unsafe» означает, что `ExecutionContext` не захватывается — это намеренно для Unity, где нет `SynchronizationContext`.

Когда `IsCompleted` истинно, вызов `GetResult()` читает встроенное поле `result` (для синхронных задач) или вызывается через интерфейс источника (для асинхронных задач). Оба пути имеют атрибут `[MethodImpl(MethodImplOptions.AggressiveInlining)]`.

---

## Методы расширения для `ValkarnTask<T>`

### `AsResult<T>()`

```csharp
public static ValkarnTask<Result<T>> AsResult<T>(this ValkarnTask<T> task)
```

Оборачивает `ValkarnTask<T>` в `ValkarnTask<Result<T>>`, перехватывая любое исключение или отмену и кодируя их в значение `Result<T>`. Это позволяет избежать try/catch на месте вызова.

```csharp
Result<string> result = await FetchDataAsync(url).AsResult();

if (result.IsSuccess)
    Process(result.Value);
else if (result.IsCanceled)
    Debug.Log("Canceled");
else
    Debug.LogError(result.Exception);
```

**Синхронный быстрый путь:** если исходная задача уже синхронно завершена, `AsResult` возвращает синхронно без задействования асинхронного механизма.

---

## `Promise<T>` — источник ручного завершения

`ValkarnTask.Promise<T>` — это выделяемый в куче источник ручного завершения для случаев, когда нужно контролировать, когда завершается `ValkarnTask<T>`, и время жизни не ограничено одной асинхронной операцией.

```csharp
public class Promise<T>
{
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

```csharp
// Обёртка над API на основе коллбэков
var promise = new ValkarnTask.Promise<string>();

SomeCallbackApi.OnComplete += value => promise.TrySetResult(value);
SomeCallbackApi.OnError   += ex    => promise.TrySetException(ex);

string result = await promise.Task;
```

В отличие от `PooledPromise<T>`, `Promise<T>` не пулируется. Он использует финализатор для обнаружения и сообщения о необнаруженных исключениях, если задача даёт ошибку и вызывающий никогда не ожидает её.

Для высокочастотных паттернов (циклы producer/consumer, операции каждый кадр) предпочтительнее `ValkarnTask.PooledPromise<T>`, который автоматически возвращается в пул после вызова `GetResult`.

---

## `PooledPromise<T>` — пулируемый источник ручного завершения

```csharp
public sealed class PooledPromise<T> : ValkarnTask.ISource<T>, IPoolNode<PooledPromise<T>>
{
    public static PooledPromise<T> Create(out uint token);
    public static PooledPromise<T> CreateCompleted(T result, out uint token);
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

После вызова `GetResult` на поддерживающей задаче promise сбрасывает свой `ValkarnTaskCompletionCore<T>` и возвращает себя в пул. Защита от двойного возврата гарантирует, что это произойдёт не более одного раза, даже если `GetResult` вызывается параллельно.

```csharp
// Паттерн: создать ValkarnTask<T>, завершающийся по готовности
var promise = ValkarnTask.PooledPromise<int>.Create(out uint token);
ValkarnTask<int> task = promise.Task;

// Запустить работу асинхронно
ThreadPool.QueueUserWorkItem(_ =>
{
    int result = DoWork();
    promise.TrySetResult(result);
});

// Потребитель ожидает; при завершении promise автоматически возвращается в пул
int value = await task;
```

---

## Сводка способов получить `ValkarnTask<T>`

| Метод | Когда использовать |
|-------|-------------------|
| `return value` внутри `async ValkarnTask<T>` | Обычные async-методы |
| `ValkarnTask.FromResult(value)` | Синхронные быстрые возвраты |
| `ValkarnTask.FromException<T>(ex)` | Предварительно ошибочные задачи |
| `ValkarnTask.FromCanceled<T>(ct)` | Предварительно отменённые задачи |
| `ValkarnTask.Run<T>(Func<T>, ...)` | Выгрузка в пул потоков |
| `ValkarnTask.Run<T>(Func<ValkarnTask<T>>, ...)` | Асинхронная работа в пуле потоков |
| `ValkarnTask.WhenAll<T1,T2>(t1, t2)` | Ожидать две типизированные задачи, получить кортеж |
| `ValkarnTask.WhenAll<T>(IEnumerable<...>)` | Ожидать N типизированных задач, получить массив |
| `ValkarnTask.WhenAny<T>(t1, t2)` | Первая из двух типизированных задач |
| `ValkarnTask.WhenAny<T>(IEnumerable<...>)` | Первая из N типизированных задач |
| `task.AsResult<T>()` | Безопасная обёртка для исключений |
| `new ValkarnTask.Promise<T>()` → `.Task` | Долгоживущее ручное завершение |
| `ValkarnTask.PooledPromise<T>.Create(...)` → `.Task` | Высокочастотное ручное завершение |
