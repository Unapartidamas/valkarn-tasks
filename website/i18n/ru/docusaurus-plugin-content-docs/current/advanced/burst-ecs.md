---
sidebar_position: 1
title: Интеграция с Burst и ECS
---

# Интеграция с Burst и ECS

Valkarn Tasks включает опциональную интеграцию с компилятором Unity Burst, Unity Collections и пакетом Entities (ECS). Весь этот функционал компилируется условно — он активен только при наличии необходимых пакетов и установленных соответствующих символов определения скриптинга.

## Требования

| Возможность | Необходимый пакет | Символ определения |
|---|---|---|
| `NativeTimerHeap`, `NativeScheduler`, `BurstSchedulerRunner` | Unity Burst 1.8+, Unity Collections 2.0+ | `VTASKS_HAS_BURST` и `VTASKS_HAS_COLLECTIONS` |
| `AsyncSystemUtilities` | Unity Entities 1.0+ | `VTASKS_HAS_ENTITIES` |

Все исходные файлы Burst/ECS обёрнуты в защитники `#if`, соответствующие этим символам. Ничто в этих файлах не компилируется и не компонуется, пока символы не установлены.

### Настройка

1. Установите необходимые пакеты через Unity Package Manager:
   - `com.unity.burst` 1.8 или новее
   - `com.unity.collections` 2.0 или новее
   - `com.unity.entities` 1.0 или новее (только для утилит ECS)

2. Добавьте символы определения скриптинга в **Project Settings > Player > Scripting Define Symbols**:
   ```
   VTASKS_HAS_BURST;VTASKS_HAS_COLLECTIONS;VTASKS_HAS_ENTITIES
   ```
   Добавляйте только символы для установленных пакетов.

---

## NativeTimerHeap

**Пространство имён:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Защитник:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeTimerHeap` — это двоичная мин-куча, совместимая с Burst, для планирования таймеров. Хранит значения `TimerEntry`, упорядоченные по дедлайну, давая O(log n) вставку и O(log n) удаление для каждого истёкшего таймера.

### Ключевые типы

```csharp
// Запись, хранимая в куче. Упорядочена по Deadline.
public struct TimerEntry : IComparable<TimerEntry>
{
    public long Deadline;
    public int  Id;
}

public struct NativeTimerHeap : IDisposable
{
    public bool IsCreated { get; }
    public int  Count     { get; }

    // Создаёт кучу. Используйте Allocator.Persistent для долгоживущих куч.
    public NativeTimerHeap(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // Вставляет новый таймер. Возвращает ID таймера (используется для идентификации коллбэка).
    // Дедлайн и значение, передаваемое в DrainExpired, должны использовать одну единицу
    // (BurstSchedulerRunner использует тики DateTime через Time.realtimeSinceStartupAsDouble).
    [BurstCompile]
    public int Schedule(long deadline);

    // Удаляет и добавляет ID всех таймеров, у которых Deadline <= currentTimestamp.
    // Возвращает количество слитых таймеров.
    [BurstCompile]
    public int DrainExpired(long currentTimestamp, NativeList<int> expiredIds);

    public void Dispose();
}
```

`NativeTimerHeap` — это неуправляемая структура. Она не может хранить управляемые делегаты — ID сопоставляются с управляемыми коллбэками в словаре `BurstSchedulerRunner` в главном потоке.

---

## NativeScheduler

**Пространство имён:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Защитник:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeScheduler` — это очередь работ, совместимая с Burst, поддерживаемая `NativeQueue<ScheduledWork>`. Скомпилированные в Burst задания ставят элементы работ в очередь; главный поток опустошает их каждый кадр.

### Ключевые типы

```csharp
public enum WorkType : byte
{
    TimerExpired  = 0,
    JobCompleted  = 1,
    Custom        = 2
}

public struct ScheduledWork
{
    public int      Id;
    public WorkType Type;
    public long     Payload;
}

public struct NativeScheduler : IDisposable
{
    public bool IsCreated { get; }

    public NativeScheduler(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // Ставит элемент работы в очередь из Burst-скомпилированного задания.
    [BurstCompile]
    public void Enqueue(ScheduledWork work);

    // Опустошает всю ожидающую работу в `results`. Вызывается только из главного потока.
    // Возвращает количество слитых элементов.
    public int Drain(NativeList<ScheduledWork> results);

    // Возвращает параллельный писатель, подходящий для использования в заданиях IJobParallelFor.
    public NativeQueue<ScheduledWork>.ParallelWriter AsParallelWriter();

    public void Dispose();
}
```

Очередь — это точка пересечения мира Burst и управляемого мира. `Enqueue` вызывается из Burst; `Drain` работает только в главном потоке.

---

## BurstSchedulerRunner

**Пространство имён:** `UnaPartidaMas.Valkarn.Tasks.Burst`
**Защитник:** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`BurstSchedulerRunner` — это управляемый мост между нативным планировщиком/таймерной кучей и остальной частью вашей игры. Он реализует `IPlayerLoopItem`, поэтому Unity вызывает `MoveNext()` один раз за кадр при зарегистрированной фазе. Каждый кадр он:

1. Опустошает `NativeScheduler` и вызывает зарегистрированные управляемые коллбэки, сопоставленные по ID работы.
2. Опустошает `NativeTimerHeap` для истёкших таймеров и вызывает их управляемые коллбэки.

Исключения, брошенные коллбэками, перенаправляются в `VlkTask.PublishUnobservedException`, а не распространяются через PlayerLoop.

### API

```csharp
public sealed class BurstSchedulerRunner : IPlayerLoopItem, IDisposable
{
    // Создаёт runner и регистрирует его в PlayerLoop. Возвращает экземпляр.
    // Освободите возвращённый runner, когда он больше не нужен.
    public static BurstSchedulerRunner Create(
        PlayerLoopTiming timing       = PlayerLoopTiming.Update,
        int initialCapacity           = 64);

    // Прямой доступ к NativeScheduler для постановки в очередь из Burst-заданий.
    public NativeScheduler Scheduler { get; }

    // Планирует управляемый коллбэк таймера. Вызывается только из главного потока.
    // Возвращает ID таймера (только для идентификации; API отмены нет).
    public int ScheduleTimer(TimeSpan delay, Action callback);

    // Связывает управляемый коллбэк с ID работы, поставленной в очередь Burst-заданием.
    // Вызывается из главного потока, до или в течение кадра завершения задания.
    public void RegisterCallback(int workId, Action callback);

    // Освобождает все нативные контейнеры и отменяет регистрацию из PlayerLoop.
    public void Dispose();
}
```

### Чем BurstSchedulerRunner отличается от планировщика по умолчанию

Планировщик по умолчанию Valkarn Tasks интегрируется напрямую с конечными автоматами `async/await` и управляет диспетчеризацией продолжений через `PlayerLoopHelper`. `BurstSchedulerRunner` добавляет отдельный канал специально для сигнализации из Burst-скомпилированных заданий:

| | Планировщик по умолчанию | BurstSchedulerRunner |
|---|---|---|
| Тип продолжения | Управляемый `Action` через `ISource` | Управляемый `Action`, зарегистрированный по ID |
| Источник сигнала | Паттерн awaiter в C# | Неуправляемый `NativeScheduler.Enqueue` |
| Источник таймера | `VlkTask.Delay` (управляемый) | `NativeTimerHeap` (неуправляемый) |
| Потокобезопасность | Продолжения главного потока | Enqueue безопасен для Burst; drain только в главном потоке |

### Паттерн использования

```csharp
// 1. Создать runner один раз (например, в bootstrap-MonoBehaviour или ISystem.OnCreate).
var runner = BurstSchedulerRunner.Create(PlayerLoopTiming.Update, initialCapacity: 128);

// 2. Запланировать коллбэк таймера (главный поток).
runner.ScheduleTimer(TimeSpan.FromSeconds(2.0), () =>
{
    Debug.Log("Прошло две секунды (неуправляемый таймер).");
});

// 3. Из Burst-задания поставить элемент работы в очередь.
//    NativeScheduler.ParallelWriter безопасен для использования из IJobParallelFor.
var writer = runner.Scheduler.AsParallelWriter();
// Внутри Execute(int index):
writer.Enqueue(new ScheduledWork { Id = myWorkId, Type = WorkType.Custom });

// 4. Зарегистрировать управляемый коллбэк в главном потоке до завершения задания.
runner.RegisterCallback(myWorkId, () =>
{
    Debug.Log($"Получен сигнал завершения задания для работы {myWorkId}.");
});

// 5. Освободить runner по завершении (например, OnDestroy или перезагрузка домена).
runner.Dispose();
```

---

## AsyncSystemUtilities

**Пространство имён:** `UnaPartidaMas.Valkarn.Tasks.ECS`
**Защитник:** `#if VTASKS_HAS_ENTITIES`

`AsyncSystemUtilities` предоставляет два вспомогательных расширения для написания асинхронных ECS-систем.

### GetWorldCancellationToken

```csharp
public static CancellationToken GetWorldCancellationToken(
    this World world,
    PlayerLoopTiming timing = PlayerLoopTiming.Update)
```

Возвращает `CancellationToken`, автоматически отменяемый при уничтожении данного `World`. Внутри запускает fire-and-forget `VlkTask`, который вызывает `VlkTask.Yield(timing)` каждый кадр, пока `world.IsCreated` истинно, затем отменяет `CancellationTokenSource`, когда цикл завершается.

Если мир уже уничтожен при вызове этого метода, возвращает токен, который уже находится в отменённом состоянии.

Передавайте этот токен каждому async-методу, запускаемому из системы, чтобы незавершённая работа автоматически останавливалась при исчезновении мира.

### SafeEntityExists

```csharp
public static bool SafeEntityExists(this EntityManager entityManager, Entity entity)
```

Вызывает `entityManager.Exists(entity)` и возвращает `false`, если брошен `ObjectDisposedException`. Это может произойти при обращении к `EntityManager` после удаления мира, что является реальным состоянием гонки в асинхронном коде, работающем через границы кадров.

Используйте после каждой точки `await` перед записью обратно в сущность.

---

## Рабочий пример: Асинхронная ECS-система

Следующий пример из `Samples~/ECS/AsyncLoadSystem.cs`. Он демонстрирует канонический паттерн одноразовой асинхронной инициализации из `ISystem`.

```csharp
#if VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Entities;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.ECS;

public partial struct AsyncLoadSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // Получить токен отмены, привязанный к времени жизни этого World.
        // Если World уничтожается, вся асинхронная работа, запущенная с этим токеном,
        // будет автоматически отменена.
        var worldCt = state.World.GetWorldCancellationToken();

        // Запустить асинхронную инициализацию и забыть задачу.
        // Forget() направляет любое необработанное исключение в VlkTask.PublishUnobservedException.
        InitializeAsync(state.WorldUnmanaged, worldCt).Forget();
    }

    public void OnUpdate(ref SystemState state) { }
    public void OnDestroy(ref SystemState state) { }

    static async VlkTask InitializeAsync(WorldUnmanaged world, CancellationToken ct)
    {
        // Фаза 1: Загрузить данные в фоновом потоке.
        // RunOnThreadPool переключается на рабочий поток, выполняет делегат,
        // и автоматически возвращается в главный поток.
        var configData = await VlkTask.RunOnThreadPool(
            () => LoadFromDisk(),
            cancellationToken: ct);

        // Фаза 2: Применить результаты в главном потоке.
        // Проверить отмену на случай уничтожения World во время загрузки.
        ct.ThrowIfCancellationRequested();
        ApplyConfiguration(world, configData);
    }

    static ConfigData LoadFromDisk()
    {
        // Только чистый C# — здесь нельзя вызывать Unity или ECS API.
        return new ConfigData { MaxEnemies = 100, SpawnRadius = 50f };
    }

    static void ApplyConfiguration(WorldUnmanaged world, ConfigData data)
    {
        // Безопасно: мы в главном потоке.
        UnityEngine.Debug.Log($"Loaded config: MaxEnemies={data.MaxEnemies}");
    }

    struct ConfigData
    {
        public int   MaxEnemies;
        public float SpawnRadius;
    }
}
#endif
```

Пример с регулированием ИИ (`Samples~/ECS/AISystemExample.cs`) строится на этом паттерне и добавляет `AsyncThrottle` для ограничения количества одновременно выполняющихся async-задач. Подробности о этом паттерне см. в документации по Throttling.

---

## Ограничения

Следующие ограничения применяются ко всему асинхронному коду Burst/ECS. Их нарушение приведёт к ошибкам редактора, нарушениям безопасности заданий или молчаливому повреждению данных.

### Внутри Burst-скомпилированного кода

- **Никаких управляемых типов.** Burst не может компилировать код, который выделяет, обращается или ссылается на управляемые объекты (классы, делегаты, массивы, строки, `List<T>` и т.д.). Допускаются только blittable-структуры и нативные контейнеры.
- **Никаких исключений.** Burst не поддерживает `try`/`catch`/`throw`. Используйте коды возврата или флаги для передачи ошибок.
- **Никакого `async`/`await`.** Асинхронные конечные автоматы C# являются управляемыми и не могут быть скомпилированы Burst. `NativeScheduler` и `NativeTimerHeap` предоставляют побочный канал для сигнализации управляемых продолжений, но сами продолжения выполняются в главном потоке.
- **Никакого изменяемого статического управляемого состояния.** Burst-задания могут читать статические поля readonly, но не должны писать в управляемые статики.

### Через точки await в ECS-системах

- **Время жизни сущностей.** Сущности могут быть уничтожены, пока async-метод приостановлен. Всегда вызывайте `entityManager.SafeEntityExists(entity)` после каждого `await` перед записью обратно.
- **Устаревание ComponentLookup.** `ComponentLookup`, `RefRW` и другие типы на основе chunk-указателей становятся недействительными после структурных изменений, которые могут произойти в любом кадре. Не кэшируйте их через точки `await`. Повторно получайте их из `SystemState` после возобновления, или используйте `EntityManager` напрямую.
- **Параметры `ref`.** Async-методы не могут иметь параметры `ref`, `in` или `out` (ошибка C# CS1988). Извлекайте все данные ECS синхронно в синхронном методе `OnUpdate` и передавайте их в async-метод по значению.
- **`SystemAPI` в async-методах.** `SystemAPI` генерируется исходным кодом и работает только внутри partial-методов `ISystem`. В `async`-методах он недоступен. Выполняйте все запросы `SystemAPI` до первого `await`.
- **Потокобезопасность.** `EntityManager`, `ComponentLookup` и структурные изменения работают только в главном потоке. Используйте `VlkTask.RunOnThreadPool` только для чистых C#-вычислений без вызовов Unity или ECS API.
