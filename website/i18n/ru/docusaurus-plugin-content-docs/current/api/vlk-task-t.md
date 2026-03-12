---
sidebar_position: 2
title: VlkTask<T>
---

# `VlkTask<T>`

`VlkTask<T>` — это асинхронный тип задачи с возвращаемым значением в Valkarn Tasks. Это `readonly struct`, несущий либо встроенный результат (при синхронном завершении), либо ссылку на пулируемый объект-источник (при асинхронном завершении).

**Пространство имён:** `UnaPartidaMas.Valkarn.Tasks`

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncVlkTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct VlkTask<T>
```

Для `T` нет обобщённых ограничений. Любой тип — тип-значение, ссылочный тип, структура или класс — допустим.

---

## Создание экземпляров

### Синхронно завершённые задачи

Эти фабричные методы возвращают `VlkTask<T>` без объекта-источника. Ноль аллокаций.

#### `VlkTask.FromResult<T>(T value)`

Возвращает завершённый `VlkTask<T>`, несущий `value` встроенно. Объявлен как статический метод на необобщённом типе `VlkTask`.

```csharp
public static VlkTask<T> FromResult<T>(T value)
```

```csharp
VlkTask<int> task = VlkTask.FromResult(42);
VlkTask<string> name = VlkTask.FromResult("Valkarn");
VlkTask<Vector3> pos = VlkTask.FromResult(transform.position);
```

Возвращаемая структура имеет `source == null`. Её ожидание не приводит к аллокации продолжения — компилятор немедленно видит `IsCompleted == true`.

#### `VlkTask.FromException<T>(Exception exception)`

Возвращает ошибочный `VlkTask<T>`. Его ожидание повторно бросит исключение с сохранённым оригинальным стеком вызовов через `ExceptionDispatchInfo`.

```csharp
public static VlkTask<T> FromException<T>(Exception exception)
```

```csharp
VlkTask<Texture2D> LoadTexture(string path)
{
    if (string.IsNullOrEmpty(path))
        return VlkTask.FromException<Texture2D>(
            new ArgumentException("Path must not be empty.", nameof(path)));

    return LoadTextureAsync(path);
}
```

#### `VlkTask.FromCanceled<T>(CancellationToken ct = default)`

Возвращает отменённый `VlkTask<T>`. Его ожидание бросит `OperationCanceledException`.

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

### Через `async`-методы

Любой `async`-метод, объявленный для возврата `VlkTask<T>`, автоматически использует `AsyncVlkTaskMethodBuilder<TResult>`:

```csharp
async VlkTask<int> ComputeAsync()
{
    await VlkTask.Yield();
    return 42;
}
```

Компилятор генерирует конечный автомат. Если метод завершается синхронно (никогда не приостанавливается), `AsyncVlkTaskMethodBuilder<T>.Task` возвращает `new VlkTask<T>(result)` с `source == null` — ноль аллокаций.

---

## Выполнение работы в пуле потоков

Эти методы запускают делегат в пуле потоков .NET и возвращают результат в главный поток (при указанном `PlayerLoopTiming`). Это удобные обёртки над более длинными вариантами `RunOnThreadPool`.

#### `VlkTask.Run<T>(Func<T> func, PlayerLoopTiming timing, CancellationToken ct)`

Запускает синхронный `Func<T>` в пуле потоков.

```csharp
public static VlkTask<T> Run<T>(
    Func<T> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
// Вычислить в пуле потоков, результат возвращается в главный поток при следующем Update
int hash = await VlkTask.Run(() => ComputeExpensiveHash(data));
```

#### `VlkTask.Run<T>(Func<VlkTask<T>> func, PlayerLoopTiming timing, CancellationToken ct)`

Запускает асинхронный `Func<VlkTask<T>>` в пуле потоков. Используйте, когда работа сама является асинхронной (например, файловый ввод-вывод).

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
public VlkTask.Status GetStatus()
```

Возвращает текущий `VlkTask.Status`. Возможные значения: `Pending`, `Succeeded`, `Faulted`, `Canceled`. Для `source == null` всегда возвращает `Succeeded`.

### `GetAwaiter()`

```csharp
public Awaiter GetAwaiter()
```

Возвращает структуру `Awaiter`. Используется компилятором для реализации `await`. Вы также можете вызывать его напрямую для синхронного получения результата (безопасно только когда `IsCompleted` истинно).

```csharp
VlkTask<int> task = VlkTask.FromResult(10);
int value = task.GetAwaiter().GetResult(); // безопасно — синхронно завершено
```

Вызов `GetResult()` на ожидающей задаче бросает `InvalidOperationException`.

### `AsNonGeneric()`

```csharp
public VlkTask AsNonGeneric()
```

Преобразует этот `VlkTask<T>` в необобщённый `VlkTask`, отбрасывая тип результата. Результирующая задача разделяет тот же базовый источник и токен, поэтому завершается одновременно.

```csharp
VlkTask<int> typedTask = ComputeAsync();
VlkTask voidTask = typedTask.AsNonGeneric();
await voidTask;   // ждёт завершения, игнорирует результат
```

Полезно при передаче задач смешанных типов в комбинаторы или когда вас интересует только момент завершения, а не значение.

---

## Комбинаторы, возвращающие `VlkTask<T>`

### `WhenAll` — типизированная перегрузка с двумя задачами

```csharp
public static VlkTask<(T1, T2)> WhenAll<T1, T2>(
    VlkTask<T1> task1, VlkTask<T2> task2)
```

Запускает обе задачи параллельно и возвращает кортеж результатов. Если какая-либо задача даёт ошибку или отменяется, первое исключение побеждает, а ошибка другой сообщается через `VlkTask.UnobservedException`.

**Деструктуризация кортежа** естественно работает с C#-деструктуризацией:

```csharp
var (profile, inventory) = await VlkTask.WhenAll(
    FetchProfileAsync(userId),
    FetchInventoryAsync(userId)
);
```

**Быстрый путь без аллокаций:** если обе задачи синхронно завершены в момент вызова, пулируемый объект не создаётся и результирующий кортеж возвращается встроенно.

### `WhenAll` — типизированная перегрузка с коллекцией

```csharp
public static VlkTask<T[]> WhenAll<T>(IEnumerable<VlkTask<T>> tasks)
```

Ожидает все задачи в коллекции параллельно и возвращает `T[]` в порядке индексов.

```csharp
var urls = new[] { "https://a.com", "https://b.com", "https://c.com" };
VlkTask<string>[] downloads = urls.Select(u => DownloadAsync(u)).ToArray();
string[] results = await VlkTask.WhenAll(downloads);
```

Если коллекция пуста, возвращает `VlkTask.FromResult(Array.Empty<T>())` — ноль аллокаций. Если все задачи уже синхронно завершены, массив результатов строится встроенно без создания promise комбинатора.

### `WhenAny` — типизированная перегрузка с двумя задачами

```csharp
public static VlkTask<(int winnerIndex, T result)> WhenAny<T>(
    VlkTask<T> task1, VlkTask<T> task2)
```

Возвращает, как только завершится любая задача. Результирующий кортеж содержит 0-основанный индекс победителя и его значение. Проигравшие задачи продолжают выполняться; их ошибки (если есть) сообщаются через `VlkTask.UnobservedException`. Отмены проигравших намеренно не сообщаются.

```csharp
var (winnerIndex, result) = await VlkTask.WhenAny(
    FetchFromCacheAsync(key),
    FetchFromNetworkAsync(key)
);

if (winnerIndex == 0)
    Debug.Log("Cache won");
```

### `WhenAny` — типизированная перегрузка с коллекцией

```csharp
public static VlkTask<(int winnerIndex, T result)> WhenAny<T>(
    IEnumerable<VlkTask<T>> tasks)
```

Та же семантика, что и перегрузка с двумя задачами, расширенная до произвольного числа задач. Требует хотя бы одну задачу; бросает `ArgumentException` для пустых коллекций.

```csharp
var tasks = servers.Select(s => s.FetchAsync(query)).ToArray();
var (winnerIndex, data) = await VlkTask.WhenAny(tasks);
Debug.Log($"Server {winnerIndex} responded first");
```

---

## Вспомогательные фабричные методы

### `VlkTask.Create<T>(Func<VlkTask<T>> factory)`

Вызывает делегат-фабрику и ожидает результирующую задачу. Полезно, когда нужно отложить создание асинхронной операции.

```csharp
public static async VlkTask<T> Create<T>(Func<VlkTask<T>> factory)
```

```csharp
var result = await VlkTask.Create(() => LoadLevelDataAsync(levelId));
```

---

## Структура `Awaiter` (вложенная)

`VlkTask<T>.Awaiter` — это awaiter, обращённый к компилятору. Это `readonly struct`, реализующий `ICriticalNotifyCompletion`. Обычно вы не взаимодействуете с ним напрямую.

```csharp
public readonly struct Awaiter : ICriticalNotifyCompletion
{
    public bool IsCompleted { get; }
    public T GetResult();
    public void OnCompleted(Action continuation);
    public void UnsafeOnCompleted(Action continuation);
}
```

`UnsafeOnCompleted` — это путь, используемый `AsyncVlkTaskMethodBuilder<T>`. Метка «unsafe» означает, что `ExecutionContext` не захватывается — это намеренно для Unity, где нет `SynchronizationContext`.

Когда `IsCompleted` истинно, вызов `GetResult()` читает встроенное поле `result` (для синхронных задач) или вызывается через интерфейс источника (для асинхронных задач). Оба пути имеют атрибут `[MethodImpl(MethodImplOptions.AggressiveInlining)]`.

---

## Методы расширения для `VlkTask<T>`

### `AsResult<T>()`

```csharp
public static VlkTask<Result<T>> AsResult<T>(this VlkTask<T> task)
```

Оборачивает `VlkTask<T>` в `VlkTask<Result<T>>`, перехватывая любое исключение или отмену и кодируя их в значение `Result<T>`. Это позволяет избежать try/catch на месте вызова.

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

`VlkTask.Promise<T>` — это выделяемый в куче источник ручного завершения для случаев, когда нужно контролировать, когда завершается `VlkTask<T>`, и время жизни не ограничено одной асинхронной операцией.

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
// Обёртка над API на основе коллбэков
var promise = new VlkTask.Promise<string>();

SomeCallbackApi.OnComplete += value => promise.TrySetResult(value);
SomeCallbackApi.OnError   += ex    => promise.TrySetException(ex);

string result = await promise.Task;
```

В отличие от `PooledPromise<T>`, `Promise<T>` не пулируется. Он использует финализатор для обнаружения и сообщения о необнаруженных исключениях, если задача даёт ошибку и вызывающий никогда не ожидает её.

Для высокочастотных паттернов (циклы producer/consumer, операции каждый кадр) предпочтительнее `VlkTask.PooledPromise<T>`, который автоматически возвращается в пул после вызова `GetResult`.

---

## `PooledPromise<T>` — пулируемый источник ручного завершения

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

После вызова `GetResult` на поддерживающей задаче promise сбрасывает свой `VlkTaskCompletionCore<T>` и возвращает себя в пул. Защита от двойного возврата гарантирует, что это произойдёт не более одного раза, даже если `GetResult` вызывается параллельно.

```csharp
// Паттерн: создать VlkTask<T>, завершающийся по готовности
var promise = VlkTask.PooledPromise<int>.Create(out uint token);
VlkTask<int> task = promise.Task;

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

## Сводка способов получить `VlkTask<T>`

| Метод | Когда использовать |
|-------|-------------------|
| `return value` внутри `async VlkTask<T>` | Обычные async-методы |
| `VlkTask.FromResult(value)` | Синхронные быстрые возвраты |
| `VlkTask.FromException<T>(ex)` | Предварительно ошибочные задачи |
| `VlkTask.FromCanceled<T>(ct)` | Предварительно отменённые задачи |
| `VlkTask.Run<T>(Func<T>, ...)` | Выгрузка в пул потоков |
| `VlkTask.Run<T>(Func<VlkTask<T>>, ...)` | Асинхронная работа в пуле потоков |
| `VlkTask.WhenAll<T1,T2>(t1, t2)` | Ожидать две типизированные задачи, получить кортеж |
| `VlkTask.WhenAll<T>(IEnumerable<...>)` | Ожидать N типизированных задач, получить массив |
| `VlkTask.WhenAny<T>(t1, t2)` | Первая из двух типизированных задач |
| `VlkTask.WhenAny<T>(IEnumerable<...>)` | Первая из N типизированных задач |
| `task.AsResult<T>()` | Безопасная обёртка для исключений |
| `new VlkTask.Promise<T>()` → `.Task` | Долгоживущее ручное завершение |
| `VlkTask.PooledPromise<T>.Create(...)` → `.Task` | Высокочастотное ручное завершение |
