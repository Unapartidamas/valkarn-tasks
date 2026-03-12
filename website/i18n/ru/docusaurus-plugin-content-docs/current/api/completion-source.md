---
sidebar_position: 3
title: Источники завершения
---

# ValkarnTaskCompletionSource

`ValkarnTaskCompletionSource<T>` и необобщённый `ValkarnTaskCompletionSource` дают вам ручной контроль над `ValkarnTask`. Это асинхронный эквивалент самостоятельного написания результата — вы держите объект-источник, раздаёте его `.Task` вызывающим, а затем разрешаете, портите или отменяете его из отдельного места вызова.

Это эквивалент Valkarn Tasks для `TaskCompletionSource<T>` из BCL, но поддерживаемый `ValkarnTask.Promise<T>` для поддержания модели аллокаций в соответствии с остальной библиотекой.

---

## ValkarnTaskCompletionSource&lt;T&gt;

```csharp
public class ValkarnTaskCompletionSource<T>
{
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled();
}
```

### Task

```csharp
public ValkarnTask<T> Task { get; }
```

Задача, которую будет наблюдать ожидающий код. Распространяйте её всем вызывающим, которым нужно ждать результата. Несколько вызывающих могут одновременно `await` одну и ту же задачу.

### TrySetResult

```csharp
public bool TrySetResult(T result);
```

Успешно завершает задачу с указанным значением. Все ожидающие продолжения возобновляются. Возвращает `true`, если завершение принято; возвращает `false`, если задача уже была завершена (любым предыдущим вызовом `TrySet*`). Никогда не бросает исключений.

### TrySetException

```csharp
public bool TrySetException(Exception ex);
```

Делает задачу ошибочной с указанным исключением. Ожидающий код получит исключение при вызове `await`. Бросает `ArgumentNullException`, если `ex` равен `null`. Возвращает `true`, если принято, `false`, если уже завершено.

### TrySetCanceled

```csharp
public bool TrySetCanceled();
```

Отменяет задачу. Ожидающий код получит `OperationCanceledException`. Возвращает `true`, если принято, `false`, если уже завершено.

---

## Необобщённый ValkarnTaskCompletionSource

В публичном API нет отдельного необобщённого класса `ValkarnTaskCompletionSource`. Для задач с void-возвратом при ручном управлении используйте `ValkarnTask.Promise` напрямую:

```csharp
var promise = new ValkarnTask.Promise();
ValkarnTask task = promise.Task;

promise.TrySetResult();    // завершить
promise.TrySetException(ex);
promise.TrySetCanceled();
```

`ValkarnTask.Promise` предоставляет ту же поверхность `TrySet*` и ту же защиту от двойного завершения, но создаёт необобщённый `ValkarnTask`, а не `ValkarnTask<T>`.

---

## Защита от двойного завершения

Все методы `TrySet*` безопасны для вызова из любого потока в любое время, включая параллельный. Первый вызов, выигрывающий compare-and-swap на внутреннем конечном автомате, завершается успешно; каждый последующий вызов возвращает `false` и не имеет никакого эффекта. Это означает:

- Двукратный вызов `TrySetResult` ничего не делает при втором вызове.
- Вызов `TrySetResult` после `TrySetException` ничего не делает.
- Два потока, гонящихся за завершением одного источника одновременно, безопасны — один побеждает, второй молча игнорируется.

Если вам нужно знать, кто «выиграл», проверьте возвращаемое значение. Если вам всё равно (сигнал fire-and-forget), вы можете безопасно его игнорировать.

```csharp
// Безопасная гонка — только один из этих вызовов фактически завершит задачу
_ = source.TrySetResult(value);
_ = source.TrySetCanceled();
```

---

## Распространённые паттерны

### Мост для API на основе коллбэков

Многие API Unity и платформы передают результаты через коллбэки, а не async/await. `ValkarnTaskCompletionSource<T>` позволяет аккуратно обернуть их.

```csharp
public ValkarnTask<Texture2D> LoadTextureAsync(string url)
{
    var tcs = new ValkarnTaskCompletionSource<Texture2D>();

    StartCoroutine(LoadCoroutine(url, tcs));

    return tcs.Task;
}

IEnumerator LoadCoroutine(string url, ValkarnTaskCompletionSource<Texture2D> tcs)
{
    var request = UnityWebRequestTexture.GetTexture(url);
    yield return request.SendWebRequest();

    if (request.result == UnityWebRequest.Result.Success)
        tcs.TrySetResult(DownloadHandlerTexture.GetContent(request));
    else
        tcs.TrySetException(new Exception(request.error));
}
```

Теперь вызывающий может просто `await` возвращённую задачу:

```csharp
Texture2D tex = await LoadTextureAsync("https://example.com/image.png");
```

### Одноразовый сигнал (асинхронный шлюз)

Используйте `ValkarnTask.Promise`, когда вам нужен сигнал, срабатывающий однажды и разблокирующий любое количество ждущих. Это аналогично `ManualResetEventSlim`, но нативно асинхронный.

```csharp
public class AsyncGate
{
    readonly ValkarnTask.Promise _promise = new();

    // Любое количество вызывающих может это ожидать
    public ValkarnTask WaitAsync() => _promise.Task;

    // Вызвать один раз для разблокировки всех
    public void Open() => _promise.TrySetResult();
}

// Использование
var gate = new AsyncGate();

// Несколько систем независимо ожидают шлюз
async ValkarnTask SystemAAsync()
{
    await gate.WaitAsync();
    // продолжить после открытия шлюза
}

async ValkarnTask SystemBAsync()
{
    await gate.WaitAsync();
    // продолжает одновременно с SystemA
}

// Где-то в другом месте — открываем шлюз
gate.Open();
```

После вызова `TrySetResult()` все текущие и будущие вызовы `await` на задаче завершаются немедленно (синхронно, если задача уже готова к моменту их выполнения).

### Обёртка стороннего async-операции с поддержкой отмены

```csharp
public ValkarnTask<Result> RunWithTimeoutAsync(
    Func<ValkarnTask<Result>> operation,
    float timeoutSeconds,
    CancellationToken ct)
{
    var tcs = new ValkarnTaskCompletionSource<Result>();

    RunCoreAsync(tcs, operation, timeoutSeconds, ct).Forget();

    return tcs.Task;
}

async ValkarnTask RunCoreAsync(
    ValkarnTaskCompletionSource<Result> tcs,
    Func<ValkarnTask<Result>> operation,
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

### Шлюз отложенной инициализации

Распространённый паттерн Unity — предоставлять задачу «готовности», которую компоненты могут ожидать независимо от того, началась инициализация или нет.

```csharp
public class ServiceBus : MonoBehaviour
{
    readonly ValkarnTask.Promise _readyPromise = new();

    // Любой может ожидать это в любой момент — до или после Initialize()
    public ValkarnTask Ready => _readyPromise.Task;

    async void Start()
    {
        await LoadConfigAsync();
        await ConnectAsync();
        _readyPromise.TrySetResult();  // разблокирует всех ждущих
    }
}

// В любом другом компоненте
async ValkarnTask OnEnableAsync()
{
    await ServiceBus.Instance.Ready;  // ждёт если не готово, возвращается мгновенно если уже готово
    DoWork();
}
```

---

## Взаимосвязь с ValkarnTask.Promise

`ValkarnTaskCompletionSource<T>` — это тонкая публичная обёртка вокруг `ValkarnTask.Promise<T>`. Оба предоставляют одинаковую функциональность. Разница в соглашении об именовании:

| Тип | Возвращает | Типичное использование |
|-----|-----------|----------------------|
| `ValkarnTaskCompletionSource<T>` | `ValkarnTask<T>` | Публичный API, отражает стиль BCL `TaskCompletionSource<T>` |
| `ValkarnTask.Promise<T>` | `ValkarnTask<T>` | Внутреннее использование, немного более прямой |
| `ValkarnTask.Promise` | `ValkarnTask` | Void-сигналы (шлюзы, события) |

Все три поддерживают свою задачу с `ValkarnTaskCompletionCore<T>` — внутренней структурой на основе конечного автомата, обрабатывающей потокобезопасность и гонку continuation/result с использованием двухфазного протокола на основе CAS.
