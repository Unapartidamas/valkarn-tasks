---
sidebar_position: 3
title: 完了ソース
---

# VlkTaskCompletionSource

`VlkTaskCompletionSource<T>`と非ジェネリックの`VlkTaskCompletionSource`は、`VlkTask`を手動で制御する手段を提供します。非同期の結果を自分で書くのと同等です — ソースオブジェクトを保持し、その`.Task`を呼び出し元に渡し、別の呼び出し元から解決、フォルト、またはキャンセルします。

これはValkarn TasksでのBCLの`TaskCompletionSource<T>`相当ですが、ライブラリの残りの部分とアロケーションモデルを一致させるために`VlkTask.Promise<T>`によってバックアップされています。

---

## VlkTaskCompletionSource&lt;T&gt;

```csharp
public class VlkTaskCompletionSource<T>
{
    public VlkTask<T> Task { get; }
    public bool TrySetResult(T result);
    public bool TrySetException(Exception ex);
    public bool TrySetCanceled();
}
```

### Task

```csharp
public VlkTask<T> Task { get; }
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

## 非ジェネリックのVlkTaskCompletionSource

パブリックAPIには別の非ジェネリックの`VlkTaskCompletionSource`クラスはありません。void戻り値の手動タスクには、`VlkTask.Promise`を直接使用してください：

```csharp
var promise = new VlkTask.Promise();
VlkTask task = promise.Task;

promise.TrySetResult();    // 完了
promise.TrySetException(ex);
promise.TrySetCanceled();
```

`VlkTask.Promise`は同じ`TrySet*`サーフェスと同じ二重完了保護を公開しますが、`VlkTask<T>`の代わりに非ジェネリックの`VlkTask`を生成します。

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

多くのUnityおよびプラットフォームAPIはasync/awaitではなくコールバックで結果を提供します。`VlkTaskCompletionSource<T>`を使用してクリーンにラップできます。

```csharp
public VlkTask<Texture2D> LoadTextureAsync(string url)
{
    var tcs = new VlkTaskCompletionSource<Texture2D>();

    StartCoroutine(LoadCoroutine(url, tcs));

    return tcs.Task;
}

IEnumerator LoadCoroutine(string url, VlkTaskCompletionSource<Texture2D> tcs)
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

`VlkTask.Promise`を使用して、一度発火して任意の数のウェイターをアンブロックするシグナルが必要な場合。これは`ManualResetEventSlim`に似ていますが非同期ネイティブです。

```csharp
public class AsyncGate
{
    readonly VlkTask.Promise _promise = new();

    // 任意の数の呼び出し元がこれをawaitできる
    public VlkTask WaitAsync() => _promise.Task;

    // 一度呼び出してすべてをアンブロック
    public void Open() => _promise.TrySetResult();
}

// 使用例
var gate = new AsyncGate();

// 複数のシステムが独立してゲートをawait
async VlkTask SystemAAsync()
{
    await gate.WaitAsync();
    // ゲートが開いた後に進む
}

async VlkTask SystemBAsync()
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
public VlkTask<Result> RunWithTimeoutAsync(
    Func<VlkTask<Result>> operation,
    float timeoutSeconds,
    CancellationToken ct)
{
    var tcs = new VlkTaskCompletionSource<Result>();

    RunCoreAsync(tcs, operation, timeoutSeconds, ct).Forget();

    return tcs.Task;
}

async VlkTask RunCoreAsync(
    VlkTaskCompletionSource<Result> tcs,
    Func<VlkTask<Result>> operation,
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
    readonly VlkTask.Promise _readyPromise = new();

    // いつでもawaitできる — Initialize()の前でも後でも
    public VlkTask Ready => _readyPromise.Task;

    async void Start()
    {
        await LoadConfigAsync();
        await ConnectAsync();
        _readyPromise.TrySetResult();  // すべてのウェイターをアンブロック
    }
}

// 任意の別のコンポーネントで
async VlkTask OnEnableAsync()
{
    await ServiceBus.Instance.Ready;  // 準備できていない場合は待機、既に準備完了なら即座に返す
    DoWork();
}
```

---

## VlkTask.Promiseとの関係

`VlkTaskCompletionSource<T>`は`VlkTask.Promise<T>`の薄いパブリックラッパーです。どちらも同じ機能を提供します。違いは命名規則です：

| 型 | 返す | 一般的な使用 |
|------|---------|------------|
| `VlkTaskCompletionSource<T>` | `VlkTask<T>` | パブリックAPI、BCLの`TaskCompletionSource<T>`スタイルを反映 |
| `VlkTask.Promise<T>` | `VlkTask<T>` | 内部使用、よりダイレクト |
| `VlkTask.Promise` | `VlkTask` | voidシグナル（ゲート、イベント） |

3つすべては`VlkTaskCompletionCore<T>`によってタスクをバックアップします。これはCASベースの二段階プロトコルを使用してスレッド安全性とコンティニュエーション/結果の競合を処理する内部の構造体ベースのステートマシンです。
