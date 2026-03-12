---
sidebar_position: 2
title: チャネル
---

# チャネル

チャネルはプロデューサーとコンシューマーの間でデータを受け渡すための、スレッドセーフで非同期対応のパイプラインを提供します。バックグラウンドスレッド、イベントコールバック、ジョブ完了など一方から作業が生成され、メインスレッドやワーカープールなど別の場所で消費されるゲームシステムに特に適しています。

## チャネルの作成

チャネルは`VlkTask.Channel`静的ファクトリークラスを通じて作成されます。パブリックコンストラクターはありません。

### 無界チャネル

```csharp
Channel<T> channel = VlkTask.Channel.CreateUnbounded<T>(bool multiConsumer = false);
```

無界チャネルには容量制限がありません。チャネルが完了していない限り、`WriteAsync`と`TryWrite`は常に即座に成功します。コンシューマーが読み取るまで、アイテムは内部の`Queue<T>`に蓄積されます。

**パラメーター**

| パラメーター | デフォルト | 説明 |
|-----------|---------|-------------|
| `multiConsumer` | `false` | `true`の場合、複数の並行`ReadAsync`呼び出しをサポート（競合コンシューマー）。`false`の場合、一度に1つのリーダーのみが`ReadAsync`をawaitできます。 |

### 有界チャネル

```csharp
Channel<T> channel = VlkTask.Channel.CreateBounded<T>(int capacity, bool multiConsumer = false);
```

有界チャネルは固定サイズのリングバッファーに最大`capacity`個のアイテムを保持します。バッファーが満杯の場合、`WriteAsync`はスペースが空くまで呼び出しコードを非同期サスペンドします（バックプレッシャー）。`TryWrite`は満杯の時に待機せず即座に`false`を返します。

**パラメーター**

| パラメーター | デフォルト | 説明 |
|-----------|---------|-------------|
| `capacity` | 必須 | バッファーが保持できるアイテムの最大数。ゼロより大きくなければなりません。 |
| `multiConsumer` | `false` | 無界と同様 — 複数の並行リーダーを有効にします。 |

**有界と無界の選択**

バックプレッシャーを適用したい場合、つまりコンシューマーが遅れた場合にプロデューサーを自動的に遅くしたい場合は`CreateBounded`を使用してください。プロデューサーのレートが自然に制限されている場合（入力イベントなど）や、メモリ成長を既に考慮している場合は`CreateUnbounded`を使用してください。

---

## Channel&lt;T&gt;

`Channel<T>`はパイプラインの2つの側面を別々のオブジェクトとして公開するコンテナです。

```csharp
public sealed class Channel<T>
{
    public ChannelReader<T> Reader { get; }
    public ChannelWriter<T> Writer { get; }
}
```

プロデューサー側に`Writer`参照を、コンシューマー側に`Reader`参照を保持してください。同じスレッドである必要はありません。

---

## ChannelWriter&lt;T&gt;

`ChannelWriter<T>`はチャネルの書き込み側です。`channel.Writer`から取得してください。

### TryWrite

```csharp
public abstract bool TryWrite(T item);
```

サスペンドせずにアイテムの書き込みを試みます。アイテムが受け入れられた場合`true`を返します；チャネルが満杯（有界）または完了した場合`false`を返します。

アイテムを破棄できるホットパスで使用するか、ループでポーリングして非同期ステートマシンからのアロケーションを避けたい場合に使用します。

```csharp
if (!channel.Writer.TryWrite(item))
{
    // チャネルが満杯またはクローズ済み — 適切に処理
}
```

### WriteAsync

```csharp
public abstract VlkTask WriteAsync(T item);
```

必要に応じて呼び出し元を非同期サスペンドしながらチャネルにアイテムを書き込みます。

- **無界チャネル**：チャネルがオープンである限り常に同期的に完了（ゼロアロケーション高速パス）。
- **有界チャネル**：バッファーにスペースがある場合は同期的に完了；バッファーが満杯の場合は呼び出し元をサスペンドし、コンシューマーがアイテムを読み取ってスロットを解放すると再開されます。

`Complete()`が書き込みの前または最中に呼ばれている場合は`ChannelClosedException`をスローします。

```csharp
await channel.Writer.WriteAsync(item);
```

複数のプロデューサーが有界チャネルで`WriteAsync`を並行して呼び出すことができます。各サスペンドされたライターはFIFO順でキューされ、スペースが空くとアンブロックされます。

### Complete

```csharp
public abstract void Complete();
```

これ以上アイテムが書き込まれないことをシグナルします。`Complete()`が呼ばれた後：

- 既に書き込まれたアイテムはバッファーに残り、消費できます。
- `WriteAsync`または`TryWrite`の新しい呼び出しは`ChannelClosedException`で失敗します。
- バッファーが完全にドレインされると、`ChannelReader<T>.Completion`が完了し、保留中または将来の`ReadAsync`呼び出しは`ChannelClosedException`をスローします。

`Complete()`は単一呼び出しに対してべき等です — 複数回呼び出しても安全（後続の呼び出しは無視されます）。

```csharp
// 作業の終了をシグナル
channel.Writer.Complete();
```

---

## ChannelReader&lt;T&gt;

`ChannelReader<T>`はチャネルの読み取り側です。`channel.Reader`から取得してください。

### ReadAsync

```csharp
public abstract VlkTask<T> ReadAsync();
```

チャネルから次のアイテムを読み取ります。現在アイテムが利用できない場合、呼び出し元は到着するまで非同期サスペンドされます。チャネルが完了かつ完全にドレインされた場合、`ChannelClosedException`をスローします。

```csharp
T item = await channel.Reader.ReadAsync();
```

**シングルコンシューマーモード**（デフォルト）：一度に1つの`ReadAsync`のみがインフライトになれます。2つ目の並行`ReadAsync`を開始しようとすると即座にスローします。この制限により内部のゼロアロケーション最適化が可能になります — リーダーコアは呼び出しごとのプールレンタルなしに、チャネル実装に直接埋め込まれます。

**マルチコンシューマーモード**（`multiConsumer: true`）：任意の数の`ReadAsync`呼び出しが同時に保留できます。各保留中の呼び出し元はキューされ、アイテムが利用可能になるとFIFO順で解決されます。

### TryRead

```csharp
public abstract bool TryRead(out T item);
```

サスペンドせずにアイテムの読み取りを試みます。アイテムが利用可能な場合`true`を返して`item`を設定します；チャネルが空の場合`false`を返します（`item`を`default`に設定）。

`TryRead`は空だが開いているチャネルと空でクローズされたチャネルを区別しません。ポーリングループで`TryRead`を使用している場合、クローズ状態を検出するには`Completion`を使用してください。

### ReadAllAsync

```csharp
public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default);
```

チャネルが完了してドレインされるまですべてのアイテムを反復する`IAsyncEnumerable<T>`を返します。列挙は`ChannelClosedException`を伝播させずにクリーンに終了します。

```csharp
await foreach (var item in channel.Reader.ReadAllAsync(ct))
{
    Process(item);
}
// チャネルが完了して空の場合にここに到達
```

`ReadAllAsync`に渡されたキャンセルトークンはフォールバックとして使用されます。`GetAsyncEnumerator`にもトークンが渡される場合（`await foreach`が`WithCancellation`で行うように）、`foreach`側のトークンが優先されます。

### Completion

```csharp
public abstract VlkTask Completion { get; }
```

`Complete()`が呼ばれた後にチャネルが完全にドレインされると完了する`VlkTask`。具体的には：

- `Complete()`が既に空のチャネルで呼ばれた場合、`Completion`は即座に解決されます。
- `Complete()`がバッファーにアイテムが残っている間に呼ばれた場合、最後のアイテムが消費されてから`Completion`が解決されます。

`Completion`をawaitすることがパイプラインの終了を待つ標準的な方法です。

```csharp
channel.Writer.Complete();
await channel.Reader.Completion;
// すべてのアイテムが消費された
```

---

## ChannelClosedException

```csharp
public sealed class ChannelClosedException : InvalidOperationException
```

2つの状況でスローされます：

1. **完了してドレインされたチャネルからの読み取り** — チャネルが完了とマークされ、アイテムが残っていない場合に`ReadAsync()`がスローします。
2. **完了したチャネルへの書き込み** — 書き込みの前に`Complete()`が呼ばれた場合に`WriteAsync()`がスローします。

`ChannelClosedException`は`InvalidOperationException`から継承します。`TryRead`または`TryWrite`ではスローされません；代わりに`false`を返します。

コンストラクター：

```csharp
new ChannelClosedException()
new ChannelClosedException(string message)
new ChannelClosedException(Exception innerException)
```

---

## 有界チャネル：バックプレッシャーの詳細

有界チャネルのバッファーが満杯で`WriteAsync`が呼ばれると、ライターはサスペンドされ、保留ライターレコードが内部にエンキューされます。ライターはアイテムを保持します。コンシューマーが`ReadAsync`または`TryRead`を呼び出してアイテムをデキューすると：

1. 解放されたスロットは即座に最も古い保留ライターにクレームされます。
2. そのライターのアイテムがバッファーに配置されます。
3. ライターのawaitコードが再開されます。

これは満杯の有界チャネルがアイテムを失ったりバッファー容量を無駄にしたりしないことを意味します — スロットが空くことと、ブロックされたライターが再開されることの間には常に一対一の対応があります。バッファーが空の状態で保留ライターが待機中にリーダーが到着した場合、アイテムはバッファーに触れることなく直接ハンドオフされます。

---

## パターン

### 基本的なプロデューサー/コンシューマー

```csharp
var channel = VlkTask.Channel.CreateUnbounded<WorkItem>();

// プロデューサー（例：バックグラウンドスレッドやコールバックで実行）
async VlkTask ProduceAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var work = await FetchNextWorkItemAsync(ct);
        await channel.Writer.WriteAsync(work);
    }
    channel.Writer.Complete();
}

// コンシューマー（呼び出し先を選択して実行）
async VlkTask ConsumeAsync()
{
    await foreach (var item in channel.Reader.ReadAllAsync())
    {
        await ProcessAsync(item);
    }
}
```

### 複数プロデューサー、シングルコンシューマー

複数のプロデューサーがそれぞれ`channel.Writer`への参照を保持し、`WriteAsync`を並行して呼び出すことができます。すべてのチャネル操作は内部ロックで保護されているため、これは安全です。

```csharp
var channel = VlkTask.Channel.CreateBounded<Event>(capacity: 64);

// 複数のプロデューサーが並行して書き込む
VlkTask ProducerA() => ProduceFrom(sourceA, channel.Writer);
VlkTask ProducerB() => ProduceFrom(sourceB, channel.Writer);
VlkTask ProducerC() => ProduceFrom(sourceC, channel.Writer);

// シングルコンシューマー（デフォルト — multiConsumerフラグ不要）
async VlkTask ConsumerAsync()
{
    await foreach (var ev in channel.Reader.ReadAllAsync())
        HandleEvent(ev);
}
```

複数プロデューサーで有界チャネルを使用する場合、`Complete()`を慎重に調整してください — すべてのプロデューサーが書き込みを終了した後にのみ呼び出してください。そうでないと一部のライターが`ChannelClosedException`を受け取る可能性があります。

### 複数プロデューサー、複数コンシューマー

```csharp
// multiConsumer: trueで複数のコンシューマーからの並行ReadAsyncが有効になる
var channel = VlkTask.Channel.CreateUnbounded<Job>(multiConsumer: true);

async VlkTask WorkerAsync(int id, CancellationToken ct)
{
    try
    {
        while (true)
        {
            var job = await channel.Reader.ReadAsync();
            await ExecuteJobAsync(job, ct);
        }
    }
    catch (ChannelClosedException)
    {
        // チャネルが完了 — グレースフルに終了
    }
}
```

各アイテムはちょうど1つのコンシューマーに届きます。コンシューマーはFIFO順でアイテムを競います（最も長く待っているコンシューマーが次の利用可能なアイテムを取得します）。

### グレースフルシャットダウン

推奨されるシャットダウンシーケンスは：

1. すべてのプロデューサーに停止をシグナルします（例：`CancellationToken`をキャンセル）。
2. すべてのプロデューサーが書き込みを停止した後、`channel.Writer.Complete()`を呼び出します。
3. すべてのアイテムが消費されたことを確認するため`channel.Reader.Completion`をawaitします。

```csharp
cts.Cancel();                        // プロデューサーを停止
await allProducersTask;              // それらが終了するのを待つ
channel.Writer.Complete();           // チャネルを封印
await channel.Reader.Completion;     // 残りのアイテムをドレイン
```

`ReadAllAsync`を使用している場合、ステップ3は自動的に発生します — `await foreach`ループはチャネルが完了して空になると終了します。

---

## System.Threading.Channelsとの比較

| 機能 | Valkarnチャネル | System.Threading.Channels |
|---------|-----------------|---------------------------|
| 戻り値型 | `VlkTask` / `VlkTask<T>` | `ValueTask` / `ValueTask<T>` |
| アロケーション（ホットパス） | ゼロ（シングルコンシューマー無界） | ほぼゼロ |
| `WaitToReadAsync` | なし — `ReadAsync`または`ReadAllAsync`を使用 | あり |
| `TryComplete(Exception)` | なし — `Complete()`を使用 | あり |
| `Count` / `CanCount` | 公開なし | 一部のチャネル型に存在 |
| 満杯時のドロップポリシー | 非対応 — `WriteAsync`がブロック | `DropWrite`、`DropNewest`、`DropOldest`、`Wait` |
| 非同期列挙可能 | `ReadAllAsync()` | `ReadAllAsync()` |
| スレッド安全性 | 完全（ロックベース） | 完全（ロックベース） |

主要な違いは、ValkarnチャネルがUnityビルドで`VlkTask`とネイティブに統合してオーバーヘッドゼロのawaitを実現し、シングルコンシューマーパスは`ReadAsync`呼び出しごとのプールレンタル/返却を、完了コアをチャネルに直接埋め込むことで回避している点です。
