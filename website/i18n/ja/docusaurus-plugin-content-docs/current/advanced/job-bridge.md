---
sidebar_position: 2
title: Job & Awaitable ブリッジ
---

# Job & Awaitable ブリッジ

Valkarn Tasksは、UnityのJob SystemおよびAwaitable APIを`VlkTask`パイプラインに接続するブリッジ型のセットを提供します。各ブリッジは薄いアロケーション最小化レイヤーであり、隠れた魔法は存在しません。

ここで説明するすべての型は名前空間`UnaPartidaMas.Valkarn.Tasks.Bridge`にあり、`#if UNITY_5_3_OR_NEWER`（Awaitableサポートは`#if UNITY_2023_1_OR_NEWER`）でガードされています。

---

## JobHandleExtensions — 単一のJobHandleをawait

最もシンプルなブリッジです。任意の`JobHandle`で`.ToVlkTask()`を呼び出すと、ジョブが完了したときに完了する`VlkTask`を返します。

```csharp
public static VlkTask ToVlkTask(
    this JobHandle handle,
    PlayerLoopTiming timing          = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

### 動作の仕組み

1. **ファストパス。** `handle.IsCompleted`がすでにtrueの場合、`handle.Complete()`を即座に呼び出し、`VlkTask.CompletedTask`を返します — ゼロアロケーション、PlayerLoop登録なし。
2. **通常パス。** プールされた`JobHandlePromise`をレンタルし、指定された`timing`でPlayerLoopに登録し、`VlkTask`にラップして返します。各フレームで`MoveNext()`が`JobHandle.ScheduleBatchedJobs()`（エディットモードとバッチモードでジョブキューをフラッシュするため）を呼び出し、次に`handle.IsCompleted`を確認します。ハンドルが完了すると、プロミスがタスクを完了し、自身をプールに返却します。
3. **キャンセル。** `CancellationToken`が発火すると、ハンドルを強制的に完了し（ジョブシステムのリークを防ぐため`handle.Complete()`は常に呼び出されます）、タスクはキャンセル状態に遷移します。

### 基本的な使用法

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Jobs;

// ジョブをスケジュールして即座にawait
var handle = myJob.Schedule();
await handle.ToVlkTask();

// デフォルト以外のタイミングとキャンセルを使用
await handle.ToVlkTask(PlayerLoopTiming.PreUpdate, destroyCancellationToken);
```

---

## JobHandleWhenAll — 複数のJobHandleを並行してawait

独立した複数のジョブをスケジュールし、すべて完了した後にのみ再開する場合は`JobHandleExtensions.WhenAll`を使用します。

```csharp
// 最もシンプルなオーバーロード: Updateタイミングですべてのハンドルを待機
public static VlkTask WhenAll(params JobHandle[] handles)

// フルオーバーロード: タイミングとキャンセルを設定可能
public static VlkTask WhenAll(
    JobHandle[]       handles,
    PlayerLoopTiming  timing,
    CancellationToken cancellationToken = default)

// フルオーバーロードの拡張メソッドエイリアス
public static VlkTask ToVlkTask(
    this JobHandle[]  handles,
    PlayerLoopTiming  timing              = PlayerLoopTiming.Update,
    CancellationToken cancellationToken   = default)
```

### 動作の仕組み

- **ファストパス。** 配列内のすべてのハンドルがすでに完了している場合、すべてのハンドルをファイナライズし、`VlkTask.CompletedTask`を即座に返します。
- **空の配列。** `VlkTask.CompletedTask`を返します。
- **通常パス。** プールされた`JobHandleArrayPromise`が作成されます。内部的に`ArrayPool<JobHandle>.Shared`から`JobHandle[]`をレンタルし（呼び出しごとのヒープアロケーションを回避）、入力ハンドルをコピーし、PlayerLoopに登録します。各フレームで、コンパクトなスワップアンドシュリンクループを使用して保留中のハンドルのみを反復し、`JobHandle.ScheduleBatchedJobs()`を呼び出してワーカーを実行し続けます。
- **キャンセル。** 残りのすべてのハンドルを強制完了し、タスクをキャンセルします。

### 使用法

```csharp
var handleA = jobA.Schedule();
var handleB = jobB.Schedule();
var handleC = jobC.Schedule();

// 3つすべてを待機
await JobHandleExtensions.WhenAll(handleA, handleB, handleC);

// または配列の拡張メソッドを使用
JobHandle[] handles = { handleA, handleB, handleC };
await handles.ToVlkTask();
```

---

## TempNativeArrayScope — awaitポイントをまたぐNativeArrayのライフタイム

### 問題

`Allocator.TempJob`で確保された`NativeArray<T>`は短いライフタイムを持ちます。配列を確保してジョブをスケジュールし、ジョブハンドルを`await`して配列のdisposeを忘れると、Unityのセーフティシステムがメモリリークを報告します。プレーンな`try`/`finally`は機能しますが、長い非同期メソッドでは誤りやすいです。

`TempNativeArrayScope<T>`は`NativeArray<T>`をラップし、`using`ステートメントを使用してスコープ終了時にdisposeする構造体です — ネイティブメモリに適用されたRAIIパターンです。

### API

```csharp
public struct TempNativeArrayScope<T> : IDisposable where T : struct
{
    // ラップされた配列にアクセス。すでにdisposeされている場合はObjectDisposedExceptionをスロー
    public NativeArray<T> Array { get; }

    // スコープがdisposeされておらず配列が作成されている場合にtrue
    public bool IsCreated { get; }

    // Allocator.TempJobで新しいNativeArray<T>を確保して所有権を取得
    public static TempNativeArrayScope<T> Create(int length);

    // すでに確保されたNativeArray<T>の所有権を取得
    public static TempNativeArrayScope<T> Wrap(NativeArray<T> existing);

    // 配列をdisposeする。べき等: 複数回呼び出しても安全
    public void Dispose();
}

// 非ジェネリックヘルパー（型推論の便宜のため）
public static class TempNativeArrayScope
{
    public static TempNativeArrayScope<T> Create<T>(int length) where T : struct;
    public static TempNativeArrayScope<T> Wrap<T>(NativeArray<T> existing) where T : struct;
}
```

`Dispose`は`Interlocked`ではなくプレーンな`int`フラグを使用します。スコープは`using var`によるメインスレッドのシングルスレッド使用を前提としているためです。

### 使用法

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Collections;
using Unity.Jobs;

async VlkTask ProcessDataAsync(int count, CancellationToken ct)
{
    // usingステートメントにより、通常完了・例外・キャンセルのいずれでも
    // スコープ終了時にDispose()が呼ばれることが保証される
    using var inputScope  = TempNativeArrayScope.Create<float>(count);
    using var outputScope = TempNativeArrayScope.Create<float>(count);

    NativeArray<float> input  = inputScope.Array;
    NativeArray<float> output = outputScope.Array;

    // 入力を設定してジョブをスケジュール
    var job = new MyProcessingJob { Input = input, Output = output };
    var handle = job.Schedule(count, 64);

    // メインスレッドをブロックせずにawait
    // NativeArrayは有効なまま — ジョブはまだ実行中
    await handle.ToVlkTask(cancellationToken: ct);

    // ジョブが完了した。ここで結果を読み取る
    float total = 0f;
    for (int i = 0; i < count; i++)
        total += output[i];

    UnityEngine.Debug.Log($"Sum: {total}");

    // inputScope.Dispose()とoutputScope.Dispose()がここで自動的に実行される
}
```

すでに確保した配列をラップすることもできます：

```csharp
var existing = new NativeArray<int>(1024, Allocator.TempJob);
using var scope = TempNativeArrayScope.Wrap(existing);
// scopeはexistingを所有し、disposeする
```

### よくある落とし穴: スコープなしのNativeArrayライフタイム

このパターンは壊れており、セーフティシステムエラーを引き起こします：

```csharp
// 誤り: 例外が発生した場合、配列がジョブより長生きするか、リークする可能性がある
var array = new NativeArray<float>(1024, Allocator.TempJob);
var handle = job.Schedule(1024, 64);
await handle.ToVlkTask(); // サスペンドポイント — 配列は生き続けなければならない
array.Dispose();          // 上でが例外が発生した場合は到達しない
```

すべてのコードパスでdisposeを保証するには`TempNativeArrayScope`または`try`/`finally`を使用してください。

---

## AwaitableBridge — Unity AwaitableをVlkTaskに変換

`AwaitableBridge`は、UnityのAwaitable`と`Awaitable<T>`型（Unity 2023.1以降から利用可能）を`VlkTask`互換のawaiterに変換する拡張メソッドを提供します。

**注意:** `Awaitable`は独自の`GetAwaiter()`も持っています。C#のオーバーロード解決はインスタンスメソッドを拡張メソッドより優先するため、`async VlkTask`メソッド内で`await myAwaitable`と書くだけで正しく機能します — UnityのawaiterはICriticalNotifyCompletionを実装しており、`VlkTask`ビルダーはそれを受け入れます。`.AsVlkTask()`拡張メソッドが必要なのは、`Awaitable`をコンビネーター（`VlkTask.WhenAll`、`VlkTask.WhenAny`）に渡したり、`VlkTask`変数として格納したい場合のみです。

このファイルは`#if UNITY_2023_1_OR_NEWER`でガードされています。

### API

```csharp
// AwaitableをVlkTask互換のawaiterに変換
public static AwaitableVlkTaskAwaiter   AsVlkTask(this Awaitable awaitable)

// Awaitable<T>をVlkTask互換のawaiterに変換
public static AwaitableVlkTaskAwaiter<T> AsVlkTask<T>(this Awaitable<T> awaitable)
```

両方のawaiterは`ICriticalNotifyCompletion`を実装しており、`ExecutionContext`のキャプチャをスキップします。`IsCompleted`、`GetResult`、`OnCompleted`をラップされたUnity awaiterに直接委譲します。

### 使用法

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnityEngine;

// 直接await — async VlkTaskメソッド内では変換不要
async VlkTask DirectExample()
{
    await Awaitable.NextFrameAsync();       // 変換不要
    await Awaitable.WaitForSecondsAsync(1f);
}

// 明示的変換 — コンビネーターと格納に必要
async VlkTask CombinatorExample()
{
    VlkTask a = Awaitable.NextFrameAsync().AsVlkTask();
    VlkTask b = Awaitable.WaitForSecondsAsync(2f).AsVlkTask();
    await VlkTask.WhenAll(a, b);
}

// 結果型を持つジェネリック版
async VlkTask<Texture2D> LoadTextureExample(string path)
{
    Awaitable<Texture2D> loadOp = LoadTextureAsync(path);
    return await loadOp.AsVlkTask();
}
```

---

## JobBridge — ソース生成ラッパー

`JobBridge.cs`はソースジェネレーターが使用するジェネリックプールドプロミス型`JobPromise<TJob>`を定義します。これは実装の詳細であり、通常は自分でインスタンス化しません。

```csharp
// 各フレームでJobHandleをポーリング。ソース生成されたScheduleAsyncメソッドで使用される
public sealed class JobPromise<TJob> : VlkTask.ISource, IPlayerLoopItem, IPoolNode<JobPromise<TJob>>
    where TJob : struct
{
    public static JobPromise<TJob> Create(JobHandle handle, CancellationToken ct, out uint token);
}
```

動作は`JobHandlePromise`（`JobHandleExtensions`参照）と同一ですが、プール分離のためにジョブ型に対してジェネリック化されています — 各ジョブ型が独自のプールを持ちます。

---

## ソースジェネレーター: JobBridgeGenerator

`JobBridgeGenerator`はRoslynインクリメンタルソースジェネレーター（クラス`UnaPartidaMas.Valkarn.Tasks.SourceGen.Generators.JobBridgeGenerator`）で、ジョブ型の`ScheduleAsync`拡張メソッドを自動的に生成します。

### 検出対象

ジェネレーターはコンパイル内のすべての**パブリック**構造体をスキャンして、以下のいずれかを実装しているものを検出します：
- `Unity.Jobs.IJob`
- `Unity.Jobs.IJobParallelFor`
- `Unity.Jobs.IJobFor`

プライベートおよびインターナルのジョブ構造体はスキップされます。非パブリック型の内部にネストされた構造体もスキップされます。

`UnaPartidaMas.Valkarn.Tasks.ValkarnTask`がコンパイル内に見つからない場合、ジェネレーターは何もしません。Valkarn Tasksを参照しないアセンブリでも安全です。

### 生成内容

出力ファイルは`ValkarnTask.JobBridge.Generated.g.cs`です。検出された各ジョブ型に対して`public static class __<TypeName>_AsyncExt`を生成します：

| ジョブインターフェース | 生成されるメソッドシグネチャ |
|---|---|
| `IJob` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, CancellationToken ct = default)` |
| `IJobParallelFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleParallelAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |

各生成メソッドは標準のUnity拡張メソッドを使用してジョブをスケジュールし、結果の`JobHandle`を`JobPromise<TJob>`にラップして`ValkarnTask`を返します。

ネスト型（外部クラス内のジョブ構造体など）の場合、生成されるクラス名にはアンダースコアが使用されます：`__Outer_Inner_AsyncExt`。

### 生成メソッドの使用法

```csharp
// IJobの例
public struct MyCalculationJob : IJob
{
    public NativeArray<float> Data;
    public void Execute() { /* ... */ }
}

// ジェネレーターが生成する:
//   public static ValkarnTask ScheduleAsync(this ref MyCalculationJob job, CancellationToken ct = default)

async VlkTask RunCalculation(CancellationToken ct)
{
    using var scope = TempNativeArrayScope.Create<float>(1024);
    var job = new MyCalculationJob { Data = scope.Array };
    await job.ScheduleAsync(ct);  // 生成された拡張メソッド
    // ここでscope.Arrayから結果を読み取る
}

// IJobParallelForの例
public struct MyParallelJob : IJobParallelFor
{
    public NativeArray<float> Input;
    public NativeArray<float> Output;
    public void Execute(int index) { Output[index] = Input[index] * 2f; }
}

async VlkTask RunParallel(int length, CancellationToken ct)
{
    using var inputScope  = TempNativeArrayScope.Create<float>(length);
    using var outputScope = TempNativeArrayScope.Create<float>(length);
    var job = new MyParallelJob { Input = inputScope.Array, Output = outputScope.Array };
    await job.ScheduleAsync(length, innerLoopBatchCount: 64, ct);
}
```

---

## ソースジェネレーター: AwaitableBridgeGenerator

`AwaitableBridgeGenerator`はコンパイル内に`UnityEngine.Awaitable`と`UnityEngine.Awaitable<T>`が存在するかどうかを検出し、存在する場合は`AsValkarnTask()`拡張メソッドを生成します。

出力ファイルは`ValkarnTask.AwaitableBridge.Generated.g.cs`です。生成されたコードは`namespace UnaPartidaMas.Valkarn.Tasks.Bridge`の`AwaitableBridgeExtensions`クラスに格納されます。

生成メソッド：

```csharp
// UnityEngine.Awaitableが見つかった場合に生成:
public static async ValkarnTask AsValkarnTask(this Awaitable awaitable)
{
    await awaitable;
}

// UnityEngine.Awaitable<T>が見つかった場合に生成:
public static async ValkarnTask<T> AsValkarnTask<T>(this Awaitable<T> awaitable)
{
    return await awaitable;
}
```

これらは`async ValkarnTask`メソッドなので、Valkarn Tasksのプールされた非同期ビルダーを通ります — ウォームプールではゼロアロケーションです。

ジェネレーターはガードされています：`ValkarnTask`がコンパイル内になければコードは生成されません。これにより、Unityを参照するがValkarn Tasksを参照しないアセンブリでのCS0246エラーを防ぎます。

---

## 動作するフルの例: ECSシステムでのジョブブリッジ

以下は`Samples~/ECS/JobBridgeExample.cs`からの例です。`ISystem`からBurstパラレルジョブをスケジュールし、ブロックせずにawaitし、結果を書き戻す完全なパターンを示しています。

```csharp
#if UNITY_5_3_OR_NEWER && VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnaPartidaMas.Valkarn.Tasks.ECS;

public partial struct JobBridgeExample : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<HealthData>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var worldCt = state.World.GetWorldCancellationToken();

        // OnUpdate内でエンティティデータを同期的に抽出する
        // 非同期メソッドはrefパラメーターを持てないため（CS1988）、
        // データをここでコピーして値渡しで非同期メソッドに渡す必要がある
        var query       = SystemAPI.QueryBuilder().WithAll<HealthData>().Build();
        var entityCount = query.CalculateEntityCount();
        if (entityCount == 0) return;

        var entities    = query.ToEntityArray(Allocator.TempJob);
        var healthArray = query.ToComponentDataArray<HealthData>(Allocator.TempJob);
        var results     = new NativeArray<float>(entityCount, Allocator.TempJob);

        // 非同期メソッドがNativeArrayの所有権を取得してdisposeする
        ProcessHealthAsync(state.EntityManager, entities, healthArray, results, worldCt).Forget();
        state.Enabled = false;
    }

    public void OnDestroy(ref SystemState state) { }

    static async VlkTask ProcessHealthAsync(
        EntityManager entityManager,
        NativeArray<Entity> entities,
        NativeArray<HealthData> healthArray,
        NativeArray<float> results,
        CancellationToken ct)
    {
        try
        {
            // フェーズ1: Burstジョブをスケジュール
            var job = new HealthProcessingJob
            {
                HealthInputs     = healthArray,
                ProcessedOutputs = results,
            };
            var handle = job.Schedule(entities.Length, batchSize: 64);

            // フェーズ2: メインスレッドをブロックせずに完了を待機
            await handle.ToVlkTask(cancellationToken: ct);

            // フェーズ3: 結果を適用。メインスレッドに戻っている
            ct.ThrowIfCancellationRequested();

            for (int i = 0; i < entities.Length; i++)
            {
                // ジョブ実行中にエンティティが破棄された可能性がある
                if (!entityManager.SafeEntityExists(entities[i]))
                    continue;

                entityManager.SetComponentData(entities[i], new HealthData
                {
                    CurrentHealth = results[i],
                });
            }
        }
        finally
        {
            // 常にNativeArrayをdisposeする — 成功・例外・キャンセルいずれでも実行
            if (entities.IsCreated)    entities.Dispose();
            if (healthArray.IsCreated) healthArray.Dispose();
            if (results.IsCreated)     results.Dispose();
        }
    }

    [BurstCompile]
    struct HealthProcessingJob : IJobParallelFor
    {
        [ReadOnly]  public NativeArray<HealthData> HealthInputs;
        [WriteOnly] public NativeArray<float>      ProcessedOutputs;

        public void Execute(int index)
        {
            var h = HealthInputs[index];
            var newHealth = h.CurrentHealth + h.RegenRate;
            if (newHealth > h.MaxHealth) newHealth = h.MaxHealth;
            ProcessedOutputs[index] = newHealth;
        }
    }

    struct HealthData : IComponentData
    {
        public float CurrentHealth;
        public float MaxHealth;
        public float RegenRate;
    }
}
#endif
```

---

## ブリッジ型のまとめ

| 型 | 目的 | アロケーション |
|---|---|---|
| `JobHandleExtensions.ToVlkTask()` | 単一の`JobHandle`をawait | ファストパスはゼロ；それ以外はプールされたプロミス |
| `JobHandleExtensions.WhenAll()` | 複数の`JobHandle`を並行してawait | ファストパスはゼロ；それ以外はプールされたプロミス + `ArrayPool`レンタル |
| `TempNativeArrayScope<T>` | `NativeArray`のRAIIライフタイム管理 | なし（構造体） |
| `AwaitableBridge.AsVlkTask()` | `Awaitable`/`Awaitable<T>`をVlkTaskに変換 | なし（構造体awaiter） |
| 生成された`ScheduleAsync()` | 型付きジョブを直接await | プールされた`JobPromise<TJob>` |
