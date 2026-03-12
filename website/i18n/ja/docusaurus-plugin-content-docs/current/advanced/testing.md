---
sidebar_position: 3
title: テスト
---

# テスト

Valkarn Tasksは、非同期コード向けに高速で決定論的なユニットテストを書くための専用テストインフラを同梱しています — リアルタイマーなし、フレーム待機なし、コアテストスイートにUnity Editorも不要です。

---

## 概要

テストサポートは`Testing/`アセンブリ（`UnaPartidaMas.Valkarn.Tasks.Testing`）にあり、`InternalsVisibleTo`を通じてランタイムから可視です。2つのパブリック型を公開します：

| 型 | 目的 |
|------|---------|
| `VlkTaskTestHelper` | テストセッション向けにValkarn Tasksランタイムを初期化・終了する |
| `TestClock` | 決定論的な時間とフレームプロバイダー；テスト中にUnityの`TimeProvider`を置き換える |

---

## VlkTaskTestHelper

`VlkTaskTestHelper`は`UnaPartidaMas.Valkarn.Tasks.Testing`名前空間の静的ユーティリティクラスです。

### 動作内容

`Setup()`は3つのことを実行します：
1. `TestClock`を作成して`TimeProvider.Current`としてインストールし、リアルUnity時間プロバイダーを置き換えます。
2. `PlayerLoopHelper.InitializeForTest()`を呼び出し、16すべてのPlayerLoopタイミングの`ContinuationQueue`と`PlayerLoopRunner`配列を確保してランタイムを初期化済みとしてマークします。
3. テストが時間を制御できるように`TestClock`を返します。

`Teardown()`はセットアップを逆転します：
1. 新鮮な何もしない`TestClock`を`TimeProvider.Current`としてインストールして、テストフィクスチャ間で古い時間が漏れるのを防ぎます。
2. `PlayerLoopHelper.ShutdownForTest()`を呼び出し、キューとランナーを解体します。

### 使用パターン

```csharp
using NUnit.Framework;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Testing;

[TestFixture]
public class MyAsyncTests
{
    TestClock clock;

    [SetUp]
    public void SetUp()
    {
        clock = VlkTaskTestHelper.Setup();
    }

    [TearDown]
    public void TearDown()
    {
        VlkTaskTestHelper.Teardown();
    }

    // テストはここに
}
```

`[SetUp]`で各テストごとに1回`Setup`を呼び出し、`[TearDown]`で各テストごとに1回`Teardown`を呼び出してください。このパターンにより、各テストがクリーンなランタイム状態で開始し、次のテストに漏れないことが保証されます。

### デフォルトのデルタタイム

`Setup`はオプションの`defaultDeltaTime`パラメーター（デフォルト: `1/60`秒、つまり60fps）を受け入れます：

```csharp
clock = VlkTaskTestHelper.Setup(defaultDeltaTime: 1f / 30f);  // 30fpsシミュレーション
```

この値は`TestClock.AdvanceFrame()`と`TestClock.AdvanceFrames(n)`によって使用されます。

---

## TestClock

`TestClock`は`ITimeProvider`を実装し、テストに以下のフルコントロールを提供します：

- シミュレーション時間（`GetTimestamp()`経由、`Stopwatch.Frequency`ティックでバックアップ）
- 現在フレームの`DeltaTime`と`UnscaledDeltaTime`
- `FrameCount`

### 時間の進行

#### `Advance(TimeSpan duration)`

指定された期間だけ時間を一ステップで進めます。期間全体がそのフレームの`DeltaTime`として適用され、`FrameCount`が1増加し、すべてのPlayerLoopタイミングが処理されます。処理後、`DeltaTime`は呼び出し前の値に復元されます。

```csharp
var task = VlkTask.Delay(3000);  // 3秒遅延

clock.Advance(TimeSpan.FromSeconds(2));
Assert.IsFalse(task.IsCompleted);   // まだ

clock.Advance(TimeSpan.FromSeconds(1));
Assert.IsTrue(task.IsCompleted);    // ちょうど3秒で完了
```

個々のフレームをシミュレートせずに特定の時点に飛びたい場合に使用します。

#### `AdvanceFrame()`

現在の`DeltaTime`を使用して1フレーム進めます。`FrameCount`が1増加し、すべてのPlayerLoopタイミングが処理され、処理前に1ミリ秒の`Thread.Sleep`が挿入されます。このスリープは、バックグラウンドワーカースレッド（`RunOnThreadPool`とUnity Job Systemインテグレーションで使用）がフレームティック間に完了するための小さなウィンドウを必要とするためです — なしでは、テストがリアルフレームでは発生しない競合状態を引き起こすギャップなしにフレームを連続実行します。

```csharp
clock.AdvanceFrame();
```

#### `AdvanceFrames(int count)`

`AdvanceFrame()`を`count`回呼び出します。

```csharp
clock.AdvanceFrames(10);  // 10フレームをシミュレート
```

#### `ProcessTick(PlayerLoopTiming timing)`

時間や`FrameCount`を進めずに単一のPlayerLoopタイミングフェーズを処理します。特定のタイミング（例：`PlayerLoopTiming.FixedUpdate`）でスケジュールされたコードをテストするとき、他のすべてのタイミングを処理したくない場合に便利です。

```csharp
clock.ProcessTick(PlayerLoopTiming.FixedUpdate);
```

### デルタタイムの制御

#### `SetDeltaTime(float deltaTime)`

`DeltaTime`と`UnscaledDeltaTime`の両方をすべての後続フレームで同じ値に設定します。

#### `SetDeltaTime(float deltaTime, float unscaledDeltaTime)`

非単位の`Time.timeScale`をシミュレートするために独立して設定します。例えば、時間が停止している間に`DelayType.UnscaledDeltaTime`を使用するコードをテストする場合：

```csharp
clock.SetDeltaTime(deltaTime: 0f, unscaledDeltaTime: 0.05f);

var scaledTask = VlkTask.Delay(500, DelayType.DeltaTime);
var unscaledTask = VlkTask.Delay(500, DelayType.UnscaledDeltaTime);

clock.AdvanceFrames(9);  // アンスケール450ms、スケール0ms
Assert.IsFalse(scaledTask.IsCompleted);    // 停止したゲーム時間は500msに到達しない
Assert.IsFalse(unscaledTask.IsCompleted); // 450ms < 500ms

clock.AdvanceFrame();  // アンスケール500ms
Assert.IsTrue(unscaledTask.IsCompleted);  // アンスケール遅延完了
Assert.IsFalse(scaledTask.IsCompleted);  // スケールはまだ動かない
```

---

## 非同期VlkTaskメソッドのユニットテストの書き方

### 同期的に完了するテスト

一部の`VlkTask`操作はサスペンドせずに完了します — 例えば、`VlkTask.CompletedTask`、`VlkTask.FromResult(value)`、またはawaitされる前に完了した`VlkTaskCompletionSource`をawaitする場合。これらはクロックを全く必要とせず、テストアセンブリ内部の`TestHelper`クラスを直接使用できます：

```csharp
[TestFixture]
public class SyncTests
{
    [SetUp]
    public void SetUp()
    {
        // メインスレッドIDのみ設定 — クロックやPlayerLoopは不要
        TestHelper.EnsureInitialized();
    }

    [Test]
    public void FromResult_ReturnsValue()
    {
        var task = VlkTask.FromResult(42);
        Assert.IsTrue(task.IsCompleted);
        Assert.AreEqual(42, task.GetAwaiter().GetResult());
    }
}
```

`TestHelper`（`Tests/Editor/TestHelper.cs`内）はテストスイート自体が使用する内部ヘルパーです：

- `TestHelper.EnsureInitialized()`は`VlkTaskPoolShared.MainThreadId`を現在のスレッドに設定します。これにより、プール操作が正しい（非アトミック）ファストパスにルートされます。
- `TestHelper.RunSync(VlkTask task)`はタスクがすでに完了していることをアサートして`GetResult()`を呼び出します — 同期的に完了するはずのコードパスのテストに便利です。
- `TestHelper.RunSync<T>(VlkTask<T> task)`は同様に結果値を返します。
- `TestHelper.UnobservedExceptionCollector`はそのスコープの間`VlkTask.UnobservedException`をサブスクライブし、アサート用に未観測の例外を収集します。

### 時間を必要とするテスト

`VlkTask.Delay`、`VlkTask.Yield`、またはPlayerLoopタイミングでスケジュールされたコードを含むテストは、`VlkTaskTestHelper.Setup()`と返される`TestClock`を必要とします。

**例: 遅延ベースメソッドのテスト**

以下のメソッドがあるとします：

```csharp
public static async VlkTask WaitAndLog(int ms)
{
    await VlkTask.Delay(ms);
    Log("done");
}
```

リアルな待機なしでテスト：

```csharp
[TestFixture]
public class DelayTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = VlkTaskTestHelper.Setup();

    [TearDown]
    public void TearDown() => VlkTaskTestHelper.Teardown();

    [Test]
    public void WaitAndLog_CompletesAfterDelay()
    {
        var task = WaitAndLog(3000);
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(task.IsCompleted);

        // 結果を取得（フォルトの場合はスロー）
        task.GetAwaiter().GetResult();
    }

    [Test]
    public void WaitAndLog_ZeroDelay_CompletesImmediately()
    {
        var task = WaitAndLog(0);
        // VlkTask.Delay(0)は即座にCompletedTaskを返す
        Assert.IsTrue(task.IsCompleted);
    }
}
```

### キャンセルのテスト

```csharp
[Test]
public void Delay_CancelledMidway_ThrowsOCE()
{
    var cts = new CancellationTokenSource();
    var task = VlkTask.Delay(5000, cts.Token);
    Assert.IsFalse(task.IsCompleted);

    cts.Cancel();
    // キャンセルは次のティックで伝播する
    clock.AdvanceFrame();

    Assert.IsTrue(task.IsCompleted);
    Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
}
```

### 未観測例外の収集

```csharp
[Test]
public void FaultedTask_PublishesUnobservedException()
{
    using var collector = new TestHelper.UnobservedExceptionCollector();

    var tcs = new VlkTaskCompletionSource();
    var task = tcs.Task;

    // awaitしない — fire and forget
    task.Forget();

    // 例外で完了 — 未観測パスをトリガー
    tcs.TrySetException(new InvalidOperationException("oops"));

    Assert.AreEqual(1, collector.Exceptions.Count);
    Assert.IsInstanceOf<InvalidOperationException>(collector.Exceptions[0]);
}
```

---

## 例: チャネルプロデューサー/コンシューマーのテスト

```csharp
[TestFixture]
public class ChannelPipelineTests
{
    [SetUp]
    public void SetUp() => TestHelper.EnsureInitialized();

    [Test]
    public void Producer_WritesItems_ConsumerReadsInOrder()
    {
        var channel = VlkTask.Channel.CreateUnbounded<int>();

        // プロデューサー: 同期的に書き込み
        channel.Writer.TryWrite(1);
        channel.Writer.TryWrite(2);
        channel.Writer.TryWrite(3);
        channel.Writer.Complete();

        // コンシューマー: アイテムがあるチャネルへのReadAsyncは同期的に完了する
        Assert.AreEqual(1, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(2, TestHelper.RunSync(channel.Reader.ReadAsync()));
        Assert.AreEqual(3, TestHelper.RunSync(channel.Reader.ReadAsync()));
    }

    [Test]
    public void ReadAsync_BeforeWrite_PendsThenCompletesOnWrite()
    {
        var channel = VlkTask.Channel.CreateUnbounded<string>();

        // 書き込み前に読み取りを発行
        var readTask = channel.Reader.ReadAsync();
        Assert.IsFalse(readTask.IsCompleted);

        // 書き込みが保留中の読み取りを同期的に完了させる
        channel.Writer.TryWrite("hello");
        Assert.IsTrue(readTask.IsCompleted);
        Assert.AreEqual("hello", readTask.GetAwaiter().GetResult());
    }

    [Test]
    public void Complete_DrainedChannel_CompletionTaskCompletes()
    {
        var channel = VlkTask.Channel.CreateUnbounded<int>();
        channel.Writer.TryWrite(42);

        var completion = channel.Reader.Completion;
        Assert.IsFalse(completion.IsCompleted);

        channel.Writer.Complete();
        Assert.IsFalse(completion.IsCompleted);  // アイテムがまだ未読

        channel.Reader.TryRead(out _);
        Assert.IsTrue(completion.IsCompleted);   // ドレイン済み — 完了が発火
    }

    [Test]
    public void ClosedChannel_Read_ThrowsChannelClosedException()
    {
        var channel = VlkTask.Channel.CreateUnbounded<int>();
        channel.Writer.Complete();

        var readTask = channel.Reader.ReadAsync();
        Assert.IsTrue(readTask.IsCompleted);
        Assert.Throws<ChannelClosedException>(() => readTask.GetAwaiter().GetResult());
    }
}
```

---

## 例: フレーム進行を使ったVlkTask.Delayのテスト

この例では、設定可能な遅延を待つメソッドのテストで、部分的な進行がタスクを早期に完了させないことを確認する方法を示します。

```csharp
[TestFixture]
public class DelayFrameTests
{
    TestClock clock;

    [SetUp]
    public void SetUp() => clock = VlkTaskTestHelper.Setup(defaultDeltaTime: 1f / 60f);

    [TearDown]
    public void TearDown() => VlkTaskTestHelper.Teardown();

    [Test]
    public void Delay_500ms_RequiresTenFramesAt50ms()
    {
        clock.SetDeltaTime(0.05f);  // フレームあたり50ms
        var task = VlkTask.Delay(500);

        clock.AdvanceFrames(9);
        Assert.IsFalse(task.IsCompleted, "450ms経過 — まだ完了しないはず");

        clock.AdvanceFrame();
        Assert.IsTrue(task.IsCompleted, "500ms経過 — 完了しているはず");
    }

    [Test]
    public void Delay_Realtime_UsesTimestampNotDeltaTime()
    {
        // リアルタイム遅延はDeltaTimeを無視し、Stopwatchタイムスタンプを使用
        clock.SetDeltaTime(0f);  // DeltaTimeを固定
        var task = VlkTask.Delay(200, DelayType.Realtime);

        // AdvanceはTimeSpanを通じてタイムスタンプのみを移動させる
        clock.Advance(TimeSpan.FromMilliseconds(199));
        Assert.IsFalse(task.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(task.IsCompleted);
    }
}
```

---

## NUnitインテグレーション

テストスイートは**NUnit 3.x**（`TestRunner.csproj`で3.xにピン止め）を使用します。NUnit 4はテストが使用するクラシックな`Assert.IsTrue`/`Assert.AreEqual` APIを削除しました；アサーションを制約ベースのAPIに更新しない限りNUnit 4にアップグレードしないでください。

### Unity Test Framework

Unity Editorでは、`Tests/Editor/`のテストは**Unity Test Framework**（UTF）によって発見・実行されます（NUnitベース）。テストランナーインテグレーションの動作：

- UTFは各`[Test]`をメインスレッドで実行します。
- `VlkTaskTestHelper.Setup()`は各テスト前に`TestClock`をインストールします；UTFの`[SetUp]`属性がそれを呼び出します。
- `VlkTaskTestHelper.Teardown()`は`[TearDown]`でクロックを削除します。
- コルーチンや`[UnityTest]`は不要です：すべてのValkarn Tasksテストは同期的なNUnit `[Test]`メソッドを使用します。`TestClock`が時間を手動で駆動するため、リアルなフレーム進行は不要です。

---

## _TestRunner~プロジェクト

`_TestRunner~/`ディレクトリには、.NET SDKと`dotnet test`を使用して**Unity外で**テストスイート全体をコンパイルして実行するスタンドアロンの.NET 8プロジェクトが含まれています。これはCIとコントリビューターのローカル開発に使用されます。

### 構造

```
_TestRunner~/
  TestRunner.csproj   — .NET 8プロジェクトファイル
  bin~/               — ビルド出力（gitignore済み）
  obj~/               — 中間出力（gitignore済み）
```

### 動作の仕組み

`TestRunner.csproj`は4セットのソースを単一アセンブリにコンパイルします：

| ソースセット | パス | 備考 |
|------------|------|-------|
| ランタイム | `../Runtime/**/*.cs` | すべてのランタイムファイル |
| テスト | `../Testing/**/*.cs` | `VlkTaskTestHelper`、`TestClock` |
| テスト | `../Tests/Editor/**/*.cs` | すべての`.cs`テストファイル |
| 除外 | `UnityTimeProvider.cs`、`Bridge/`、`Burst/`、`ECS/` | リアルUnity APIが必要 — 除外 |

このプロジェクトは`UNITY_5_3_OR_NEWER`、`VTASKS_HAS_BURST`、`VTASKS_HAS_COLLECTIONS`、または`VTASKS_HAS_ENTITIES`を定義しないため、すべてのUnity固有の`#if`分岐はコンパイルアウトされます。つまり、テストはピュアC#コードパスを実行します。

アナライザーDLLは`<Analyzer>`アイテムとして読み込まれるため、IDEで発火する同じルールが`_TestRunner~`プロジェクトの`dotnet build`中にも発火します。

### テストの実行

```bash
cd _TestRunner~
dotnet test
```

またはリポジトリルートから：

```bash
dotnet test _TestRunner~/TestRunner.csproj
```

### 除外されるテストファイル

3つのテストサブディレクトリはリアルUnityランタイムAPIが必要なため`_TestRunner~`プロジェクトから除外されます：

| ディレクトリ | 理由 |
|-----------|--------|
| `Tests/Editor/Bridge/` | Job Systemブリッジのテスト；`Unity.Jobs`が必要 |
| `Tests/Editor/Burst/` | Burstスケジューラーのテスト；`Unity.Burst`が必要 |
| `Tests/Editor/ECS/` | ECSユーティリティのテスト；`Unity.Entities`が必要 |

これらのテストはUnity Test Framework経由でUnity Editorの内部でのみ実行されます。

### 新しいテストの追加

1. `Tests/Editor/`以下（Unity APIサブディレクトリ内ではない）に新しい`.cs`ファイルを追加。
2. クラスを`[TestFixture]`で、メソッドを`[Test]`で修飾。
3. テストが時間制御を必要とする場合は`[SetUp]`/`[TearDown]`で`VlkTaskTestHelper.Setup()`/`Teardown()`を使用。
4. テストが同期タスクアサーションのみ必要な場合（遅延なし）は`[SetUp]`で`TestHelper.EnsureInitialized()`を使用 — teardownは不要。
5. `dotnet test _TestRunner~/TestRunner.csproj`を実行してUnity外で確認。
6. Unity Test Frameworkを実行してUnity内で確認。
