---
sidebar_position: 2
title: PlayerLoopTiming
---

# PlayerLoopTiming

```csharp
public enum PlayerLoopTiming : byte
```

名前空間：`UnaPartidaMas.Valkarn.Tasks`

ValkarnTasks操作がサスペンド後に再開するために使用するUnity PlayerLoopフェーズを指定します。`PlayerLoopTiming`値を渡すことで、`await`コンティニュエーションが**フレーム内のいつ**実行されるか、および遅延や待機条件などの繰り返しアイテムが**いつ**チェックされるかを制御します。

列挙型の値はUniTaskの`PlayerLoopTiming`列挙型と正確に一致しており、UniTaskからの移行が簡単です。

---

## デフォルト

`PlayerLoopTiming.Update`（値`8`）はすべての操作のデフォルトです。タイミング引数を渡さない場合、`Update`が使用されます。

---

## 値

### Initialization

```
値: 0
親フェーズ: UnityEngine.PlayerLoop.Initialization
位置: フェーズの最初のサブシステム
```

そのフェーズ内のUnity独自の初期化サブシステムより前、Initializationフェーズの最初に実行されます。これは`EarlyUpdate`の前にフレームごとに一度起動します。ゲームプレイコードではほとんど役立ちませんが、フレーム内の他のものが触れる前に状態を読み取りまたは設定する必要があるシステムに関連する場合があります。

---

### LastInitialization

```
値: 1
親フェーズ: UnityEngine.PlayerLoop.Initialization
位置: フェーズの最後のサブシステム
```

UnityのビルトインInitializationサブシステムの後、Initializationフェーズの終わりに実行されます。初期化フェーズのタイミングが必要だがUnity独自のシステムを先に実行させたい場合は`Initialization`よりこちらを優先してください。

---

### EarlyUpdate

```
値: 2
親フェーズ: UnityEngine.PlayerLoop.EarlyUpdate
位置: フェーズの最初のサブシステム
```

Unityが入力イベントを処理する前および物理シミュレーションステップの前、EarlyUpdateの開始時に実行されます。これは前フレームの入力データが利用可能なフレーム内の最も早いポイントです。ゲームプレイコードが実行される前に入力をサンプリングする必要があるシステムに有用です。

---

### LastEarlyUpdate

```
値: 3
親フェーズ: UnityEngine.PlayerLoop.EarlyUpdate
位置: フェーズの最後のサブシステム
```

UnityのEarlyUpdateサブシステムが完了した後、EarlyUpdateの終わりに実行されます。

---

### FixedUpdate

```
値: 4
親フェーズ: UnityEngine.PlayerLoop.FixedUpdate
位置: フェーズの最初のサブシステム
```

各FixedUpdateステップの開始時に実行されます。これは`MonoBehaviour.FixedUpdate()`のタイミングに対応します。Unityは物理タイムステップとフレームデルタタイムの整合に応じて、レンダリングフレームごとにゼロまたは複数のFixedUpdateステップを実行する場合があります。

物理シミュレーションと同期する必要があるすべてに使用します：`Rigidbody`速度の読み取り、力の適用、物理で決定されたステップ数の待機。

```csharp
// 10物理ステップを待機
await ValkarnTask.DelayFrame(10, PlayerLoopTiming.FixedUpdate, ct);

// 物理条件がtrueになるまで待機、各物理ステップでチェック
await ValkarnTask.WaitUntil(() => rb.velocity.magnitude < 0.1f,
    PlayerLoopTiming.FixedUpdate, ct);
```

---

### LastFixedUpdate

```
値: 5
親フェーズ: UnityEngine.PlayerLoop.FixedUpdate
位置: フェーズの最後のサブシステム
```

UnityのPhysicsサブシステム（`Physics.Simulate`を含む）が完了した後、FixedUpdateフェーズの終わりに実行されます。シミュレーションが進んだ後に物理結果を読み取るために使用します。

---

### PreUpdate

```
値: 6
親フェーズ: UnityEngine.PlayerLoop.PreUpdate
位置: フェーズの最初のサブシステム
```

FixedUpdateの後でUpdateの前に実行されるPreUpdateフェーズの開始時に実行されます。UnityはPreUpdateをウィンドゾーン更新やネットワークイベント処理などのタスクに使用します。

---

### LastPreUpdate

```
値: 7
親フェーズ: UnityEngine.PlayerLoop.PreUpdate
位置: フェーズの最後のサブシステム
```

PreUpdateの終わりに実行されます。

---

### Update

```
値: 8  （デフォルト）
親フェーズ: UnityEngine.PlayerLoop.Update
位置: フェーズの最初のサブシステム
```

すべてのValkarnTasks操作の**デフォルトタイミング**。`MonoBehaviour.Update()`が起動するUpdateフェーズの開始時に実行されます。ゲームプレイコードの圧倒的多数に適した選択です。

```csharp
// これらはすべてデフォルトでUpdateを使用
await ValkarnTask.Yield();
await ValkarnTask.Delay(1000, ct);
await ValkarnTask.WaitUntil(() => _ready, ct: ct);
await ValkarnTask.NextFrame(ct: ct);
await ValkarnTask.DelayFrame(3, ct: ct);

// 明示的 — 上記と同一
await ValkarnTask.Yield(PlayerLoopTiming.Update);
```

---

### LastUpdate

```
値: 9
親フェーズ: UnityEngine.PlayerLoop.Update
位置: フェーズの最後のサブシステム
```

すべての`MonoBehaviour.Update()`呼び出しと他のUpdateサブシステムが完了した後、Updateフェーズの終わりに実行されます。コンティニュエーションが他のスクリプトの`Update()`メソッドの結果を観測しなければならない場合に使用します — 例えば、別のスクリプトが`Update`中に書き込む値をポーリングする場合。

```csharp
// このフレームのすべてのMonoBehaviour.Update()呼び出し後に再開
await ValkarnTask.Yield(PlayerLoopTiming.LastUpdate);
```

---

### PreLateUpdate

```
値: 10
親フェーズ: UnityEngine.PlayerLoop.PreLateUpdate
位置: フェーズの最初のサブシステム
```

`MonoBehaviour.LateUpdate()`が起動するPreLateUpdateフェーズの開始時に実行されます。カメラフォロー、トランスフォーム調整、`Update`中に設定された最終位置に反応すべきすべてに使用します。

```csharp
// MonoBehaviour.LateUpdate()と同じポイントで再開
await ValkarnTask.Yield(PlayerLoopTiming.PreLateUpdate);
```

---

### LastPreLateUpdate

```
値: 11
親フェーズ: UnityEngine.PlayerLoop.PreLateUpdate
位置: フェーズの最後のサブシステム
```

すべての`MonoBehaviour.LateUpdate()`呼び出しの後、PreLateUpdateフェーズの終わりに実行されます。スクリプトが`LateUpdate`中に設定した値を読む必要がある場合に使用します。

---

### PostLateUpdate

```
値: 12
親フェーズ: UnityEngine.PlayerLoop.PostLateUpdate
位置: フェーズの最初のサブシステム
```

PostLateUpdateの開始時に実行されます。このフェーズはフレームのレンダリングがサブミットされた後に起動します。Unityはフレームバッファーのプレゼンテーションやレンダリング状態のクリーンアップなどのタスクに使用します。`Camera.onPostRender`コールバックもここで起動します。

フレーム終了時の操作に使用します：スクリーンショットキャプチャ、ストリーミングレベルのアンロード、シーンが完全にレンダリングされた後で次のフレームが始まる前に行う必要がある作業。

---

### LastPostLateUpdate

```
値: 13
親フェーズ: UnityEngine.PlayerLoop.PostLateUpdate
位置: フェーズの最後のサブシステム
```

Unityがフレームローカル状態をリセットして次のフレームを開始する前の標準フレームループの最後のポイント。タイミング目的ではフレームの絶対的な終わりです。

---

### TimeUpdate

```
値: 14
親フェーズ: UnityEngine.PlayerLoop.TimeUpdate
位置: フェーズの最初のサブシステム
```

TimeUpdateフェーズの開始時に実行されます。UnityはこのフェーズをTime関連の状態（`Time.time`など）の進行に使用します。このタイミングは新しいUnityバージョンでは`EarlyUpdate`の前に起動します。

このタイミングはゲームコードではほとんど必要ありません。主に他のシステムが新しい時間値を読む前に時間の進行を傍受または観測する必要があるシステムのために存在します。

---

### LastTimeUpdate

```
値: 15
親フェーズ: UnityEngine.PlayerLoop.TimeUpdate
位置: フェーズの最後のサブシステム
```

UnityのTime関連サブシステムが完了した後、TimeUpdateフェーズの終わりに実行されます。

---

## まとめ表

| 値 | 名前 | Unityフェーズ | 位置 | 相当するMonoBehaviourコールバック |
|---|---|---|---|---|
| 0 | `Initialization` | `Initialization` | 最初 | — |
| 1 | `LastInitialization` | `Initialization` | 最後 | — |
| 2 | `EarlyUpdate` | `EarlyUpdate` | 最初 | — |
| 3 | `LastEarlyUpdate` | `EarlyUpdate` | 最後 | — |
| 4 | `FixedUpdate` | `FixedUpdate` | 最初 | `FixedUpdate()` |
| 5 | `LastFixedUpdate` | `FixedUpdate` | 最後 | `FixedUpdate()`後 |
| 6 | `PreUpdate` | `PreUpdate` | 最初 | — |
| 7 | `LastPreUpdate` | `PreUpdate` | 最後 | — |
| **8** | **`Update`**（デフォルト） | **`Update`** | **最初** | **`Update()`** |
| 9 | `LastUpdate` | `Update` | 最後 | すべての`Update()`後 |
| 10 | `PreLateUpdate` | `PreLateUpdate` | 最初 | `LateUpdate()` |
| 11 | `LastPreLateUpdate` | `PreLateUpdate` | 最後 | すべての`LateUpdate()`後 |
| 12 | `PostLateUpdate` | `PostLateUpdate` | 最初 | レンダリング後 |
| 13 | `LastPostLateUpdate` | `PostLateUpdate` | 最後 | フレーム終了 |
| 14 | `TimeUpdate` | `TimeUpdate` | 最初 | — |
| 15 | `LastTimeUpdate` | `TimeUpdate` | 最後 | — |

---

## PlayerLoopTimingを受け入れるAPI

時間ベースおよび条件ベースのすべてのValkarnTasks APIはオプションの`PlayerLoopTiming`パラメーターを受け入れます。デフォルトは常に`Update`です。

| メソッド | シグネチャ |
|---|---|
| `ValkarnTask.Yield` | `Yield(PlayerLoopTiming timing = Update)` |
| `ValkarnTask.NextFrame` | `NextFrame(PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.DelayFrame` | `DelayFrame(int frameCount, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.Delay(int)` | `Delay(int ms, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.Delay(TimeSpan)` | `Delay(TimeSpan delay, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.WaitUntil` | `WaitUntil(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `ValkarnTask.WaitWhile` | `WaitWhile(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |

---

## コード例

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using System.Threading;

// 次のUpdateティックにyield（デフォルト）
await ValkarnTask.Yield();

// 次のFixedUpdateティックにyield — 物理駆動コード内で使用
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// スケール済みdeltaTimeを使用して500ms待機、各Updateでチェック
await ValkarnTask.Delay(500, ct: destroyCancellationToken);

// 非スケールdeltaTimeを使用して500ms待機 — Time.timeScaleの影響を受けない
await ValkarnTask.Delay(
    500,
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// リアル壁時計時間を使用して500ms待機（Stopwatchベース）
await ValkarnTask.Delay(
    500,
    DelayType.Realtime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// LateUpdateでチェックしてフラグが設定されるまで待機
bool _isReady;
await ValkarnTask.WaitUntil(() => _isReady, PlayerLoopTiming.PreLateUpdate, destroyCancellationToken);

// フレームの最後までロードしながら待機
await ValkarnTask.WaitWhile(() => _loading, PlayerLoopTiming.LastPostLateUpdate, destroyCancellationToken);

// ちょうど5物理ステップをスキップ
await ValkarnTask.DelayFrame(5, PlayerLoopTiming.FixedUpdate, destroyCancellationToken);

// レンダリングされた1フレームを進める
await ValkarnTask.NextFrame(PlayerLoopTiming.Update, destroyCancellationToken);
```
