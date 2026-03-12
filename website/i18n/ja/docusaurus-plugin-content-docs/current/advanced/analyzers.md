---
sidebar_position: 1
title: アナライザーのルール
---

# アナライザーのルール

Valkarn Tasksはパッケージのインポート時に自動的に有効になる2つのRoslynアナライザーパッケージを同梱しています：

- **`UnaPartidaMas.Valkarn.Tasks.SourceGen.dll`** — ValkarnTaskの正確性とUnityライフサイクルに関するルール。ルールIDは`TT`で始まります。
- **`UnaPartidaMas.Valkarn.Tasks.Analyzer.dll`** — UniTaskから移行するコードベース向けの移行ルール。ルールIDは`MIG`で始まります。これらのルールはコンパイル内に**`Cysharp.Threading.Tasks.UniTask`が存在する場合のみ**発火するため、新規プロジェクトでは無効です。

両パッケージはパッケージの`Analyzers/`フォルダに配置されたコンパイル済みDLLです。Unityは`.asmdef`参照を通じてRoslynアナライザーとして読み込みます；`_TestRunner~`プロジェクトは`TestRunner.csproj`の`<Analyzer>`アイテムを通じて読み込みます。

---

## 正確性ルール (TT)

これらのルールは`ValkarnTask`の単一消費の性質とfire-and-forgetの誤用に関連するバグを検出します。

---

### TT001 — ValkarnTaskが既にawaitされた

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 警告 |
| カテゴリ | ValkarnTask |
| コード修正 | なし |

`ValkarnTask`は単一消費です：一度awaitされると、内部トークンは古くなります。同じ変数への2度目の`await`はその古くなったトークンに遭遇してスローします。アナライザーは同一メソッド内で`ValkarnTask`/`ValkarnTask<T>`型の同じローカル変数、パラメーター、またはフィールドが複数回awaitされる場合を検出します。

**発火条件:**
```csharp
async ValkarnTask Bad()
{
    ValkarnTask work = DoWorkAsync();
    await work;   // 最初のawait — 問題なし
    await work;   // TT001: 既にawaitされた
}
```

**修正:** 結果で分岐する必要がある場合は、最初のawaitの前に`.AsResult()`でキャプチャするか、タスクがちょうど一度awaitされるように再構成してください。
```csharp
async ValkarnTask Good()
{
    var result = await DoWorkAsync().AsResult();
    // 両方の分岐でresultを使用
}
```

---

### TT002 — ValkarnTaskがawaitもdiscardもされていない

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | **エラー** |
| カテゴリ | ValkarnTask |
| コード修正 | なし |

式文として使用されている`ValkarnTask`を返すメソッド呼び出し — awaitされず、代入もされず、`.Forget()`が続かない — はサイレントなバグです。タスク内でスローされた例外は観測されず、タスクのプールされたステートマシンはプールに返却されません。

アナライザーは式の型が`ValkarnTask`または`ValkarnTask<T>`に解決される式文をチェックします。スキップされるもの：
- 代入式（`tasks[i] = DoWork()`）
- `.Forget()`で終わるチェーン（意図的なfire-and-forget）

**発火条件:**
```csharp
void Bad()
{
    LoadDataAsync();      // TT002: awaitもdiscardもされていない
    ProcessItemAsync();   // TT002
}
```

**修正 — awaitする:**
```csharp
async ValkarnTask Good()
{
    await LoadDataAsync();
    await ProcessItemAsync();
}
```

**修正 — 明示的なfire-and-forget:**
```csharp
void GoodFireAndForget()
{
    LoadDataAsync().Forget();
}
```

---

### TT013 — ValkarnTaskが返されたが消費されていない

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 警告 |
| カテゴリ | ValkarnTask |
| コード修正 | なし |

このルールIDは**将来のデータフロー解析のために予約されています**。代入されたが決してawaitもdiscardもされないパターン — `ValkarnTask`を変数に格納して、awaitもdiscardもしない — を検出することを意図しています。これはTT002がカバーしていません（TT002は裸の式文のみを検査します）。

現在の実装はシンタックスアクションを登録しません。データフロー解析が実装されると、TT013はTT002を補完して以下をカバーします：
```csharp
async ValkarnTask Bad()
{
    ValkarnTask task = DoWorkAsync();  // 代入されたがawaitされない
    DoOtherThing();
    // taskが放棄された — TT013（将来）
}
```

`[FireAndForget]`で修飾されたメソッドは除外されます。

---

## ライフサイクルルール (TT)

これらのルールはMonoBehaviourのライフタイム管理とキャンセルに関連します。

---

### TT010 — MonoBehaviour非同期メソッドで自動キャンセルが有効

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 情報 |
| カテゴリ | ValkarnTask |
| コード修正 | なし |

情報ヒント。`UnityEngine.MonoBehaviour`を継承するクラス内の`async ValkarnTask`（または`async ValkarnTask<T>`）メソッドは、メソッドが`[NoAutoCancel]`で修飾されていない限り、オブジェクトが破棄されると自動的にキャンセルされます。この診断は開発者が覚えておかなくても良いように、その動作をIDEで可視化します。

**発火条件:**
```csharp
public class MyBehaviour : MonoBehaviour
{
    async ValkarnTask LoadLevel()  // TT010: 自動キャンセルが有効
    {
        await ValkarnTask.Delay(2000);
    }
}
```

**オプトアウトするには**（そのメソッドのTT010を抑制する）、`[NoAutoCancel]`を追加します：
```csharp
[NoAutoCancel]
async ValkarnTask LoadLevel(CancellationToken ct)
{
    await ValkarnTask.Delay(2000, ct);
}
```

---

### TT011 — WhenAllが異なるライフタイムスコープを混在させている

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 警告 |
| カテゴリ | ValkarnTask |
| コード修正 | なし |

異なるライフタイムスコープのタスクで呼び出された`ValkarnTask.WhenAll`は、予想外の部分キャンセル動作を引き起こす可能性があります。1つのタスクが`MonoBehaviour`に紐付けられ（MonoBehaviourを継承するクラスのインスタンスメソッドによって返される）、別のタスクが紐付けられていない場合（スタティックタスク、または非MonoBehaviourクラスから）、オブジェクトを破棄すると1つのタスクはキャンセルされますが、もう1つはキャンセルされず、コンビネーターは不確定な状態に残ります。

**発火条件:**
```csharp
// EnemyAI : MonoBehaviourを前提とする
await ValkarnTask.WhenAll(
    enemyAI.PatrolAsync(),   // 紐付け: Destroy時に自動キャンセル
    GlobalMusic.FadeAsync()  // 非紐付け: 無期限に継続
);
// TT011: WhenAllがライフタイムを混在させている — PatrolAsync() vs GlobalMusic.FadeAsync()
```

**修正 — 両方のタスクに共有キャンセルトークンを渡す:**
```csharp
using var cts = new CancellationTokenSource();
await ValkarnTask.WhenAll(
    enemyAI.PatrolAsync(cts.Token),
    GlobalMusic.FadeAsync(cts.Token)
);
```

---

### TT012 — キャンセルチェックのない非同期ループ

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 警告 |
| カテゴリ | ValkarnTask |
| コード修正 | なし |

`for`、`while`、`foreach`、または`do`-`while`ループを含む`async ValkarnTask`メソッドで、ループボディにキャンセルチェックがないものは「ゾンビループ」です：ライフサイクルキャンセルがトリガーされた場合（MonoBehaviourが破棄されるなど）、自動キャンセルシグナルがループを中断できません。所有オブジェクトがなくなってもループは実行し続けます。

アナライザーはループボディに以下のいずれかが含まれる場合にキャンセルチェックがあると判断します：
- `await`式（awaitされた操作がトークンを観測して`OperationCanceledException`をスローできる）
- `ThrowIfCancellationRequested`（識別子またはメンバーアクセスとして）
- `IsCancellationRequested`（識別子またはメンバーアクセスとして）

このチェックはネストされたラムダまたはローカル関数の内部には**下りません**。

**発火条件:**
```csharp
async ValkarnTask PollForever(CancellationToken ct)
{
    while (true)                     // TT012: ボディにキャンセルチェックなし
    {
        ProcessNextItem();
        Thread.Sleep(16);            // 同期的 — awaitではない
    }
}
```

**修正 — awaitまたは明示的チェックを追加:**
```csharp
async ValkarnTask PollForever(CancellationToken ct)
{
    while (true)
    {
        ct.ThrowIfCancellationRequested();
        ProcessNextItem();
        await ValkarnTask.Yield();       // このチェックも満たす
    }
}
```

---

### TT014 — CancellationTokenパラメーターなしの[NoAutoCancel]

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 警告 |
| カテゴリ | ValkarnTask |
| コード修正 | なし |

`MonoBehaviour`の`async ValkarnTask`メソッドへの`[NoAutoCancel]`はメソッドを自動ライフサイクルキャンセルからオプトアウトします。しかしメソッドに`CancellationToken`パラメーターがない場合、キャンセルを観測するメカニズムがなく、属性は無意味でほぼ確実に忘れられたパラメーターを示しています。

**発火条件:**
```csharp
public class Enemy : MonoBehaviour
{
    [NoAutoCancel]
    async ValkarnTask Chase()      // TT014: [NoAutoCancel]だがCancellationTokenパラメーターなし
    {
        await ValkarnTask.Delay(1000);
    }
}
```

**修正 — `CancellationToken`パラメーターを追加:**
```csharp
[NoAutoCancel]
async ValkarnTask Chase(CancellationToken ct)
{
    await ValkarnTask.Delay(1000, ct);
}
```

---

## 情報ルール (TT)

---

### TT015 — Awaitableブリッジアダプターが生成された

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 情報 |
| カテゴリ | ValkarnTask |
| コード修正 | なし |

`async ValkarnTask`メソッド内でUnityの`Awaitable`（`UnityEngine`から）を`await`すると、Valkarn Tasksは`AwaitableBridge`を通じてブリッジアダプターを自動的に生成します。この診断は情報提供です：ブリッジが有効であることを確認します。操作は不要です。

**発火条件:**
```csharp
async ValkarnTask LoadScene()
{
    await SceneManager.LoadSceneAsync("Main");  // TT015: ブリッジアダプターが生成された
}
```

変更は不要です。ブリッジは変換を透過的に処理します。

---

## コード品質ルール (TT)

---

### TT016 — awaitのない非同期メソッド

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 警告 |
| カテゴリ | ValkarnTask |
| コード修正 | なし |

`await`式を含まない`async ValkarnTask`（または`async ValkarnTask<T>`）メソッド — `await foreach`、`await using`、`using`宣言内の`await`を含む — はメリットなしにステートマシンアロケーションのフルオーバーヘッドを発生させます。コンパイラはまだステートマシンを生成しますが、サスペンドポイントがないため、メソッドは常に同期的に完了します。

アナライザーは`AwaitExpressionSyntax`、`await`キーワードを伴う`foreach`、`await`キーワードを伴う`using`宣言、および`await`キーワードを伴う`using`文をチェックします。

**発火条件:**
```csharp
async ValkarnTask<int> ComputeTotal()  // TT016: ボディにawaitなし
{
    return items.Sum(x => x.Value);
}
```

**修正 — `async`を削除して完了したタスクを返す:**
```csharp
ValkarnTask<int> ComputeTotal()
{
    return ValkarnTask.FromResult(items.Sum(x => x.Value));
}
```

void戻り値の場合：
```csharp
ValkarnTask DoSetup()
{
    Initialize();
    return ValkarnTask.CompletedTask;
}
```

---

### TT017 — ValkarnTask\<T\>への[FireAndForget]

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 警告 |
| カテゴリ | ValkarnTask |
| コード修正 | なし |

`[FireAndForget]`はメソッドが結果をawaitせずに意図的に実行されることを示します。`ValkarnTask<T>`（型付き結果）を返すメソッドに適用すると常に`T`を破棄し、型付き戻り値を無意味にします。メソッドは代わりに`ValkarnTask`（void）を返すべきです。

**発火条件:**
```csharp
[FireAndForget]
async ValkarnTask<int> SendReport()   // TT017: 戻り値が破棄される
{
    await UploadAsync();
    return 42;                     // この42は決して観測されない
}
```

**修正 — 戻り型を`ValkarnTask`に変更:**
```csharp
[FireAndForget]
async ValkarnTask SendReport()
{
    await UploadAsync();
}
```

---

## 移行ルール (MIG)

移行アナライザーはコンパイル内に**`Cysharp.Threading.Tasks.UniTask`型が存在する場合のみ**有効になります。ルールはUniTaskからValkarn Tasksへの移行を案内する情報または警告です。これらのルールには自動修正がありません；以下の説明に手動での変更方法を示します。

---

### MIG001 — UniTask型が検出された

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 情報 |
| カテゴリ | ValkarnTaskMigration |

`UniTask`型（構造体自体または`UniTask<T>`）の使用が検出されました。`ValkarnTask`または`ValkarnTask<T>`に置き換えてください。

```csharp
// 変更前
UniTask<Sprite> LoadSprite(string path) { ... }

// 変更後
ValkarnTask<Sprite> LoadSprite(string path) { ... }
```

---

### MIG002 — cancelImmediatelyパラメーターは不要

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 情報 |
| カテゴリ | ValkarnTaskMigration |

UniTaskの`Delay`などの時間ベースメソッドは、高速キャンセルを選択するための`cancelImmediately`パラメーターを受け入れます。Valkarn Tasksはデフォルトで即座にキャンセルします — `cancelImmediately`パラメーターはありません。引数を削除してください。

```csharp
// 変更前
await UniTask.Delay(1000, cancelImmediately: true, cancellationToken: ct);

// 変更後
await ValkarnTask.Delay(1000, ct);
```

---

### MIG003 — SuppressCancellationThrow()が検出された

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 警告 |
| カテゴリ | ValkarnTaskMigration |

UniTaskの`.SuppressCancellationThrow()`はキャンセルされたタスクをスローせずに`(bool isCancelled, T result)`タプルに変換します。Valkarn Tasksは同じ目的で`.AsResult()`を使用し、`Result<T>`構造体を返します。

```csharp
// 変更前
var (isCancelled, value) = await myUniTask.SuppressCancellationThrow();

// 変更後
var result = await myValkarnTask.AsResult();
if (result.IsCanceled) { ... }
T value = result.Value;
```

---

### MIG004 — Awaitable戻り型が検出された

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 情報 |
| カテゴリ | ValkarnTaskMigration |

メソッドがUnityの`Awaitable`型を返します。Valkarn Tasksランタイムとネイティブに統合し、自動キャンセル、プーリング、フルコンビネーターセットをサポートする`ValkarnTask`への置き換えを検討してください。

---

### MIG005 — SingleConsumerUnboundedチャネルが検出された

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 警告 |
| カテゴリ | ValkarnTaskMigration |

UniTaskの`Channel.CreateSingleConsumerUnbounded<T>()`はバックプレッシャーなしのチャネルを作成します。バックプレッシャーを追加してロード時の無制限メモリ増加を防ぐために、`ValkarnTask.Channel.CreateBounded<T>(capacity)`への置き換えを検討してください。

```csharp
// 変更前
var ch = Channel.CreateSingleConsumerUnbounded<Event>();

// 変更後（バックプレッシャーあり）
var ch = ValkarnTask.Channel.CreateBounded<Event>(capacity: 256);

// またはunboundedが意図的な場合
var ch = ValkarnTask.Channel.CreateUnbounded<Event>();
```

---

### MIG006 — UniTask PlayerLoopTimingが検出された

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 情報 |
| カテゴリ | ValkarnTaskMigration |

`Cysharp.Threading.Tasks`名前空間の`PlayerLoopTiming`列挙値が検出されました。`using`ディレクティブを`UnaPartidaMas.Valkarn.Tasks`に変更してください；列挙値の名前は同一です。

```csharp
// 変更前
using Cysharp.Threading.Tasks;
timing = PlayerLoopTiming.Update;

// 変更後
using UnaPartidaMas.Valkarn.Tasks;
timing = PlayerLoopTiming.Update;  // 同じ名前、異なる名前空間
```

---

### MIG007 — async UniTaskVoidが検出された

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 警告 |
| カテゴリ | ValkarnTaskMigration |

`async UniTaskVoid`はUniTaskのfire-and-forgetメソッドパターンでした。Valkarn Tasksは2つのオプションで置き換えます：

```csharp
// 変更前
async UniTaskVoid StartLoadAsync() { ... }

// 変更後 — オプション1: [FireAndForget]属性
[FireAndForget]
async ValkarnTask StartLoadAsync() { ... }

// 変更後 — オプション2: 標準ValkarnTask + 呼び出し元での.Forget()
async ValkarnTask StartLoadAsync() { ... }
// 呼び出し:
StartLoadAsync().Forget();
```

---

### MIG008 — Awaitable MainThreadAsync()が検出された

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 情報 |
| カテゴリ | ValkarnTaskMigration |

`Awaitable.MainThreadAsync()`はUnityの`Awaitable` APIでメインスレッドに戻るために使用されていました。Valkarn TasksはPlayerLoop統合によってデフォルトでメインスレッドでコンティニュエーションを実行するため、明示的な`MainThreadAsync()`呼び出しは通常不要で削除できます。

---

### MIG009 — UniTask.RunOnThreadPoolが検出された

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 警告 |
| カテゴリ | ValkarnTaskMigration |

`ValkarnTask.RunOnThreadPool`に置き換えてください。APIは同一です。

```csharp
// 変更前
await UniTask.RunOnThreadPool(() => HeavyWork());

// 変更後
await ValkarnTask.RunOnThreadPool(() => HeavyWork());
```

---

### MIG010 — .ToCoroutine()が検出された

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 警告 |
| カテゴリ | ValkarnTaskMigration |

`.ToCoroutine()`はレガシーコルーチン呼び出し元向けのUniTaskブリッジでした。消費するコードを`async ValkarnTask`メソッドとして書き直してください。

```csharp
// 変更前
IEnumerator LegacyCaller() { yield return MyUniTask().ToCoroutine(); }

// 変更後
async ValkarnTask ModernCaller() { await MyValkarnTask(); }
```

---

### MIG011 — UniTask.Create()が検出された

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 警告 |
| カテゴリ | ValkarnTaskMigration |

`UniTask.Create(Func<UniTask>)`はファクトリデリゲートをラップします。手動制御の完了のために`ValkarnTask.Promise<T>`パターンに置き換えてください。

```csharp
// 変更前
var task = UniTask.Create(async () => { await DoWork(); return 42; });

// 変更後
var promise = new ValkarnTaskCompletionSource<int>();
DoWorkThenComplete(promise);
var task = promise.Task;
```

---

### MIG012 — UniTask.Lazy/Deferが検出された

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 情報 |
| カテゴリ | ValkarnTaskMigration |

`UniTask.Lazy<T>`と`UniTask.Defer`はタスクが同期的に完了する可能性がある場合のアロケーション回避のために存在していました。Valkarn Tasksにはゼロアロケーションの同期ファストパスが組み込まれています：`ValkarnTask.CompletedTask`または`ValkarnTask.FromResult(value)`を返してもアロケーションは発生しません。`Lazy`/`Defer`ラッパーを削除してください。

---

### MIG013 — .ToUniTask()/.AsUniTask()が検出された

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 情報 |
| カテゴリ | ValkarnTaskMigration |

UnityのAwaitable`からUniTaskへの変換呼び出し。Valkarn Tasksは`Awaitable`をネイティブにブリッジするため（TT015参照）、削除してください。

---

### MIG014 — UniTaskAsyncEnumerableが検出された

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 警告 |
| カテゴリ | ValkarnTaskMigration |

UniTaskは独自の`IUniTaskAsyncEnumerable<T>`と`UniTaskAsyncEnumerable`ユーティリティを同梱していました。代わりに`System.Linq.Async`と一緒にBCLの`IAsyncEnumerable<T>`を使用してください。`IAsyncEnumerable<T>`はC#の`await foreach`によってネイティブにサポートされています。

```csharp
// 変更前
IUniTaskAsyncEnumerable<int> GetItems() { ... }

// 変更後
IAsyncEnumerable<int> GetItems() { ... }
```

---

### MIG015 — TimeoutControllerが検出された

| プロパティ | 値 |
|------------|------------------------|
| 重大度 | 情報 |
| カテゴリ | ValkarnTaskMigration |

UniTaskの`TimeoutController`は再利用可能なタイムアウトのヘルパーでした。BCLが直接サポートする`TimeSpan`で構築された標準の`CancellationTokenSource`に置き換えてください。

```csharp
// 変更前
var controller = new TimeoutController();
var ct = controller.Timeout(TimeSpan.FromSeconds(5));

// 変更後
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var ct = cts.Token;
```

---

## ルールの抑制

すべてのルールに対して標準のRoslyn抑制メカニズムが機能します：

```csharp
// 単一の発生をインラインで抑制
#pragma warning disable TT012
while (true) { DoSomething(); }
#pragma warning restore TT012
```

またはプロジェクト全体で抑制するために`.editorconfig`を使用：
```ini
[*.cs]
dotnet_diagnostic.TT012.severity = none
```

移行ルール（`MIG*`）も同様に抑制できます。移行完了後は`.editorconfig`で重大度を`none`に設定してグローバルに無効化できます。
