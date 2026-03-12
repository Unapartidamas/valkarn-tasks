---
sidebar_position: 2
title: オブジェクトプーリング
---

# オブジェクトプーリング

Valkarn Tasksは、各`VlkTask`をバックアップするオブジェクトをプーリングすることで、一般的な非同期パスにおけるGCアロケーションを排除します。このページでは、プールのアーキテクチャ — オブジェクトの保存方法、取得・返却方法、システムが提供するライフサイクル保証 — を説明します。

---

## 概要

`async`メソッドがサスペンドすると、ライブラリはコンパイラーが生成したステートマシンを保存する場所と、awaiterがサブスクライブできる完了メカニズムが必要です。`System.Threading.Tasks`では、これはタスクオブジェクト自体です — 呼び出しごとに1回のヒープアロケーション。Valkarn Tasksでは、この役割を`VlkTask.ISource`を実装するプールされたオブジェクトが担います。

プール設計には3つの目標があります：

1. **メインスレッドのホットパスでアトミックゼロ。** Unityのゲームループは慣例的にシングルスレッドです。メインスレッドからのレンタルと返却はプレーンな読み書きであるべきです。
2. **安全なクロススレッドアクセス。** `VlkTask.Run`を使用するバックグラウンドタスクはスレッドプールスレッドで動作します。プールは並行するレンタル/返却を正しく処理しなければなりません。
3. **適応的トリミングによる有界成長。** プールはトラフィックスパイク後に無制限に成長すべきではありませんが、絶えず再アロケーションするほど積極的に縮小すべきでもありません。

---

## `VlkTaskPool<T>`

`VlkTaskPool<T>`はコアプールクラスです。`internal sealed`です — 直接操作することはありませんが、理解することでアロケーションがどこに向かうかを把握できます。

```
VlkTaskPool<T>
  |
  +-- fastItem: T            （シングルスロットキャッシュ、メインスレッドのみ、プレーン読み書き）
  |
  +-- stackHead: T           （Treiberスタックヘッド、クロススレッド安全のためCASベース）
  +-- stackSize: int
  |
  +-- maxSize: int           （VlkTask.DefaultMaxPoolSizeで制限）
  +-- totalCreated: int      （トリム比率のためのライフタイムアロケーションを追跡）
```

### 高速スロット（メインスレッド）

`fastItem`フィールドは最後に返却されたオブジェクトの単一予約スロットです。メインスレッドでは、レンタルと返却はプレーンな読み書きです — アトミック操作なし、スピニングなし。これはUnityゲームループ操作の圧倒的多数をカバーします。

```
レンタル（メインスレッド）：
  fastItem != null  →  取得（fastItem = null）して返す   [ゼロアトミック]
  fastItem == null  →  Treiberスタックにフォールスルー

返却（メインスレッド）：
  fastItem == null  →  fastItem = item                   [ゼロアトミック]
  fastItem != null  →  Treiberスタックにフォールスルー
```

### Treiberスタック（オーバーフロー / バックグラウンドスレッド）

高速スロットが占有されている場合（または呼び出しスレッドがメインスレッドでない場合）、プールはロックフリーのTreiberスタックを使用します — CAS（compare-and-swap）を使用した古典的な侵入型リンクリスト：

```
レンタル（任意のスレッド）：
  while (true):
    head = Volatile.Read(stackHead)
    if head == null: nullを返す（プール空）
    next = head.NextNode
    if CAS(stackHead, next, head) == head: headを返す  // レースに勝った
    spinner.SpinOnce()                                  // 負けた、再試行

返却（任意のスレッド）：
  if stackSize >= maxSize: falseを返す（プールが満杯、アイテムを破棄）
  while (true):
    head = Volatile.Read(stackHead)
    item.NextNode = head
    if CAS(stackHead, item, head) == head: stackSize++; trueを返す
    spinner.SpinOnce()
```

スタックは侵入型です：各プールされたオブジェクトが独自の`NextNode`ポインターを保存するため、外部ラッパーノードは不要です。これは`IPoolNode<T>`インターフェースによって強制されます。

### スレッドルーティング

```csharp
internal static class VlkTaskPoolShared
{
    internal static volatile int MainThreadId;
}
```

すべてのプールインスタンスは単一の`MainThreadId`を共有します。レンタル/返却操作は`Thread.CurrentThread.ManagedThreadId == MainThreadId`を確認して正しいパスにルーティングします。`volatile`フィールドにより、IDがスタートアップ時に公開された後のクロススレッド可視性が保証されます。

---

## `IPoolNode<T>`

プールに参加する型はこのインターフェースを実装する必要があります：

```csharp
internal interface IPoolNode<T> where T : class
{
    ref T NextNode { get; }
}
```

`ref T NextNode`はnextポインターを保存するオブジェクト内フィールドへの参照を返します。プールはrefを通じてこのフィールドに直接書き込み、別のラッパーノードを排除します。ライブラリ内のすべてのプールされた型 — ランナー、プロミス、コンビネーター — はプライベートフィールドを宣言して公開することでこのインターフェースを実装します：

```csharp
// PooledPromise<T>の例
PooledPromise<T> nextNode;
public ref PooledPromise<T> NextNode => ref nextNode;
```

---

## プールのライフサイクル：取得、使用、返却

プールされたオブジェクトの完全なライフサイクルは：

```
呼び出し元が非同期メソッドを呼び出す
  |
  +--> AsyncVlkTaskMethodBuilder.AwaitUnsafeOnCompleted()
        |
        +--> runner == null? YES --> Pool.TryRent() ?? Pool.TrackNew(new Runner())
              |
              +--> runnerはビルダーに保存；ステートマシンはrunnerにコピー
              |
              +--> awaiter.UnsafeOnCompleted(runner.MoveNextAction)
              |
              [... 非同期処理が進む ...]
              |
              +--> runner.SetResult() / runner.SetException()
                    |
                    +--> VlkTaskCompletionCore.TrySetResult() --> コンティニュエーション起動
                          |
                          +--> 呼び出し元のawaiterがGetResult(token)を呼ぶ
                                |
                                +--> core.GetResult(token) -- 結果を読むか再スロー
                                |
                                +--> TryReturn():
                                      stateMachine = default
                                      core.Reset()           // 世代をインクリメント
                                      Pool.TryReturn(this)
```

`TryReturn`メソッドは常に`core.Reset()`を呼ぶ前にステートマシンをクリアします。この順序は重要です：`Reset()`は世代カウンターをインクリメントし、スロットを並行するレンターに利用可能として見えるようにします。`Reset()`の後にステートマシンがクリアされると、別のスレッドのレンターがスロットを取得してそのステートマシンが上書きされる可能性があります。

---

## `VlkTaskCompletionCore<TResult>`

`VlkTaskCompletionCore<TResult>`はすべてのプールされたオブジェクト内に埋め込まれた`internal struct`です。これはプロミスの実際のステートマシンです — 完了状態の追跡、結果とエラーの保存、`OnCompleted`（コンティニュエーションの登録）と`TrySetResult`（完了シグナル）の競合解決を行います。

```
フィールド：
  result:          TResult     -- 成功値
  error:           object      -- ExceptionDispatchInfoまたはOperationCanceledException
  errorKind:       byte        -- 0=なし、1=フォルト、2=キャンセル(OCE)、3=キャンセル(EDI)
  generation:      int         -- 単調増加；トークン比較のためuintにキャスト
  completedCount:  int         -- 0=保留中、1=クレーム済み、2=完了（二段階公開）
  continuation:    Action<object>
  continuationState: object
  hasUnhandledError: bool
```

### 二段階完了プロトコル

完了はARM64でのstoreリリース/loadアクワイアペアが必要なため、二段階CASを使用します：

```
TrySetResult(value):
  フェーズ1: CAS(completedCount, 0 -> 1)   -- 排他的所有権をクレーム
  フェーズ2: resultを書き込む
  フェーズ3: Volatile.Write(completedCount, 2)  -- リリースセマンティクスで公開
  フェーズ4: InvokeContinuation()
```

読者はresultを読む前に`Volatile.Read(completedCount)`（アクワイアセマンティクス）を使用し、フェーズ2で書き込まれた値を確認します。

### `OnCompleted`と`TrySetResult`の競合解決

3つのパターンが発生する可能性があります：

```
パターンA — OnCompletedが先：
  OnCompletedがCAS(continuation, null -> cont)でコンティニュエーションを保存
  TrySetResultがnull以外のコンティニュエーションを読み取る -> 起動

パターンB — TrySetResultが先（同期高速パス）：
  TrySetResultがCAS(continuation, null -> sentinel)でContinuationSentinelを配置
  OnCompletedがsentinelを読み取る -> インラインでコンティニュエーションを即座に起動

パターンC（並行競合）：
  C.1: OnCompletedがCASに勝つ -> TrySetResultがそれを読む -> 起動
  C.2: TrySetResultがCASに勝つ（sentinelを配置）-> OnCompletedがsentinelを検出 -> インラインで起動
```

sentinelは純粋にマーカー値として使用される静的な`Action<object>`オブジェクトです — 実際にデリゲートとして起動されることはありません。

### トークン検証とABA安全性

`GetStatus`、`GetResult`、`OnCompleted`へのすべての呼び出しは、`uint token`を現在の`generation`と検証します。`Reset()`が`Interlocked.Increment(ref generation)`を呼び出すと、古いトークンを持つ未処理の`VlkTask`構造体は、リサイクルされた状態を暗黙的に操作する代わりに`InvalidOperationException`を受け取ります。32ビットの世代カウンターのラップアラウンド（単一スロットの約40億回の再利用が必要）は実際には不可能と見なされます。

### リセットと未処理エラー報告

`Reset()`はプール返却時に呼ばれます。世代をインクリメントする前に、エラーが保存されているがまだ観測されていないかどうか（つまり、フォルト後に`GetResult`が一度も呼ばれていないか）を確認します。そうであれば、`VlkTask.UnobservedException`を通じて例外を公開します。キャンセルエラーは意図的なことが多いため、`VlkTaskSettings`で`LogUnobservedCancellations`が有効になっている場合のみ報告されます。

非プールの`Promise`と`Promise<T>`オブジェクトでは、未処理エラー報告はファイナライザーから`ReportUnobservedIfNeeded()`を通じて行われ、状態をクリアせずに同じロジックに従います。

---

## プール設定

3つの設定がプールのサイジングを制御します。Unityビルドでは、これらは`VlkTaskSettings` ScriptableObjectアセットから読み取られ（フォールバックデフォルト付き）、静的プロパティを通じてランタイムでオーバーライドできます：

```csharp
// プール型ごとの最大オブジェクト数（TStateMachineまたはプロミス型ごと）
VlkTask.DefaultMaxPoolSize = 256;   // デフォルト: 256

// トリムチェック間のフレーム数（Unity PlayerLoopフレーム）
VlkTask.TrimCheckInterval = 300;    // デフォルト: 300（60fpsで約5秒）

// トリムパス後に保持する最小オブジェクト数
VlkTask.MinPoolSize = 8;            // デフォルト: 8
```

`DefaultMaxPoolSize`はプール構築時に適用される上限です。グローバルではなくプールインスタンスごとに適用されます — `AsyncVlkTaskRunner<LoadSceneStateMachine>`のプールと`AsyncVlkTaskRunner<FetchDataStateMachine>`のプールはそれぞれ独自の上限を持ちます。

### プールのトリミング

`PlayerLoopHelper`は`TrimCheckInterval`フレームごとにメインスレッドで`PoolRegistry.TrimAll(minPoolSize)`を呼び出します。各プールはヒステリシス戦略を使用します：

```
各トリムチェック：
  currentSize = fastItem.count + stackSize
  if currentSize <= MinPoolSize: 連続カウントをリセット、スキップ

  ratio = currentSize / totalCreated
  if ratio > 0.5（プールが作成されたすべてのオブジェクトの50%超を保持）：
    trimConsecutiveCount++
    if trimConsecutiveCount >= hysteresisThreshold:
      スタックからexcessオブジェクトの一部（releaseRatio）を解放
      （fastItemは保持 — 最もキャッシュフレンドリーなスロット）
  else:
    trimConsecutiveCountをリセット
```

ヒステリシスにより、短時間のトラフィックスパイクがすべてのオブジェクトを即座にアロケーションして即座にトリムすることを防ぎます。高速スロットはトリミング中に常に保持されます。これは最も最近使用されたアイテムを表し、再び必要になる可能性が最も高いためです。

---

## `PoolRegistry`とモニタリング

すべての`VlkTaskPool<T>`は構築時にグローバルな`PoolRegistry`に自身を登録します。レジストリは`IPoolInfo`参照のリストを維持し、以下を公開します：

```csharp
internal interface IPoolInfo
{
    Type PooledType { get; }
    int Size { get; }
    int MaxSize { get; }
    bool IsAlive { get; }
    int Trim(int minPoolSize);
}
```

パブリックAPIを使用してランタイムですべてのアクティブなプールを列挙できます：

```csharp
foreach (var (type, size, maxSize) in VlkTask.GetPoolInfo())
{
    Debug.Log($"{type.Name}: {size}/{maxSize}");
}
```

これはUnity Editorの**Task Tracker**ウィンドウが表示するのと同じデータです。ウィンドウは`GetPoolInfo()`をポーリングしてプール占有率のライブテーブルを表示し、プールがウォームアップされているか、どの型が一貫して上限に達しているか、トリミングが期待通りに機能しているかを確認できます。

`IsAlive`がfalseを返すデッドプールエントリは、プールインスタンスが何らかの理由でGCされた場合にレジストリが無制限に成長するのを防ぐため、`GetAll()`と`TrimAll()`呼び出し中に遅延プルーニングされます。

---

## `PooledPromise`と`PooledPromise<T>`

これらはカスタム非同期パターンで使用することを意図したプール済み完了ソースです — 例えば、コールバックベースのAPIや繰り返しのプロデューサー/コンシューマーチャネルのラップに使用します。

```csharp
// プールから保留中のプロミスを取得
var promise = VlkTask.PooledPromise<string>.Create(out uint token);
VlkTask<string> task = promise.Task;

// コンシューマーにタスクを渡す
// ... 後で、任意のスレッドから ...
promise.TrySetResult("hello");

// コンシューマーがtaskをawaitしてGetResultが呼ばれると、
// プロミスはリセットされてプールに自動的に返却されます。
```

主要な特性：

- `Create(out uint token)`はプールからレンタルするか、プールが追跡する新しいインスタンスをアロケーションします。
- `CreateCompleted(T result, out uint token)`は同じことをしますが、即座に結果をシグナルするので、タスクは返却時に既に完了しています。
- バッキングタスクの`GetResult`が呼ばれた後、`TryReturn()`が起動します：プロミスは`core.Reset()`を呼び出し、自身をプールに返却します。
- ダブルリターンガード（`Interlocked.Exchange(ref returned, 1)`）により、`GetResult`が二度呼ばれてもプールの破損を防ぎます。

**非プール代替：`Promise`と`Promise<T>`。** これらはプールに返却されないヒープアロケーションのクラスです。ライフタイムが予測できない、または同じソースが複数のawaitサイクルを生き延びる必要がある長期的な操作に使用します。未処理例外の報告はファイナライザーに依存します。

---

## コンビネータープール

`WhenAll`と`WhenAny`コンビネーターもプールを使用します。各アリティと型の組み合わせは独自のプールを持ちます：

| コンビネーター | プール型 |
|-----------|-----------|
| `WhenAll(task1, task2)`（型付き） | `VlkTaskPool<WhenAllPromise<T1, T2>>` |
| `WhenAll(IEnumerable<VlkTask<T>>)` | `VlkTaskPool<WhenAllArrayPromise<T>>` |
| `WhenAll(task1, task2)`（void） | `VlkTaskPool<WhenAllVoidPromise2>` |
| `WhenAny(task1, task2)`（型付き） | `VlkTaskPool<WhenAnyPromise2<T>>` |
| `WhenAny(IEnumerable<VlkTask<T>>)` | `VlkTaskPool<WhenAnyArrayPromise<T>>` |

配列ベースのコンビネーター（`WhenAll<T>(IEnumerable<...>)`と`WhenAny<T>(IEnumerable<...>)`）は内部のソース/トークン配列に`System.Buffers.ArrayPool<T>.Shared`を使用するため、それらの配列も呼び出しごとに新しくアロケーションされるのではなくリサイクルされます。

すべてのコンビネーターは同じゼロアロケーションのショートサーキットを適用します：`WhenAll`または`WhenAny`が呼ばれた時点ですべての入力が同期的に完了している場合、新しいプールされたオブジェクトは作成されません。

```csharp
// ゼロアロケーション — 両方のタスクが同期完了
var t1 = VlkTask.FromResult(1);
var t2 = VlkTask.FromResult(2);
VlkTask<(int, int)> combined = VlkTask.WhenAll(t1, t2);
// combined.source == null; 結果は(1, 2)でインライン保存
```
