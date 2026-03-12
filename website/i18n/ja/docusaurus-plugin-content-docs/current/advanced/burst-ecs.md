---
sidebar_position: 1
title: Burst & ECS統合
---

# Burst & ECS統合

Valkarn TasksはUnityのBurstコンパイラー、Unity Collections、およびEntities（ECS）パッケージとのオプション統合を含みます。この機能はすべて条件付きコンパイルされます — 必要なパッケージが存在し、対応するスクリプティング定義シンボルが設定されている場合のみアクティブです。

## 必要要件

| 機能 | 必要なパッケージ | スクリプティング定義 |
|---|---|---|
| `NativeTimerHeap`、`NativeScheduler`、`BurstSchedulerRunner` | Unity Burst 1.8以降、Unity Collections 2.0以降 | `VTASKS_HAS_BURST`および`VTASKS_HAS_COLLECTIONS` |
| `AsyncSystemUtilities` | Unity Entities 1.0以降 | `VTASKS_HAS_ENTITIES` |

すべてのBurst/ECSソースファイルはこれらの定義に一致する`#if`ガードでラップされています。定義が存在しない限り、これらのファイル内のコードはコンパイルもリンクもされません。

### セットアップ

1. Unity Package Managerで必要なパッケージをインストールします：
   - `com.unity.burst` 1.8以降
   - `com.unity.collections` 2.0以降
   - `com.unity.entities` 1.0以降（ECSユーティリティのみ）

2. **Project Settings > Player > Scripting Define Symbols**にスクリプティング定義シンボルを追加します：
   ```
   VTASKS_HAS_BURST;VTASKS_HAS_COLLECTIONS;VTASKS_HAS_ENTITIES
   ```
   インストールしたパッケージの定義のみ追加する必要があります。

---

## NativeTimerHeap

**名前空間：** `UnaPartidaMas.Valkarn.Tasks.Burst`
**ガード：** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeTimerHeap`はタイマーをスケジューリングするためのBurst互換のバイナリ最小ヒープです。`TimerEntry`値をデッドラインの順で保存し、O(log n)の挿入と各期限切れタイマーごとのO(log n)削除を提供します。

### 主要な型

```csharp
// ヒープに保存されるエントリ。Deadlineでソートされる。
public struct TimerEntry : IComparable<TimerEntry>
{
    public long Deadline;
    public int  Id;
}

public struct NativeTimerHeap : IDisposable
{
    public bool IsCreated { get; }
    public int  Count     { get; }

    // ヒープを作成。長期ヒープにはAllocator.Persistentを使用。
    public NativeTimerHeap(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // 新しいタイマーを挿入。タイマーID（コールバックを識別するために使用）を返す。
    // deadlineとDrainExpiredに渡す値は同じ単位でなければならない
    // （BurstSchedulerRunnerはTime.realtimeSinceStartupAsDoubleを通じてDateTimeティックを使用）。
    [BurstCompile]
    public int Schedule(long deadline);

    // Deadline <= currentTimestampのすべてのタイマーを削除してIDをexpiredIdsに追加。
    // ドレインされたタイマーの数を返す。
    [BurstCompile]
    public int DrainExpired(long currentTimestamp, NativeList<int> expiredIds);

    public void Dispose();
}
```

`NativeTimerHeap`はアンマネージド構造体です。マネージドデリゲートを保存できません — IDはメインスレッドの`BurstSchedulerRunner`ディクショナリーでマネージドコールバックにマッチングされます。

---

## NativeScheduler

**名前空間：** `UnaPartidaMas.Valkarn.Tasks.Burst`
**ガード：** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`NativeScheduler`は`NativeQueue<ScheduledWork>`に支えられたBurst互換のワークキューです。Burstコンパイルされたジョブがワークアイテムをエンキューし、メインスレッドが各フレームでドレインします。

### 主要な型

```csharp
public enum WorkType : byte
{
    TimerExpired  = 0,
    JobCompleted  = 1,
    Custom        = 2
}

public struct ScheduledWork
{
    public int      Id;
    public WorkType Type;
    public long     Payload;
}

public struct NativeScheduler : IDisposable
{
    public bool IsCreated { get; }

    public NativeScheduler(int initialCapacity, AllocatorManager.AllocatorHandle allocator);

    // Burstコンパイルされたジョブからワークアイテムをエンキュー。
    [BurstCompile]
    public void Enqueue(ScheduledWork work);

    // 保留中のすべての作業をresultsにドレイン。メインスレッドからのみ呼び出す。
    // ドレインされたアイテムの数を返す。
    public int Drain(NativeList<ScheduledWork> results);

    // IJobParallelForジョブで使用するのに適した並行ライターを返す。
    public NativeQueue<ScheduledWork>.ParallelWriter AsParallelWriter();

    public void Dispose();
}
```

キューはBurstの世界とマネージドの世界の交差点です。`Enqueue`はBurst呼び出し可能；`Drain`はメインスレッドのみ。

---

## BurstSchedulerRunner

**名前空間：** `UnaPartidaMas.Valkarn.Tasks.Burst`
**ガード：** `#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS`

`BurstSchedulerRunner`はネイティブスケジューラー/タイマーヒープとゲームの残りの部分を結ぶマネージドブリッジです。`IPlayerLoopItem`を実装するため、Unityは登録されたタイミングでフレームごとに`MoveNext()`を呼び出します。各フレームで：

1. `NativeScheduler`をドレインしてワークIDでマッチングされた登録済みマネージドコールバックを起動します。
2. 期限切れタイマーのために`NativeTimerHeap`をドレインしてそのマネージドコールバックを起動します。

コールバックによってスローされた例外はPlayerLoopを伝播させる代わりに`VlkTask.PublishUnobservedException`に転送されます。

### API

```csharp
public sealed class BurstSchedulerRunner : IPlayerLoopItem, IDisposable
{
    // ランナーを作成してPlayerLoopに登録。インスタンスを返す。
    // 不要になったら返却されたランナーをDisposeしてください。
    public static BurstSchedulerRunner Create(
        PlayerLoopTiming timing       = PlayerLoopTiming.Update,
        int initialCapacity           = 64);

    // Burstジョブからのエンキューのためのネイティブスケジューラーへの直接アクセス。
    public NativeScheduler Scheduler { get; }

    // マネージドタイマーコールバックをスケジュール。メインスレッドから呼び出す必要がある。
    // タイマーID（識別のみ；キャンセルAPIはない）を返す。
    public int ScheduleTimer(TimeSpan delay, Action callback);

    // マネージドコールバックをBurstジョブがエンキューしたワークIDに関連付ける。
    // メインスレッドから、ジョブが完了するフレームの前または最中に呼び出す必要がある。
    public void RegisterCallback(int workId, Action callback);

    // すべてのネイティブコンテナーをDisposeしてPlayerLoopから登録解除。
    public void Dispose();
}
```

### BurstSchedulerRunnerとデフォルトスケジューラーの違い

デフォルトのValkarn Tasksスケジューラーはasync/awaitステートマシンと直接統合し、`PlayerLoopHelper`を通じてコンティニュエーションのディスパッチを管理します。`BurstSchedulerRunner`はBurstコンパイルされたジョブからのシグナリングに特化した別のレーンを追加します：

| | デフォルトスケジューラー | BurstSchedulerRunner |
|---|---|---|
| コンティニュエーション型 | `ISource`経由のマネージド`Action` | IDで登録されたマネージド`Action` |
| シグナルソース | C# awaiterパターン | アンマネージド`NativeScheduler.Enqueue` |
| タイマーソース | `VlkTask.Delay`（マネージド） | `NativeTimerHeap`（アンマネージド） |
| スレッド安全性 | メインスレッドコンティニュエーション | EnqueueはBurst安全；drainはメインスレッドのみ |

### 使用パターン

```csharp
// 1. ランナーを一度作成（例：ブートストラップMonoBehaviourまたはISystem.OnCreateで）。
var runner = BurstSchedulerRunner.Create(PlayerLoopTiming.Update, initialCapacity: 128);

// 2. タイマーコールバックをスケジュール（メインスレッド）。
runner.ScheduleTimer(TimeSpan.FromSeconds(2.0), () =>
{
    Debug.Log("2秒経過（アンマネージドタイマー）。");
});

// 3. Burstジョブからワークアイテムをエンキュー。
//    NativeScheduler.ParallelWriterはIJobParallelForから安全に使用できる。
var writer = runner.Scheduler.AsParallelWriter();
// Execute(int index)内:
writer.Enqueue(new ScheduledWork { Id = myWorkId, Type = WorkType.Custom });

// 4. ジョブが完了する前にメインスレッドでマネージドコールバックを登録。
runner.RegisterCallback(myWorkId, () =>
{
    Debug.Log($"ワーク{myWorkId}のジョブ完了シグナルを受信しました。");
});

// 5. 完了したらランナーをDispose（例：OnDestroyまたはドメインリロード）。
runner.Dispose();
```

---

## AsyncSystemUtilities

**名前空間：** `UnaPartidaMas.Valkarn.Tasks.ECS`
**ガード：** `#if VTASKS_HAS_ENTITIES`

`AsyncSystemUtilities`は非同期ECSシステムを記述するための2つの拡張ヘルパーを提供します。

### GetWorldCancellationToken

```csharp
public static CancellationToken GetWorldCancellationToken(
    this World world,
    PlayerLoopTiming timing = PlayerLoopTiming.Update)
```

指定された`World`が破棄されると自動的にキャンセルされる`CancellationToken`を返します。内部的にはfire-and-forgetの`VlkTask`を開始し、`world.IsCreated`がtrueの間フレームごとに`VlkTask.Yield(timing)`を呼び出し、ループが終了すると`CancellationTokenSource`をキャンセルします。

このメソッドを呼び出した時点でワールドが既に破棄されている場合、既にキャンセル状態のトークンを返します。

このトークンをシステムから起動するすべての非同期メソッドに渡すことで、ワールドが消えると飛行中の作業が自動的に停止されます。

### SafeEntityExists

```csharp
public static bool SafeEntityExists(this EntityManager entityManager, Entity entity)
```

`entityManager.Exists(entity)`を呼び出し、`ObjectDisposedException`がスローされた場合は`false`を返します。これはワールドが破棄された後に`EntityManager`がアクセスされた場合に発生する可能性があり、フレーム境界を越えて生存する非同期コードでは実際の競合状態です。

エンティティに書き戻す前に、すべての`await`ポイントの後でこれを使用してください。

---

## 実例：非同期ECSシステム

以下の例は`Samples~/ECS/AsyncLoadSystem.cs`からです。`ISystem`からのワンショット非同期初期化の標準パターンを示しています。

```csharp
#if VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Entities;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.ECS;

public partial struct AsyncLoadSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // このWorldのライフタイムに紐付けられたキャンセルトークンを取得。
        // Worldが破棄されると、このトークンで起動されたすべての非同期作業は
        // 自動的にキャンセルされます。
        var worldCt = state.World.GetWorldCancellationToken();

        // 非同期初期化を起動してタスクを忘れる。
        // Forget()は未処理の例外をVlkTask.PublishUnobservedExceptionにルーティングします。
        InitializeAsync(state.WorldUnmanaged, worldCt).Forget();
    }

    public void OnUpdate(ref SystemState state) { }
    public void OnDestroy(ref SystemState state) { }

    static async VlkTask InitializeAsync(WorldUnmanaged world, CancellationToken ct)
    {
        // フェーズ1: バックグラウンドスレッドでデータをロード。
        // RunOnThreadPoolはワーカースレッドに切り替え、デリゲートを実行し、
        // 自動的にメインスレッドに戻ります。
        var configData = await VlkTask.RunOnThreadPool(
            () => LoadFromDisk(),
            cancellationToken: ct);

        // フェーズ2: メインスレッドで結果を適用。
        // ローディング中にWorldが破棄された場合のためにキャンセルを確認。
        ct.ThrowIfCancellationRequested();
        ApplyConfiguration(world, configData);
    }

    static ConfigData LoadFromDisk()
    {
        // 純粋なC#のみ — ここではUnityまたはECS APIの呼び出しは不可。
        return new ConfigData { MaxEnemies = 100, SpawnRadius = 50f };
    }

    static void ApplyConfiguration(WorldUnmanaged world, ConfigData data)
    {
        // 安全: メインスレッド上にいます。
        UnityEngine.Debug.Log($"設定を読み込みました: MaxEnemies={data.MaxEnemies}");
    }

    struct ConfigData
    {
        public int   MaxEnemies;
        public float SpawnRadius;
    }
}
#endif
```

AIスロットリングの例（`Samples~/ECS/AISystemExample.cs`）はこのパターンに基づいて構築され、`AsyncThrottle`を追加して並行して飛行中の非同期タスクの数を制限します。そのパターンの詳細については、スロットリングのドキュメントを参照してください。

---

## 制限事項

以下の制約はすべてのBurst/ECS非同期コードに適用されます。これらに違反すると、エディターエラー、ジョブ安全違反、またはデータの暗黙的な破損が発生します。

### Burstコンパイルされたコード内

- **マネージド型なし。** Burstはマネージドオブジェクト（クラス、デリゲート、配列、文字列、`List<T>`など）をアロケーション、アクセス、または参照するコードをコンパイルできません。ブリッタブル構造体とネイティブコンテナーのみが許可されています。
- **例外なし。** Burstは`try`/`catch`/`throw`をサポートしません。エラーを伝えるには戻りコードまたはフラグを使用してください。
- **`async`/`await`なし。** C#の非同期ステートマシンはマネージドでBurstによってコンパイルできません。`NativeScheduler`と`NativeTimerHeap`はマネージドコンティニュエーションへのシグナリング用のサイドチャネルを提供しますが、コンティニュエーション自体はメインスレッドで実行されます。
- **可変マネージド静的状態なし。** Burstジョブはstaticなreadonlyフィールドを読み取れますが、マネージドな静的フィールドに書き込んではいけません。

### ECSシステムのawaitポイントをまたぐ場合

- **エンティティのライフタイム。** 非同期メソッドがサスペンドしている間にエンティティが破棄される可能性があります。書き戻す前に、すべての`await`の後で必ず`entityManager.SafeEntityExists(entity)`を呼び出してください。
- **ComponentLookupの陳腐化。** `ComponentLookup`、`RefRW`、その他のチャンクポインター型は構造的変更（任意のフレームで発生する可能性がある）後に無効になります。`await`ポイントをまたいでこれらをキャッシュしないでください。再開後に`SystemState`から再取得するか、直接`EntityManager`を使用してください。
- **`ref`パラメーター。** 非同期メソッドは`ref`、`in`、または`out`パラメーターを持てません（C#エラーCS1988）。同期`OnUpdate`メソッドですべてのECSデータを同期的に抽出し、非同期メソッドに値で渡してください。
- **非同期メソッド内の`SystemAPI`。** `SystemAPI`はソース生成されており、部分的な`ISystem`メソッド内でのみ機能します。`async`メソッドでは使用できません。最初の`await`の前にすべての`SystemAPI`クエリを実行してください。
- **スレッド安全性。** `EntityManager`、`ComponentLookup`、構造的変更はメインスレッドのみです。UnityまたはECS APIの呼び出しなしの純粋なC#計算にのみ`VlkTask.RunOnThreadPool`を使用してください。
