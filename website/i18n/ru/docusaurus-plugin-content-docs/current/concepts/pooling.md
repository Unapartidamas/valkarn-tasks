---
sidebar_position: 2
title: Пулирование объектов
---

# Пулирование объектов

Valkarn Tasks устраняет аллокации GC на распространённых асинхронных путях, пулируя объекты, которые стоят за каждым `ValkarnTask`. На этой странице объясняется архитектура пула — как хранятся объекты, как они приобретаются и возвращаются, и какие гарантии жизненного цикла предоставляет система.

---

## Обзор

Когда `async`-метод приостанавливается, библиотеке нужно место для хранения компилятор-сгенерированного конечного автомата и механизма завершения, на который может подписаться awaiter. В `System.Threading.Tasks` эту роль играет сам объект `Task` — одна аллокация в куче на вызов. В Valkarn Tasks эту роль играют пулируемые объекты, реализующие `ValkarnTask.ISource`.

Дизайн пула преследует три цели:

1. **Ноль атомарных операций на горячем пути главного потока.** Игровой цикл Unity по соглашению однопоточный. Аренда и возврат из главного потока должны быть простыми чтениями и записями.
2. **Безопасный кросс-поточный доступ.** Фоновые задачи, использующие `ValkarnTask.Run`, работают на потоках из пула. Пул должен корректно обрабатывать параллельную аренду/возврат.
3. **Ограниченный рост с адаптивной обрезкой.** Пулы не должны расти без ограничений после всплеска трафика, но и не должны сжиматься так агрессивно, чтобы постоянно перераспределяться.

---

## `ValkarnTaskPool<T>`

`ValkarnTaskPool<T>` — основной класс пула. Он `internal sealed` — вы не взаимодействуете с ним напрямую, но понимание его работы объясняет, куда уходят ваши аллокации.

```
ValkarnTaskPool<T>
  |
  +-- fastItem: T            (однослотовый кэш, только главный поток, простое чтение/запись)
  |
  +-- stackHead: T           (головной узел стека Трайбера, на основе CAS для кросс-поточной безопасности)
  +-- stackSize: int
  |
  +-- maxSize: int           (ограничен ValkarnTask.DefaultMaxPoolSize)
  +-- totalCreated: int      (отслеживает пожизненные аллокации для коэффициента обрезки)
```

### Быстрый слот (главный поток)

Поле `fastItem` — это один зарезервированный слот для самого последнего возвращённого объекта. На главном потоке аренда и возврат — это простые чтение и запись — без атомарных операций, без ожиданий. Это покрывает подавляющее большинство операций игрового цикла Unity.

```
Аренда (главный поток):
  fastItem != null  →  взять (fastItem = null), вернуть   [ноль атомарных]
  fastItem == null  →  перейти к стеку Трайбера

Возврат (главный поток):
  fastItem == null  →  fastItem = item                     [ноль атомарных]
  fastItem != null  →  перейти к стеку Трайбера
```

### Стек Трайбера (переполнение / фоновые потоки)

Когда быстрый слот занят (или вызывающий поток не является главным), пул использует lock-free стек Трайбера — классический интрузивный связный список, использующий compare-and-swap (CAS):

```
Аренда (любой поток):
  while (true):
    head = Volatile.Read(stackHead)
    if head == null: return null (пул пуст)
    next = head.NextNode
    if CAS(stackHead, next, head) == head: return head  // выиграли гонку
    spinner.SpinOnce()                                   // проиграли, повтор

Возврат (любой поток):
  if stackSize >= maxSize: return false (пул полон, удаляем элемент)
  while (true):
    head = Volatile.Read(stackHead)
    item.NextNode = head
    if CAS(stackHead, item, head) == head: stackSize++; return true
    spinner.SpinOnce()
```

Стек интрузивный: каждый пулируемый объект хранит свой указатель `NextNode`, поэтому внешний узел-обёртка не нужен. Это обеспечивается интерфейсом `IPoolNode<T>`.

### Маршрутизация по потокам

```csharp
internal static class ValkarnTaskPoolShared
{
    internal static volatile int MainThreadId;
}
```

Все экземпляры пулов разделяют один `MainThreadId`. Операции аренды/возврата проверяют `Thread.CurrentThread.ManagedThreadId == MainThreadId` для выбора правильного пути. Поле `volatile` обеспечивает видимость между потоками после публикации ID при запуске.

---

## `IPoolNode<T>`

Любой тип, участвующий в пуле, должен реализовывать этот интерфейс:

```csharp
internal interface IPoolNode<T> where T : class
{
    ref T NextNode { get; }
}
```

`ref T NextNode` возвращает ссылку на поле внутри объекта, хранящее указатель на следующий элемент. Пул пишет напрямую в это поле через ref, устраняя необходимость в отдельном узле-обёртке. Все пулируемые типы в библиотеке — runner'ы, promise'ы, комбинаторы — реализуют этот интерфейс, объявляя приватное поле и предоставляя к нему доступ:

```csharp
// Пример из PooledPromise<T>
PooledPromise<T> nextNode;
public ref PooledPromise<T> NextNode => ref nextNode;
```

---

## Жизненный цикл пула: приобретение, использование, возврат

Полный жизненный цикл пулируемого объекта:

```
Вызывающий вызывает async-метод
  |
  +--> AsyncValkarnTaskMethodBuilder.AwaitUnsafeOnCompleted()
        |
        +--> runner == null? ДА --> Pool.TryRent() ?? Pool.TrackNew(new Runner())
              |
              +--> runner сохраняется в builder'е; конечный автомат копируется в runner
              |
              +--> awaiter.UnsafeOnCompleted(runner.MoveNextAction)
              |
              [... асинхронная работа продолжается ...]
              |
              +--> runner.SetResult() / runner.SetException()
                    |
                    +--> ValkarnTaskCompletionCore.TrySetResult() --> продолжение вызвано
                          |
                          +--> awaiter вызывающего вызывает GetResult(token)
                                |
                                +--> core.GetResult(token) -- читает результат или повторно бросает
                                |
                                +--> TryReturn():
                                      stateMachine = default
                                      core.Reset()           // увеличивает поколение
                                      Pool.TryReturn(this)
```

Метод `TryReturn` всегда очищает конечный автомат перед вызовом `core.Reset()`. Порядок важен: `Reset()` увеличивает счётчик поколений, делая слот доступным для параллельных арендаторов. Если бы конечный автомат очищался после `Reset()`, арендатор на другом потоке мог бы получить слот и перезаписать его конечный автомат.

---

## `ValkarnTaskCompletionCore<TResult>`

`ValkarnTaskCompletionCore<TResult>` — это `internal struct`, встроенная в каждый пулируемый объект. Это фактический конечный автомат promise — отслеживающий состояние завершения, сохраняющий результаты и ошибки, и разрешающий гонку между `OnCompleted` (регистрация продолжения) и `TrySetResult` (сигнал завершения).

```
Поля:
  result:          TResult     -- значение успеха
  error:           object      -- ExceptionDispatchInfo или OperationCanceledException
  errorKind:       byte        -- 0=нет, 1=ошибка, 2=отмена(OCE), 3=отмена(EDI)
  generation:      int         -- монотонно возрастающий; приводится к uint для сравнения токенов
  completedCount:  int         -- 0=в ожидании, 1=захвачен, 2=завершён (двухфазная публикация)
  continuation:    Action<object>
  continuationState: object
  hasUnhandledError: bool
```

### Двухфазный протокол завершения

Завершение использует двухфазный CAS для безопасности на ARM64, где нужны пары store-release / load-acquire:

```
TrySetResult(value):
  Фаза 1: CAS(completedCount, 0 -> 1)   -- захват эксклюзивного владения
  Фаза 2: запись result
  Фаза 3: Volatile.Write(completedCount, 2)  -- публикация с семантикой release
  Фаза 4: InvokeContinuation()
```

Читатели используют `Volatile.Read(completedCount)` (семантика acquire) перед чтением результата, гарантируя видимость значения, записанного в Фазе 2.

### Разрешение гонки между `OnCompleted` и `TrySetResult`

Возможны три паттерна:

```
Паттерн A — OnCompleted первым:
  OnCompleted сохраняет продолжение через CAS(continuation, null -> cont)
  TrySetResult читает ненулевое продолжение -> вызывает его

Паттерн B — TrySetResult первым (синхронный быстрый путь):
  TrySetResult помещает ContinuationSentinel через CAS(continuation, null -> sentinel)
  OnCompleted читает sentinel -> немедленно вызывает продолжение встроенно

Паттерн C (параллельная гонка):
  C.1: OnCompleted выигрывает CAS -> TrySetResult читает его -> вызывает
  C.2: TrySetResult выигрывает CAS (помещает sentinel) -> OnCompleted обнаруживает sentinel -> вызывает встроенно
```

Sentinel — это статический объект `Action<object>`, используемый чисто как маркерное значение — он никогда не вызывается как делегат.

### Валидация токена и защита от ABA

Каждый вызов `GetStatus`, `GetResult` и `OnCompleted` проверяет токен `uint` относительно текущего `generation`. Когда `Reset()` вызывает `Interlocked.Increment(ref generation)`, любая выдающаяся структура `ValkarnTask`, держащая старый токен, получит `InvalidOperationException` вместо молчаливой работы с переработанным состоянием. 32-битный счётчик поколений, совершающий полный оборот (что требует ~4 миллиардов повторных использований одного слота), на практике считается невозможным.

### Сброс и сообщение о необнаруженных ошибках

`Reset()` вызывается во время возврата в пул. Перед увеличением поколения он проверяет, была ли ошибка сохранена, но не наблюдалась (т.е. `GetResult` никогда не вызывался после ошибки). Если да, исключение публикуется через `ValkarnTask.UnobservedException`. Ошибки отмены сообщаются только если `LogUnobservedCancellations` включён в `ValkarnTaskSettings`, поскольку отмена часто является намеренной.

Для непулируемых объектов `Promise` и `Promise<T>` сообщение о необнаруженных ошибках происходит из финализатора через `ReportUnobservedIfNeeded()`, который следует той же логике без очистки состояния.

---

## Конфигурация пула

Три настройки управляют размером пула. В сборках Unity они читаются из ScriptableObject-ресурса `ValkarnTaskSettings` (с резервными значениями по умолчанию) и могут быть переопределены во время выполнения через статические свойства:

```csharp
// Максимальное количество объектов на тип пула (на TStateMachine или на тип promise)
ValkarnTask.DefaultMaxPoolSize = 256;   // по умолчанию: 256

// Количество кадров между проверками обрезки (кадры Unity PlayerLoop)
ValkarnTask.TrimCheckInterval = 300;    // по умолчанию: 300 (~5 секунд при 60fps)

// Минимальное количество объектов для хранения после прохода обрезки
ValkarnTask.MinPoolSize = 8;            // по умолчанию: 8
```

`DefaultMaxPoolSize` — это потолок, применяемый во время создания пула. Он применяется на экземпляр пула, а не глобально — пул для `AsyncValkarnTaskRunner<LoadSceneStateMachine>` и пул для `AsyncValkarnTaskRunner<FetchDataStateMachine>` имеют свой собственный потолок.

### Обрезка пула

`PlayerLoopHelper` вызывает `PoolRegistry.TrimAll(minPoolSize)` каждые `TrimCheckInterval` кадров в главном потоке. Каждый пул использует стратегию гистерезиса:

```
Каждая проверка обрезки:
  currentSize = fastItem.count + stackSize
  if currentSize <= MinPoolSize: сбросить счётчик подряд, пропустить

  ratio = currentSize / totalCreated
  if ratio > 0.5 (пул держит > 50% всех когда-либо созданных объектов):
    trimConsecutiveCount++
    if trimConsecutiveCount >= hysteresisThreshold:
      освободить некоторую долю (releaseRatio) избыточных объектов из стека
      (fastItem сохраняется — это самый кэш-дружелюбный слот)
  else:
    сбросить trimConsecutiveCount
```

Гистерезис предотвращает ситуацию, когда краткий всплеск трафика сразу вызывает аллокацию всех объектов и их немедленную обрезку. Быстрый слот всегда сохраняется при обрезке, поскольку он представляет самый последний использованный элемент и поэтому с наибольшей вероятностью понадобится снова.

---

## `PoolRegistry` и мониторинг

Каждый `ValkarnTaskPool<T>` регистрируется в глобальном `PoolRegistry` во время создания. Реестр поддерживает список ссылок `IPoolInfo`, предоставляющих:

```csharp
internal interface IPoolInfo
{
    Type PooledType { get; }
    int Size { get; }
    int MaxSize { get; }
    bool IsAlive { get; }
    int Trim(int minPoolSize);
}
```

Вы можете перечислить все активные пулы во время выполнения с помощью публичного API:

```csharp
foreach (var (type, size, maxSize) in ValkarnTask.GetPoolInfo())
{
    Debug.Log($"{type.Name}: {size}/{maxSize}");
}
```

Это те же данные, что отображаются в окне **Task Tracker** в редакторе Unity. Окно опрашивает `GetPoolInfo()` и отображает живую таблицу заполнения пулов, позволяя видеть, прогреты ли пулы, достигает ли какой-либо тип своего потолка и работает ли обрезка как ожидается.

Мёртвые записи пула (где `IsAlive` возвращает false) лениво удаляются из списка реестра при вызовах `GetAll()` и `TrimAll()`, предотвращая бесконечный рост реестра при сборке пулируемых экземпляров сборщиком мусора.

---

## `PooledPromise` и `PooledPromise<T>`

Это пулируемые источники завершения, предназначенные для использования в пользовательских асинхронных паттернах — например, оборачивания API на основе коллбэков или повторяющегося канала producer/consumer.

```csharp
// Получить ожидающий promise из пула
var promise = ValkarnTask.PooledPromise<string>.Create(out uint token);
ValkarnTask<string> task = promise.Task;

// Передать задачу потребителю
// ... позже, из любого потока ...
promise.TrySetResult("hello");

// Когда потребитель ожидает task и вызывается GetResult,
// promise сбрасывается и автоматически возвращается в пул.
```

Ключевые характеристики:

- `Create(out uint token)` арендует из пула или создаёт новый экземпляр, отслеживаемый пулом.
- `CreateCompleted(T result, out uint token)` делает то же, но сразу сигнализирует результат, поэтому задача уже завершена при возврате.
- После вызова `GetResult` на поддерживающей задаче срабатывает `TryReturn()`: promise вызывает `core.Reset()` и возвращает себя в пул.
- Защита от двойного возврата (`Interlocked.Exchange(ref returned, 1)`) предотвращает повреждение пула при двукратном вызове `GetResult`.

**Непулируемая альтернатива: `Promise` и `Promise<T>`.** Это классы, выделяемые в куче, которые не возвращаются в пул. Используйте их для долгоживущих операций с непредсказуемым временем жизни или когда один и тот же источник должен переживать несколько циклов await. Они полагаются на финализатор для сообщения о необнаруженных исключениях.

---

## Пулы комбинаторов

Комбинаторы `WhenAll` и `WhenAny` также используют пулы. Каждая комбинация арности и типа имеет свой собственный пул:

| Комбинатор | Тип пула |
|-----------|----------|
| `WhenAll(task1, task2)` (типизированный) | `ValkarnTaskPool<WhenAllPromise<T1, T2>>` |
| `WhenAll(IEnumerable<ValkarnTask<T>>)` | `ValkarnTaskPool<WhenAllArrayPromise<T>>` |
| `WhenAll(task1, task2)` (void) | `ValkarnTaskPool<WhenAllVoidPromise2>` |
| `WhenAny(task1, task2)` (типизированный) | `ValkarnTaskPool<WhenAnyPromise2<T>>` |
| `WhenAny(IEnumerable<ValkarnTask<T>>)` | `ValkarnTaskPool<WhenAnyArrayPromise<T>>` |

Комбинаторы на основе массивов (`WhenAll<T>(IEnumerable<...>)` и `WhenAny<T>(IEnumerable<...>)`) используют `System.Buffers.ArrayPool<T>.Shared` для своих внутренних массивов источников/токенов, так что эти массивы также переиспользуются, а не создаются заново на каждый вызов.

Все комбинаторы применяют одно и то же короткое замыкание без аллокаций: если все входные данные синхронно завершены в момент вызова `WhenAll` или `WhenAny`, новый пулируемый объект никогда не создаётся.

```csharp
// Ноль аллокаций — обе задачи синхронно завершены
var t1 = ValkarnTask.FromResult(1);
var t2 = ValkarnTask.FromResult(2);
ValkarnTask<(int, int)> combined = ValkarnTask.WhenAll(t1, t2);
// combined.source == null; результат (1, 2) хранится встроенно
```
