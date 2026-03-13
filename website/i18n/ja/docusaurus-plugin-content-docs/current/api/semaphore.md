---
sidebar_position: 7
title: ValkarnSemaphore
---

# ValkarnSemaphore

`ValkarnSemaphore` は `ValkarnTask` 上に構築されたゼロアロケーションの非同期セマフォです。`SemaphoreSlim` と同様に動作しますが、Valkarn Tasks パイプラインとネイティブに統合されています — `Task` のアロケーションなし、安定状態での GC 負荷なし。

```csharp
namespace UnaPartidaMas.Valkarn.Tasks
```

**高速パス（競合なし）：** `ValkarnTask.CompletedTask` を即座に返します — ゼロアロケーション。
**低速パス（競合あり）：** ウェイターノードは有界プールから借用され、完了時に返却されます — 安定状態では GC ゼロ。

---

## コンストラクター

```csharp
public ValkarnSemaphore(int initialCount, int maxCount)
```

| パラメーター | 説明 |
|-----------|-------------|
| `initialCount` | 即座に利用可能なスロット数。`[0, maxCount]` の範囲内である必要があります。 |
| `maxCount` | スロット数の上限。0 より大きい必要があります。 |

---

## プロパティ

```csharp
public int CurrentCount { get; } // 現在利用可能なスロット数（スレッドセーフ）
```

---

## メソッド

### WaitAsync

```csharp
public ValkarnTask WaitAsync(CancellationToken ct = default)
```

スロットを非同期に取得します。スロットが利用可能な場合、完了済みの `ValkarnTask` を即座に返します（ゼロアロケーション）。スロットが利用できない場合、`Release()` が呼び出されるまで呼び出し元を中断します。ウェイターは FIFO 順で処理されます。

待機中に `ct` が発火した場合、`OperationCanceledException` をスローします。

### Wait

```csharp
public void Wait(CancellationToken ct = default)
```

同期バリアント。スロットが利用できない場合、呼び出しスレッドをブロックします。`await` を使用できない場合（例：非非同期エントリーポイント）にのみ使用してください。

### Release

```csharp
public void Release(int releaseCount = 1)
```

`releaseCount` 個のスロットを返却します。保留中のウェイターは内部ロックの外で FIFO 順に完了されます — 継続はロックが保持された状態では実行されません。

解放によって `maxCount` を超える場合、`InvalidOperationException` をスローします。

### Dispose

```csharp
public void Dispose()
```

保留中のウェイターをすべてキャンセルし、リソースを解放します。冪等です。

---

## 使用例

### 相互排他（1 スロット）

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

### 有界並行性（N スロット）

```csharp
// 最大 4 件の同時ネットワークリクエストを許可します。
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

### プロデューサー / コンシューマーゲート

```csharp
// 0 スロットで開始 — コンシューマーはプロデューサーがシグナルを送るまで待機します。
var gate = new ValkarnSemaphore(initialCount: 0, maxCount: 1);

async ValkarnTask ProducerAsync(CancellationToken ct)
{
    await ProduceDataAsync(ct);
    gate.Release(); // コンシューマーにシグナルを送る
}

async ValkarnTask ConsumerAsync(CancellationToken ct)
{
    await gate.WaitAsync(ct); // シグナルを待つ
    await ConsumeDataAsync(ct);
}
```

---

## スレッドセーフティ

すべての操作はスレッドセーフです。`Release()` はバックグラウンドスレッドを含む任意のスレッドから呼び出すことができます。完了は内部ロックの外でディスパッチされます — セマフォに再入する継続はデッドロックを起こしません。

---

## SemaphoreSlim との比較

| | `ValkarnSemaphore` | `SemaphoreSlim` |
|---|---|---|
| 高速パスのアロケーション | ゼロ | ゼロ |
| 低速パスのアロケーション | プールされたノード、GC ゼロ | `Task` アロケーション |
| ValkarnTask との統合 | あり | `.AsValkarnTask()` が必要 |
| `IDisposable` | あり | あり |
| FIFO 順序 | あり | あり |
| キャンセル | あり | あり |
