---
sidebar_position: 1
title: Интеграция с Unity PlayerLoop
---

# Интеграция с Unity PlayerLoop

ValkarnTasks не использует потоки или фоновые планировщики для возобновления ваших `async`-методов. Всё выполняется в главном потоке Unity, управляемом собственной системой PlayerLoop Unity. Понимание этого поможет вам выбрать правильный момент для вашего случая использования и точно понять, когда ваш код возобновляется после `await`.

## Что такое Unity PlayerLoop

PlayerLoop Unity — это внутренний движковый цикл, управляющий каждым кадром. Это не единственный вызов `Update()` — это иерархическая последовательность фаз, выполняемых в определённом порядке каждый кадр:

```
Initialization
EarlyUpdate
FixedUpdate      (повторяется при шаге физики)
PreUpdate
Update
PreLateUpdate
PostLateUpdate
TimeUpdate
```

Каждая из этих фаз верхнего уровня содержит подсистемы, которые Unity (и сторонние пакеты) вставляют для выполнения своей логики в конкретных точках. `MonoBehaviour.Update()`, например, выполняется внутри фазы `Update`. `MonoBehaviour.LateUpdate()` выполняется внутри `PreLateUpdate`.

Поскольку ValkarnTasks подключается непосредственно к этому циклу, `await ValkarnTask.Yield()` не блокирует поток — он регистрирует коллбэк, который Unity вызывает при следующем тике выбранной фазы, и немедленно возвращает управление.

## 16 фаз PlayerLoop

ValkarnTasks встраивается в 16 точках: по одной в **начале** и **конце** каждой из 8 фаз PlayerLoop Unity. Варианты `Last` добавляются в конец списка подсистем родительской фазы; обычные варианты добавляются в начало.

| Значение | Целочисленное значение enum | Родительская фаза | Позиция в родительской |
|---|---|---|---|
| `Initialization` | 0 | `UnityEngine.PlayerLoop.Initialization` | Первая |
| `LastInitialization` | 1 | `UnityEngine.PlayerLoop.Initialization` | Последняя |
| `EarlyUpdate` | 2 | `UnityEngine.PlayerLoop.EarlyUpdate` | Первая |
| `LastEarlyUpdate` | 3 | `UnityEngine.PlayerLoop.EarlyUpdate` | Последняя |
| `FixedUpdate` | 4 | `UnityEngine.PlayerLoop.FixedUpdate` | Первая |
| `LastFixedUpdate` | 5 | `UnityEngine.PlayerLoop.FixedUpdate` | Последняя |
| `PreUpdate` | 6 | `UnityEngine.PlayerLoop.PreUpdate` | Первая |
| `LastPreUpdate` | 7 | `UnityEngine.PlayerLoop.PreUpdate` | Последняя |
| `Update` | 8 | `UnityEngine.PlayerLoop.Update` | Первая |
| `LastUpdate` | 9 | `UnityEngine.PlayerLoop.Update` | Последняя |
| `PreLateUpdate` | 10 | `UnityEngine.PlayerLoop.PreLateUpdate` | Первая |
| `LastPreLateUpdate` | 11 | `UnityEngine.PlayerLoop.PreLateUpdate` | Последняя |
| `PostLateUpdate` | 12 | `UnityEngine.PlayerLoop.PostLateUpdate` | Первая |
| `LastPostLateUpdate` | 13 | `UnityEngine.PlayerLoop.PostLateUpdate` | Последняя |
| `TimeUpdate` | 14 | `UnityEngine.PlayerLoop.TimeUpdate` | Первая |
| `LastTimeUpdate` | 15 | `UnityEngine.PlayerLoop.TimeUpdate` | Последняя |

`Update` (значение 8) является **значением по умолчанию** для всех операций ValkarnTasks — `Yield()`, `Delay()`, `WaitUntil()`, `WaitWhile()`, `NextFrame()` и `DelayFrame()`.

## Маркерные структуры и идемпотентное встраивание

Для встраивания в PlayerLoop Unity ValkarnTasks создаёт один `PlayerLoopSystem` на фазу. Каждая система идентифицируется уникальной маркерной структурой, определённой внутри `PlayerLoopHelper`:

```csharp
struct ValkarnTaskInitialization     { }
struct ValkarnTaskLastInitialization { }
struct ValkarnTaskEarlyUpdate        { }
struct ValkarnTaskLastEarlyUpdate    { }
struct ValkarnTaskFixedUpdate        { }
struct ValkarnTaskLastFixedUpdate    { }
struct ValkarnTaskPreUpdate          { }
struct ValkarnTaskLastPreUpdate      { }
struct ValkarnTaskUpdate             { }
struct ValkarnTaskLastUpdate         { }
struct ValkarnTaskPreLateUpdate      { }
struct ValkarnTaskLastPreLateUpdate  { }
struct ValkarnTaskPostLateUpdate     { }
struct ValkarnTaskLastPostLateUpdate { }
struct ValkarnTaskTimeUpdate         { }
struct ValkarnTaskLastTimeUpdate     { }
```

Перед встраиванием `PlayerLoopHelper` проверяет, присутствует ли `ValkarnTaskUpdate` где-либо в текущем дереве PlayerLoop. Если да, встраивание пропускается. Это делает встраивание **идемпотентным** — многократный вызов `Init()` (что может произойти в редакторе) никогда не приводит к регистрации дублирующихся систем.

Встраивание всегда читает **текущий** PlayerLoop с помощью `PlayerLoop.GetCurrentPlayerLoop()`, а не `GetDefaultPlayerLoop()`. Это означает, что системы, ранее установленные другими пакетами, сохраняются.

## ContinuationQueue — одноразовые коллбэки

Когда вы делаете `await ValkarnTask.Yield()` (или что-то, что приостанавливается ровно на один тик), компилятор-сгенерированный конечный автомат вызывает `OnCompleted` на awaiter'е. Awaiter вызывает `PlayerLoopHelper.AddContinuation(timing, action, state)`, что ставит коллбэк в очередь `ContinuationQueue` для этой фазы.

`ContinuationQueue` использует дизайн **двойного буфера**:

1. **Активный буфер** (`actionList`): хранит продолжения, поставленные в очередь до и во время текущего дренирования.
2. **Ожидающий буфер** (`waitingList`): перехватывает продолжения, поставленные в очередь _во время_ дренирования активного буфера (т.е. реентерантные постановки из самого продолжения).
3. **Кросс-поточный стек Трайбера** (`crossThreadHead`): продолжения, отправленные из фоновых потоков, попадают сюда через lock-free compare-and-swap. В начале каждого дренирования весь стек атомарно захватывается и переносится в активный буфер.

Каждый тик PlayerLoop `ContinuationQueue.Run()` выполняет следующую последовательность:

```
1. Слить crossThreadHead → actionList     (lock-free атомарный захват, затем копирование под SpinLock)
2. Зафиксировать счётчик, установить isDraining = true  (под SpinLock)
3. Выполнить все коллбэки                (вне блокировки, ссылки очищены для GC)
4. Поменять местами actionList ↔ waitingList (под SpinLock, установить isDraining = false)
```

После прогрева примерно одного-двух кадров внутренние массивы стабилизируются на своей максимальной отметке, и очередь работает с **нулевыми аллокациями**.

Кросс-поточные узлы пулируются в ограниченном lock-free пуле (максимум 1024 узла), чтобы избежать аллокаций при каждой постановке в очередь из фонового потока.

## PlayerLoopRunner — повторяющиеся элементы

Некоторые операции должны проверяться при каждом тике до их завершения: `Delay()`, `DelayFrame()`, `WaitUntil()`, `WaitWhile()` и `NextFrame()`. Они реализуют интерфейс `IPlayerLoopItem`:

```csharp
internal interface IPlayerLoopItem
{
    // Вернуть true для продолжения выполнения; вернуть false для удаления из runner'а.
    bool MoveNext();
}
```

Элементы добавляются в `PlayerLoopRunner` через `PlayerLoopHelper.AddAction(timing, item)`. Каждый тик `PlayerLoopRunner.Run()` итерирует все зарегистрированные элементы и вызывает `MoveNext()` на каждом. Элементы, возвращающие `false`, удаляются **компакцией на месте** — массив компактируется за один проход, сохраняя порядок вставки. Элементы, добавленные во время вызова `Run()`, безопасно добавляются и будут обработаны при следующем тике.

```
Тик N:
  ContinuationQueue.Run()   → возобновить все одноразовые awaiter'ы
  PlayerLoopRunner.Run()    → тикнуть все повторяющиеся элементы (Delay, WaitUntil, ...)
```

Оба выполняются для каждой из 16 фаз в порядке, в котором движок их вызывает.

Фаза `Update` также отслеживает глобальный счётчик кадров и периодически запускает обрезку пула объектов (каждые `ValkarnTask.TrimCheckInterval` кадров).

## Инициализация — RuntimeInitializeOnLoadMethod

ValkarnTasks инициализируется при `RuntimeInitializeLoadType.SubsystemRegistration`, самом раннем хуке, предоставляемом Unity, который срабатывает до `Awake()` на любом объекте сцены:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void Init()
{
    // 1. Захватить ID главного потока для проверок безопасности потоков
    // 2. Создать ContinuationQueue и PlayerLoopRunner для всех 16 фаз
    // 3. Встроиться в PlayerLoop (идемпотентно)
    // 4. Зарегистрировать коллбэк изменения состояния play mode (только редактор)
}
```

ID главного потока захватывается в этот момент и разделяется со всеми подсистемами пула и очереди, чтобы они могли отличить постановки в очередь из главного потока (путь SpinLock) от постановок из других потоков (путь стека Трайбера).

## Обработка перезагрузки домена (редактор)

В редакторе Unity вход или выход из Play Mode вызывает перезагрузку домена. Статическое состояние предыдущей сессии play в противном случае сохранялось бы и вызывало устаревшие ссылки.

ValkarnTasks обрабатывает это с помощью коллбэка `EditorApplication.playModeStateChanged`, регистрируемого при `Init()`. Когда состояние переходит в `ExitingPlayMode` или `EnteredEditMode`, выполняется следующая очистка:

```csharp
// Сброс всех очередей и runner'ов
for (int i = 0; i < 16; i++)
{
    s_continuationQueues[i] = null;
    s_playerLoopRunners[i] = null;
}

// Сброс других подсистем
ValkarnTask.ResetStatics();
TimeProvider.ResetToDefault();   // возврат к UnityTimeProvider
PoolRegistry.Clear();
ContinuationQueue.ContinuationNode.ResetPool();
```

При следующем входе в Play Mode `Init()` срабатывает через `RuntimeInitializeOnLoadMethod` и выделяются новые очереди и runner'ы. Проверка встраивания PlayerLoop (`HasValkarnTaskSystems`) предотвращает повторное добавление маркерных систем, если домен не был полностью перезагружен.

## Выбор правильной фазы

В большинстве случаев следует использовать стандартную фазу `Update`. Обращайтесь к другим только при наличии конкретной причины.

| Фаза | Когда использовать |
|---|---|
| `Initialization` / `LastInitialization` | Очень ранняя настройка кадра; редко нужна в игровом коде |
| `EarlyUpdate` / `LastEarlyUpdate` | Считывание ввода; выполняется до физики и до `Update` |
| `FixedUpdate` / `LastFixedUpdate` | Логика, синхронизированная с физикой; соответствует темпу `MonoBehaviour.FixedUpdate` |
| `PreUpdate` / `LastPreUpdate` | До основного диспатча обновлений Unity; полезно для пользовательского пре-прохода планировщика |
| **`Update`** (по умолчанию) | **Стандартная игровая логика; соответствует `MonoBehaviour.Update`** |
| `LastUpdate` | Логика, которая должна выполняться после всех вызовов `MonoBehaviour.Update` |
| `PreLateUpdate` / `LastPreLateUpdate` | Соответствует `MonoBehaviour.LateUpdate`; следование камеры и трансформации |
| `PostLateUpdate` / `LastPostLateUpdate` | После отправки рендеринга; финализация UI, захват скриншота |
| `TimeUpdate` / `LastTimeUpdate` | Синхронизация значений времени; требуется очень редко |

```csharp
// Возобновить на следующем тике Update (по умолчанию)
await ValkarnTask.Yield();

// Возобновить в начале следующего FixedUpdate (безопасно для физики)
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// Ждать 2 секунды, отсчитывая нескалированное время, проверяемое в LateUpdate
await ValkarnTask.Delay(
    TimeSpan.FromSeconds(2),
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.PreLateUpdate);

// Ждать выполнения условия, проверяемого после всех вызовов LateUpdate
await ValkarnTask.WaitUntil(() => _ready, PlayerLoopTiming.LastPreLateUpdate);
```
