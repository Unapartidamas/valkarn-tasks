---
sidebar_position: 6
title: Valkarn Tasks を選ぶ理由
---

# Valkarn Tasks を選ぶ理由

> Unity向けのゼロアロケーション async/await。UniTaskより高速。Awaitableよりスマート。

---

## 問題: Unity における async の欠陥

Unity 開発者は、シーンのロード、アセットのダウンロード、アニメーションの待機、スポーンの遅延、サーバーとの通信など、あらゆる場面で非同期操作を必要とします。現在の選択肢は次のとおりです。

| 選択肢 | 問題点 |
|--------|---------|
| **コルーチン** | 戻り値なし、エラーハンドリングなし、キャンセルなし、コンポジションなし |
| **System.Threading.Tasks** | `async` 呼び出しごとにアロケーション（約144〜232バイト）、GC を誘発、Unityライフサイクルの認識なし |
| **UniTask** | 優秀だが: 18分後のトークン衝突、コンパイル時診断なし、ファイナライザー依存のエラー報告 |
| **Unity Awaitable**（2023+） | クラスベース（アロケーションあり）、コンビネーターなし、チャネルなし、テストサポートなし |

Valkarn Tasks は、ソース生成によるゼロアロケーションの単一パッケージでこれらすべてを解決します。

---

## ゼロアロケーション — すべてのバイトが重要な理由

### アロケーションがゲームに与える影響

`new object()`、`new List<T>()`、または `async Task` の呼び出しは、ガベージコレクターが追跡する**マネージドヒープ**上にアロケーションを行います。UnityはBoehm GCを使用しており、2つの重大な問題があります。

1. **ストップ・ザ・ワールドの一時停止** — GCが実行されると、ゲームがフリーズします。60fpsで2msの停止はフレーム予算の12%を消費し、120fps（VR）では24%を消費します。
2. **予測不可能なタイミング** — GCはボス戦、カットシーン、または競技中のマッチ中に実行される可能性があります。

### Valkarn Tasks が異なる点

| シナリオ | `System.Task` | UniTask | **Valkarn Tasks** |
|----------|--------------|---------|------------------|
| `async Method()` — 同期的に完了 | 144バイト | **0バイト** | **0バイト** |
| `async Method()` — 一度サスペンド | 232バイト以上 | 0バイト（プール済み） | **0バイト（プール済み）** |
| `WhenAll(a, b)` | 232バイト | 0バイト（プール済み） | **0バイト（ソース生成プール済み）** |
| `WhenAny(a, b)` | 144バイト | 72バイト | **0バイト** |
| Promise（手動完了） | 144バイト | 104バイト | **88バイト** |
| プールされた Promise（再利用可能） | 144バイト | 0バイト | **0バイト** |

50〜100の非同期操作を含む典型的なゲームフレームでは、`System.Task` は7〜23KBのガベージを生成します。Valkarn Tasks は**ゼロ**です。

### あなたのゲームへの影響

- **GCスタッターなし** — 非同期操作によるヒッチなしのロックされたフレームレート
- **VR対応** — GCスパイクなしの90/120fpsターゲット
- **モバイル対応** — 制限されたRAMデバイスでのメモリプレッシャーの軽減
- **コンソール認定対応** — 予測可能なメモリ動作が認定要件の通過を助けます

---

## パフォーマンス — 最高水準とのベンチマーク

ベンチマーク: BenchmarkDotNet v0.14.0、.NET 9.0、Intel Core i7-10875H。

### コア async/await — ValueTask より2倍高速

| ベンチマーク | ValueTask | UniTask | **Valkarn Tasks** | vs ValueTask |
|-----------|-----------|---------|------------------|--------------|
| 一括100タスク | 956 ns | 508 ns | **489 ns** | **1.95×** |
| 一括1,000タスク | 9,697 ns | 5,016 ns | **4,728 ns** | **2.05×** |
| CancellationToken あり | 38.8 ns | 36.2 ns | **29.6 ns** | **1.31×** |
| 例外処理 | 10,399 ns | 8,247 ns | **9,248 ns** | **1.12×** |

すべてのパス: **0バイトのアロケーション**。

### コンビネーター — Task より最大9.6倍高速

| ベンチマーク | Task | UniTask | **Valkarn Tasks** | vs Task |
|-----------|------|---------|------------------|---------|
| WhenAll（2タスク） | 117 ns / 232B | 13.6 ns / 0B | **12.1 ns / 0B** | **9.6×** |
| WhenAll（5タスク） | 156 ns / 272B | 25.1 ns / 0B | **25.3 ns / 0B** | **6.2×** |
| WhenAny（2タスク） | 39.0 ns / 144B | 60.0 ns / 72B | **11.6 ns / 0B** | **3.4×** |
| プールされた Promise | 59.5 ns / 144B | 53.6 ns / 0B | **38.3 ns / 0B** | **1.55×** |

WhenAny は **UniTask より5.2倍高速** でゼロバイトのアロケーションです。

### オブジェクトプール — 4.3ナノ秒

| 操作 | 時間 | アロケーション |
|-----------|------|------------|
| メインスレッド高速スロットの貸出＋返却 | **4.3 ns** | 0バイト |
| クロススレッド Treiber スタック | ~15 ns | 0バイト |

メインスレッドではアトミック操作ゼロ — IL2CPP では `Volatile.Read` が9.2倍遅いため、これは重要です。

### 実際のゲームへの影響（60fpsで毎フレーム50の非同期操作）

| ライブラリ | 時間/フレーム | フレーム予算 | GC/秒 |
|---------|-----------|-------------|-----------|
| System.Task | ~48 µs | 0.29% | ~430 KB/s |
| UniTask | ~25 µs | 0.15% | ~3.6 KB/s |
| **Valkarn Tasks** | **~24 µs** | **0.14%** | **0 KB/s** |

10分間で、`System.Task` は**約258MB**の非同期ガベージを生成します。Valkarn Tasks は**ゼロ**です。

---

## 他のライブラリにはない機能

### 自動ライフサイクルキャンセル

```csharp
public partial class EnemySpawner : MonoBehaviour
{
    async ValkarnTask Start()
    {
        while (true)
        {
            await ValkarnTask.Delay(3000);
            SpawnWave();
        }
        // このGameObjectが破棄されると自動的にキャンセルされます。
        // CancellationToken不要。メモリリークなし。ゾンビタスクなし。
    }
}
```

破棄後に実行される非同期メソッドによる `MissingReferenceException` はもう不要です。手動での `OnDestroy` クリーンアップも、忘れられた `CancellationTokenSource` の破棄も不要です。

### クリティカルセクション

```csharp
async ValkarnTask SaveProgress()
{
    var data = await LoadPlayerData();       // キャンセル可能

    await using (ValkarnTask.Critical())
    {
        await db.Insert(data);              // GOが破棄されても完了します
        await db.Commit();
    }                                       // 保留中のキャンセルがここで適用されます

    await SendNotification();               // 再びキャンセル可能
}
```

プレイヤーが終了したり、シーンがアンロードされたりしても、データベースの書き込み、ネットワークリクエスト、ファイルの保存は完了します。破損したセーブデータなし。不完全な分析データなし。失われたレシートなし。

### コンパイル時診断

| 診断コード | 検出内容 |
|------------|----------------|
| TT001 | ValkarnTask の二重 await（解放後使用バグ） |
| TT002 | タスクの await 忘れ（サイレントな失敗） |
| TT012 | キャンセルチェックなしの非同期ループ（ゾンビループ） |
| TT013 | 返されたが await されていないタスク（ファイア・アンド・フォーゲットバグ） |
| TT016 | await がない非同期メソッド（不要なオーバーヘッド） |
| TT017 | `ValkarnTask<T>` への `[FireAndForget]`（結果の破棄） |

バグはテスト中のランタイムクラッシュではなく、IDEで赤い波線として検出されます。

### Result\<T\> — try/catch なしのエラーハンドリング

```csharp
var result = await loadTask.AsResult();

if (result.Succeeded)       Use(result.Value);
else if (result.IsCanceled) ShowRetryDialog();
else                         Debug.LogError(result.Error);
```

すべてのエラーパスが明示的です。飲み込まれた例外なし。見逃したハンドラーなし。

### バックプレッシャー付きチャネル

```csharp
var channel = ValkarnTask.Channel.CreateBounded<EnemySpawnRequest>(capacity: 10);

// プロデューサー（ゲームロジック）
await channel.Writer.WriteAsync(new EnemySpawnRequest { type = EnemyType.Dragon });

// コンシューマー（スポーンシステム）
await foreach (var request in channel.Reader.ReadAllAsync())
    await SpawnEnemy(request);
```

システムをクリーンに分離します。スポーンをレート制限します。ネットワークメッセージをキューに入れます。入力イベントをバッファリングします。コンシューマーが追いつけない場合、プロデューサーは速度を落とし、メモリスパイクを防ぎます。

### TestClock による決定論的テスト

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

時間依存のロジックを即座にテストします。テストで `yield return new WaitForSeconds(3)` は不要です。不安定なCIタイミングもありません。

### 世代トークンの安全性

UniTask は `short` トークン（16ビット）を使用します。65,536回のプールサイクル後（活発な非同期作業の約18分後）、古い参照が別のタスクの結果をサイレントに読み取ります — 再現がほぼ不可能な解放後使用バグです。

Valkarn Tasks はプールスロットごとに `uint` 世代カウンターを使用します: スロットあたり**4,294,967,296サイクル**で衝突。現実的なシナリオでは不可能です。

---

## 数週間ではなく数分での移行

### UniTask からの移行

```
Step 1: Valkarn Tasks をインストール
Step 2: IDEで UniTask の使用箇所に黄色の電球が表示される
Step 3: 右クリック → "ソリューション内のすべての出現箇所を修正"（Ctrl+.）
Step 4: UniTask パッケージの参照を削除
```

**15の移行診断**（MIG001〜MIG015）がすべての UniTask API を自動的にカバーします。500〜2,000の非同期メソッドを持つ典型的なプロジェクトは5分以内に移行できます。95%以上が完全自動化されています。

### Unity Awaitable からの移行

同じワンクリック移行:
- `async Awaitable` → `async ValkarnTask`
- `Awaitable.NextFrameAsync()` → `ValkarnTask.NextFrame()`
- `Awaitable.BackgroundThreadAsync()` → `ValkarnTask.SwitchToThreadPool()`
- `Awaitable.MainThreadAsync()` → 削除（Valkarn はデフォルトでメインスレッドで実行）

---

## 比較マトリックス

| 機能 | System.Task | UniTask | Awaitable | **Valkarn Tasks** |
|---------|------------|---------|-----------|------------------|
| ゼロアロック同期パス | なし | あり | なし | **あり** |
| ゼロアロックコンビネーター | なし | なし | なし | **あり（ソース生成）** |
| 構造体ベース | なし | あり | なし | **あり** |
| ライフサイクル自動キャンセル | なし | 手動 | 部分的 | **自動** |
| 兄弟キャンセルなし | なし | なし | なし | **あり** |
| クリティカルセクション | なし | なし | なし | **あり** |
| Result\<T\>（例外なし） | なし | 部分的 | なし | **あり** |
| TestClock | なし | なし | なし | **あり** |
| ジョブシステムブリッジ | なし | なし | なし | **あり** |
| コンパイル時診断 | なし | なし | なし | **あり（17ルール）** |
| 制限付きプール＋トリム | なし | なし | 該当なし | **あり** |
| 決定論的エラー報告 | なし | なし（ファイナライザー） | 部分的 | **あり（プール返却）** |
| 完全なチャネル | あり（.NET） | 最小限 | なし | **あり** |
| Awaitableブリッジ | 該当なし | 最小限 | ネイティブ | **透過的** |
| IL2CPP最適化プール | なし | なし（操作ごとにVolatile） | 該当なし | **あり（アトミックゼロ）** |
| トークン衝突安全性 | 該当なし | 18分（short） | 該当なし | **永遠になし（uint世代）** |
| UniTaskからの自動移行 | 該当なし | 該当なし | 該当なし | **あり（15修正）** |
| Awaitableからの自動移行 | 該当なし | 該当なし | 該当なし | **あり（8修正）** |

---

**あなたのゲームは、スタッターしない async を受け取るに値します。**
