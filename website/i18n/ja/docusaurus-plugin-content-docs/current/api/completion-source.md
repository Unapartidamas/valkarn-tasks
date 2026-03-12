---
sidebar_position: 3
title: 完了ソース
---

# ValkarnTaskCompletionSource

`ValkarnTaskCompletionSource<T>`と非ジェネリックの`ValkarnTaskCompletionSource`は、`ValkarnTask`を手動で制御する手段を提供します。非同期の結果を自分で書くのと同等です — ソースオブジェクトを保持し、その`.Task`を呼び出し元に渡し、別の呼び出し元から解決、フォルト、またはキャンセルします。

これはValkarn TasksでのBCLの`TaskCompletionSource<T>`相当ですが、ライブラリの残りの部分とアロケーションモデルを一致させるために`ValkarnTask.Promise<T>`によってバックアップされています。

---

## ValkarnTaskCompletionSource&lt;T&gt;

```csharp
public class ValkarnTaskCompletionSource<T>
{
    public ValkarnTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled();
}
```

### Task

```csharp
public ValkarnTask<T> Task { get; }
```

awaitするコードが観測するタスク。結果を待つ必要があるすべての呼び出し元に配布してください。複数の呼び出し元が同じタスクを並行して`await`できます。

### TrySetResult

```csharp
public bool TrySetResult(T result);
```

指定された値でタスクを正常に完了します。すべてのawaitコンティニュエーションが再開されます。完了が受け入れられた場合`true`を返します；タスクが既に（任意の以前の`TrySet*`呼び出しによって）完了している場合は`false`を返します。例外をスローしません。

### TrySetException

```csharp
public bool TrySetException(Exception ex);
```

提供された例外でタスクをフォルトさせます。awaitコードは`await`時に例外を受け取ります。`ex`が`null`の場合は`ArgumentNullException`をスローします。受け入れられた場合`true`、既に完了している場合`false`を返します。

### TrySetCanceled

```csharp
public bool TrySetCanceled();
```

タスクをキャンセルします。awaitコードは`OperationCanceledException`を受け取ります。受け入れられた場合`true`、既に完了している場合`false`を返します。

---

## 非ジェネリックのValkarnTaskCompletionSource

パブリックAPIには別の非ジェネリックの`ValkarnTaskCompletionSource`クラスはありません。void戻り値の手動タスクには、`ValkarnTask.Promise`を直接使用してください：

```csharp
var promise = new ValkarnTask.Promise();
ValkarnTask task = promise.Task;

promise.TrySetResult();    // 完了
promise.TrySetException(ex);
promise.TrySetCanceled();
```

`ValkarnTask.Promise`は同じ`TrySet*`サーフェスと同じ二重完了保護を公開しますが、`ValkarnTask<T>`の代わりに非ジェネリックの`ValkarnTask`を生成します。

---

## 二重完了保護

すべての`TrySet*`メソッドは、並行してを含む任意のスレッドから任意の時点で安全に呼び出せます。内部ステートマシンのcompare-and-swapに最初に勝った呼び出しが成功します；その後の呼び出しはすべて`false`を返して効果がありません。これは以下を意味します：

- `TrySetResult`を二度呼び出すと2回目は何もしません。
- `TrySetException`の後に`TrySetResult`を呼び出すと何もしません。
- 2つのスレッドが同時に同じソースを完了しようと競合しても安全 — 一方が勝ち、もう一方は静かに無視されます。

どの呼び出し元が「勝った」かを知る必要がある場合は、戻り値を確認してください。気にしない場合（fire-and-forgetシグナル）は、安全に無視できます。

```csharp
// 安全な競合 — これらのうち一方だけが実際にタスクを完了する
_ = source.TrySetResult(value);
_ = source.TrySetCanceled();
```

---

## 一般的なパターン

### コールバックAPIのブリッジ

多くのUnityおよびプラットフォームAPIはasync/awaitではなくコールバックで結果を提供します。`ValkarnTaskCompletionSource<T>`を使用してクリーンにラップできます。

```csharp
public ValkarnTask<Texture2D> LoadTextureAsync(string url)
{
    var tcs = new ValkarnTaskCompletionSource<Texture2D>();

    StartCoroutine(LoadCoroutine(url, tcs));

    return tcs.Task;
}

IEnumerator LoadCoroutine(string url, ValkarnTaskCompletionSource<Texture2D> tcs)
{
    var request = UnityWebRequestTexture.GetTexture(url);
    yield return request.SendWebRequest();

    if (request.result == UnityWebRequest.Result.Success)
        tcs.TrySetResult(DownloadHandlerTexture.GetContent(request));
    else
        tcs.TrySetException(new Exception(request.error));
}
```

呼び出し元はシンプルに返されたタスクを`await`できます：

```csharp
Texture2D tex = await LoadTextureAsync("https://example.com/image.png");
```

### ワンショットシグナル（非同期ゲート）

`ValkarnTask.Promise`を使用して、一度発火して任意の数のウェイターをアンブロックするシグナルが必要な場合。これは`ManualResetEventSlim`に似ていますが非同期ネイティブです。

```csharp
public class AsyncGate
{
    readonly ValkarnTask.Promise _promise = new();

    // 任意の数の呼び出し元がこれをawaitできる
    public ValkarnTask WaitAsync() => _promise.Task;

    // 一度呼び出してすべてをアンブロック
    public void Open() => _promise.TrySetResult();
}

// 使用例
var gate = new AsyncGate();

// 複数のシステムが独立してゲートをawait
async ValkarnTask SystemAAsync()
{
    await gate.WaitAsync();
    // ゲートが開いた後に進む
}

async ValkarnTask SystemBAsync()
{
    await gate.WaitAsync();
    // SystemAと同時に進む
}

// どこか別の場所でゲートを開く
gate.Open();
```

`TrySetResult()`が呼ばれると、タスクへの現在および将来のすべての`await`呼び出しは即座に完了します（タスクが既に完了している時点で実行される場合は同期的に）。

### キャンセルサポートを伴うサードパーティ非同期操作のラップ

```csharp
public ValkarnTask<Result> RunWithTimeoutAsync(
    Func<ValkarnTask<Result>> operation,
    float timeoutSeconds,
    CancellationToken ct)
{
    var tcs = new ValkarnTaskCompletionSource<Result>();

    RunCoreAsync(tcs, operation, timeoutSeconds, ct).Forget();

    return tcs.Task;
}

async ValkarnTask RunCoreAsync(
    ValkarnTaskCompletionSource<Result> tcs,
    Func<ValkarnTask<Result>> operation,
    float timeout,
    CancellationToken ct)
{
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
    linked.CancelAfter(TimeSpan.FromSeconds(timeout));

    try
    {
        var result = await operation();
        tcs.TrySetResult(result);
    }
    catch (OperationCanceledException)
    {
        tcs.TrySetCanceled();
    }
    catch (Exception ex)
    {
        tcs.TrySetException(ex);
    }
}
```

### 遅延初期化ゲート

コンポーネントが初期化がまだ始まっていない場合でもawaitできる「準備完了」タスクを公開する一般的なUnityパターン。

```csharp
public class ServiceBus : MonoBehaviour
{
    readonly ValkarnTask.Promise _readyPromise = new();

    // いつでもawaitできる — Initialize()の前でも後でも
    public ValkarnTask Ready => _readyPromise.Task;

    async void Start()
    {
        await LoadConfigAsync();
        await ConnectAsync();
        _readyPromise.TrySetResult();  // すべてのウェイターをアンブロック
    }
}

// 任意の別のコンポーネントで
async ValkarnTask OnEnableAsync()
{
    await ServiceBus.Instance.Ready;  // 準備できていない場合は待機、既に準備完了なら即座に返す
    DoWork();
}
```

---

## ValkarnTask.Promiseとの関係

`ValkarnTaskCompletionSource<T>`は`ValkarnTask.Promise<T>`の薄いパブリックラッパーです。どちらも同じ機能を提供します。違いは命名規則です：

| 型 | 返す | 一般的な使用 |
|------|---------|------------|
| `ValkarnTaskCompletionSource<T>` | `ValkarnTask<T>` | パブリックAPI、BCLの`TaskCompletionSource<T>`スタイルを反映 |
| `ValkarnTask.Promise<T>` | `ValkarnTask<T>` | 内部使用、よりダイレクト |
| `ValkarnTask.Promise` | `ValkarnTask` | voidシグナル（ゲート、イベント） |

3つすべては`ValkarnTaskCompletionCore<T>`によってタスクをバックアップします。これはCASベースの二段階プロトコルを使用してスレッド安全性とコンティニュエーション/結果の競合を処理する内部の構造体ベースのステートマシンです。
