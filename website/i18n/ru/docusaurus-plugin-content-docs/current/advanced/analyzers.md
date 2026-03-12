---
sidebar_position: 1
title: Правила анализатора
---

# Правила анализатора

Valkarn Tasks поставляется с двумя пакетами анализаторов Roslyn, которые активируются автоматически при импорте пакета:

- **`UnaPartidaMas.Valkarn.Tasks.SourceGen.dll`** — правила для корректности ValkarnTask и жизненного цикла Unity. Идентификаторы правил начинаются с `TT`.
- **`UnaPartidaMas.Valkarn.Tasks.Analyzer.dll`** — правила миграции для кодовых баз, переходящих с UniTask. Идентификаторы правил начинаются с `MIG`. Эти правила срабатывают **только при наличии `Cysharp.Threading.Tasks.UniTask` в компиляции**, поэтому в новых проектах они молчат.

Оба пакета — предварительно скомпилированные DLL, расположенные в папке `Analyzers/` пакета. Unity загружает их как анализаторы Roslyn через ссылку `.asmdef`; проект `_TestRunner~` загружает их через элементы `<Analyzer>` в `TestRunner.csproj`.

---

## Правила корректности (TT)

Эти правила выявляют ошибки, связанные с природой единственного потребления `ValkarnTask` и неправильным использованием fire-and-forget.

---

### TT001 — ValkarnTask уже ожидался

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Warning               |
| Категория  | ValkarnTask            |
| Исправление кода | Нет              |

`ValkarnTask` рассчитан на единственное потребление: после ожидания внутренний токен устаревает. Второй `await` той же переменной встретит устаревший токен и выбросит исключение. Анализатор обнаруживает, когда одна и та же локальная переменная, параметр или поле типа `ValkarnTask`/`ValkarnTask<T>` ожидается более одного раза внутри одного метода.

**Срабатывает на:**
```csharp
async ValkarnTask Bad()
{
    ValkarnTask work = DoWorkAsync();
    await work;   // первый await — нормально
    await work;   // TT001: уже ожидался
}
```

**Исправление:** если нужно ветвление по результату, захватите его через `.AsResult()` до первого await, или перестройте код так, чтобы задача ожидалась ровно один раз.
```csharp
async ValkarnTask Good()
{
    var result = await DoWorkAsync().AsResult();
    // используем result в обоих ветках
}
```

---

### TT002 — ValkarnTask не ожидался и не отброшен

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | **Error**             |
| Категория  | ValkarnTask            |
| Исправление кода | Нет              |

Вызов метода, возвращающего `ValkarnTask`, использованный как выражение-оператор — без await, без присваивания и без последующего `.Forget()` — является скрытой ошибкой. Исключения, выброшенные внутри задачи, никогда не наблюдаются, а пулируемый конечный автомат задачи никогда не возвращается в пул.

Анализатор проверяет выражения-операторы, где тип выражения разрешается в `ValkarnTask` или `ValkarnTask<T>`. Пропускает:
- Выражения присваивания (`tasks[i] = DoWork()`)
- Цепочки, оканчивающиеся на `.Forget()` (намеренный fire-and-forget)

**Срабатывает на:**
```csharp
void Bad()
{
    LoadDataAsync();      // TT002: не ожидался и не отброшен
    ProcessItemAsync();   // TT002
}
```

**Исправление — ожидать:**
```csharp
async ValkarnTask Good()
{
    await LoadDataAsync();
    await ProcessItemAsync();
}
```

**Исправление — явный fire-and-forget:**
```csharp
void GoodFireAndForget()
{
    LoadDataAsync().Forget();
}
```

---

### TT013 — ValkarnTask возвращён, но никогда не потреблён

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Warning               |
| Категория  | ValkarnTask            |
| Исправление кода | Нет              |

Этот идентификатор правила **зарезервирован для будущего анализа потока данных**. Предназначен для обнаружения паттерна «присвоено, но никогда не ожидалось» — сохранение `ValkarnTask` в переменную и последующее неожидание и неотбрасывание её — что TT002 не охватывает, поскольку TT002 проверяет только голые выражения-операторы.

Текущая реализация не регистрирует синтаксических действий. Когда анализ потока данных будет реализован, TT013 дополнит TT002, охватывая:
```csharp
async ValkarnTask Bad()
{
    ValkarnTask task = DoWorkAsync();  // присвоено, но никогда не ожидалось
    DoOtherThing();
    // task заброшен — TT013 (в будущем)
}
```

Методы, украшенные `[FireAndForget]`, будут освобождены от этого правила.

---

## Правила жизненного цикла (TT)

Эти правила связаны с управлением временем жизни MonoBehaviour и отменой.

---

### TT010 — Авто-отмена активна для async-метода MonoBehaviour

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Info                  |
| Категория  | ValkarnTask            |
| Исправление кода | Нет              |

Информационная подсказка. Любой `async ValkarnTask` (или `async ValkarnTask<T>`) метод внутри класса, наследующего от `UnityEngine.MonoBehaviour`, будет автоматически отменён при уничтожении объекта, **если только** метод не украшен `[NoAutoCancel]`. Этот диагностический вывод делает такое поведение видимым в IDE без необходимости помнить о нём.

**Срабатывает на:**
```csharp
public class MyBehaviour : MonoBehaviour
{
    async ValkarnTask LoadLevel()  // TT010: авто-отмена активна
    {
        await ValkarnTask.Delay(2000);
    }
}
```

**Для отказа** (и подавления TT010 для этого метода) добавьте `[NoAutoCancel]`:
```csharp
[NoAutoCancel]
async ValkarnTask LoadLevel(CancellationToken ct)
{
    await ValkarnTask.Delay(2000, ct);
}
```

---

### TT011 — WhenAll смешивает разные области времени жизни

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Warning               |
| Категория  | ValkarnTask            |
| Исправление кода | Нет              |

`ValkarnTask.WhenAll`, вызванный с задачами из разных областей времени жизни, может привести к неожиданному поведению частичной отмены. Если одна задача привязана к `MonoBehaviour` (возвращена методом экземпляра класса, наследующего `MonoBehaviour`), а другая не привязана (статическая задача или из класса, не являющегося MonoBehaviour), уничтожение объекта отменит одну задачу, но не другую, оставив комбинатор в неопределённом состоянии.

**Срабатывает на:**
```csharp
// Предполагая EnemyAI : MonoBehaviour
await ValkarnTask.WhenAll(
    enemyAI.PatrolAsync(),   // привязана: автоматически отменяется при Destroy
    GlobalMusic.FadeAsync()  // не привязана: живёт бесконечно
);
// TT011: WhenAll смешивает времена жизни — PatrolAsync() против GlobalMusic.FadeAsync()
```

**Исправление — дать обеим задачам общий токен отмены:**
```csharp
using var cts = new CancellationTokenSource();
await ValkarnTask.WhenAll(
    enemyAI.PatrolAsync(cts.Token),
    GlobalMusic.FadeAsync(cts.Token)
);
```

---

### TT012 — Async-цикл без проверки отмены

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Warning               |
| Категория  | ValkarnTask            |
| Исправление кода | Нет              |

Метод `async ValkarnTask`, содержащий цикл `for`, `while`, `foreach` или `do`-`while`, тело которого не имеет проверки отмены, является «зомби-циклом»: если срабатывает отмена жизненного цикла (например, MonoBehaviour уничтожен), сигнал авто-отмены не может прервать цикл. Цикл продолжает выполняться, даже если владеющий объект уже уничтожен.

Анализатор считает, что тело цикла имеет проверку отмены, если оно содержит любое из:
- Выражение `await` (ожидаемая операция может наблюдать токен и выбросить `OperationCanceledException`)
- `ThrowIfCancellationRequested` (как идентификатор или доступ к члену)
- `IsCancellationRequested` (как идентификатор или доступ к члену)

Проверка **не** спускается во вложенные лямбды или локальные функции.

**Срабатывает на:**
```csharp
async ValkarnTask PollForever(CancellationToken ct)
{
    while (true)                     // TT012: нет проверки отмены в теле
    {
        ProcessNextItem();
        Thread.Sleep(16);            // синхронное — не await
    }
}
```

**Исправление — добавить await или явную проверку:**
```csharp
async ValkarnTask PollForever(CancellationToken ct)
{
    while (true)
    {
        ct.ThrowIfCancellationRequested();
        ProcessNextItem();
        await ValkarnTask.Yield();       // также удовлетворяет проверке
    }
}
```

---

### TT014 — [NoAutoCancel] без параметра CancellationToken

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Warning               |
| Категория  | ValkarnTask            |
| Исправление кода | Нет              |

`[NoAutoCancel]` на `async ValkarnTask`-методе в `MonoBehaviour` отказывает методу в автоматической отмене жизненного цикла. Но если метод не имеет параметра `CancellationToken`, у него нет механизма для наблюдения отмены вообще, что делает атрибут бессмысленным и почти наверняка указывает на забытый параметр.

**Срабатывает на:**
```csharp
public class Enemy : MonoBehaviour
{
    [NoAutoCancel]
    async ValkarnTask Chase()      // TT014: [NoAutoCancel] без параметра CancellationToken
    {
        await ValkarnTask.Delay(1000);
    }
}
```

**Исправление — добавить параметр `CancellationToken`:**
```csharp
[NoAutoCancel]
async ValkarnTask Chase(CancellationToken ct)
{
    await ValkarnTask.Delay(1000, ct);
}
```

---

## Информационные правила (TT)

---

### TT015 — Сгенерирован мост-адаптер для Awaitable

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Info                  |
| Категория  | ValkarnTask            |
| Исправление кода | Нет              |

Когда вы `await` Unity `Awaitable` (из `UnityEngine`) внутри `async ValkarnTask`-метода, Valkarn Tasks автоматически генерирует мост-адаптер через `AwaitableBridge`. Этот диагностический вывод информационный: он подтверждает, что мост активен. Никаких действий не требуется.

**Срабатывает на:**
```csharp
async ValkarnTask LoadScene()
{
    await SceneManager.LoadSceneAsync("Main");  // TT015: сгенерирован мост-адаптер
}
```

Никаких изменений не нужно. Мост обрабатывает преобразование прозрачно.

---

## Правила качества кода (TT)

---

### TT016 — Async-метод без await

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Warning               |
| Категория  | ValkarnTask            |
| Исправление кода | Нет              |

Метод `async ValkarnTask` (или `async ValkarnTask<T>`), не содержащий выражений `await` — включая `await foreach`, `await using` и `await` внутри объявлений `using` — несёт полные накладные расходы на аллокацию конечного автомата без какой-либо пользы. Компилятор всё равно генерирует конечный автомат, но поскольку точки приостановки нет, метод всегда завершается синхронно.

Анализатор проверяет: `AwaitExpressionSyntax`, `foreach` с ключевым словом `await`, объявления `using` с ключевым словом `await` и операторы `using` с ключевым словом `await`.

**Срабатывает на:**
```csharp
async ValkarnTask<int> ComputeTotal()  // TT016: нет await в теле
{
    return items.Sum(x => x.Value);
}
```

**Исправление — удалить `async` и вернуть завершённую задачу:**
```csharp
ValkarnTask<int> ComputeTotal()
{
    return ValkarnTask.FromResult(items.Sum(x => x.Value));
}
```

Или для void-возврата:
```csharp
ValkarnTask DoSetup()
{
    Initialize();
    return ValkarnTask.CompletedTask;
}
```

---

### TT017 — [FireAndForget] на ValkarnTask\<T\>

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Warning               |
| Категория  | ValkarnTask            |
| Исправление кода | Нет              |

`[FireAndForget]` сигнализирует, что метод намеренно запускается без ожидания его результата. Применение его к методу, возвращающему `ValkarnTask<T>` (типизированный результат), всегда отбрасывает `T`, делая типизированный возврат бессмысленным. Метод должен возвращать `ValkarnTask` (void) вместо этого.

**Срабатывает на:**
```csharp
[FireAndForget]
async ValkarnTask<int> SendReport()   // TT017: возвращаемое значение отбрасывается
{
    await UploadAsync();
    return 42;                     // это 42 никогда не увидят
}
```

**Исправление — изменить тип возврата на `ValkarnTask`:**
```csharp
[FireAndForget]
async ValkarnTask SendReport()
{
    await UploadAsync();
}
```

---

## Правила миграции (MIG)

Анализатор миграции активируется **только при наличии типа `Cysharp.Threading.Tasks.UniTask`** в компиляции. Правила носят информационный или предупредительный характер для сопровождения перехода с UniTask на Valkarn Tasks. Ни одно из этих правил не имеет автоисправлений; описания ниже объясняют ручное изменение.

---

### MIG001 — Обнаружен тип UniTask

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Info                  |
| Категория  | ValkarnTaskMigration   |

Обнаружено использование типа `UniTask` (самой структуры или `UniTask<T>`). Замените его на эквивалент `ValkarnTask` или `ValkarnTask<T>`.

```csharp
// До
UniTask<Sprite> LoadSprite(string path) { ... }

// После
ValkarnTask<Sprite> LoadSprite(string path) { ... }
```

---

### MIG002 — Параметр cancelImmediately не нужен

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Info                  |
| Категория  | ValkarnTaskMigration   |

`Delay` и другие методы UniTask, основанные на времени, принимают параметр `cancelImmediately` для быстрой отмены. Valkarn Tasks отменяет немедленно по умолчанию — параметра `cancelImmediately` нет. Удалите аргумент.

```csharp
// До
await UniTask.Delay(1000, cancelImmediately: true, cancellationToken: ct);

// После
await ValkarnTask.Delay(1000, ct);
```

---

### MIG003 — Обнаружен SuppressCancellationThrow()

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Warning               |
| Категория  | ValkarnTaskMigration   |

`.SuppressCancellationThrow()` из UniTask преобразует отменённую задачу в кортеж `(bool isCancelled, T result)` без выброса исключения. Valkarn Tasks использует `.AsResult()` для той же цели, возвращая структуру `Result<T>`.

```csharp
// До
var (isCancelled, value) = await myUniTask.SuppressCancellationThrow();

// После
var result = await myValkarnTask.AsResult();
if (result.IsCanceled) { ... }
T value = result.Value;
```

---

### MIG004 — Обнаружен тип возврата Awaitable

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Info                  |
| Категория  | ValkarnTaskMigration   |

Метод возвращает тип `Awaitable` от Unity. Рассмотрите замену на `ValkarnTask`, который нативно интегрируется со средой выполнения Valkarn Tasks и поддерживает авто-отмену, пулирование и полный набор комбинаторов.

---

### MIG005 — Обнаружен канал SingleConsumerUnbounded

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Warning               |
| Категория  | ValkarnTaskMigration   |

`Channel.CreateSingleConsumerUnbounded<T>()` из UniTask создаёт канал без обратного давления. Рассмотрите замену на `ValkarnTask.Channel.CreateBounded<T>(capacity)` для добавления обратного давления и предотвращения неограниченного роста памяти под нагрузкой.

```csharp
// До
var ch = Channel.CreateSingleConsumerUnbounded<Event>();

// После (с обратным давлением)
var ch = ValkarnTask.Channel.CreateBounded<Event>(capacity: 256);

// Или если неограниченность намеренна
var ch = ValkarnTask.Channel.CreateUnbounded<Event>();
```

---

### MIG006 — Обнаружен UniTask PlayerLoopTiming

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Info                  |
| Категория  | ValkarnTaskMigration   |

Обнаружено значение enum `PlayerLoopTiming` из пространства имён `Cysharp.Threading.Tasks`. Измените директиву `using` на `UnaPartidaMas.Valkarn.Tasks`; имена значений enum идентичны.

```csharp
// До
using Cysharp.Threading.Tasks;
timing = PlayerLoopTiming.Update;

// После
using UnaPartidaMas.Valkarn.Tasks;
timing = PlayerLoopTiming.Update;  // то же имя, другое пространство имён
```

---

### MIG007 — Обнаружен async UniTaskVoid

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Warning               |
| Категория  | ValkarnTaskMigration   |

`async UniTaskVoid` был паттерном fire-and-forget метода в UniTask. Valkarn Tasks заменяет его двумя вариантами:

```csharp
// До
async UniTaskVoid StartLoadAsync() { ... }

// После — вариант 1: атрибут [FireAndForget]
[FireAndForget]
async ValkarnTask StartLoadAsync() { ... }

// После — вариант 2: стандартный ValkarnTask + .Forget() на месте вызова
async ValkarnTask StartLoadAsync() { ... }
// Вызывается как:
StartLoadAsync().Forget();
```

---

### MIG008 — Обнаружен Awaitable MainThreadAsync()

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Info                  |
| Категория  | ValkarnTaskMigration   |

`Awaitable.MainThreadAsync()` использовался в API Unity `Awaitable` для возврата в основной поток. Valkarn Tasks по умолчанию запускает продолжения в основном потоке через интеграцию с PlayerLoop, поэтому явные вызовы `MainThreadAsync()` обычно излишни и могут быть удалены.

---

### MIG009 — Обнаружен UniTask.RunOnThreadPool

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Warning               |
| Категория  | ValkarnTaskMigration   |

Замените на `ValkarnTask.RunOnThreadPool`. API идентичен.

```csharp
// До
await UniTask.RunOnThreadPool(() => HeavyWork());

// После
await ValkarnTask.RunOnThreadPool(() => HeavyWork());
```

---

### MIG010 — Обнаружен .ToCoroutine()

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Warning               |
| Категория  | ValkarnTaskMigration   |

`.ToCoroutine()` был мостом UniTask для устаревших вызывающих на корутинах. Перепишите потребляющий код как метод `async ValkarnTask` вместо этого.

```csharp
// До
IEnumerator LegacyCaller() { yield return MyUniTask().ToCoroutine(); }

// После
async ValkarnTask ModernCaller() { await MyValkarnTask(); }
```

---

### MIG011 — Обнаружен UniTask.Create()

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Warning               |
| Категория  | ValkarnTaskMigration   |

`UniTask.Create(Func<UniTask>)` оборачивает делегат-фабрику. Замените паттерном `ValkarnTask.Promise<T>` для управления завершением вручную.

```csharp
// До
var task = UniTask.Create(async () => { await DoWork(); return 42; });

// После
var promise = new ValkarnTaskCompletionSource<int>();
DoWorkThenComplete(promise);
var task = promise.Task;
```

---

### MIG012 — Обнаружен UniTask.Lazy/Defer

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Info                  |
| Категория  | ValkarnTaskMigration   |

`UniTask.Lazy<T>` и `UniTask.Defer` существовали для избежания аллокаций, когда задача могла завершиться синхронно. Valkarn Tasks имеет встроенный быстрый путь без аллокаций для синхронного случая: возврат `ValkarnTask.CompletedTask` или `ValkarnTask.FromResult(value)` никогда не выделяет память. Удалите обёртки `Lazy`/`Defer`.

---

### MIG013 — Обнаружен .ToUniTask()/.AsUniTask()

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Info                  |
| Категория  | ValkarnTaskMigration   |

Вызовы преобразования из Unity `Awaitable` в UniTask. Удалите их; Valkarn Tasks нативно поддерживает мост для `Awaitable` (см. TT015).

---

### MIG014 — Обнаружен UniTaskAsyncEnumerable

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Warning               |
| Категория  | ValkarnTaskMigration   |

UniTask поставлялся с собственными `IUniTaskAsyncEnumerable<T>` и утилитами `UniTaskAsyncEnumerable`. Используйте вместо них `IAsyncEnumerable<T>` из BCL с `System.Linq.Async`. `IAsyncEnumerable<T>` нативно поддерживается C# через `await foreach`.

```csharp
// До
IUniTaskAsyncEnumerable<int> GetItems() { ... }

// После
IAsyncEnumerable<int> GetItems() { ... }
```

---

### MIG015 — Обнаружен TimeoutController

| Свойство   | Значение               |
|------------|------------------------|
| Серьёзность | Info                  |
| Категория  | ValkarnTaskMigration   |

`TimeoutController` из UniTask был вспомогательным классом для повторно используемых таймаутов. Замените стандартным `CancellationTokenSource`, созданным с `TimeSpan`, что BCL поддерживает напрямую.

```csharp
// До
var controller = new TimeoutController();
var ct = controller.Timeout(TimeSpan.FromSeconds(5));

// После
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var ct = cts.Token;
```

---

## Подавление правил

Стандартные механизмы подавления Roslyn работают для всех правил:

```csharp
// Подавить одно вхождение встроенно
#pragma warning disable TT012
while (true) { DoSomething(); }
#pragma warning restore TT012
```

Или через `.editorconfig` для подавления на уровне проекта:
```ini
[*.cs]
dotnet_diagnostic.TT012.severity = none
```

Правила миграции (`MIG*`) могут быть подавлены тем же способом, или отключены глобально после завершения миграции, установив серьёзность в `none` в `.editorconfig`.
