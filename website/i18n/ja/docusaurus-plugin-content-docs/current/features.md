---
sidebar_position: 4
title: 機能
---

# 機能

Valkarn Tasks の完全な機能リファレンス。

---

## コア非同期プリミティブ

### ValkarnTask 構造体

`UniTask` と Unity の `Awaitable` の両方を置き換える、ゼロアロケーションの構造体ベースの非同期戻り値型です。

```csharp
async ValkarnTask LoadLevel() { ... }
async ValkarnTask<int> CountEnemies() { ... }
```

- **ゼロアロケーション同期高速パス** — メソッドが一度もサスペンドせずに完了した場合、ヒープアロケーションはゼロです。
- **プール済み非同期パス** — メソッドがサスペンドした場合、ステートマシンランナーは境界付きで縮小可能なプールから取得されます。ボクシングなし、自動トリミング付きの制限付きプール。
- **IL2CPP優先プーリング** — メインスレッドでのプール操作はアトミックゼロです。IL2CPP は Mono に比べて `Volatile` を9.2倍、`Interlocked` を2.9倍遅くします。Valkarnはホットパスで両方を回避します。
- **世代トークンの安全性** — プールスロットごとの `uint` 世代カウンター。衝突まで1スロットあたり4,294,967,296サイクル — 実際には不可能です。（UniTask は `short` トークンを使用: 活発な非同期作業の約18分後に衝突。）

### Result\<T\> — 例外のないエラーハンドリング

```csharp
var result = await loadTask.AsResult();

if (result.Succeeded)    Use(result.Value);
else if (result.IsCanceled) ShowRetry();
else                         Debug.LogError(result.Error);
```

`Result<T>` と `Result` は `readonly struct` 値であり、例外をスローせずにタスクの結果を表します。どちらも `bool` への暗黙の変換をサポートします（成功時に true）。

### 手動完了ソース

```csharp
var promise = new ValkarnTask.Promise<string>();
promise.TrySetResult("done");
string value = await promise.Task;
```

`TrySetResult`、`TrySetException`、`TrySetCanceled` をサポートします。ファイナライザーベースの未観測例外報告により、エラーがサイレントに失われることはありません。

### プール済み完了ソース

繰り返しパターン向けの自動リセットプール済みバリアント（チャネルとコンビネーターが内部で使用）:

```csharp
var source = ValkarnTask.PooledPromise<int>.Create(out uint token);
source.TrySetResult(42);
int value = await source.Task; // ソースは自動的にプールに返ります
```

---

## Awaitable ブリッジ

Unity の `Awaitable` との透過的な相互運用 — 手動変換不要:

```csharp
async ValkarnTask LoadGame()
{
    await SceneManager.LoadSceneAsync("Level2"); // Unity Awaitable — 直接動作します
    await Resources.LoadAsync<Texture2D>("hero"); // Unity Awaitable — 直接動作します
    await ValkarnTask.Delay(1000);                // Valkarn ネイティブ
}
```

ソースジェネレーターが `Awaitable` の await を検出し、アダプターを自動的に生成します。

---

## ライフサイクルキャンセル

### 自動（ソース生成）

クラスを `partial` としてマークするだけで、ソースジェネレーターが残りを処理します:

```csharp
public partial class EnemySpawner : MonoBehaviour
{
    async ValkarnTask Start()
    {
        while (true)
        {
            await ValkarnTask.Delay(3000);
            SpawnWave();
            // このGameObjectが破棄されると自動的にキャンセルされます。
        }
    }
}
```

- **MonoBehaviour** — `destroyCancellationToken` にバインド
- **ScriptableObject** — アプリケーションのライフタイムにバインド
- **通常クラス** — 自動バインドなし（手動トークンが必要）

### オプトアウト

```csharp
[NoAutoCancel]
async ValkarnTask BackgroundSync()
{
    await SyncToServer(); // 破棄時に自動キャンセルされない
}
```

### 手動 CancellationToken オーバーライド

明示的な `CancellationToken` を渡すと、自動注入されたライフサイクルトークンが上書きされます:

```csharp
async ValkarnTask DoWork(CancellationToken ct)
{
    await ValkarnTask.Delay(1000, ct);
}
```

### 兄弟キャンセルなし

タスクが兄弟タスクをキャンセルすることはありません。`WhenAll` はすべてのタスクを待機し、`WhenAny` は最初の結果を返しますが、負けたタスクは引き続き実行されます。これにより、タスクに副作用がある場合のデータ破損を防ぎます。

---

## クリティカルセクション

ライフサイクルキャンセルによって中断されてはならない操作の場合:

```csharp
async ValkarnTask SaveProgress()
{
    var data = await LoadPlayerData();        // キャンセル可能

    await using (ValkarnTask.Critical())
    {
        await db.Insert(data);               // GOが破棄されてもキャンセルされない
        await db.Commit();
    }                                        // 保留中のキャンセルがここで適用される

    await SendNotification();                // 再びキャンセル可能
}
```

クリティカルセクション内では、キャンセルは**無視されるのではなく延期されます**。セクションが終了すると、保留中のキャンセルが適用されます。

---

## コンビネーター

### WhenAll（型付き）

```csharp
// 直接 — いずれかのタスクが失敗するとスローします
var (enemies, map) = await ValkarnTask.WhenAll(LoadEnemies(), LoadMap());

// 安全 — スローしない動作のために AsResult() でラップします
var (a, b) = await ValkarnTask.WhenAll(
    LoadEnemies().AsResult(), LoadMap().AsResult());
```

すべてのタスクがすでに完了している場合のゼロアロケーション同期高速パス。`IEnumerable<ValkarnTask<T>>` オーバーロードは内部配列に `ArrayPool<T>` を使用します。

### WhenAll（void）

```csharp
await ValkarnTask.WhenAll(SaveA(), SaveB(), SaveC());
await ValkarnTask.WhenAll(taskList); // IEnumerable<ValkarnTask>
```

### WhenAny

```csharp
var (winnerIndex, result) = await ValkarnTask.WhenAny(DownloadFromA(), DownloadFromB());
```

最初に完了した結果を返します。負けたタスクは自然に実行を続けます。

### ファイア・アンド・フォーゲット

```csharp
SendAnalytics("event").Forget();

[FireAndForget]
async ValkarnTask SendAnalytics(string eventName) { ... }
// 呼び出し元は .Forget() 不要 — 警告は生成されない
```

`.Forget()` はエラーを `ValkarnTask.UnobservedException` にルーティングします。サイレントに飲み込まれることはありません。

### AsNonGeneric

```csharp
ValkarnTask voidTask = typedTask.AsNonGeneric();
```

---

## 時間と遅延

```csharp
await ValkarnTask.Delay(1000);                                    // ミリ秒
await ValkarnTask.Delay(TimeSpan.FromSeconds(2));                  // TimeSpan
await ValkarnTask.Delay(1000, DelayType.UnscaledDeltaTime);       // タイムスケール無視
await ValkarnTask.Delay(1000, DelayType.Realtime);                 // ストップウォッチベース

await ValkarnTask.Yield();                                         // 次の PlayerLoop ティック
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);             // 特定のタイミング
await ValkarnTask.NextFrame();                                      // 確実に次のフレーム
await ValkarnTask.DelayFrame(5);                                    // N フレーム

await ValkarnTask.WaitUntil(() => player.IsReady);
await ValkarnTask.WaitWhile(() => isLoading);
```

---

## スレッド切り替え

```csharp
async ValkarnTask ProcessData()
{
    var raw = await DownloadData();

    await ValkarnTask.SwitchToThreadPool();
    var processed = HeavyComputation(raw);  // バックグラウンドスレッド

    await ValkarnTask.SwitchToMainThread();
    ApplyToGameObject(processed);           // メインスレッド
}
```

---

## 16の PlayerLoop タイミング

| グループ | タイミング |
|-------|---------|
| Initialization | `Initialization`、`LastInitialization` |
| EarlyUpdate | `EarlyUpdate`、`LastEarlyUpdate` |
| FixedUpdate | `FixedUpdate`、`LastFixedUpdate` |
| PreUpdate | `PreUpdate`、`LastPreUpdate` |
| Update | `Update`、`LastUpdate` |
| PreLateUpdate | `PreLateUpdate`、`LastPreLateUpdate` |
| PostLateUpdate | `PostLateUpdate`、`LastPostLateUpdate` |
| TimeUpdate | `TimeUpdate`、`LastTimeUpdate` |

特に指定がない限り、すべての操作はデフォルトで `PlayerLoopTiming.Update` になります。

---

## チャネル

```csharp
// 無制限
var channel = ValkarnTask.Channel.CreateUnbounded<EnemySpawnRequest>();

// 制限付き — 満杯時にバックプレッシャー
var channel = ValkarnTask.Channel.CreateBounded<LogEntry>(capacity: 100);

// マルチコンシューマー
var channel = ValkarnTask.Channel.CreateUnbounded<WorkItem>(multiConsumer: true);

// プロデューサー
await channel.Writer.WriteAsync(entry);
bool accepted = channel.Writer.TryWrite(entry);

// コンシューマー
await foreach (var item in channel.Reader.ReadAllAsync())
    Process(item);

// 完了
channel.Writer.Complete();
await channel.Reader.Completion;
```

---

## 決定論的テスト（TestClock）

```csharp
[Test]
public void Respawn_WaitsThreeSeconds()
{
    var clock = new TestClock();
    var task = spawner.RespawnEnemy();

    clock.Advance(TimeSpan.FromSeconds(2));
    Assert.IsFalse(task.IsCompleted);

    clock.Advance(TimeSpan.FromSeconds(1));
    Assert.IsTrue(task.IsCompleted);
}
```

時間依存のすべての操作は `TimeProvider.Current` から読み取ります。テストでは、`TestClock` に置き換えます。`AdvanceFrame()` は1回の PlayerLoop ティックをシミュレートします。

---

## ジョブシステムブリッジ

```csharp
var job = new PathfindingJob { start = a, end = b, results = results };
await job.ScheduleAsync();
// results NativeArray は await 直後に読み取り可能

await job.ScheduleParallelAsync(dataCount, batchSize); // IJobParallelFor

// キャンセル — キャンセルを報告する前にジョブハンドルを完了させます（ジョブリークなし）
await job.ScheduleAsync(cancellationToken);
```

---

## コンパイル時診断

| コード | 重大度 | 説明 |
|------|----------|-------------|
| TT001 | 警告 | `ValkarnTask` の二重 await / 解放後使用 |
| TT002 | エラー | `async ValkarnTask` の結果が式文として使用されている — await または `.Forget()` が必要 |
| TT010 | 情報 | MonoBehaviour の非同期メソッドが Destroy 時に自動キャンセルされる |
| TT011 | 警告 | `WhenAll` に異なるライフタイムのタスクが含まれている |
| TT012 | 警告 | キャンセルチェックのない非同期ループ（潜在的なゾンビループ） |
| TT013 | 警告 | `ValkarnTask` が返されたが await されておらず、明示的に破棄もされていない |
| TT014 | 警告 | 手動 `CancellationToken` パラメーターなしの `[NoAutoCancel]` |
| TT015 | 情報 | `async ValkarnTask` 内で `Awaitable` を await — ブリッジアダプターが生成される |
| TT016 | 警告 | await 式のない非同期メソッド |
| TT017 | 警告 | `ValkarnTask<T>` への `[FireAndForget]` — 戻り値を破棄する |

---

## プール管理

すべての非同期メソッドランナーは `ValkarnPool<T>` を通じてプール管理されます:

- **メインスレッド** — ロックフリースタック、アトミックゼロ
- **バックグラウンドスレッド** — CAS 操作を使用した Treiber ロックフリースタック
- **フレームベースのトリミング** — 300フレームごと（60fpsで約5秒）、余分なオブジェクトが徐々に解放される
- 設定可能な最小値（デフォルト: 8）を**下回ることはありません**

実行時の監視:

```csharp
foreach (var (type, size, maxSize) in ValkarnTask.GetPoolInfo())
    Debug.Log($"{type}: {size}/{maxSize}");
```

---

## ValkarnTaskSettings

`ScriptableObject` で設定（`Assets > Create > Valkarn > Tasks > Task Settings`、`Resources/` に配置）:

| 設定 | デフォルト | 説明 |
|---------|---------|-------------|
| `DefaultMaxPoolSize` | 256 | プールタイプごとの最大アイテム数 |
| `MinPoolSize` | 8 | この値を下回るトリムはしない |
| `TrimCheckInterval` | 300 | トリムチェック間のフレーム数 |
| `TrimHysteresisCount` | 2 | トリム前の連続チェック数 |
| `TrimReleaseRatio` | 0.25 | サイクルごとに解放される余剰の割合 |
| `EnableAutoCancel` | true | MonoBehaviour タスクを destroyCancellationToken に自動バインド |
| `LogUnobservedCancellations` | false | 未観測のキャンセルを警告としてログする |
| `MaxExceptionLogsPerFrame` | 10 | フレームごとの例外ログの上限 |

---

## エラーハンドリング

```csharp
// 未観測の例外 — プール返却時に決定論的に発火
ValkarnTask.UnobservedException += ex =>
    Debug.LogError($"Unobserved: {ex}");
```

- `WhenAll` — 最初の例外をスロー、追加の例外は `UnobservedException` にルーティング
- `WhenAny` — 勝者の例外はスローされる。敗者の障害は `UnobservedException` に送られる
- ライフサイクルキャンセル — `OperationCanceledException` はデフォルトで抑制される（設定可能）

### ファクトリーメソッド

```csharp
ValkarnTask.CompletedTask          // void、ゼロアロック
ValkarnTask.FromResult<T>(value)   // 型付き、ゼロアロック
ValkarnTask.FromException(ex)      // 障害あり
ValkarnTask.FromException<T>(ex)
ValkarnTask.FromCanceled()         // キャンセル済み
ValkarnTask.FromCanceled<T>()
ValkarnTask.Never                  // 完了しない（WhenAny のセンチネル）
```
