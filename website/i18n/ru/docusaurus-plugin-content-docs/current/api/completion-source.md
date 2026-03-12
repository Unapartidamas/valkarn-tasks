---
sidebar_position: 3
title: Источники завершения
---

# VlkTaskCompletionSource

`VlkTaskCompletionSource<T>` и необобщённый `VlkTaskCompletionSource` дают вам ручной контроль над `VlkTask`. Это асинхронный эквивалент самостоятельного написания результата — вы держите объект-источник, раздаёте его `.Task` вызывающим, а затем разрешаете, портите или отменяете его из отдельного места вызова.

Это эквивалент Valkarn Tasks для `TaskCompletionSource<T>` из BCL, но поддерживаемый `VlkTask.Promise<T>` для поддержания модели аллокаций в соответствии с остальной библиотекой.

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

## Необобщённый VlkTaskCompletionSource

В публичном API нет отдельного необобщённого класса `VlkTaskCompletionSource`. Для задач с void-возвратом при ручном управлении используйте `VlkTask.Promise` напрямую:

```csharp
var promise = new VlkTask.Promise();
VlkTask task = promise.Task;

promise.TrySetResult();    // завершить
promise.TrySetException(ex);
promise.TrySetCanceled();
```

`VlkTask.Promise` предоставляет ту же поверхность `TrySet*` и ту же защиту от двойного завершения, но создаёт необобщённый `VlkTask`, а не `VlkTask<T>`.

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

Многие API Unity и платформы передают результаты через коллбэки, а не async/await. `VlkTaskCompletionSource<T>` позволяет аккуратно обернуть их.

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

Теперь вызывающий может просто `await` возвращённую задачу:

```csharp
Texture2D tex = await LoadTextureAsync("https://example.com/image.png");
```

### Одноразовый сигнал (асинхронный шлюз)

Используйте `VlkTask.Promise`, когда вам нужен сигнал, срабатывающий однажды и разблокирующий любое количество ждущих. Это аналогично `ManualResetEventSlim`, но нативно асинхронный.

```csharp
public class AsyncGate
{
    readonly VlkTask.Promise _promise = new();

    // Любое количество вызывающих может это ожидать
    public VlkTask WaitAsync() => _promise.Task;

    // Вызвать один раз для разблокировки всех
    public void Open() => _promise.TrySetResult();
}

// Использование
var gate = new AsyncGate();

// Несколько систем независимо ожидают шлюз
async VlkTask SystemAAsync()
{
    await gate.WaitAsync();
    // продолжить после открытия шлюза
}

async VlkTask SystemBAsync()
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

### Шлюз отложенной инициализации

Распространённый паттерн Unity — предоставлять задачу «готовности», которую компоненты могут ожидать независимо от того, началась инициализация или нет.

```csharp
public class ServiceBus : MonoBehaviour
{
    readonly VlkTask.Promise _readyPromise = new();

    // Любой может ожидать это в любой момент — до или после Initialize()
    public VlkTask Ready => _readyPromise.Task;

    async void Start()
    {
        await LoadConfigAsync();
        await ConnectAsync();
        _readyPromise.TrySetResult();  // разблокирует всех ждущих
    }
}

// В любом другом компоненте
async VlkTask OnEnableAsync()
{
    await ServiceBus.Instance.Ready;  // ждёт если не готово, возвращается мгновенно если уже готово
    DoWork();
}
```

---

## Взаимосвязь с VlkTask.Promise

`VlkTaskCompletionSource<T>` — это тонкая публичная обёртка вокруг `VlkTask.Promise<T>`. Оба предоставляют одинаковую функциональность. Разница в соглашении об именовании:

| Тип | Возвращает | Типичное использование |
|-----|-----------|----------------------|
| `VlkTaskCompletionSource<T>` | `VlkTask<T>` | Публичный API, отражает стиль BCL `TaskCompletionSource<T>` |
| `VlkTask.Promise<T>` | `VlkTask<T>` | Внутреннее использование, немного более прямой |
| `VlkTask.Promise` | `VlkTask` | Void-сигналы (шлюзы, события) |

Все три поддерживают свою задачу с `VlkTaskCompletionCore<T>` — внутренней структурой на основе конечного автомата, обрабатывающей потокобезопасность и гонку continuation/result с использованием двухфазного протокола на основе CAS.
