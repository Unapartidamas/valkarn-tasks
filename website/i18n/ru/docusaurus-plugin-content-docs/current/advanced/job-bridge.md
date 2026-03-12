---
sidebar_position: 2
title: Мосты для Job и Awaitable
---

# Мосты для Job и Awaitable

Valkarn Tasks предоставляет набор типов-мостов, соединяющих Job System Unity и API `Awaitable` с конвейером `VlkTask`. Каждый мост является тонким слоем с минимальными аллокациями; никакой скрытой магии нет.

Все типы, описанные здесь, находятся в пространстве имён `UnaPartidaMas.Valkarn.Tasks.Bridge` и защищены `#if UNITY_5_3_OR_NEWER` (или `#if UNITY_2023_1_OR_NEWER` для поддержки Awaitable).

---

## JobHandleExtensions — ожидание одного JobHandle

Простейший мост. Вызовите `.ToVlkTask()` на любом `JobHandle`, чтобы получить обратно `VlkTask`, завершающийся при окончании задания.

```csharp
public static VlkTask ToVlkTask(
    this JobHandle handle,
    PlayerLoopTiming timing          = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

### Как это работает

1. **Быстрый путь.** Если `handle.IsCompleted` уже истинно, `handle.Complete()` вызывается немедленно и возвращается `VlkTask.CompletedTask` — ноль аллокаций, никакой регистрации в PlayerLoop.
2. **Обычный путь.** Берётся пулируемый `JobHandlePromise`, регистрируется в PlayerLoop при указанной `timing` и возвращается обёрнутым в `VlkTask`. Каждый кадр `MoveNext()` вызывает `JobHandle.ScheduleBatchedJobs()` (для очистки очереди заданий в режиме редактора и пакетном режиме) и затем проверяет `handle.IsCompleted`. Когда handle готов, promise завершает задачу и возвращается в пул.
3. **Отмена.** Если `CancellationToken` срабатывает, handle принудительно завершается (`handle.Complete()` всегда вызывается для предотвращения утечек Job System) и задача переходит в отменённое состояние.

### Базовое использование

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Jobs;

// Запланировать задание и немедленно его ожидать.
var handle = myJob.Schedule();
await handle.ToVlkTask();

// С нестандартной фазой и отменой.
await handle.ToVlkTask(PlayerLoopTiming.PreUpdate, destroyCancellationToken);
```

---

## JobHandleWhenAll — ожидание нескольких JobHandle параллельно

Когда нужно запланировать несколько независимых заданий и возобновить только после завершения всех, используйте `JobHandleExtensions.WhenAll`.

```csharp
// Простейшая перегрузка: ждёт все handle при фазе Update.
public static VlkTask WhenAll(params JobHandle[] handles)

// Полная перегрузка: настраиваемые фаза и отмена.
public static VlkTask WhenAll(
    JobHandle[]       handles,
    PlayerLoopTiming  timing,
    CancellationToken cancellationToken = default)

// Псевдоним метода расширения для полной перегрузки.
public static VlkTask ToVlkTask(
    this JobHandle[]  handles,
    PlayerLoopTiming  timing              = PlayerLoopTiming.Update,
    CancellationToken cancellationToken   = default)
```

### Как это работает

- **Быстрый путь.** Если все handle в массиве уже завершены, все они финализируются и немедленно возвращается `VlkTask.CompletedTask`.
- **Пустой массив.** Возвращает `VlkTask.CompletedTask`.
- **Обычный путь.** Создаётся пулируемый `JobHandleArrayPromise`. Внутри он берёт `JobHandle[]` из `ArrayPool<JobHandle>.Shared` (избегая аллокации кучи на вызов), копирует входные handle и регистрируется в PlayerLoop. Каждый кадр итерирует только ещё не завершённые handle с помощью цикла замены и сжатия, и вызывает `JobHandle.ScheduleBatchedJobs()` для поддержания работы worker'ов.
- **Отмена.** Все оставшиеся handle принудительно завершаются и задача отменяется.

### Использование

```csharp
var handleA = jobA.Schedule();
var handleB = jobB.Schedule();
var handleC = jobC.Schedule();

// Ждать все три.
await JobHandleExtensions.WhenAll(handleA, handleB, handleC);

// Или используя метод расширения на массиве.
JobHandle[] handles = { handleA, handleB, handleC };
await handles.ToVlkTask();
```

---

## TempNativeArrayScope — время жизни NativeArray через точки await

### Проблема

`NativeArray<T>`, выделенный с `Allocator.TempJob`, имеет короткое время жизни. Если вы выделите его, запланируете задание, `await` handle задания, а затем забудете освободить массив, система безопасности Unity сообщит об утечке памяти. Использование простого `try`/`finally` работает, но легко ошибиться в длинном async-методе.

`TempNativeArrayScope<T>` — это структура, оборачивающая `NativeArray<T>` и освобождающая его при завершении области видимости с помощью оператора `using` — паттерн RAII, применённый к нативной памяти.

### API

```csharp
public struct TempNativeArrayScope<T> : IDisposable where T : struct
{
    // Обращается к обёрнутому массиву. Бросает ObjectDisposedException если уже освобождён.
    public NativeArray<T> Array { get; }

    // True если область не освобождена и массив создан.
    public bool IsCreated { get; }

    // Выделяет новый NativeArray<T> с Allocator.TempJob и берёт владение.
    public static TempNativeArrayScope<T> Create(int length);

    // Берёт владение уже выделенным NativeArray<T>.
    public static TempNativeArrayScope<T> Wrap(NativeArray<T> existing);

    // Освобождает массив. Идемпотентен: безопасно вызывать несколько раз.
    public void Dispose();
}

// Необобщённый вспомогательный класс (удобство вывода типа).
public static class TempNativeArrayScope
{
    public static TempNativeArrayScope<T> Create<T>(int length) where T : struct;
    public static TempNativeArrayScope<T> Wrap<T>(NativeArray<T> existing) where T : struct;
}
```

`Dispose` использует простой флаг `int` вместо `Interlocked`, поскольку область предназначена для однопоточного использования в главном потоке через `using var`.

### Использование

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Collections;
using Unity.Jobs;

async VlkTask ProcessDataAsync(int count, CancellationToken ct)
{
    // Оператор using гарантирует вызов Dispose() при выходе из области,
    // независимо от нормального завершения, исключения или отмены.
    using var inputScope  = TempNativeArrayScope.Create<float>(count);
    using var outputScope = TempNativeArrayScope.Create<float>(count);

    NativeArray<float> input  = inputScope.Array;
    NativeArray<float> output = outputScope.Array;

    // Заполнить input, запланировать задание.
    var job = new MyProcessingJob { Input = input, Output = output };
    var handle = job.Schedule(count, 64);

    // Ждать без блокирования главного потока.
    // NativeArray'ы остаются действительными — задание ещё выполняется.
    await handle.ToVlkTask(cancellationToken: ct);

    // Задание завершено. Здесь читаем результаты.
    float total = 0f;
    for (int i = 0; i < count; i++)
        total += output[i];

    UnityEngine.Debug.Log($"Sum: {total}");

    // inputScope.Dispose() и outputScope.Dispose() выполняются автоматически здесь.
}
```

Вы также можете обернуть уже выделенный массив:

```csharp
var existing = new NativeArray<int>(1024, Allocator.TempJob);
using var scope = TempNativeArrayScope.Wrap(existing);
// scope владеет existing и освободит его.
```

### Распространённая ловушка: время жизни NativeArray без области

Этот паттерн некорректен и вызовет ошибку системы безопасности:

```csharp
// НЕПРАВИЛЬНО: массив может пережить задание или быть утечкой при исключении.
var array = new NativeArray<float>(1024, Allocator.TempJob);
var handle = job.Schedule(1024, 64);
await handle.ToVlkTask(); // точка приостановки — массив должен оставаться живым
array.Dispose();          // никогда не достигается при исключении выше
```

Используйте `TempNativeArrayScope` или `try`/`finally` для гарантии освобождения во всех путях кода.

---

## AwaitableBridge — преобразование Unity Awaitable в VlkTask

`AwaitableBridge` предоставляет методы расширения для преобразования типов `Awaitable` и `Awaitable<T>` Unity (доступных с Unity 2023.1) в awaiter'ы, совместимые с `VlkTask`.

**Примечание:** У `Awaitable` также есть собственный `GetAwaiter()`. Поскольку разрешение перегрузок C# всегда предпочитает методы экземпляра методам расширения, написание `await myAwaitable` внутри `async VlkTask`-метода уже работает корректно — awaiter Unity реализует `ICriticalNotifyCompletion` и builder `VlkTask` принимает его. Методы расширения `.AsVlkTask()` нужны только когда вы хотите передать `Awaitable` в комбинатор (`VlkTask.WhenAll`, `VlkTask.WhenAny`) или сохранить его как переменную `VlkTask`.

Этот файл защищён `#if UNITY_2023_1_OR_NEWER`.

### API

```csharp
// Преобразовать Awaitable в awaiter, совместимый с VlkTask.
public static AwaitableVlkTaskAwaiter   AsVlkTask(this Awaitable awaitable)

// Преобразовать Awaitable<T> в awaiter, совместимый с VlkTask.
public static AwaitableVlkTaskAwaiter<T> AsVlkTask<T>(this Awaitable<T> awaitable)
```

Оба awaiter'а реализуют `ICriticalNotifyCompletion`, что пропускает захват `ExecutionContext`. Они делегируют `IsCompleted`, `GetResult` и `OnCompleted` напрямую обёрнутому awaiter'у Unity.

### Использование

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnityEngine;

// Прямое ожидание — работает без преобразования в async VlkTask-методе.
async VlkTask DirectExample()
{
    await Awaitable.NextFrameAsync();       // Преобразование не нужно.
    await Awaitable.WaitForSecondsAsync(1f);
}

// Явное преобразование — нужно для комбинаторов и хранения.
async VlkTask CombinatorExample()
{
    VlkTask a = Awaitable.NextFrameAsync().AsVlkTask();
    VlkTask b = Awaitable.WaitForSecondsAsync(2f).AsVlkTask();
    await VlkTask.WhenAll(a, b);
}

// Обобщённая версия с типом результата.
async VlkTask<Texture2D> LoadTextureExample(string path)
{
    Awaitable<Texture2D> loadOp = LoadTextureAsync(path);
    return await loadOp.AsVlkTask();
}
```

---

## JobBridge — обёртка, генерируемая исходным кодом

`JobBridge.cs` определяет `JobPromise<TJob>`, обобщённый пулируемый тип promise, используемый генератором исходного кода. Это деталь реализации; вы обычно не создаёте его напрямую.

```csharp
// Опрашивает JobHandle каждый кадр. Используется методами ScheduleAsync, генерируемыми исходным кодом.
public sealed class JobPromise<TJob> : VlkTask.ISource, IPlayerLoopItem, IPoolNode<JobPromise<TJob>>
    where TJob : struct
{
    public static JobPromise<TJob> Create(JobHandle handle, CancellationToken ct, out uint token);
}
```

Поведение идентично `JobHandlePromise` (см. `JobHandleExtensions`), за исключением того, что это обобщённый тип по типу задания для изоляции пулов — каждый тип задания получает свой пул.

---

## Генератор исходного кода: JobBridgeGenerator

`JobBridgeGenerator` — это инкрементальный генератор исходного кода Roslyn (класс `UnaPartidaMas.Valkarn.Tasks.SourceGen.Generators.JobBridgeGenerator`), автоматически создающий методы расширения `ScheduleAsync` для ваших типов заданий.

### Что обнаруживает

Генератор сканирует все **публичные** структуры в компиляции, реализующие один из:
- `Unity.Jobs.IJob`
- `Unity.Jobs.IJobParallelFor`
- `Unity.Jobs.IJobFor`

Приватные и внутренние структуры заданий пропускаются. Если структура вложена внутри непубличного типа, она также пропускается.

Генератор ничего не делает, если `UnaPartidaMas.Valkarn.Tasks.ValkarnTask` не найден в компиляции, поэтому он безопасен в сборках, не ссылающихся на Valkarn Tasks.

### Что генерирует

Выходной файл — `ValkarnTask.JobBridge.Generated.g.cs`. Для каждого обнаруженного типа задания генерирует `public static class __<TypeName>_AsyncExt`, содержащий:

| Интерфейс задания | Сгенерированная сигнатура метода |
|---|---|
| `IJob` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, CancellationToken ct = default)` |
| `IJobParallelFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleParallelAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |

Каждый сгенерированный метод планирует задание с использованием стандартных методов расширения Unity, затем оборачивает результирующий `JobHandle` в `JobPromise<TJob>` и возвращает `ValkarnTask`.

Для вложенных типов (например, структура задания внутри внешнего класса) имя сгенерированного класса использует подчёркивания: `__Outer_Inner_AsyncExt`.

### Использование сгенерированных методов

```csharp
// Пример IJob
public struct MyCalculationJob : IJob
{
    public NativeArray<float> Data;
    public void Execute() { /* ... */ }
}

// Генератор создаёт:
//   public static ValkarnTask ScheduleAsync(this ref MyCalculationJob job, CancellationToken ct = default)

async VlkTask RunCalculation(CancellationToken ct)
{
    using var scope = TempNativeArrayScope.Create<float>(1024);
    var job = new MyCalculationJob { Data = scope.Array };
    await job.ScheduleAsync(ct);  // сгенерированный метод расширения
    // Здесь читаем результаты из scope.Array.
}

// Пример IJobParallelFor
public struct MyParallelJob : IJobParallelFor
{
    public NativeArray<float> Input;
    public NativeArray<float> Output;
    public void Execute(int index) { Output[index] = Input[index] * 2f; }
}

async VlkTask RunParallel(int length, CancellationToken ct)
{
    using var inputScope  = TempNativeArrayScope.Create<float>(length);
    using var outputScope = TempNativeArrayScope.Create<float>(length);
    var job = new MyParallelJob { Input = inputScope.Array, Output = outputScope.Array };
    await job.ScheduleAsync(length, innerLoopBatchCount: 64, ct);
}
```

---

## Генератор исходного кода: AwaitableBridgeGenerator

`AwaitableBridgeGenerator` обнаруживает наличие `UnityEngine.Awaitable` и `UnityEngine.Awaitable<T>` в компиляции и генерирует методы расширения `AsValkarnTask()` при их обнаружении.

Выходной файл — `ValkarnTask.AwaitableBridge.Generated.g.cs`. Сгенерированный код находится в `namespace UnaPartidaMas.Valkarn.Tasks.Bridge` под классом `AwaitableBridgeExtensions`.

Сгенерированные методы:

```csharp
// Генерируется при обнаружении UnityEngine.Awaitable:
public static async ValkarnTask AsValkarnTask(this Awaitable awaitable)
{
    await awaitable;
}

// Генерируется при обнаружении UnityEngine.Awaitable<T>:
public static async ValkarnTask<T> AsValkarnTask<T>(this Awaitable<T> awaitable)
{
    return await awaitable;
}
```

Это методы `async ValkarnTask`, поэтому они проходят через пулируемый async builder Valkarn Tasks — при тёплом пуле без аллокаций.

Генератор защищён: если `ValkarnTask` не найден в компиляции, код не генерируется. Это предотвращает ошибки CS0246 в сборках, ссылающихся на Unity, но не на Valkarn Tasks.

---

## Полный рабочий пример: Мост задания в ECS-системе

Следующий пример из `Samples~/ECS/JobBridgeExample.cs`. Он показывает полный паттерн планирования Burst-параллельного задания из `ISystem`, его ожидания без блокировки и записи результатов обратно.

```csharp
#if UNITY_5_3_OR_NEWER && VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnaPartidaMas.Valkarn.Tasks.ECS;

public partial struct JobBridgeExample : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<HealthData>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var worldCt = state.World.GetWorldCancellationToken();

        // Извлечь все данные сущностей синхронно внутри OnUpdate.
        // Async-методы не могут иметь ref-параметры (CS1988), поэтому данные
        // должны быть скопированы здесь и переданы в async-метод по значению.
        var query       = SystemAPI.QueryBuilder().WithAll<HealthData>().Build();
        var entityCount = query.CalculateEntityCount();
        if (entityCount == 0) return;

        var entities    = query.ToEntityArray(Allocator.TempJob);
        var healthArray = query.ToComponentDataArray<HealthData>(Allocator.TempJob);
        var results     = new NativeArray<float>(entityCount, Allocator.TempJob);

        // Async-метод берёт владение NativeArray'ами и освобождает их.
        ProcessHealthAsync(state.EntityManager, entities, healthArray, results, worldCt).Forget();
        state.Enabled = false;
    }

    public void OnDestroy(ref SystemState state) { }

    static async VlkTask ProcessHealthAsync(
        EntityManager entityManager,
        NativeArray<Entity> entities,
        NativeArray<HealthData> healthArray,
        NativeArray<float> results,
        CancellationToken ct)
    {
        try
        {
            // Фаза 1: Запланировать Burst-задание.
            var job = new HealthProcessingJob
            {
                HealthInputs     = healthArray,
                ProcessedOutputs = results,
            };
            var handle = job.Schedule(entities.Length, batchSize: 64);

            // Фаза 2: Ждать завершения без блокировки главного потока.
            await handle.ToVlkTask(cancellationToken: ct);

            // Фаза 3: Применить результаты. Мы снова в главном потоке.
            ct.ThrowIfCancellationRequested();

            for (int i = 0; i < entities.Length; i++)
            {
                // Сущность может быть уничтожена пока задание выполнялось.
                if (!entityManager.SafeEntityExists(entities[i]))
                    continue;

                entityManager.SetComponentData(entities[i], new HealthData
                {
                    CurrentHealth = results[i],
                });
            }
        }
        finally
        {
            // Всегда освобождать NativeArray'ы — выполняется при успехе, исключении или отмене.
            if (entities.IsCreated)    entities.Dispose();
            if (healthArray.IsCreated) healthArray.Dispose();
            if (results.IsCreated)     results.Dispose();
        }
    }

    [BurstCompile]
    struct HealthProcessingJob : IJobParallelFor
    {
        [ReadOnly]  public NativeArray<HealthData> HealthInputs;
        [WriteOnly] public NativeArray<float>      ProcessedOutputs;

        public void Execute(int index)
        {
            var h = HealthInputs[index];
            var newHealth = h.CurrentHealth + h.RegenRate;
            if (newHealth > h.MaxHealth) newHealth = h.MaxHealth;
            ProcessedOutputs[index] = newHealth;
        }
    }

    struct HealthData : IComponentData
    {
        public float CurrentHealth;
        public float MaxHealth;
        public float RegenRate;
    }
}
#endif
```

---

## Сводка типов мостов

| Тип | Назначение | Аллокация |
|---|---|---|
| `JobHandleExtensions.ToVlkTask()` | Ожидать один `JobHandle` | Ноль на быстром пути; иначе пулируемый promise |
| `JobHandleExtensions.WhenAll()` | Ожидать несколько `JobHandle` параллельно | Ноль на быстром пути; иначе пулируемый promise + аренда `ArrayPool` |
| `TempNativeArrayScope<T>` | RAII-управление временем жизни `NativeArray` | Нет (структура) |
| `AwaitableBridge.AsVlkTask()` | Преобразовать `Awaitable`/`Awaitable<T>` в VlkTask | Нет (awaiter-структура) |
| Сгенерированный `ScheduleAsync()` | Ожидать типизированное задание напрямую | Пулируемый `JobPromise<TJob>` |
