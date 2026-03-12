---
sidebar_position: 4
title: 設定とResult
---

# ValkarnTaskSettings

`ValkarnTaskSettings`はValkarn Tasksのランタイム動作を制御するUnityの`ScriptableObject`です — 主にガベージを避けるために非同期ステートマシンとプロミスオブジェクトをリサイクルするオブジェクトプールです。

---

## 設定アセットの作成

1. Projectウィンドウで`Resources`フォルダーを右クリックします（ない場合は作成してください）。
2. **Assets > Create > Valkarn Tasks > Task Settings**を選択します。
3. ファイルを`ValkarnTaskSettings`と名前をつけて`Resources`フォルダー内に配置します。

ファイルは正確に`ValkarnTaskSettings`という名前で、プロジェクト内のどこかの`Resources`フォルダーに配置する必要があります。アセットは`Resources.Load<ValkarnTaskSettings>("ValkarnTaskSettings")`でランタイムにロードされます。

アセットが見つからない場合、すべての設定はビルトインのデフォルトにフォールバックします。アセットなしでもライブラリは正しく動作します — デフォルトを変更したい場合にのみ作成が必要です。

---

## ランタイムでの設定アクセス

Unityビルドでは、設定はキャッシュされたシングルトンを通じてアセットから読み取られます：

```csharp
ValkarnTaskSettings settings = ValkarnTaskSettings.Instance;
```

よく使用されるプールパラメーターは便宜上`ValkarnTask`に直接公開されています：

```csharp
int max      = ValkarnTask.DefaultMaxPoolSize;   // ValkarnTaskSettings.Instanceから読み取る
int min      = ValkarnTask.MinPoolSize;
int interval = ValkarnTask.TrimCheckInterval;
```

非Unityビルド（テスト、スタンドアロン.NET）では、`ValkarnTaskSettings`はScriptableObjectの代わりにミュータブルなプロパティを持つ静的クラスです。同じプロパティ名が適用され、直接書き込めます：

```csharp
// 非Unity / テストビルドのみ
ValkarnTask.DefaultMaxPoolSize = 512;
ValkarnTask.TrimCheckInterval  = 600;
ValkarnTask.MinPoolSize        = 16;
```

---

## 設定可能なプロパティ

### プール設定

#### DefaultMaxPoolSize

| | |
|-|-|
| 型 | `int` |
| デフォルト | `256` |
| 有効な範囲 | `8` – `1024` |
| Inspectorツールチップ | "プール型ごとの最大アイテム数。超過アイテムはトリムされます。" |

型ごとの各プールに保持されるオブジェクトの最大数。各個別のジェネリックインスタンス化（例：`PooledPromise<int>`、`PooledPromise<string>`）はこの値で上限が設定される独自のプールを持ちます。

タスクが完了してその内部オブジェクトがプールに返却されるとき、プールが既に`DefaultMaxPoolSize`個のアイテムを保持している場合、返却されたオブジェクトは破棄されます（GC対象）。これにより非同期アクティビティのバーストの後でも無制限のメモリ成長を防ぎます。

プロファイリングで持続的な高スループットの非同期ワークロード中に頻繁なGCアロケーションが示される場合はこの値を増やしてください。メモリプレッシャーが懸念事項でタスクが頻繁に再利用されない場合は減らしてください。

#### MinPoolSize

| | |
|-|-|
| 型 | `int` |
| デフォルト | `8` |
| 有効な範囲 | `1` – `64` |
| Inspectorツールチップ | "最小プールサイズ — これ以下に縮小しません。" |

プールトリムパスはどのプールもこの数以下に減らしません。これにより暖かいプールのベースラインが常に利用可能であることが保証され、トリムパスがすべてを解放してしまう可能性がある静かな期間の後のアロケーションスパイクを回避します。

#### TrimCheckInterval

| | |
|-|-|
| 型 | `int` |
| デフォルト | `300` |
| 有効な範囲 | `30` – `1000` |
| Inspectorツールチップ | "トリムチェック間のフレーム数。60fpsで300 ≈ 5秒。" |

プールトリムパスの間に経過するフレーム数。トリムパスはすべての登録済みプールを検索し、プールが一貫してオーバーサイズであれば超過オブジェクト（`MinPoolSize`を超えるもの）を解放します。

60 fpsではデフォルト値の300は約5秒のチェック間隔に相当します。より積極的で頻繁なトリミングが必要な場合はこの値を下げてください（より頻繁なトリム作業のコストを払います）。プロファイリングでトリムパスがスパイクとして現れる場合は上げてください。

#### TrimHysteresisCount

| | |
|-|-|
| 型 | `int` |
| デフォルト | `2` |
| 有効な範囲 | `1` – `10` |
| Inspectorツールチップ | "トリミング前の連続したしきい値超えチェック数。" |

プールは連続してこの回数分オーバーサイズが観測された後にのみトリムされます。これによりスラッシングを防ぎます — ゲームが短いスパイクの後に静かな期間があった場合、ヒステリシスカウント2はプールが1回の静かなサイクルを乗り越えてからオブジェクトの解放を開始することを意味します。

#### TrimReleaseRatio

| | |
|-|-|
| 型 | `float` |
| デフォルト | `0.25` |
| 有効な範囲 | `0.1` – `1.0` |
| Inspectorツールチップ | "トリムサイクルごとに解放する超過の割合（0.25 = 25%）。" |

プールがトリムされると、一度にすべてではなく、サイクルごとに超過容量（`MinPoolSize`を超えるアイテム）のこの割合が解放されます。`0.25`の値は各トリムパスが超過の25%を削除することを意味します。この段階的な解放により、負荷が再び上昇した場合のアロケーションバーストを引き起こす可能性があるプールサイズの突然の低下を回避します。

すべての超過を各サイクルで即座に解放したい場合は`1.0`に設定してください。

---

### ライフサイクル

#### EnableAutoCancel

| | |
|-|-|
| 型 | `bool` |
| デフォルト | `true` |
| Inspectorツールチップ | "MonoBehaviourのタスクをdestroyancellationTokenに自動バインドします。" |

有効な場合、`MonoBehaviour`から開始されたタスクはその`MonoBehaviour`の`destroyCancellationToken`に自動的にリンクされます。タスクの実行中に`MonoBehaviour`が破棄されると、タスクは破棄されたオブジェクトに対して実行を続ける代わりにキャンセルされます。

キャンセルを手動で管理していて自動バインディングを望まない場合のみ無効にしてください。

---

### エラー処理

#### LogUnobservedCancellations

| | |
|-|-|
| 型 | `bool` |
| デフォルト | `false` |
| Inspectorツールチップ | "未観測のキャンセルを警告としてログに記録します。" |

デフォルトでは、キャンセルされたがawaitされなかったタスク（未観測のキャンセル）は静かに無視されます。これを有効にすると発生時に警告がログに記録されます。暗黙的にキャンセルするfire-and-forgetタスクを見つけるために開発中に有用です。

未観測のフォルトは、この設定に関わらず常に`ValkarnTask.UnobservedException`イベントを通じて報告されます。

#### MaxExceptionLogsPerFrame

| | |
|-|-|
| 型 | `int` |
| デフォルト | `10` |
| 有効な範囲 | `1` – `100` |
| Inspectorツールチップ | "スパムを防ぐためのフレームごとの最大例外ログエントリ数。" |

単一フレームで出力される未観測例外ログエントリの数を制限します。多くのタスクが同じフレームでフォルトした場合（例：ネットワーク障害後）、これによりコンソールが数百の同一スタックトレースで溢れるのを防ぎます。

---

## ランタイムでのプールトリミングの仕組み

各Unityプレイヤーループ更新で、`PlayerLoopHelper`はフレームカウンターをインクリメントします。カウンターが`TrimCheckInterval`に達すると、`PoolRegistry.TrimAll(MinPoolSize)`を呼び出します。登録済みのすべてのプールは、少なくとも`TrimHysteresisCount`回連続してオーバー容量であったかどうかを確認します。そうであれば、超過の`TrimReleaseRatio`を解放します。

プールは最初の使用時に自動的に登録されます。`ValkarnTask.GetPoolInfo()`メソッドはすべての現在登録済みプールのスナップショットを型、現在のサイズ、最大サイズとともに返します — これはTask Trackerウィンドウが表示するものです。

```
フレーム0 ──────────────────────────────────────────────────────────
  プレイヤーループ実行
  プールA: サイズ200、最大256 — 正常、トリム不要

フレーム300 ────────────────────────────────────────────────────────
  TrimAll起動
  プールA: サイズ18、最大256 — MinPoolSize(8)超過だが最初の超過チェック
  AのhysteresisCount = 1（まだしきい値2に達していない）

フレーム600 ────────────────────────────────────────────────────────
  TrimAll起動
  プールA: サイズ18、最大256 — MinPoolSize(8)超過、2回目の超過チェック
  AのhysteresisCount = 2 — しきい値到達！
  超過 = 18 - 8 = 10; 10 * 0.25 = 2個のオブジェクトを解放
  プールA: サイズ16
```

---

## Task Trackerウィンドウ

**Window > Valkarn Tasks > Task Tracker**から開きます。

Task TrackerはプレイモードでライブプールState表示するEditor専用ウィンドウです。設定可能な間隔（デフォルト0.5秒、ツールバーのスライダーで0.1〜5秒に調整可能）でリフレッシュされます。

### プールタブ

最後のドメインリロード以降にアクティブだったすべてのプール型を現在のサイズ（最大から）でソートして一覧表示します。各行は以下を表示します：

| カラム | 説明 |
|--------|-------------|
| 型 | プールされたオブジェクトの型名、ジェネリック引数を展開 |
| サイズ | プール内の現在のオブジェクト数 |
| 最大 | このプールの`DefaultMaxPoolSize`上限 |
| 使用率 | `サイズ / 最大`をパーセンテージで表示するプログレスバー |

プールがまだ使用されていない場合、「プールはアクティブではありません。プールは最初の使用時に作成されます。」というメッセージが表示されます。

### 設定タブ

`ValkarnTask.DefaultMaxPoolSize`、`ValkarnTask.TrimCheckInterval`、`ValkarnTask.MinPoolSize`から読み取った3つのライブプールパラメーターを表示します。値はプレイモード中のみ表示されます — 編集モードでは、ライブ値を見るためにプレイモードに入るよう指示するメモが表示されます。

設定タブは`ValkarnTaskSettings`アセットへの参照も表示します（`Resources`に存在する場合）、クリックして確認または変更できます。アセットが見つからない場合は警告が表示されます。

---

## Result&lt;T&gt;

`Result<T>`はスローせずに操作の結果を表すための判別共用体の構造体です。`WhenAll`コンビネーターがタスクごとの結果を報告するために使用する戻り値型であり、汎用目的の結果パターンとしても利用できます。

```csharp
public readonly struct Result<T>
{
    public bool IsSuccess  { get; }
    public bool IsFailure  { get; }   // フォルトまたはキャンセルの場合true
    public bool IsFaulted  { get; }
    public bool IsCanceled { get; }

    public ValkarnTask.Status Status { get; }
    public T Value    { get; }        // 成功でない場合はスロー
    public Exception Error { get; }   // フォルトでない場合はnull

    public static Result<T> Success(T value);
    public static Result<T> Failure(string error);    // InvalidOperationExceptionでラップ
    public static Result<T> Faulted(Exception error);
    public static Result<T> Canceled(OperationCanceledException oce = null);

    public static implicit operator bool(Result<T> r);  // 成功の場合true
}
```

非ジェネリックの`Result`はvoidタスク用に存在し、`Value`なしで同じシェイプを持ちます。

### AsResult拡張メソッド

`ValkarnTask`または`ValkarnTask<T>`を独自のコードでtry/catchなしに`Result`に変換します：

```csharp
public static ValkarnTask<Result<T>> AsResult<T>(this ValkarnTask<T> task);
public static ValkarnTask<Result>    AsResult(this ValkarnTask task);
```

基礎タスクが既に同期的に完了している場合（ゼロアロケーション高速パス）、`AsResult`は非同期ステートマシンを作成せずに即座に返します。そうでない場合、`OperationCanceledException`とその他のすべての例外をキャッチして適切な`Result`バリアントに変換する非同期メソッドにラップします。

### Result&lt;T&gt;を使用する場面

以下の場合に例外をキャッチする代わりに`Result<T>`を使用します：

- 複数のタスクを並行して呼び出してバッチ全体を短絡させずにタスクごとの結果を取得したい場合。
- 例外コントロールフローなしに型安全な方法で失敗する可能性のある操作を表現したい場合。
- 呼び出し元がtry/catchでラップしたくないかもしれないメソッドから返す場合。

```csharp
// 複数のタスクを起動し、個別の失敗に関わらずすべての結果を取得
Result<int>[] results = await ValkarnTask.WhenAll(
    FetchScoreAsync(playerA).AsResult(),
    FetchScoreAsync(playerB).AsResult(),
    FetchScoreAsync(playerC).AsResult()
);

foreach (var r in results)
{
    if (r.IsSuccess)
        Debug.Log($"スコア: {r.Value}");
    else if (r.IsFaulted)
        Debug.LogError($"失敗: {r.Error.Message}");
    else
        Debug.Log("キャンセルされました");
}
```

`IsSuccess`を確認するか、暗黙的な`bool`演算子を使用することが結果で分岐する優先方法です：

```csharp
var result = await SomeOperationAsync().AsResult();

if (result)
{
    Use(result.Value);
}
```

`IsSuccess`が`false`のときに`result.Value`にアクセスすると`InvalidOperationException`がスローされます。

### ファクトリーメソッド

| メソッド | 設定されるStatus | 設定されるError |
|--------|-----------|-----------|
| `Result<T>.Success(value)` | `Succeeded` | なし |
| `Result<T>.Failure(message)` | `Faulted` | `new InvalidOperationException(message)` |
| `Result<T>.Faulted(exception)` | `Faulted` | 提供された例外 |
| `Result<T>.Canceled(oce?)` | `Canceled` | 提供された`OperationCanceledException`、またはnull |

`Succeeded`プロパティは`Result`と`Result<T>`の両方に存在しますが、`[Obsolete]`とマークされています — `IsFailure`、`IsFaulted`、`IsCanceled`との一貫性のために代わりに`IsSuccess`を使用してください。
