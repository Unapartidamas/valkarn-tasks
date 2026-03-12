---
sidebar_position: 1
title: 構造体タスク
---

# 構造体タスク

`VlkTask`と`VlkTask<T>`はValkarn Tasksのコアとなる非同期戻り値型です。常にヒープ上にアロケーションされる参照型である`System.Threading.Tasks.Task`とは異なり、両方のValkarnタスク型は`readonly struct`値です。このページでは、それが実際に何を意味するか、ゼロアロケーション正常系パスがどのように機能するか、そしてコンパイラーがasync/await機構とどのように統合するかを説明します。

---

## なぜ`readonly struct`なのか？

`Task<T>`のようなクラスベースのタスクは、同期的に完了するメソッドであっても、非同期メソッドが呼び出されるたびにヒープ上にアロケーションされなければなりません。60 fpsで動作するUnityゲームループでは、フレームごとに数百の小さな非同期操作が積み重なり、測定可能なGC負荷につながります。

`VlkTask`と`VlkTask<T>`は`readonly partial struct`として宣言されています：

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncVlkTaskMethodBuilder))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct VlkTask
{
    internal readonly ISource source;
    internal readonly uint token;
}
```

```csharp
[AsyncMethodBuilder(typeof(CompilerServices.AsyncVlkTaskMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct VlkTask<T>
{
    internal readonly VlkTask.ISource<T> source;
    internal readonly T result;
    internal readonly uint token;
}
```

構造体であることは、タスク値自体がヒープ上ではなくスタック上（または親オブジェクト内にインライン）に存在することを意味します。`readonly`修飾子により、コンパイラーが不変性について推論でき、誤ったコピーのバグを防げます。`StructLayout.Auto`によりランタイムがターゲットプラットフォーム向けにフィールドの順序を最適化できます。

### 主要な不変条件：`source == null`

設計は単一の不変条件を中心に構築されています：

> `source`が`null`の場合、タスクはエラーなしで同期的に完了しています。ヒープオブジェクトは関与しません。

`VlkTask.CompletedTask`は`default(VlkTask)`です — その`source`フィールドはnullなので、コストゼロです。`VlkTask<T>`は`result`フィールドにインラインで結果を保持しており、`VlkTask.FromResult(value)`もゼロアロケーション呼び出しになります：

```csharp
// ゼロアロケーション — sourceはnull、resultはインライン保存
VlkTask<int> task = VlkTask.FromResult(42);

// こちらもゼロアロケーション — sourceはnull
VlkTask done = VlkTask.CompletedTask;
```

---

## ゼロアロケーション正常系パス

`async`メソッドが一度もサスペンドせず完了した場合（不完全な操作に対する`await`でyieldしない）、メソッド全体が呼び出しスレッド上で同期的に実行されます。ビルダーはこれを検出し、`source == null`のタスクを返します。

awaiterは即座にこれをチェックします：

```csharp
public bool IsCompleted
{
    get
    {
        var s = task.source;
        return s == null || s.GetStatus(task.token).IsCompleted();
    }
}
```

`OnCompleted`が呼ばれる前に`IsCompleted`がtrueの場合、ステートマシンはコンティニュエーションを登録しません。`GetResult`は即座に呼ばれ、`source == null`の`VlkTask<T>`では、結果は構造体のインライン`result`フィールドから読み取られます：

```csharp
public T GetResult()
{
    var s = task.source;
    if (s == null)
        return task.result;   // インライン、ISource呼び出しなし
    return s.GetResult(task.token);
}
```

オブジェクトは作成されず、インターフェースディスパッチも発生せず、コンティニュエーションデリゲートもアロケーションされません。await全体が直接の値読み取りとして解決されます。

### ソースが必要な場合

非同期メソッドがサスペンドした場合（まだ完了していないものをawaitした場合）、ビルダーはプールされた`AsyncVlkTaskRunner<TStateMachine>`オブジェクト（ジェネリックバリアントの場合は`AsyncVlkTaskRunner<TStateMachine, TResult>`）をアロケーションします。このオブジェクトは二重の役割を持ちます：コンパイラーが生成したステートマシンを値として保持し、`VlkTask.ISource`を実装するので、タスクのバッキングソースとして直接使用できます。呼び出し元に返されるタスクはこのランナーと世代`uint`トークンをラップします。

完了時に、呼び出し元がawaiterの`GetResult`を呼び出すと、ランナーはリセットされてプールに戻ります — アロケーションは多くのメソッド呼び出しにわたって償却されます。

---

## `ISource`インターフェース

`VlkTask`構造体と非同期バッキングオブジェクトの間のコントラクトが`VlkTask.ISource`です：

```csharp
public interface ISource
{
    VlkTask.Status GetStatus(uint token);
    void GetResult(uint token);
    void OnCompleted(Action<object> continuation, object state, uint token);
    VlkTask.Status UnsafeGetStatus();
}

public interface ISource<out T> : ISource
{
    new T GetResult(uint token);
}
```

`ISource`を実装するオブジェクトは`VlkTask`をバックアップできます。ライブラリにはいくつかの実装が含まれています：

| 型 | 目的 |
|------|---------|
| `AsyncVlkTaskRunner<TStateMachine>` | すべての`async VlkTask`メソッドをバックアップ（内部） |
| `AsyncVlkTaskRunner<TStateMachine, TResult>` | すべての`async VlkTask<T>`メソッドをバックアップ（内部） |
| `VlkTask.PooledPromise` | 自動プール返却付き手動完了ソース |
| `VlkTask.PooledPromise<T>` | 上記のジェネリックバリアント |
| `VlkTask.Promise` | プーリングなし手動完了ソース（長期的な操作向け） |
| `VlkTask.Promise<T>` | 上記のジェネリックバリアント |

`uint token`パラメーターは世代ガードです。プールされたソースが再利用のためにリセットされると、世代カウンターがインクリメントされます。古いトークンを持つ`VlkTask`構造体は、リサイクルされた状態を暗黙的に読み取る代わりに、即座に`InvalidOperationException`を受け取ります。

---

## `VlkTask` vs `VlkTask<T>`

| 機能 | `VlkTask` | `VlkTask<T>` |
|---------|-----------|-------------|
| 戻り値 | なし（void相当） | `T` |
| インライン結果保存 | `result`フィールドなし | `result`フィールド（型`T`） |
| Awaiter `GetResult` | `void` | `T`を返す |
| ビルダー型 | `AsyncVlkTaskMethodBuilder` | `AsyncVlkTaskMethodBuilder<TResult>` |
| 同期完了値 | `VlkTask.CompletedTask` | `VlkTask.FromResult(value)` |
| 非ジェネリックへの変換 | 該当なし | `.AsNonGeneric()` |

非同期メソッドに意味のある戻り値がない場合は`VlkTask`を使用し、結果を生成する場合は`VlkTask<T>`を使用してください。`WhenAll`のようなコンビネーターで型付きと非型付きのタスクを混在させる必要がある場合は、`AsNonGeneric()`を使って`VlkTask<T>`を常に`VlkTask`にダウンキャストできます。

---

## 非同期メソッドビルダーの仕組み

C#コンパイラーは戻り値型の`[AsyncMethodBuilder(...)]`に指定された型を探します。`VlkTask`の場合は`AsyncVlkTaskMethodBuilder`、`VlkTask<T>`の場合は`AsyncVlkTaskMethodBuilder<TResult>`です。

ビルダー自体は、ビルダーオブジェクト自体のヒープアロケーションを避けるために構造体です。2つのフィールドを持ちます（ジェネリックバリアントは3つ）：

```csharp
public struct AsyncVlkTaskMethodBuilder
{
    IStateMachineRunnerPromise runner;   // 最初のサスペンドまでnull
    Exception syncException;            // 同期フォルトパスのみ設定
}

public struct AsyncVlkTaskMethodBuilder<TResult>
{
    IStateMachineRunnerPromise<TResult> runner;
    Exception syncException;
    TResult result;                     // 同期成功パスのみ設定
}
```

### ビルダーのライフサイクル

コンパイラーはこれらのメソッドを順番に呼び出します：

**1. `Create()`** — デフォルトビルダーを返します（すべてのフィールドnull/デフォルト）。アロケーションなし。

**2. `Start(ref stateMachine)`** — `stateMachine.MoveNext()`を同期的に呼び出します。メソッドが不完全な`await`に到達せずに完了した場合、`SetResult`/`SetException`が呼ばれ、`runner`はnullのままです。

**3. `AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine)`** — メソッドが不完全な`await`に遭遇したときに呼ばれます。`runner`がnull（最初のサスペンド）の場合、`AsyncVlkTaskRunner`をレンタルまたは作成し、ステートマシンをそこにコピーします。次に`awaiter.UnsafeOnCompleted(runner.MoveNextAction)`を呼び出してステートマシンコンティニュエーションを登録します。

**4. `SetResult()` / `SetException(exception)`** — ランナーの`VlkTaskCompletionCore`への完了をシグナルし、登録済みのawaiterを起こします。

**5. `Task`プロパティ** — 呼び出し元が`VlkTask`値を取得するために確認します。同期成功パス（`runner == null && syncException == null`）では、`default`（またはジェネリックバリアントの場合`new VlkTask<T>(result)`）を返します — ゼロアロケーション。非同期パスでは、ランナーをソースとしてラップします。

重要な最適化として、`runner`は遅延アロケーションされます。メソッドが同期的に完了した場合（キャッシュヒット、ガード、早期リターンなど一般的なケース）、プールされたオブジェクトは一切レンタルされません。

---

## `VlkTaskStatus`の状態

ステータスは`VlkTask`内にネストされた`byte`サイズの列挙型で表されます：

```csharp
public enum Status : byte
{
    Pending   = 0,   // まだ完了していない
    Succeeded = 1,   // 正常に完了した
    Faulted   = 2,   // 未処理例外で完了した
    Canceled  = 3    // OperationCanceledExceptionで完了した
}
```

ステータスを直接確認できます：

```csharp
VlkTask task = SomeOperation();
VlkTask.Status status = task.GetStatus();

switch (status)
{
    case VlkTask.Status.Pending:
        // まだ実行中 — GetResultを呼べない
        break;
    case VlkTask.Status.Succeeded:
        // 正常に完了した
        break;
    case VlkTask.Status.Faulted:
        // 例外で完了 — GetResultは再スローする
        break;
    case VlkTask.Status.Canceled:
        // OperationCanceledExceptionで完了した
        break;
}
```

同期完了高速パス（`source == null`）では、`GetStatus()`はインターフェース呼び出しなしで`Succeeded`を返します：

```csharp
public Status GetStatus()
{
    if (source == null) return Status.Succeeded;
    return source.GetStatus(token);
}
```

`IsCompleted`プロパティは同じパターンに従い、`Pending`以外の任意の状態で`true`を返します。

---

## IL2CPPへの影響

IL2CPP はC#をC++ソースコードにコンパイルしてからネイティブコードにビルドします。ジェネリック値型（構造体を含む）は生成コードで完全に特殊化されており、これはこのライブラリにとって重要な結果をもたらします。

**ステートマシンの特殊化。** コンパイラーは非同期メソッドごとに固有のステートマシン構造体を生成します。`AsyncVlkTaskRunner<TStateMachine>`もそれゆえ非同期メソッドごとに固有であり、`VlkTaskPool<AsyncVlkTaskRunner<TStateMachine>>`はメソッドごとに別のプールです。これは実際には有益です：プールは互換性のない型にわたって共有されることがなく、型の混同リスクを排除します。

**ステートマシンのボクシングなし。** ステートマシンはランナーオブジェクト内に値として保存されます。IL2CPPはこれを正しく処理します。なぜならランナーは具体的な`TStateMachine`フィールドを持つ`sealed class`だからです。

**ストリッピング保護。** `[AsyncMethodBuilder]`属性によりビルダー型が生きた状態を保ちます。ただし、アグレッシブなストリッピングを行うIL2CPPでインターフェース参照を通じて`VlkTask.ISource`を使用する場合は、`UnaPartidaMas.Valkarn.Tasks`アセンブリを保持する`link.xml`エントリを追加してください：

```xml
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```

**`ICriticalNotifyCompletion`。** awaiter構造体は`ICriticalNotifyCompletion`を実装しており、コンパイラーに`OnCompleted`の代わりに`UnsafeOnCompleted`を呼ぶよう指示します。「unsafe」バリアントは意図的に`ExecutionContext`のキャプチャをスキップします。これはUnityでは正しい動作です — UnityのデフォルトConfiguration では`SynchronizationContext`がなく、キャプチャすると利点のないオーバーヘッドが増えます。IL2CPPでは、標準`Task`が常に支払う`ExecutionContext.Run`パスのオーバーヘッドも回避できます。

---

## 実践的な例

### アロケーションなしの早期リターン

```csharp
// ホットパスで同期的に完了するasync VlkTask<int>
async VlkTask<int> GetCachedValue(string key)
{
    if (_cache.TryGetValue(key, out var value))
        return value;            // コンパイラーがSetResult(value)を呼ぶ；sourceはnullのまま

    var result = await FetchFromDatabaseAsync(key);
    _cache[key] = result;
    return result;
}
```

値がキャッシュにある場合、メソッドは一度もサスペンドしません。返された`VlkTask<int>`は`source == null`でインラインに結果を保持します。このパスではヒープアロケーションは発生しません。

### awaitの前にIsCompletedを確認する

```csharp
VlkTask<Texture2D> loadTask = LoadTextureAsync("sprites/hero.png");

if (loadTask.IsCompleted)
{
    // 既に完了 — GetAwaiter().GetResult()はISource呼び出しなしでインライン結果を読む
    Texture2D tex = loadTask.GetAwaiter().GetResult();
    ApplyTexture(tex);
}
else
{
    // 本当に非同期 — コンティニュエーションを登録
    ApplyTextureAsync(loadTask).Forget();
}
```

### 未処理例外の監視

awaitされなかったフォルトタスク（fire-and-forgetパターン）は、`VlkTask.UnobservedException`イベントを通じて例外を報告します。これは、プールされたソースのプール返却時に決定的に発生します。

```csharp
VlkTask.UnobservedException += ex =>
{
    Debug.LogError($"[VlkTask] 未処理: {ex}");
};
```

イベントはスレッドセーフです；ハンドラーはロックフリーのcompare-exchangeループを使用して任意のスレッドから追加・削除できます。
