---
sidebar_position: 2
title: PlayerLoopTiming
---

# PlayerLoopTiming

```csharp
public enum PlayerLoopTiming : byte
```

Пространство имён: `UnaPartidaMas.Valkarn.Tasks`

Указывает, какую фазу Unity PlayerLoop использует операция ValkarnTasks для возобновления после приостановки. Передача значения `PlayerLoopTiming` контролирует **когда в кадре** выполняется продолжение `await` и **когда** проверяются повторяющиеся элементы, такие как задержки и условия ожидания.

Значения enum совпадают с enum `PlayerLoopTiming` в UniTask, что упрощает миграцию с UniTask.

---

## Значение по умолчанию

`PlayerLoopTiming.Update` (значение `8`) является значением по умолчанию для всех операций. Если аргумент фазы не передан, используется `Update`.

---

## Значения

### Initialization

```
Значение: 0
Родительская фаза: UnityEngine.PlayerLoop.Initialization
Позиция: первая подсистема в фазе
```

Выполняется в самом начале фазы Initialization, до собственных подсистем инициализации Unity в этой фазе. Срабатывает один раз за кадр, до `EarlyUpdate`. Редко полезна для игрового кода, но может быть актуальна для систем, которые должны читать или настраивать состояние до того, как что-либо ещё в кадре к нему обратится.

---

### LastInitialization

```
Значение: 1
Родительская фаза: UnityEngine.PlayerLoop.Initialization
Позиция: последняя подсистема в фазе
```

Выполняется в конце фазы Initialization, после встроенных подсистем инициализации Unity. Предпочтительнее `Initialization`, если вам нужна фаза инициализации, но вы хотите, чтобы сначала выполнились собственные системы Unity.

---

### EarlyUpdate

```
Значение: 2
Родительская фаза: UnityEngine.PlayerLoop.EarlyUpdate
Позиция: первая подсистема в фазе
```

Выполняется в начале EarlyUpdate, до обработки Unity событий ввода и до шага физической симуляции. Это самая ранняя точка в кадре, где доступны данные ввода за предыдущий кадр. Полезна для систем, которые должны считывать ввод до выполнения любого игрового кода.

---

### LastEarlyUpdate

```
Значение: 3
Родительская фаза: UnityEngine.PlayerLoop.EarlyUpdate
Позиция: последняя подсистема в фазе
```

Выполняется в конце EarlyUpdate, после завершения собственных подсистем EarlyUpdate Unity.

---

### FixedUpdate

```
Значение: 4
Родительская фаза: UnityEngine.PlayerLoop.FixedUpdate
Позиция: первая подсистема в фазе
```

Выполняется в начале каждого шага FixedUpdate. Соответствует моменту выполнения `MonoBehaviour.FixedUpdate()`. Unity может выполнять ноль или более шагов FixedUpdate на один отрисованный кадр, в зависимости от того, как временной шаг физики выравнивается с дельтой кадра.

Используйте эту фазу для всего, что должно оставаться синхронизированным с физической симуляцией: чтение скоростей `Rigidbody`, применение сил или ожидание физически определённого количества шагов.

```csharp
// Ждать 10 шагов физики
await VlkTask.DelayFrame(10, PlayerLoopTiming.FixedUpdate, ct);

// Ждать выполнения физического условия, проверяемого на каждом шаге физики
await VlkTask.WaitUntil(() => rb.velocity.magnitude < 0.1f,
    PlayerLoopTiming.FixedUpdate, ct);
```

---

### LastFixedUpdate

```
Значение: 5
Родительская фаза: UnityEngine.PlayerLoop.FixedUpdate
Позиция: последняя подсистема в фазе
```

Выполняется в конце фазы FixedUpdate, после завершения подсистем физики Unity (включая `Physics.Simulate`). Используйте для чтения результатов физики после продвижения симуляции.

---

### PreUpdate

```
Значение: 6
Родительская фаза: UnityEngine.PlayerLoop.PreUpdate
Позиция: первая подсистема в фазе
```

Выполняется в начале фазы PreUpdate, которая запускается после FixedUpdate и перед Update. Unity использует PreUpdate для задач типа обновления зон ветра и обработки сетевых событий.

---

### LastPreUpdate

```
Значение: 7
Родительская фаза: UnityEngine.PlayerLoop.PreUpdate
Позиция: последняя подсистема в фазе
```

Выполняется в конце PreUpdate.

---

### Update

```
Значение: 8  (по умолчанию)
Родительская фаза: UnityEngine.PlayerLoop.Update
Позиция: первая подсистема в фазе
```

**Фаза по умолчанию** для всех операций ValkarnTasks. Выполняется в начале фазы Update, где срабатывает `MonoBehaviour.Update()`. Правильный выбор для подавляющего большинства игрового кода.

```csharp
// Все они используют Update по умолчанию
await VlkTask.Yield();
await VlkTask.Delay(1000, ct);
await VlkTask.WaitUntil(() => _ready, ct: ct);
await VlkTask.NextFrame(ct: ct);
await VlkTask.DelayFrame(3, ct: ct);

// Явный — идентично вышеуказанному
await VlkTask.Yield(PlayerLoopTiming.Update);
```

---

### LastUpdate

```
Значение: 9
Родительская фаза: UnityEngine.PlayerLoop.Update
Позиция: последняя подсистема в фазе
```

Выполняется в конце фазы Update, после всех вызовов `MonoBehaviour.Update()` и любых других подсистем Update. Используйте, когда ваше продолжение должно наблюдать результаты методов `Update()` других скриптов — например, опрашивать значение, которое другой скрипт записывает во время `Update`.

```csharp
// Возобновить после всех вызовов MonoBehaviour.Update() в этом кадре
await VlkTask.Yield(PlayerLoopTiming.LastUpdate);
```

---

### PreLateUpdate

```
Значение: 10
Родительская фаза: UnityEngine.PlayerLoop.PreLateUpdate
Позиция: первая подсистема в фазе
```

Выполняется в начале фазы PreLateUpdate, где срабатывает `MonoBehaviour.LateUpdate()`. Используйте для следования камеры, корректировки трансформаций и всего, что должно реагировать на финальные позиции, установленные во время `Update`.

```csharp
// Возобновить в той же точке, что MonoBehaviour.LateUpdate()
await VlkTask.Yield(PlayerLoopTiming.PreLateUpdate);
```

---

### LastPreLateUpdate

```
Значение: 11
Родительская фаза: UnityEngine.PlayerLoop.PreLateUpdate
Позиция: последняя подсистема в фазе
```

Выполняется в конце фазы PreLateUpdate, после всех вызовов `MonoBehaviour.LateUpdate()`. Используйте, когда нужно читать значения, которые скрипты устанавливают во время своего `LateUpdate`.

---

### PostLateUpdate

```
Значение: 12
Родительская фаза: UnityEngine.PlayerLoop.PostLateUpdate
Позиция: первая подсистема в фазе
```

Выполняется в начале PostLateUpdate. Эта фаза срабатывает после отправки рендеринга на кадр. Unity использует её для таких задач, как представление буфера кадра и очистка состояния рендеринга. Здесь также срабатывают коллбэки `Camera.onPostRender`.

Используйте эту фазу для операций в конце кадра: захват скриншотов, выгрузка потоковых уровней или любая работа, которая должна выполняться после полной отрисовки сцены, но до начала следующего кадра.

---

### LastPostLateUpdate

```
Значение: 13
Родительская фаза: UnityEngine.PlayerLoop.PostLateUpdate
Позиция: последняя подсистема в фазе
```

Последняя точка в стандартном цикле кадра перед тем, как Unity сбрасывает локальное для кадра состояние и начинает следующий кадр. Это абсолютный конец кадра для целей определения момента.

---

### TimeUpdate

```
Значение: 14
Родительская фаза: UnityEngine.PlayerLoop.TimeUpdate
Позиция: первая подсистема в фазе
```

Выполняется в начале фазы TimeUpdate. Unity использует эту фазу для продвижения связанного со временем состояния (например, `Time.time`). Эта фаза срабатывает перед `EarlyUpdate` в более новых версиях Unity.

Эта фаза редко нужна в игровом коде. Она существует прежде всего для систем, которые должны перехватывать или наблюдать продвижение времени до того, как любая другая система прочитает новые значения времени.

---

### LastTimeUpdate

```
Значение: 15
Родительская фаза: UnityEngine.PlayerLoop.TimeUpdate
Позиция: последняя подсистема в фазе
```

Выполняется в конце фазы TimeUpdate, после завершения связанных со временем подсистем Unity.

---

## Сводная таблица

| Значение | Имя | Фаза Unity | Позиция | Сопоставимый коллбэк MonoBehaviour |
|---|---|---|---|---|
| 0 | `Initialization` | `Initialization` | Первая | — |
| 1 | `LastInitialization` | `Initialization` | Последняя | — |
| 2 | `EarlyUpdate` | `EarlyUpdate` | Первая | — |
| 3 | `LastEarlyUpdate` | `EarlyUpdate` | Последняя | — |
| 4 | `FixedUpdate` | `FixedUpdate` | Первая | `FixedUpdate()` |
| 5 | `LastFixedUpdate` | `FixedUpdate` | Последняя | после `FixedUpdate()` |
| 6 | `PreUpdate` | `PreUpdate` | Первая | — |
| 7 | `LastPreUpdate` | `PreUpdate` | Последняя | — |
| **8** | **`Update`** (по умолчанию) | **`Update`** | **Первая** | **`Update()`** |
| 9 | `LastUpdate` | `Update` | Последняя | после всех `Update()` |
| 10 | `PreLateUpdate` | `PreLateUpdate` | Первая | `LateUpdate()` |
| 11 | `LastPreLateUpdate` | `PreLateUpdate` | Последняя | после всех `LateUpdate()` |
| 12 | `PostLateUpdate` | `PostLateUpdate` | Первая | после рендеринга |
| 13 | `LastPostLateUpdate` | `PostLateUpdate` | Последняя | конец кадра |
| 14 | `TimeUpdate` | `TimeUpdate` | Первая | — |
| 15 | `LastTimeUpdate` | `TimeUpdate` | Последняя | — |

---

## Какие API принимают PlayerLoopTiming

Все основанные на времени и условиях API ValkarnTasks принимают необязательный параметр `PlayerLoopTiming`. По умолчанию всегда `Update`.

| Метод | Сигнатура |
|---|---|
| `VlkTask.Yield` | `Yield(PlayerLoopTiming timing = Update)` |
| `VlkTask.NextFrame` | `NextFrame(PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `VlkTask.DelayFrame` | `DelayFrame(int frameCount, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `VlkTask.Delay(int)` | `Delay(int ms, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `VlkTask.Delay(TimeSpan)` | `Delay(TimeSpan delay, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `VlkTask.WaitUntil` | `WaitUntil(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `VlkTask.WaitWhile` | `WaitWhile(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |

---

## Примеры кода

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using System.Threading;

// Уступить следующему тику Update (по умолчанию)
await VlkTask.Yield();

// Уступить следующему тику FixedUpdate — используется в коде с физикой
await VlkTask.Yield(PlayerLoopTiming.FixedUpdate);

// Ждать 500 мс используя масштабируемый deltaTime, проверяемый на каждом Update
await VlkTask.Delay(500, ct: destroyCancellationToken);

// Ждать 500 мс используя немасштабируемый deltaTime — не зависит от Time.timeScale
await VlkTask.Delay(
    500,
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// Ждать 500 мс используя реальное системное время (на основе Stopwatch)
await VlkTask.Delay(
    500,
    DelayType.Realtime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// Ждать установки флага, проверяемого в LateUpdate
bool _isReady;
await VlkTask.WaitUntil(() => _isReady, PlayerLoopTiming.PreLateUpdate, destroyCancellationToken);

// Ждать пока загрузка, проверяемая в самом конце кадра
await VlkTask.WaitWhile(() => _loading, PlayerLoopTiming.LastPostLateUpdate, destroyCancellationToken);

// Пропустить ровно 5 шагов физики
await VlkTask.DelayFrame(5, PlayerLoopTiming.FixedUpdate, destroyCancellationToken);

// Продвинуть один полный отрисованный кадр
await VlkTask.NextFrame(PlayerLoopTiming.Update, destroyCancellationToken);
```
