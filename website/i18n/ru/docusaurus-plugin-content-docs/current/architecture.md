---
sidebar_position: 5
title: Архитектура
---

# Архитектура

Технический обзор внутреннего устройства Valkarn Tasks.

---

## Структура верхнего уровня

```
┌─────────────────────────────────────────────────────────────────┐
│                    ВРЕМЯ КОМПИЛЯЦИИ                               │
│                                                                   │
│  ┌──────────────┐  ┌──────────────────┐  ┌───────────────────┐  │
│  │  Анализатор   │  │  Генерация моста │  │  Диагностика      │  │
│  │  жизн. цикла  │  │  Awaitable       │  │  (TT001–TT017)    │  │
│  └──────────────┘  └──────────────────┘  └───────────────────┘  │
│                                                                   │
│  ┌──────────────┐  ┌──────────────────┐                          │
│  │  Генерация    │  │  Генерация       │                          │
│  │  моста Job    │  │  комбинаторов    │                          │
│  └──────────────┘  └──────────────────┘                          │
├─────────────────────────────────────────────────────────────────┤
│                    ВРЕМЯ ВЫПОЛНЕНИЯ                               │
│                                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐  │
│  │ ValkarnTask │  │ Result<T>  │  │ ValkarnPool│  │ Completion│  │
│  │  struct    │  │  struct    │  │  bounded   │  │  Core<T>  │  │
│  └────────────┘  └────────────┘  └────────────┘  └───────────┘  │
│                                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐  │
│  │ PlayerLoop │  │Continuation│  │  Channels  │  │ TestClock │  │
│  │  Helper    │  │   Queue    │  │            │  │           │  │
│  └────────────┘  └────────────┘  └────────────┘  └───────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### Расположение сборок

```
ValkarnTask.Runtime      — поставляется вместе с игрой
ValkarnTask.SourceGen    — только время компиляции (генератор кода)
ValkarnTask.Analyzer     — только время компиляции (диагностика + исправления кода)
ValkarnTask.Testing      — TestClock + тестовые утилиты
```

---

## Структура ValkarnTask

```csharp
[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask
{
    internal readonly ValkarnTask.ISource source;
    internal readonly ulong token; // packed: high 32 bits = generation, low 32 = slot index
}

[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
{
    internal readonly ValkarnTask.ISource<T> source;
    internal readonly T result;  // inline on sync fast path
    internal readonly ulong token;
}
```

**Ключевый инвариант:** `source == null` означает, что задача завершилась синхронно без ошибок — ни одного объекта в куче не было задействовано. `ValkarnTask.CompletedTask` — это `default(ValkarnTask)`; `ValkarnTask.FromResult(value)` хранит результат встроенно.

### Поколенческий токен

```csharp
// Упаковка
ulong token = ((ulong)generation << 32) | slotIndex;

// Распаковка
uint slotIndex  = (uint)(token & 0xFFFFFFFF);
uint generation = (uint)(token >> 32);
```

Проверяется при каждом вызове ISource: `slots[slotIndex].generation == expectedGeneration`. Устаревшая ссылка на переработанный слот пула немедленно бросает `InvalidOperationException`. 4 миллиарда поколений на слот — коллизия на практике невозможна. (UniTask использует `short` — коллизия примерно через 18 минут.)

---

## Контракт ISource

```csharp
public interface ISource
{
    Status GetStatus(ulong token);
    void   GetResult(ulong token);
    void   OnCompleted(Action<object> continuation, object state, ulong token);
    Status UnsafeGetStatus();
}

public interface ISource<out T> : ISource
{
    new T GetResult(ulong token);
}
```

Любой объект, реализующий `ISource`, может поддерживать `ValkarnTask`. Встроенные реализации:

| Тип | Назначение |
|------|---------|
| `AsyncValkarnRunner<TStateMachine>` | Поддерживает каждый метод `async ValkarnTask` |
| `AsyncValkarnRunner<TStateMachine, T>` | Поддерживает каждый метод `async ValkarnTask<T>` |
| `ValkarnTask.PooledPromise[<T>]` | Ручное завершение, автоматический возврат в пул |
| `ValkarnTask.Promise[<T>]` | Ручное завершение, не пулируется (долгоживущий) |
| `ExceptionSource` | Поддерживает `FromException` |
| `CanceledSource` | Поддерживает `FromCanceled` |
| `NeverSource` | Синглтон — никогда не переходит из Pending |

---

## Строитель асинхронного метода

Компилятор C# управляет протоколом строителя для пользовательских асинхронных возвращаемых типов:

```
Create()
  └─ возвращает структуру строителя (ноль выделений)

Start(ref stateMachine)
  └─ запускает машину состояний синхронно
     ├─ завершается без приостановки → SetResult(), исполнитель остаётся null
     │  └─ Task возвращает default(ValkarnTask)  ← ноль выделений
     └─ встречает незавершённый await → AwaitUnsafeOnCompleted()
           └─ арендует AsyncValkarnRunner из пула
              копирует машину состояний в исполнитель (по значению, без боксинга)
              регистрирует продолжение на awaitable
              └─ Task оборачивает исполнитель как ISource  ← асинхронный путь
```

Сам строитель — это `struct` — никакого выделения только ради строителя. `runner` выделяется **лениво**: если метод завершается синхронно, аренда из пула никогда не происходит.

---

## Исполнитель машины состояний и пул

`AsyncValkarnRunner<TStateMachine>` хранит сгенерированную компилятором машину состояний **по значению** (без боксинга) и выступает как `ISource`. Он арендуется из `ValkarnPool<T>` при первой приостановке и возвращается в `GetResult`.

Поскольку `TStateMachine` — уникальный тип для каждого асинхронного метода (для каждого экземпляра замкнутого обобщения), каждый асинхронный метод автоматически получает собственный пул через специализацию обобщений C#.

### ValkarnPool\<T\>

| Контекст | Структура | Причина |
|---------|-----------|--------|
| Основной поток Unity | Однопоточный стек | Нет синхронизации — максимальная скорость |
| Фоновые потоки | Стек Treiber без блокировок | Операции CAS, без локов |

Контекст потока определяется через `Thread.CurrentThread.IsBackground`. Форма пула (ёмкость, скорость обрезки) настраивается через `ValkarnTaskSettings`.

---

## ValkarnCompletionCore\<T\>

Общее состояние внутри каждой реализации `ISource`:

- Текущий `Status` (Pending / Succeeded / Faulted / Canceled)
- Значение результата (обобщённые источники)
- Исключение или `OperationCanceledException` (пути ошибок)
- Зарегистрированный делегат продолжения + состояние

Переходы статуса используют `Interlocked.CompareExchange` — без блокировок, потокобезопасно. **Защита от двойного завершения** гарантирует, что только первый вызов `TrySet*` выигрывает; последующие вызовы — молчаливые no-op.

---

## Интеграция PlayerLoop

`PlayerLoopHelper` вставляет лёгкие обратные вызовы исполнителя в PlayerLoop Unity при запуске (`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]`).

Каждое значение `PlayerLoopTiming` соответствует фазе. Когда вызывается `await ValkarnTask.Yield(timing)`, продолжение ставится в очередь исполнителя этой фазы и диспетчеризуется при следующем достижении Unity этой фазы.

```
Initialization  →  EarlyUpdate  →  FixedUpdate  →  PreUpdate
→  Update  →  PreLateUpdate  →  PostLateUpdate  →  TimeUpdate
  (+ варианты Last* для каждой фазы)
```

---

## Генератор кода

Генератор кода Roslyn запускается во время компиляции. Для каждого класса `partial`, расширяющего `MonoBehaviour` и содержащего методы `async ValkarnTask`, он генерирует файл partial-класса, который:

1. Объявляет поле `_valkarnCancelToken`
2. Присваивает его из `destroyCancellationToken` в `Awake`
3. Оборачивает каждый асинхронный метод для автоматической передачи токена

Сгенерированный файл никогда не показывается в отладчике и никогда не изменяет пользовательский исходный код.

Генератор также производит:

- **Адаптеры моста Awaitable** — когда `Awaitable` ожидается внутри `async ValkarnTask`
- **Асинхронные обёртки Job** — когда обнаруживаются типы `IJob` / `IJobParallelFor`
- **Пулы комбинаторов** — типизированные источники `WhenAll`/`WhenAny` для кортежей с арностью 2–8

---

## Анализаторы Roslyn

17 правил `DiagnosticAnalyzer` поставляются в `Analyzers/netstandard2.0/`. Они запускаются во время прохода компилятора C# в редакторе Unity и в CI:

- Все используют `SemanticModel` для разрешения типов (не сопоставление строк)
- Общая утилита `ValkarnTypeHelper` обнаруживает любой вариант `ValkarnTask`
- Анализатор цикла-зомби корректно пропускает вложенные локальные функции и лямбды
- Анализаторы миграции (MIG001–MIG015) автоматически активируются только при наличии ссылок на UniTask / Awaitable — в остальных случаях бездействуют

---

## Слой Burst и ECS

Три опциональных модуля, каждый защищён проверками директивы `#if`:

| Модуль | Требует | Назначение |
|--------|----------|---------|
| `JobBridge` | `Unity.Jobs` | Оборачивает `JobHandle` как awaitable; опрашивает `handle.IsCompleted` каждый тик PlayerLoop |
| `AsyncSystemBase` | `Unity.Entities` | Базовый класс системы ECS с поддержкой async |
| `BurstScheduler` | `Unity.Burst` + `Unity.Collections` | Планирует Burst-задания из асинхронного контекста; управляет `NativeTimerHeap` |

`NativeTimerHeap` — совместимая с Burst минимальная куча для высокоточных таймеров, которая полностью избегает выделений в управляемой куче.

---

## Интеграция с редактором

**Valkarn Hub** (`Tools → Valkarn → Hub`) использует `TypeCache.GetTypesDerivedFrom<IValkarnEditorPanel>()` для автоматического обнаружения всех установленных пакетов Valkarn. Ручная регистрация не требуется.

`TasksTrackerPanel` подписывается на `EditorApplication.update` для обновления диагностики пула каждые 0.5 с (настраивается) и предоставляет ссылку на ассет `ValkarnTaskSettings` для быстрого доступа.

---

## Рекомендации для IL2CPP

- Машины состояний хранятся **по значению** внутри исполнителей — без боксинга, IL2CPP обрабатывает корректно
- Исполнитель каждого асинхронного метода — отдельная специализация обобщения — типобезопасно, без перекрёстного загрязнения
- Структуры Awaiter реализуют `ICriticalNotifyCompletion` — компилятор вызывает `UnsafeOnCompleted`, пропуская захват `ExecutionContext` (нет накладных расходов в конфигурации Unity по умолчанию)
- Если включена агрессивная обрезка, сохраните сборку времени выполнения:

```xml
<!-- link.xml -->
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```
