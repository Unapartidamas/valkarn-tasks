---
sidebar_position: 7
title: ValkarnSemaphore
---

# ValkarnSemaphore

`ValkarnSemaphore` — это асинхронный семафор без выделения памяти, построенный на `ValkarnTask`. Он работает как `SemaphoreSlim`, но нативно интегрируется с конвейером Valkarn Tasks — без выделений `Task`, без давления на GC в установившемся режиме.

```csharp
namespace UnaPartidaMas.Valkarn.Tasks
```

**Быстрый путь (без конкуренции):** немедленно возвращает `ValkarnTask.CompletedTask` — нулевое выделение памяти.
**Медленный путь (с конкуренцией):** узлы ожидания берутся из ограниченного пула и возвращаются в него при завершении — нулевой GC в установившемся режиме.

---

## Конструктор

```csharp
public ValkarnSemaphore(int initialCount, int maxCount)
```

| Параметр | Описание |
|-----------|-------------|
| `initialCount` | Количество слотов, доступных немедленно. Должно быть в диапазоне `[0, maxCount]`. |
| `maxCount` | Верхняя граница количества слотов. Должно быть > 0. |

---

## Свойства

```csharp
public int CurrentCount { get; } // Доступных слотов прямо сейчас (потокобезопасно)
```

---

## Методы

### WaitAsync

```csharp
public ValkarnTask WaitAsync(CancellationToken ct = default)
```

Асинхронно занимает один слот. Немедленно возвращает завершённый `ValkarnTask`, если слот доступен (нулевое выделение). Приостанавливает вызывающего до вызова `Release()`, если слотов нет. Ожидающие обслуживаются в порядке FIFO.

Генерирует `OperationCanceledException`, если `ct` срабатывает во время ожидания.

### Wait

```csharp
public void Wait(CancellationToken ct = default)
```

Синхронный вариант. Блокирует вызывающий поток, если слотов нет. Используйте только когда нельзя применить `await` (например, в неасинхронных точках входа).

### Release

```csharp
public void Release(int releaseCount = 1)
```

Возвращает `releaseCount` слотов. Ожидающие завершаются в порядке FIFO вне внутреннего блокировщика — продолжения никогда не выполняются при удержании блокировки.

Генерирует `InvalidOperationException`, если освобождение превысит `maxCount`.

### Dispose

```csharp
public void Dispose()
```

Отменяет все ожидающие операции и освобождает ресурсы. Идемпотентен.

---

## Использование

### Взаимное исключение (1 слот)

```csharp
var sem = new ValkarnSemaphore(initialCount: 1, maxCount: 1);

async ValkarnTask WriteFileAsync(string path, string content, CancellationToken ct)
{
    await sem.WaitAsync(ct);
    try
    {
        await File.WriteAllTextAsync(path, content, ct);
    }
    finally
    {
        sem.Release();
    }
}
```

### Ограниченный параллелизм (N слотов)

```csharp
// Разрешить до 4 одновременных сетевых запросов.
var sem = new ValkarnSemaphore(initialCount: 4, maxCount: 4);

async ValkarnTask FetchAsync(string url, CancellationToken ct)
{
    await sem.WaitAsync(ct);
    try
    {
        await DownloadAsync(url, ct);
    }
    finally
    {
        sem.Release();
    }
}
```

### Шлюз производитель / потребитель

```csharp
// Начинаем с 0 слотов — потребитель ждёт, пока производитель не подаст сигнал.
var gate = new ValkarnSemaphore(initialCount: 0, maxCount: 1);

async ValkarnTask ProducerAsync(CancellationToken ct)
{
    await ProduceDataAsync(ct);
    gate.Release(); // сигнал потребителю
}

async ValkarnTask ConsumerAsync(CancellationToken ct)
{
    await gate.WaitAsync(ct); // ожидание сигнала
    await ConsumeDataAsync(ct);
}
```

---

## Потокобезопасность

Все операции потокобезопасны. `Release()` можно вызывать из любого потока, включая фоновые. Завершения диспетчеризуются вне внутреннего блокировщика — продолжение, повторно входящее в семафор, не вызовет взаимоблокировку.

---

## Сравнение с SemaphoreSlim

| | `ValkarnSemaphore` | `SemaphoreSlim` |
|---|---|---|
| Выделение на быстром пути | Нуль | Нуль |
| Выделение на медленном пути | Узел из пула, GC ноль | Выделение `Task` |
| Интеграция с ValkarnTask | Да | Требует `.AsValkarnTask()` |
| `IDisposable` | Да | Да |
| Порядок FIFO | Да | Да |
| Отмена | Да | Да |
