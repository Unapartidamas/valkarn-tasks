---
sidebar_position: 1
title: Unity PlayerLoop統合
---

# Unity PlayerLoop統合

ValkarnTasksは`async`メソッドを再開するためにスレッドやバックグラウンドスケジューラーを使用しません。すべてはUnityのメインスレッド上で、UnityのネイティブのPlayerLoopシステムによって実行されます。この仕組みを理解することで、ユースケースに適したタイミングを選択し、`await`後にコードが正確いつ再開されるかを把握できます。

## Unity PlayerLoopとは

UnityのPlayerLoopはすべてのフレームを駆動する内部エンジンループです。単一の`Update()`呼び出しではありません — フレームごとに定義された順序で実行されるフェーズの階層的なシーケンスです：

```
Initialization
EarlyUpdate
FixedUpdate      （物理がステップする場合は繰り返し）
PreUpdate
Update
PreLateUpdate
PostLateUpdate
TimeUpdate
```

これらの各トップレベルフェーズには、Unity（およびサードパーティパッケージ）が特定のポイントでロジックを実行するために挿入するサブシステムが含まれています。例えば、`MonoBehaviour.Update()`は`Update`フェーズ内で実行されます。`MonoBehaviour.LateUpdate()`は`PreLateUpdate`内で実行されます。

ValkarnTasksはこのループに直接フックするため、`await ValkarnTask.Yield()`はスレッドをパークしません — 選択されたフェーズの次のティックでUnityが呼び出すコールバックを登録して即座に戻ります。

## 16のPlayerLoopタイミング

ValkarnTasksは16のポイントで注入します：8つのUnity PlayerLoopフェーズそれぞれの**開始**と**終了**に1つずつ。`Last`バリアントは親フェーズのサブシステムリストの末尾に追加され、プレーンバリアントは先頭に追加されます。

| 値 | 列挙型整数 | 親フェーズ | 親内の位置 |
|---|---|---|---|
| `Initialization` | 0 | `UnityEngine.PlayerLoop.Initialization` | 最初 |
| `LastInitialization` | 1 | `UnityEngine.PlayerLoop.Initialization` | 最後 |
| `EarlyUpdate` | 2 | `UnityEngine.PlayerLoop.EarlyUpdate` | 最初 |
| `LastEarlyUpdate` | 3 | `UnityEngine.PlayerLoop.EarlyUpdate` | 最後 |
| `FixedUpdate` | 4 | `UnityEngine.PlayerLoop.FixedUpdate` | 最初 |
| `LastFixedUpdate` | 5 | `UnityEngine.PlayerLoop.FixedUpdate` | 最後 |
| `PreUpdate` | 6 | `UnityEngine.PlayerLoop.PreUpdate` | 最初 |
| `LastPreUpdate` | 7 | `UnityEngine.PlayerLoop.PreUpdate` | 最後 |
| `Update` | 8 | `UnityEngine.PlayerLoop.Update` | 最初 |
| `LastUpdate` | 9 | `UnityEngine.PlayerLoop.Update` | 最後 |
| `PreLateUpdate` | 10 | `UnityEngine.PlayerLoop.PreLateUpdate` | 最初 |
| `LastPreLateUpdate` | 11 | `UnityEngine.PlayerLoop.PreLateUpdate` | 最後 |
| `PostLateUpdate` | 12 | `UnityEngine.PlayerLoop.PostLateUpdate` | 最初 |
| `LastPostLateUpdate` | 13 | `UnityEngine.PlayerLoop.PostLateUpdate` | 最後 |
| `TimeUpdate` | 14 | `UnityEngine.PlayerLoop.TimeUpdate` | 最初 |
| `LastTimeUpdate` | 15 | `UnityEngine.PlayerLoop.TimeUpdate` | 最後 |

`Update`（値8）はすべてのValkarnTasks操作 — `Yield()`、`Delay()`、`WaitUntil()`、`WaitWhile()`、`NextFrame()`、`DelayFrame()` — の**デフォルト**です。

## マーカー構造体とべき等な注入

UnityのPlayerLoopに注入するため、ValkarnTasksはタイミングごとに1つの`PlayerLoopSystem`を作成します。各システムは`PlayerLoopHelper`内で定義された固有のマーカー構造体によって識別されます：

```csharp
struct ValkarnTaskInitialization     { }
struct ValkarnTaskLastInitialization { }
struct ValkarnTaskEarlyUpdate        { }
struct ValkarnTaskLastEarlyUpdate    { }
struct ValkarnTaskFixedUpdate        { }
struct ValkarnTaskLastFixedUpdate    { }
struct ValkarnTaskPreUpdate          { }
struct ValkarnTaskLastPreUpdate      { }
struct ValkarnTaskUpdate             { }
struct ValkarnTaskLastUpdate         { }
struct ValkarnTaskPreLateUpdate      { }
struct ValkarnTaskLastPreLateUpdate  { }
struct ValkarnTaskPostLateUpdate     { }
struct ValkarnTaskLastPostLateUpdate { }
struct ValkarnTaskTimeUpdate         { }
struct ValkarnTaskLastTimeUpdate     { }
```

注入前に、`PlayerLoopHelper`は`ValkarnTaskUpdate`が現在のPlayerLoopツリーのどこかに既に存在するかを確認します。存在する場合、注入はスキップされます。これにより注入が**べき等**になります — `Init()`を複数回呼び出しても（エディターで発生する可能性がある）、重複するシステムが登録されることはありません。

注入は常に`PlayerLoop.GetCurrentPlayerLoop()`で**現在の**PlayerLoopを読み取り、`GetDefaultPlayerLoop()`ではありません。これにより、他のパッケージが以前に設置したシステムが保持されます。

## ContinuationQueue — ワンショットコールバック

`await ValkarnTask.Yield()`（または正確に1ティック間サスペンドするもの）をawaitすると、コンパイラーが生成したステートマシンはawaiterの`OnCompleted`を呼び出します。awaiterは`PlayerLoopHelper.AddContinuation(timing, action, state)`を呼び出し、コールバックをそのタイミングの`ContinuationQueue`にエンキューします。

`ContinuationQueue`は**ダブルバッファー**設計を使用します：

1. **アクティブバッファー**（`actionList`）：現在のドレイン前後にキューされたコンティニュエーションを保持します。
2. **待機バッファー**（`waitingList`）：アクティブバッファーがドレインされている最中にエンキューされたコンティニュエーション（つまり、コンティニュエーション自体からの再入エンキュー）をキャッチします。
3. **クロススレッドTreiberスタック**（`crossThreadHead`）：バックグラウンドスレッドからポストされたコンティニュエーションは、ロックフリーのcompare-and-swapを通じてここに着地します。各ドレインの開始時に、スタック全体がアトミックにクレームされてアクティブバッファーに移されます。

各PlayerLoopティックで、`ContinuationQueue.Run()`はこのシーケンスを実行します：

```
1. crossThreadHeadをactionListにドレイン     （ロックフリーアトミックテイク、次にSpinLock下でコピー）
2. カウントをスナップショット、isDraining = trueに設定  （SpinLock下）
3. すべてのコールバックを実行                （ロック外、GCのためにrefをクリア）
4. actionList ↔ waitingListをスワップ        （SpinLock下、isDraining = falseに設定）
```

約1〜2フレームのウォームアップ後、内部配列は最高水位マークで安定し、キューは**ゼロアロケーション**で実行されます。

クロススレッドノードは、バックグラウンドスレッドのエンキューのたびにアロケーションするのを避けるため、有界のロックフリープール（最大1,024ノード）にプールされます。

## PlayerLoopRunner — 繰り返しアイテム

一部の操作は完了するまでティックごとにチェックされる必要があります：`Delay()`、`DelayFrame()`、`WaitUntil()`、`WaitWhile()`、`NextFrame()`。これらは`IPlayerLoopItem`インターフェースを実装します：

```csharp
internal interface IPlayerLoopItem
{
    // 実行を継続する場合はtrueを返す；ランナーから削除する場合はfalseを返す。
    bool MoveNext();
}
```

アイテムは`PlayerLoopHelper.AddAction(timing, item)`を通じて`PlayerLoopRunner`に追加されます。各ティックで、`PlayerLoopRunner.Run()`はすべての登録済みアイテムを反復して各アイテムの`MoveNext()`を呼び出します。`false`を返したアイテムは**インプレースコンパクション**によって削除されます — 配列は挿入順序を保持しながら一回のパスでコンパクトされます。`Run()`呼び出し中に追加されたアイテムは安全に追加され、次のティックで処理されます。

```
ティックN：
  ContinuationQueue.Run()   → すべてのワンショットawaiterを再開
  PlayerLoopRunner.Run()    → すべての繰り返しアイテムをティック（Delay、WaitUntil、...）
```

両方とも16のタイミングすべてに対して、エンジンが呼び出す順序で実行されます。

`Update`タイミングはグローバルフレームカウントも追跡し、定期的にオブジェクトプールのトリミングをトリガーします（`ValkarnTask.TrimCheckInterval`フレームごと）。

## 初期化 — RuntimeInitializeOnLoadMethod

ValkarnTasksは`RuntimeInitializeLoadType.SubsystemRegistration`（Unityが提供する最も早いフック、シーンオブジェクトの`Awake()`より前に起動）で初期化されます：

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void Init()
{
    // 1. スレッド安全チェックのためにメインスレッドIDをキャプチャ
    // 2. すべての16タイミングのContinuationQueueとPlayerLoopRunnerを作成
    // 3. PlayerLoopに注入（べき等）
    // 4. プレイモード状態変更コールバックを登録（エディターのみ）
}
```

メインスレッドIDはこの時点でキャプチャされ、すべてのプールとキューサブシステムと共有されます。これによりメインスレッドエンキュー（SpinLockパス）とクロススレッドエンキュー（Treiberスタックパス）を区別できます。

## ドメインリロード処理（エディター）

Unity Editorでは、プレイモードへの出入りがドメインリロードをトリガーします。前のプレイセッションの静的状態が持続すると、古い参照が残ります。

ValkarnTasksはこれを`Init()`中に登録された`EditorApplication.playModeStateChanged`コールバックで処理します。状態が`ExitingPlayMode`または`EnteredEditMode`に遷移すると、以下のクリーンアップが実行されます：

```csharp
// すべてのキューとランナーをリセット
for (int i = 0; i < 16; i++)
{
    s_continuationQueues[i] = null;
    s_playerLoopRunners[i] = null;
}

// 他のサブシステムをリセット
ValkarnTask.ResetStatics();
TimeProvider.ResetToDefault();   // UnityTimeProviderに戻す
PoolRegistry.Clear();
ContinuationQueue.ContinuationNode.ResetPool();
```

その後プレイモードに再入すると、`RuntimeInitializeOnLoadMethod`によって`Init()`が起動し、新しいキューとランナーがアロケーションされます。PlayerLoop注入チェック（`HasValkarnTaskSystems`）により、ドメインが完全にアンロードされていない場合にマーカーシステムが二度挿入されるのを防ぎます。

## 適切なタイミングの選択

ほとんどのコードはデフォルトの`Update`タイミングを使用すべきです。他を選ぶのは特定の理由がある場合のみです。

| タイミング | 使用する場面 |
|---|---|
| `Initialization` / `LastInitialization` | 非常に早いフレームセットアップ；ゲームコードではほとんど不要 |
| `EarlyUpdate` / `LastEarlyUpdate` | 入力サンプリング；物理と`Update`より前に実行 |
| `FixedUpdate` / `LastFixedUpdate` | 物理同期ロジック；`MonoBehaviour.FixedUpdate`のペースに合致 |
| `PreUpdate` / `LastPreUpdate` | Unityのメイン更新ディスパッチ前；カスタムスケジューラーのプリパスに有用 |
| **`Update`**（デフォルト） | **標準ゲームロジック；`MonoBehaviour.Update`に合致** |
| `LastUpdate` | すべての`MonoBehaviour.Update`呼び出し後に実行するロジック |
| `PreLateUpdate` / `LastPreLateUpdate` | `MonoBehaviour.LateUpdate`に合致；カメラとトランスフォームのフォロー |
| `PostLateUpdate` / `LastPostLateUpdate` | レンダリングサブミット後；UIの最終化、スクリーンショットキャプチャ |
| `TimeUpdate` / `LastTimeUpdate` | 時間値の同期；非常にまれに必要 |

```csharp
// 次のUpdateティックで再開（デフォルト）
await ValkarnTask.Yield();

// 次のFixedUpdateの開始時に再開（物理安全）
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// 2秒待機、非スケール時間で進み、LateUpdateでチェック
await ValkarnTask.Delay(
    TimeSpan.FromSeconds(2),
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.PreLateUpdate);

// 条件がtrueになるまで待機、すべてのLateUpdate呼び出し後にチェック
await ValkarnTask.WaitUntil(() => _ready, PlayerLoopTiming.LastPreLateUpdate);
```
