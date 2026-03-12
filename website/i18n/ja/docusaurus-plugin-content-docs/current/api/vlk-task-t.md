---
sidebar_position: 2
title: ValkarnTask<T>
---

# `ValkarnTask<T>`

`ValkarnTask<T>`はValkarn Tasksの値を返す非同期タスク型です。`readonly struct`であり、同期的に完了した場合はインライン結果を、非同期完了した場合はプールされたソースオブジェクトへの参照を保持します。

**名前空間：** `UnaPartidaMas.Valkarn.Tasks`

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncValkarnTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
```

`T`に対するジェネリック制約はありません。任意の型 — 値型、参照型、構造体、クラス — が有効です。

---

## インスタンスの作成

### 同期完了タスク

これらのファクトリーメソッドはバッキングソースオブジェクトのない`ValkarnTask<T>`を返します。ゼロアロケーション。

#### `ValkarnTask.FromResult<T>(T value)`

`value`をインラインに保持した完了済み`ValkarnTask<T>`を返します。非ジェネリックの`ValkarnTask`型の静的メソッドとして宣言されています。

```csharp
public static ValkarnTask<T> FromResult<T>(T value)
```

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(42);
ValkarnTask<string> name = ValkarnTask.FromResult("Valkarn");
ValkarnTask<Vector3> pos = ValkarnTask.FromResult(transform.position);
```

返された構造体は`source == null`です。awaitするとコンティニュエーションのアロケーションは発生しません — コンパイラーは即座に`IsCompleted == true`を確認します。

#### `ValkarnTask.FromException<T>(Exception exception)`

フォルトした`ValkarnTask<T>`を返します。awaitすると`ExceptionDispatchInfo`によって元のスタックトレースを保持しながら例外が再スローされます。

```csharp
public static ValkarnTask<T> FromException<T>(Exception exception)
```

```csharp
ValkarnTask<Texture2D> LoadTexture(string path)
{
    if (string.IsNullOrEmpty(path))
        return ValkarnTask.FromException<Texture2D>(
            new ArgumentException("パスは空にできません。", nameof(path)));

    return LoadTextureAsync(path);
}
```

#### `ValkarnTask.FromCanceled<T>(CancellationToken ct = default)`

キャンセルされた`ValkarnTask<T>`を返します。awaitすると`OperationCanceledException`がスローされます。

```csharp
public static ValkarnTask<T> FromCanceled<T>(CancellationToken ct = default)
```

```csharp
ValkarnTask<byte[]> Download(string url, CancellationToken ct)
{
    if (ct.IsCancellationRequested)
        return ValkarnTask.FromCanceled<byte[]>(ct);

    return DownloadAsync(url, ct);
}
```

### `async`メソッド経由

`ValkarnTask<T>`を返すと宣言された`async`メソッドは自動的に`AsyncValkarnTaskMethodBuilder<TResult>`を使用します：

```csharp
async ValkarnTask<int> ComputeAsync()
{
    await ValkarnTask.Yield();
    return 42;
}
```

コンパイラーはステートマシンを生成します。メソッドが同期的に完了した（一度もサスペンドしない）場合、`AsyncValkarnTaskMethodBuilder<T>.Task`は`source == null`の`new ValkarnTask<T>(result)`を返します — ゼロアロケーション。

---

## スレッドプールでの作業実行

これらのメソッドは.NETスレッドプール上でデリゲートを実行し、指定された`PlayerLoopTiming`でメインスレッドに結果を返します。長い名前の`RunOnThreadPool`バリアントのラッパーです。

#### `ValkarnTask.Run<T>(Func<T> func, PlayerLoopTiming timing, CancellationToken ct)`

スレッドプール上で同期`Func<T>`を実行します。

```csharp
public static ValkarnTask<T> Run<T>(
    Func<T> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
// スレッドプールで計算し、次のUpdateでメインスレッドに結果が届く
int hash = await ValkarnTask.Run(() => ComputeExpensiveHash(data));
```

#### `ValkarnTask.Run<T>(Func<ValkarnTask<T>> func, PlayerLoopTiming timing, CancellationToken ct)`

スレッドプール上で非同期`Func<ValkarnTask<T>>`を実行します。作業自体が非同期（例：ファイルI/O）の場合に使用します。

```csharp
public static ValkarnTask<T> Run<T>(
    Func<ValkarnTask<T>> func,
    PlayerLoopTiming timing = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

```csharp
string json = await ValkarnTask.Run(async () =>
{
    using var reader = File.OpenText("data.json");
    return await reader.ReadToEndAsync();
});
```

両方の`Run`オーバーロードはトークンが既にキャンセルされている場合は早期キャンセルし、作業完了後に`timing`でメインスレッドに切り替えます。

---

## インスタンスメンバー

### `IsCompleted`

```csharp
public bool IsCompleted { get; }
```

タスクが任意の終端状態（Succeeded、Faulted、またはCanceled）で完了した場合に`true`を返します。同期完了タスク（`source == null`）では、インターフェースディスパッチなしで常に`true`を返します。

```csharp
var task = SomeLongOperation();
if (task.IsCompleted)
{
    int result = task.GetAwaiter().GetResult();
    Use(result);
}
```

### `GetStatus()`

```csharp
public ValkarnTask.Status GetStatus()
```

現在の`ValkarnTask.Status`を返します。取りうる値：`Pending`、`Succeeded`、`Faulted`、`Canceled`。`source == null`の場合は常に`Succeeded`を返します。

### `GetAwaiter()`

```csharp
public Awaiter GetAwaiter()
```

`Awaiter`構造体を返します。コンパイラーが`await`を実装するために使用します。`IsCompleted`がtrueの場合のみ安全な同期的な結果取得にも使用できます。

```csharp
ValkarnTask<int> task = ValkarnTask.FromResult(10);
int value = task.GetAwaiter().GetResult(); // 安全 — 同期完了済み
```

保留中のタスクで`GetResult()`を呼び出すと`InvalidOperationException`がスローされます。

### `AsNonGeneric()`

```csharp
public ValkarnTask AsNonGeneric()
```

この`ValkarnTask<T>`を非ジェネリックの`ValkarnTask`に変換し、結果の型を破棄します。結果のタスクは同じ基礎ソースとトークンを共有するため、同時に完了します。

```csharp
ValkarnTask<int> typedTask = ComputeAsync();
ValkarnTask voidTask = typedTask.AsNonGeneric();
await voidTask;   // 完了を待つ、結果は無視
```

これは混合型タスクをコンビネーターに渡す場合や、値ではなく完了タイミングのみに関心がある場合に有用です。

---

## `ValkarnTask<T>`を返すコンビネーター

### `WhenAll` — 型付き2タスクオーバーロード

```csharp
public static ValkarnTask<(T1, T2)> WhenAll<T1, T2>(
    ValkarnTask<T1> task1, ValkarnTask<T2> task2)
```

両方のタスクを並行して実行し、結果のタプルを返します。いずれかのタスクがフォルトまたはキャンセルされた場合、最初の例外が優先され、もう一方のエラーは`ValkarnTask.UnobservedException`を通じて報告されます。

**タプル分解**はC#の分解代入で自然に機能します：

```csharp
var (profile, inventory) = await ValkarnTask.WhenAll(
    FetchProfileAsync(userId),
    FetchInventoryAsync(userId)
);
```

**ゼロアロケーション高速パス：** 両方のタスクが呼び出し地点で同期的に完了している場合、プールされたオブジェクトは作成されず、結果のタプルはインラインで返されます。

### `WhenAll` — 型付きコレクションオーバーロード

```csharp
public static ValkarnTask<T[]> WhenAll<T>(IEnumerable<ValkarnTask<T>> tasks)
```

コレクション内のすべてのタスクを並行してawaitし、インデックス順に`T[]`を返します。

```csharp
var urls = new[] { "https://a.com", "https://b.com", "https://c.com" };
ValkarnTask<string>[] downloads = urls.Select(u => DownloadAsync(u)).ToArray();
string[] results = await ValkarnTask.WhenAll(downloads);
```

コレクションが空の場合、`ValkarnTask.FromResult(Array.Empty<T>())`を返します — ゼロアロケーション。すべてのタスクが既に同期的に完了している場合、コンビネータープロミスを作成せずにインラインで結果配列が構築されます。

### `WhenAny` — 型付き2タスクオーバーロード

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    ValkarnTask<T> task1, ValkarnTask<T> task2)
```

いずれかのタスクが完了したら即座に返します。結果のタプルには勝者の0ベースのインデックスとその値が含まれます。負けたタスクは実行を続けます；それらのエラー（あれば）は`ValkarnTask.UnobservedException`を通じて報告されます。負けたキャンセルは意図的に報告されません。

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(
    FetchFromCacheAsync(key),
    FetchFromNetworkAsync(key)
);

if (winnerIndex == 0)
    Debug.Log("キャッシュが勝ち");
```

### `WhenAny` — 型付きコレクションオーバーロード

```csharp
public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
    IEnumerable<ValkarnTask<T>> tasks)
```

2タスクオーバーロードと同じセマンティクスで、任意の数のタスクに拡張されています。少なくとも1つのタスクが必要；空のコレクションに対しては`ArgumentException`がスローされます。

```csharp
var tasks = servers.Select(s => s.FetchAsync(query)).ToArray();
var (winnerIndex, data) = await ValkarnTask.WhenAny(tasks);
Debug.Log($"サーバー{winnerIndex}が最初に応答");
```

---

## ファクトリー便利メソッド

### `ValkarnTask.Create<T>(Func<ValkarnTask<T>> factory)`

ファクトリーデリゲートを呼び出し、結果のタスクをawaitします。非同期操作の構築を遅延させたい場合に有用です。

```csharp
public static async ValkarnTask<T> Create<T>(Func<ValkarnTask<T>> factory)
```

```csharp
var result = await ValkarnTask.Create(() => LoadLevelDataAsync(levelId));
```

---

## `Awaiter`構造体（ネスト）

`ValkarnTask<T>.Awaiter`はコンパイラー向けのawaiterです。`ICriticalNotifyCompletion`を実装する`readonly struct`です。通常は直接操作しません。

```csharp
public readonly struct Awaiter : ICriticalNotifyCompletion
{
    public bool IsCompleted { get; }
    public T GetResult();
    public void OnCompleted(Action continuation);
    public void UnsafeOnCompleted(Action continuation);
}
```

`UnsafeOnCompleted`は`AsyncValkarnTaskMethodBuilder<T>`が使用するパスです。「unsafe」ラベルは`ExecutionContext`がキャプチャされないことを意味します — これはUnityでは`SynchronizationContext`が有効でないため意図的です。

`IsCompleted`がtrueの場合、`GetResult()`を呼び出すと、同期タスクの場合はインライン`result`フィールドを読み取り、非同期タスクの場合はソースインターフェースを通じて呼び出します。両方のパスは`[MethodImpl(MethodImplOptions.AggressiveInlining)]`です。

---

## `ValkarnTask<T>`の拡張メソッド

### `AsResult<T>()`

```csharp
public static ValkarnTask<Result<T>> AsResult<T>(this ValkarnTask<T> task)
```

`ValkarnTask<T>`を`ValkarnTask<Result<T>>`にラップし、例外またはキャンセルをキャッチして`Result<T>`値にエンコードします。これにより呼び出し元でのtry/catchが不要になります。

```csharp
Result<string> result = await FetchDataAsync(url).AsResult();

if (result.IsSuccess)
    Process(result.Value);
else if (result.IsCanceled)
    Debug.Log("キャンセルされました");
else
    Debug.LogError(result.Exception);
```

**同期高速パス：** ソースタスクが既に同期的に完了している場合、`AsResult`は非同期機構を使わずに同期的に返します。

---

## `Promise<T>` — 手動完了ソース

`ValkarnTask.Promise<T>`は`ValkarnTask<T>`がいつ完了するかを制御する必要があり、ライフタイムが単一の非同期操作に限定されない場合のために、ヒープアロケーションされた手動完了ソースです。

```csharp
public class Promise<T>
{
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

```csharp
// コールバックベースのAPIのラップ
var promise = new ValkarnTask.Promise<string>();

SomeCallbackApi.OnComplete += value => promise.TrySetResult(value);
SomeCallbackApi.OnError   += ex    => promise.TrySetException(ex);

string result = await promise.Task;
```

`PooledPromise<T>`とは異なり、`Promise<T>`はプーリングされません。タスクがフォルトして呼び出し元がawaitしなかった場合の未処理例外の検出と報告にファイナライザーを使用します。

高頻度パターン（プロデューサー/コンシューマーループ、フレームごとの操作）には、`GetResult`が呼ばれた後に自動的にプールに返却される`ValkarnTask.PooledPromise<T>`を優先してください。

---

## `PooledPromise<T>` — プールされた手動完了ソース

```csharp
public sealed class PooledPromise<T> : ValkarnTask.ISource<T>, IPoolNode<PooledPromise<T>>
{
    public static PooledPromise<T> Create(out uint token);
    public static PooledPromise<T> CreateCompleted(T result, out uint token);
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled(CancellationToken ct = default);
}
```

バッキングタスクの`GetResult`が呼ばれた後、プロミスは`ValkarnTaskCompletionCore<T>`をリセットして自身をプールに返却します。ダブルリターンガードにより、`GetResult`が並行して呼ばれても一度しか行われないことを保証します。

```csharp
// パターン: 準備ができたときに完了するValkarnTask<T>を生成
var promise = ValkarnTask.PooledPromise<int>.Create(out uint token);
ValkarnTask<int> task = promise.Task;

// 非同期で作業をディスパッチ
ThreadPool.QueueUserWorkItem(_ =>
{
    int result = DoWork();
    promise.TrySetResult(result);
});

// コンシューマーがawait；完了時にプロミスはプールに自動返却
int value = await task;
```

---

## `ValkarnTask<T>`を取得する方法のまとめ

| メソッド | 使用する場面 |
|--------|------------|
| `async ValkarnTask<T>`内での`return value` | 通常の非同期メソッド |
| `ValkarnTask.FromResult(value)` | 同期的な高速リターン |
| `ValkarnTask.FromException<T>(ex)` | 事前フォルトタスク |
| `ValkarnTask.FromCanceled<T>(ct)` | 事前キャンセルタスク |
| `ValkarnTask.Run<T>(Func<T>, ...)` | スレッドプールオフロード |
| `ValkarnTask.Run<T>(Func<ValkarnTask<T>>, ...)` | 非同期スレッドプール作業 |
| `ValkarnTask.WhenAll<T1,T2>(t1, t2)` | 2つの型付きタスクを待機してタプルを取得 |
| `ValkarnTask.WhenAll<T>(IEnumerable<...>)` | N個の型付きタスクを待機して配列を取得 |
| `ValkarnTask.WhenAny<T>(t1, t2)` | 2つの型付きタスクのうち最初のもの |
| `ValkarnTask.WhenAny<T>(IEnumerable<...>)` | N個の型付きタスクのうち最初のもの |
| `task.AsResult<T>()` | 例外安全なラッピング |
| `new ValkarnTask.Promise<T>()` → `.Task` | 長期的な手動完了 |
| `ValkarnTask.PooledPromise<T>.Create(...)` → `.Task` | 高頻度の手動完了 |
