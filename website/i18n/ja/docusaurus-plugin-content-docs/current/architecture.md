---
sidebar_position: 5
title: アーキテクチャ
---

# アーキテクチャ

Valkarn Tasks の内部構造の技術概要。

---

## 高レベル構造

```
┌─────────────────────────────────────────────────────────────────┐
│                    コンパイル時                                    │
│                                                                   │
│  ┌──────────────┐  ┌──────────────────┐  ┌───────────────────┐  │
│  │  ライフサイクル│  │  Awaitable       │  │  診断             │  │
│  │  アナライザー  │  │  ブリッジ生成     │  │  (TT001–TT017)    │  │
│  └──────────────┘  └──────────────────┘  └───────────────────┘  │
│                                                                   │
│  ┌──────────────┐  ┌──────────────────┐                          │
│  │  ジョブブリッジ│  │  コンビネーター   │                          │
│  │  生成         │  │  生成            │                          │
│  └──────────────┘  └──────────────────┘                          │
├─────────────────────────────────────────────────────────────────┤
│                    ランタイム                                      │
│                                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐  │
│  │ ValkarnTask │  │ Result<T>  │  │ ValkarnPool│  │ Completion│  │
│  │  struct    │  │  struct    │  │  bounded   │  │  Core<T>  │  │
│  └────────────┘  └────────────┘  └────────────┘  └───────────┘  │
│                                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐  │
│  │ PlayerLoop │  │Continuation│  │  Channels  │  │ TestClock │  │
│  │  Helper    │  │   Queue    │  │            │  │           │  │
│  └────────────┘  └────────────┘  └────────────┘  └───────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### アセンブリ構成

```
ValkarnTask.Runtime      — ゲームと共にリリース
ValkarnTask.SourceGen    — コンパイル時のみ（ソースジェネレーター）
ValkarnTask.Analyzer     — コンパイル時のみ（診断＋コード修正）
ValkarnTask.Testing      — TestClock＋テストユーティリティ
```

---

## ValkarnTask 構造体

```csharp
[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask
{
    internal readonly ValkarnTask.ISource source;
    internal readonly ulong token; // packed: high 32 bits = generation, low 32 = slot index
}

[AsyncMethodBuilder(typeof(AsyncValkarnMethodBuilder<>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ValkarnTask<T>
{
    internal readonly ValkarnTask.ISource<T> source;
    internal readonly T result;  // inline on sync fast path
    internal readonly ulong token;
}
```

**主要な不変条件:** `source == null` はタスクがエラーなく同期的に完了したことを意味します — ヒープオブジェクトは関与していません。`ValkarnTask.CompletedTask` は `default(ValkarnTask)` です。`ValkarnTask.FromResult(value)` は結果をインラインで格納します。

### 世代トークン

```csharp
// パック
ulong token = ((ulong)generation << 32) | slotIndex;

// アンパック
uint slotIndex  = (uint)(token & 0xFFFFFFFF);
uint generation = (uint)(token >> 32);
```

すべての ISource 呼び出しで検証: `slots[slotIndex].generation == expectedGeneration`。リサイクルされたプールスロットへの古い参照は即座に `InvalidOperationException` をスローします。スロットあたり40億世代 — 衝突は実際には不可能です。（UniTask は `short` を使用 — 約18分後に衝突。）

---

## ISource コントラクト

```csharp
public interface ISource
{
    Status GetStatus(ulong token);
    void   GetResult(ulong token);
    void   OnCompleted(Action<object> continuation, object state, ulong token);
    Status UnsafeGetStatus();
}

public interface ISource<out T> : ISource
{
    new T GetResult(ulong token);
}
```

`ISource` を実装するオブジェクトは `ValkarnTask` をバックできます。組み込み実装:

| 型 | 目的 |
|------|---------|
| `AsyncValkarnRunner<TStateMachine>` | すべての `async ValkarnTask` メソッドをバック |
| `AsyncValkarnRunner<TStateMachine, T>` | すべての `async ValkarnTask<T>` メソッドをバック |
| `ValkarnTask.PooledPromise[<T>]` | 手動完了、自動プール返却 |
| `ValkarnTask.Promise[<T>]` | 手動完了、プールなし（長期間使用） |
| `ExceptionSource` | `FromException` をバック |
| `CanceledSource` | `FromCanceled` をバック |
| `NeverSource` | シングルトン — Pending から遷移しない |

---

## 非同期メソッドビルダー

C# コンパイラーはカスタム非同期戻り値型のビルダープロトコルを駆動します:

```
Create()
  └─ 構造体ビルダーを返す（ゼロアロック）

Start(ref stateMachine)
  └─ ステートマシンを同期的に実行
     ├─ サスペンドなしで完了 → SetResult()、ランナーは null のまま
     │  └─ Task は default(ValkarnTask) を返す  ← ゼロアロケーション
     └─ 未完了の await に到達 → AwaitUnsafeOnCompleted()
           └─ プールから AsyncValkarnRunner をレンタル
              ステートマシンをランナーにコピー（値渡し、ボクシングなし）
              awaitable に継続を登録
              └─ Task はランナーを ISource としてラップ  ← 非同期パス
```

ビルダー自体は `struct` です — ビルダーだけのためにアロケーションは発生しません。`runner` は**遅延的に**アロケーションされます: メソッドが同期的に完了した場合、プールレンタルは発生しません。

---

## ステートマシンランナーとプール

`AsyncValkarnRunner<TStateMachine>` はコンパイラー生成のステートマシンを**値として**（ボクシングなし）保持し、`ISource` として機能します。最初のサスペンド時に `ValkarnPool<T>` からレンタルされ、`GetResult` 時に返却されます。

`TStateMachine` は非同期メソッドごと（クローズドジェネリックインスタンス化ごと）にユニークな型であるため、C# ジェネリック特殊化を通じて各非同期メソッドが独自のプールを自動的に取得します。

### ValkarnPool\<T\>

| コンテキスト | 構造 | 理由 |
|---------|-----------|--------|
| Unity メインスレッド | シングルスレッドスタック | 同期なし — 最高速 |
| バックグラウンドスレッド | Treiber ロックフリースタック | CAS 操作、ロックなし |

スレッドコンテキストは `Thread.CurrentThread.IsBackground` で検出されます。プールの形状（容量、トリムレート）は `ValkarnTaskSettings` で設定します。

---

## ValkarnCompletionCore\<T\>

すべての `ISource` 実装内の共有状態:

- 現在の `Status`（Pending / Succeeded / Faulted / Canceled）
- 結果値（ジェネリックソース）
- 例外または `OperationCanceledException`（エラーパス）
- 登録された継続デリゲート＋状態

ステートの遷移は `Interlocked.CompareExchange` を使用します — ロックフリーでスレッドセーフ。**二重完了ガード**により、最初の `TrySet*` 呼び出しのみが成功し、後続の呼び出しはサイレントなノーオペレーションとなります。

---

## PlayerLoop 統合

`PlayerLoopHelper` は、起動時（`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]`）に Unity の PlayerLoop に軽量なランナーコールバックを挿入します。

各 `PlayerLoopTiming` 値はフェーズに対応します。`await ValkarnTask.Yield(timing)` が呼び出されると、継続はそのフェーズのランナーにキューイングされ、Unity がそのフェーズに到達した次のタイミングでディスパッチされます。

```
Initialization  →  EarlyUpdate  →  FixedUpdate  →  PreUpdate
→  Update  →  PreLateUpdate  →  PostLateUpdate  →  TimeUpdate
  （各フェーズの Last* バリアント含む）
```

---

## ソースジェネレーター

Roslyn ソースジェネレーターはコンパイル時に実行されます。`async ValkarnTask` メソッドを持つ `MonoBehaviour` を拡張する各 `partial` クラスに対して、次の処理を行う partial クラスファイルを生成します:

1. `_valkarnCancelToken` フィールドを宣言する
2. `Awake` で `destroyCancellationToken` から割り当てる
3. トークンを自動的にスレッドを通じて各非同期メソッドをラップする

生成されたファイルはデバッガーに表示されることはなく、ユーザーのソースコードを変更することもありません。

ジェネレーターはさらに以下も生成します:

- **Awaitable ブリッジアダプター** — `async ValkarnTask` 内で `Awaitable` が await される場合
- **ジョブ非同期ラッパー** — `IJob` / `IJobParallelFor` 型が検出された場合
- **コンビネータープール** — 2〜8のアリティタプル向けの型付き `WhenAll`/`WhenAny` ソース

---

## Roslyn アナライザー

17個の `DiagnosticAnalyzer` ルールが `Analyzers/netstandard2.0/` に含まれています。Unity エディターと CI での C# コンパイラーパス中に実行されます:

- すべてが型解決に `SemanticModel` を使用します（文字列マッチングではない）
- `ValkarnTypeHelper` 共有ユーティリティがあらゆる `ValkarnTask` バリアントを検出します
- ゾンビループアナライザーはネストされたローカル関数とラムダを正しくスキップします
- 移行アナライザー（MIG001〜MIG015）は UniTask / Awaitable が参照されている場合のみ自動的に有効化されます — それ以外は非アクティブです

---

## Burst と ECS レイヤー

3つのオプションモジュール、それぞれ `#if` 定義チェックで保護されています:

| モジュール | 要件 | 目的 |
|--------|----------|---------|
| `JobBridge` | `Unity.Jobs` | `JobHandle` を awaitable としてラップ。各 PlayerLoop ティックで `handle.IsCompleted` をポーリング |
| `AsyncSystemBase` | `Unity.Entities` | 非同期サポートを持つ ECS システムベースクラス |
| `BurstScheduler` | `Unity.Burst` + `Unity.Collections` | 非同期コンテキストから Burst ジョブをスケジュール。`NativeTimerHeap` を管理 |

`NativeTimerHeap` は、マネージドヒープアロケーションを完全に回避する高精度タイマー向けの Burst 互換最小ヒープです。

---

## エディター統合

**Valkarn Hub**（`Tools → Valkarn → Hub`）は `TypeCache.GetTypesDerivedFrom<IValkarnEditorPanel>()` を使用して、インストールされているすべての Valkarn パッケージを自動的に検出します。手動登録は不要です。

`TasksTrackerPanel` は `EditorApplication.update` を購読し、0.5秒ごと（設定可能）にプール診断を更新し、クイックアクセスのために `ValkarnTaskSettings` アセット参照を表示します。

---

## IL2CPP の考慮事項

- ステートマシンはランナー内に**値として**格納されます — ボクシングなし、IL2CPP が正しく処理
- 各非同期メソッドのランナーは独立したジェネリック特殊化 — 型安全、クロスコンタミネーションなし
- Awaiter 構造体は `ICriticalNotifyCompletion` を実装 — コンパイラーが `UnsafeOnCompleted` を呼び出し、`ExecutionContext` のキャプチャをスキップ（Unity のデフォルト設定ではオーバーヘッドなし）
- アグレッシブストリッピングが有効な場合、ランタイムアセンブリを保持します:

```xml
<!-- link.xml -->
<linker>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
</linker>
```
