---
sidebar_position: 1
title: VlkTask
---

# VlkTask

`VlkTask`はコアのawait可能型です。`readonly struct`です — 正常系パス（タスクが同期的またはプール経由で完了する場合）ではヒープアロケーションなし。

```csharp
namespace UnaPartidaMas.Valkarn.Tasks;

public readonly struct VlkTask : IEquatable<VlkTask>
```

---

## 静的ファクトリーメソッド

### Delay

```csharp
// ミリ秒（デフォルトでPlayerLoopTiming.Updateを使用）
VlkTask VlkTask.Delay(int millisecondsDelay)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(int millisecondsDelay, CancellationToken cancellationToken)
VlkTask VlkTask.Delay(int millisecondsDelay, PlayerLoopTiming timing, CancellationToken cancellationToken)

// TimeSpanオーバーロード
VlkTask VlkTask.Delay(TimeSpan delay)
VlkTask VlkTask.Delay(TimeSpan delay, PlayerLoopTiming timing)
VlkTask VlkTask.Delay(TimeSpan delay, CancellationToken cancellationToken)
```

### Yield

```csharp
// 次のフレームにyield（デフォルトでUpdateタイミング）
VlkTask VlkTask.Yield()
VlkTask VlkTask.Yield(PlayerLoopTiming timing)
VlkTask VlkTask.Yield(CancellationToken cancellationToken)
```

### WhenAll

```csharp
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2)
VlkTask VlkTask.WhenAll(VlkTask task1, VlkTask task2, VlkTask task3)
// ... 最大8パラメーター

// 戻り値あり — タプル分解をサポート
VlkTask<(T1, T2)>     VlkTask.WhenAll<T1, T2>(VlkTask<T1>, VlkTask<T2>)
VlkTask<(T1, T2, T3)> VlkTask.WhenAll<T1, T2, T3>(...)
// ... 最大8

// コレクションオーバーロード
VlkTask VlkTask.WhenAll(IEnumerable<VlkTask> tasks)
VlkTask<T[]> VlkTask.WhenAll<T>(IEnumerable<VlkTask<T>> tasks)
```

### WhenAny

```csharp
VlkTask<int> VlkTask.WhenAny(VlkTask task1, VlkTask task2)   // 最初に完了したもののインデックスを返す
VlkTask<T>   VlkTask.WhenAny<T>(VlkTask<T> task1, VlkTask<T> task2) // 最初の値を返す
```

### スレッド切り替え

```csharp
VlkTask VlkTask.SwitchToMainThread()
VlkTask VlkTask.SwitchToThreadPool()
VlkTask VlkTask.SwitchToSynchronizationContext(SynchronizationContext context)
```

### 完了済み / Never

```csharp
VlkTask VlkTask.CompletedTask      // 事前完了済み、ゼロアロケーション
VlkTask<T> VlkTask.FromResult<T>(T value)
VlkTask VlkTask.FromCanceled(CancellationToken ct)
VlkTask<T> VlkTask.FromCanceled<T>(CancellationToken ct)
VlkTask VlkTask.FromException(Exception ex)
VlkTask<T> VlkTask.FromException<T>(Exception ex)
VlkTask VlkTask.Never                // 決して完了しない
```

### Run

```csharp
// デリゲートをスレッドプールで実行
VlkTask VlkTask.Run(Action action)
VlkTask VlkTask.Run(Func<VlkTask> factory)
VlkTask<T> VlkTask.Run<T>(Func<T> func)
VlkTask<T> VlkTask.Run<T>(Func<VlkTask<T>> factory)
```

### Forget

```csharp
// Fire-and-forget: CS4014警告を安全に抑制
void VlkTask.Forget(VlkTask task)
void VlkTask.Forget(VlkTask task, Action<Exception> exceptionHandler)
```

---

## インスタンスメンバー

```csharp
VlkTaskStatus Status { get; }
bool IsCompleted { get; }
bool IsCompletedSuccessfully { get; }
bool IsFaulted { get; }
bool IsCanceled { get; }

// 結果を取得（フォルト/キャンセルの場合はスロー）
void GetResult()

// Awaiter
VlkTaskAwaiter GetAwaiter()

// ValueTaskに変換
ValueTask AsValueTask()
```

---

## 待機条件

```csharp
// 条件がtrueになるまで待機
VlkTask VlkTask.WaitUntil(Func<bool> condition)
VlkTask VlkTask.WaitUntil(Func<bool> condition, PlayerLoopTiming timing)

// 条件がtrueの間待機
VlkTask VlkTask.WaitWhile(Func<bool> condition)

// 固定フレーム数待機
VlkTask VlkTask.WaitForFrames(int frameCount)
VlkTask VlkTask.NextFrame()
```

---

## プール診断

```csharp
// プールごとに（Type type, int currentSize, int maxSize）を返す
IEnumerable<(Type, int, int)> VlkTask.GetPoolInfo()
```

**Window → Valkarn Tasks → Task Tracker**ウィンドウで利用可能。
