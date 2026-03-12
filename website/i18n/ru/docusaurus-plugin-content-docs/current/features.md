---
sidebar_position: 4
title: Возможности
---

# Возможности

Полный справочник возможностей Valkarn Tasks.

---

## Основной асинхронный примитив

### Структура ValkarnTask

Тип возвращаемого значения асинхронного метода с нулевым выделением памяти, основанный на структуре, который заменяет как `UniTask`, так и Unity `Awaitable`.

```csharp
async ValkarnTask LoadLevel() { ... }
async ValkarnTask<int> CountEnemies() { ... }
```

- **Синхронный быстрый путь с нулевым выделением** — если метод завершается без единой приостановки, выделений в куче не происходит.
- **Пулированный асинхронный путь** — если метод приостанавливается, исполнитель машины состояний берётся из ограниченного, сжимаемого пула. Ноль боксинга, ограниченные пулы с автоматической обрезкой.
- **Пулирование с приоритетом IL2CPP** — операции пула в основном потоке используют ноль атомарных операций. IL2CPP штрафует `Volatile` в 9.2 раза и `Interlocked` в 2.9 раза относительно Mono; Valkarn избегает обоих на горячем пути.
- **Безопасность поколенческих токенов** — счётчик поколений типа `uint` для каждого слота пула. 4 294 967 296 циклов на слот до коллизии — на практике невозможно. (UniTask использует токен `short`: коллизия примерно через 18 минут активной асинхронной работы.)

### Result\<T\> — обработка ошибок без исключений

```csharp
var result = await loadTask.AsResult();

if (result.Succeeded)    Use(result.Value);
else if (result.IsCanceled) ShowRetry();
else                         Debug.LogError(result.Error);
```

`Result<T>` и `Result` — значения `readonly struct`, представляющие результат задачи без бросания исключений. Оба поддерживают неявное преобразование в `bool` (true при успехе).

### Источники с ручным завершением

```csharp
var promise = new ValkarnTask.Promise<string>();
promise.TrySetResult("done");
string value = await promise.Task;
```

Поддерживает `TrySetResult`, `TrySetException` и `TrySetCanceled`. Отчёт о необработанных исключениях на основе финализатора гарантирует, что ошибки никогда не теряются молча.

### Пулированные источники завершения

Автоматически сбрасываемые пулированные варианты для повторяющихся паттернов (используются внутри каналами и комбинаторами):

```csharp
var source = ValkarnTask.PooledPromise<int>.Create(out uint token);
source.TrySetResult(42);
int value = await source.Task; // source автоматически возвращается в пул
```

---

## Мост Awaitable

Прозрачная интероперабельность с Unity `Awaitable` — без ручного преобразования:

```csharp
async ValkarnTask LoadGame()
{
    await SceneManager.LoadSceneAsync("Level2"); // Unity Awaitable — работает напрямую
    await Resources.LoadAsync<Texture2D>("hero"); // Unity Awaitable — работает напрямую
    await ValkarnTask.Delay(1000);                // Valkarn нативный
}
```

Генератор кода обнаруживает await для `Awaitable` и автоматически генерирует адаптер.

---

## Отмена по жизненному циклу

### Автоматически (через генерацию кода)

Пометьте класс как `partial` — генератор кода сделает остальное:

```csharp
public partial class EnemySpawner : MonoBehaviour
{
    async ValkarnTask Start()
    {
        while (true)
        {
            await ValkarnTask.Delay(3000);
            SpawnWave();
            // Автоматически отменяется при уничтожении этого GameObject.
        }
    }
}
```

- **MonoBehaviour** — привязан к `destroyCancellationToken`
- **ScriptableObject** — привязан к времени жизни приложения
- **Обычный класс** — нет автоматической привязки (требуется ручной токен)

### Отказ от автоотмены

```csharp
[NoAutoCancel]
async ValkarnTask BackgroundSync()
{
    await SyncToServer(); // не отменяется автоматически при уничтожении
}
```

### Ручное переопределение CancellationToken

Передача явного `CancellationToken` переопределяет автоматически внедрённый токен жизненного цикла:

```csharp
async ValkarnTask DoWork(CancellationToken ct)
{
    await ValkarnTask.Delay(1000, ct);
}
```

### Нет отмены родственных задач

Задачи никогда не отменяют родственные задачи. `WhenAll` ждёт ВСЕ задачи; `WhenAny` возвращает первый результат, но проигравшие задачи продолжают выполняться. Это предотвращает повреждение данных, когда задачи имеют побочные эффекты.

---

## Критические секции

Для операций, которые нельзя прерывать отменой жизненного цикла:

```csharp
async ValkarnTask SaveProgress()
{
    var data = await LoadPlayerData();        // можно отменить

    await using (ValkarnTask.Critical())
    {
        await db.Insert(data);               // НЕ отменяется, даже если GO уничтожен
        await db.Commit();
    }                                        // отложенная отмена применяется здесь

    await SendNotification();                // снова можно отменить
}
```

Внутри критической секции отмена **откладывается** — а не игнорируется. Когда секция заканчивается, отложенная отмена применяется.

---

## Комбинаторы

### WhenAll (типизированный)

```csharp
// Прямой — бросает исключение, если любая задача завершилась с ошибкой
var (enemies, map) = await ValkarnTask.WhenAll(LoadEnemies(), LoadMap());

// Безопасный — оберните с AsResult() для поведения без бросания
var (a, b) = await ValkarnTask.WhenAll(
    LoadEnemies().AsResult(), LoadMap().AsResult());
```

Синхронный быстрый путь с нулевым выделением, когда все задачи уже завершены. Перегрузка `IEnumerable<ValkarnTask<T>>` использует `ArrayPool<T>` для внутренних массивов.

### WhenAll (void)

```csharp
await ValkarnTask.WhenAll(SaveA(), SaveB(), SaveC());
await ValkarnTask.WhenAll(taskList); // IEnumerable<ValkarnTask>
```

### WhenAny

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(DownloadFromA(), DownloadFromB());
```

Возвращает первый завершённый результат. Проигравшие задачи продолжают выполняться естественным образом.

### Выстрелить и забыть

```csharp
SendAnalytics("event").Forget();

[FireAndForget]
async ValkarnTask SendAnalytics(string eventName) { ... }
// Вызывающие не нуждаются в .Forget() — предупреждение не генерируется
```

`.Forget()` направляет ошибки в `ValkarnTask.UnobservedException`. Никогда не проглатываются молча.

### AsNonGeneric

```csharp
ValkarnTask voidTask = typedTask.AsNonGeneric();
```

---

## Время и задержки

```csharp
await ValkarnTask.Delay(1000);                                    // миллисекунды
await ValkarnTask.Delay(TimeSpan.FromSeconds(2));                  // TimeSpan
await ValkarnTask.Delay(1000, DelayType.UnscaledDeltaTime);       // игнорировать timescale
await ValkarnTask.Delay(1000, DelayType.Realtime);                 // на основе Stopwatch

await ValkarnTask.Yield();                                         // следующий тик PlayerLoop
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);             // конкретный тайминг
await ValkarnTask.NextFrame();                                      // гарантированно следующий кадр
await ValkarnTask.DelayFrame(5);                                    // N кадров

await ValkarnTask.WaitUntil(() => player.IsReady);
await ValkarnTask.WaitWhile(() => isLoading);
```

---

## Переключение потоков

```csharp
async ValkarnTask ProcessData()
{
    var raw = await DownloadData();

    await ValkarnTask.SwitchToThreadPool();
    var processed = HeavyComputation(raw);  // фоновый поток

    await ValkarnTask.SwitchToMainThread();
    ApplyToGameObject(processed);           // основной поток
}
```

---

## 16 таймингов PlayerLoop

| Группа | Тайминги |
|-------|---------|
| Initialization | `Initialization`, `LastInitialization` |
| EarlyUpdate | `EarlyUpdate`, `LastEarlyUpdate` |
| FixedUpdate | `FixedUpdate`, `LastFixedUpdate` |
| PreUpdate | `PreUpdate`, `LastPreUpdate` |
| Update | `Update`, `LastUpdate` |
| PreLateUpdate | `PreLateUpdate`, `LastPreLateUpdate` |
| PostLateUpdate | `PostLateUpdate`, `LastPostLateUpdate` |
| TimeUpdate | `TimeUpdate`, `LastTimeUpdate` |

Все операции по умолчанию используют `PlayerLoopTiming.Update`, если не указано иное.

---

## Каналы

```csharp
// Неограниченный
var channel = ValkarnTask.Channel.CreateUnbounded<EnemySpawnRequest>();

// Ограниченный — обратное давление при заполнении
var channel = ValkarnTask.Channel.CreateBounded<LogEntry>(capacity: 100);

// Мульти-потребитель
var channel = ValkarnTask.Channel.CreateUnbounded<WorkItem>(multiConsumer: true);

// Производитель
await channel.Writer.WriteAsync(entry);
bool accepted = channel.Writer.TryWrite(entry);

// Потребитель
await foreach (var item in channel.Reader.ReadAllAsync())
    Process(item);

// Завершение
channel.Writer.Complete();
await channel.Reader.Completion;
```

---

## Детерминированное тестирование (TestClock)

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

Все зависящие от времени операции читают из `TimeProvider.Current`. В тестах замените его на `TestClock`. `AdvanceFrame()` симулирует один тик PlayerLoop.

---

## Мост Job System

```csharp
var job = new PathfindingJob { start = a, end = b, results = results };
await job.ScheduleAsync();
// results NativeArray доступен для чтения сразу после await

await job.ScheduleParallelAsync(dataCount, batchSize); // IJobParallelFor

// Отмена — завершает дескриптор задания перед отчётом об отмене (нет утечки задания)
await job.ScheduleAsync(cancellationToken);
```

---

## Диагностика времени компиляции

| Код | Серьёзность | Описание |
|------|----------|-------------|
| TT001 | Предупреждение | Двойной await / использование после освобождения `ValkarnTask` |
| TT002 | Ошибка | Результат `async ValkarnTask` используется как оператор-выражение — должен быть awaited или `.Forget()` |
| TT010 | Информация | Асинхронный метод в MonoBehaviour автоматически отменяется при Destroy |
| TT011 | Предупреждение | `WhenAll` содержит задачи с разным временем жизни |
| TT012 | Предупреждение | Асинхронный цикл без проверки отмены (потенциальный цикл-зомби) |
| TT013 | Предупреждение | `ValkarnTask` возвращён, но никогда не awaited и не отброшен явно |
| TT014 | Предупреждение | `[NoAutoCancel]` без ручного параметра `CancellationToken` |
| TT015 | Информация | Ожидание `Awaitable` внутри `async ValkarnTask` — генерируется адаптер моста |
| TT016 | Предупреждение | Асинхронный метод без выражения await |
| TT017 | Предупреждение | `[FireAndForget]` на `ValkarnTask<T>` — отбрасывает возвращаемое значение |

---

## Управление пулом

Каждый исполнитель асинхронного метода пулируется через `ValkarnPool<T>`:

- **Основной поток** — стек без блокировок, ноль атомарных операций
- **Фоновые потоки** — стек Treiber без блокировок с операциями CAS
- **Обрезка на основе кадров** — каждые 300 кадров (~5 с при 60 fps), лишние объекты постепенно освобождаются
- **Никогда не сжимается** ниже настраиваемого минимума (по умолчанию: 8)

Мониторинг во время выполнения:

```csharp
foreach (var (type, size, maxSize) in ValkarnTask.GetPoolInfo())
    Debug.Log($"{type}: {size}/{maxSize}");
```

---

## ValkarnTaskSettings

Настройка через `ScriptableObject` (`Assets > Create > Valkarn > Tasks > Task Settings`, поместить в `Resources/`):

| Настройка | По умолчанию | Описание |
|---------|---------|-------------|
| `DefaultMaxPoolSize` | 256 | Максимум элементов для каждого типа пула |
| `MinPoolSize` | 8 | Никогда не обрезать ниже этого |
| `TrimCheckInterval` | 300 | Кадров между проверками обрезки |
| `TrimHysteresisCount` | 2 | Последовательных проверок перед обрезкой |
| `TrimReleaseRatio` | 0.25 | Доля лишнего, освобождаемого за цикл |
| `EnableAutoCancel` | true | Автоматически привязывать задачи MonoBehaviour к destroyCancellationToken |
| `LogUnobservedCancellations` | false | Логировать необработанные отмены как предупреждения |
| `MaxExceptionLogsPerFrame` | 10 | Ограничение логов исключений за кадр |

---

## Обработка ошибок

```csharp
// Необработанные исключения — детерминированно срабатывают при возврате в пул
ValkarnTask.UnobservedException += ex =>
    Debug.LogError($"Unobserved: {ex}");
```

- `WhenAll` — бросает первое исключение, дополнительные направляет в `UnobservedException`
- `WhenAny` — исключение победителя бросается; ошибки проигравших идут в `UnobservedException`
- Отмена жизненного цикла — `OperationCanceledException` подавляется по умолчанию (настраивается)

### Фабричные методы

```csharp
ValkarnTask.CompletedTask          // void, ноль выделений
ValkarnTask.FromResult<T>(value)   // типизированный, ноль выделений
ValkarnTask.FromException(ex)      // с ошибкой
ValkarnTask.FromException<T>(ex)
ValkarnTask.FromCanceled()         // отменённый
ValkarnTask.FromCanceled<T>()
ValkarnTask.Never                  // никогда не завершается (страж для WhenAny)
```
