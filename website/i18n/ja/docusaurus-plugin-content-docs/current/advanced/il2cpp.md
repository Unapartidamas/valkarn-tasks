---
sidebar_position: 2
title: IL2CPP互換性
---

# IL2CPP互換性

Valkarn TasksはIL2CPP下で正しく動作するよう、最初から設計されています。このページでは、取られたすべての対策と、iOS、WebGL、コンソールなどのIL2CPPプラットフォームで安全に出荷するために必要なこと — またはしなくて良いこと — を説明します。

---

## IL2CPPが特別な配慮を必要とする理由

IL2CPPはC# ILをC++ソースコードに変換し、ネイティブコンパイラでコンパイルします。非同期ライブラリに関連するパイプラインの2つの特性があります：

1. **コードストリッピング。** Unityのマネージドコードストリッパー（IL2CPPのリンカーを使用）は、静的に解析可能なコールグラフから参照されていない型、メソッド、フィールドを削除します。インターフェースディスパッチ、ジェネリック共有、またはリフレクションを通じてのみアクセスされる型 — プールされたプロミスクラスや`ISource`実装を含む — はサイレントにストリップされる可能性があります。

2. **ジェネリック共有。** IL2CPPはすべてのジェネリックインスタンス化に対して別々のネイティブバイナリを生成しません。代わりに参照型間でコードを共有し、値型には特定のインスタンス化を使用します。これにより、開発時（Mono）には隠れていてIL2CPPビルドでのみ表面化するバグが生じる可能性があります。

---

## link.xmlファイル

ストリッピングに対する主要な防御はlink.xmlファイルで、以下の場所にあります：

```
Runtime/link.xml
```

内容：

```xml
<linker>
  <!-- Valkarn.Tasksランタイムアセンブリのすべての型を保持する
       IL2CPPコードストリッピングは、インターフェースディスパッチ、ジェネリック共有、
       またはリフレクション（プロミスクラス、プールされたランナー、ISource実装など）を
       通じてのみアクセスされる内部型を削除する可能性がある -->
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks.Burst" preserve="all"/>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks.ECS" preserve="all"/>
</linker>
```

`preserve="all"`は、静的解析が何を見つけるかに関わらず、それらのアセンブリ内のすべての型、メソッド、フィールド、コンストラクターを保持するようリンカーに指示します。これは、ストリッパーが追跡できないジェネリックパラメーターを通じて内部型がアクセスされるライブラリに最も安全な設定です。

Unityはプロジェクトにインポートされたパッケージフォルダー内に配置されている場合、link.xmlファイルを自動的に発見して適用します。手動の手順は不要です。

ソースを**フォーク**または**埋め込む**場合（パッケージを使用する代わりに）、`link.xml`を`Resources`隣接フォルダーにコピーするか、Unityが発見できる任意の場所に配置してください（[Unityマネージドコードストリッピングドキュメント](https://docs.unity3d.com/Manual/ManagedCodeStripping.html)を参照）。

---

## link.xmlなしにストリップされる内部型

以下のカテゴリの内部型が主要なストリッピングリスクです：

### プールされたプロミスクラス（ISource実装）

すべてのコンビネーターと遅延型は`ValkarnTask.ISource`または`ValkarnTask.ISource<T>`を実装するプールされたプロミスクラスを作成します：

- `AsyncValkarnTaskRunner<TStateMachine>` — プールされたステートマシンランナー、非同期メソッドごとに1つ（`TStateMachine`に特化）
- `WhenAllPromise<T1, T2>`、`WhenAllArrayPromise<T>`、`WhenAllVoidPromise2`、`WhenAllVoidPromise3`、`WhenAllVoidArrayPromise`
- `DeltaTimeDelayPromise`、`UnscaledDeltaTimeDelayPromise`、`RealtimeDelayPromise`
- `CanceledSource`、`CanceledSource<T>`、`ExceptionSource`、`ExceptionSource<T>`、`NeverSource`
- チャネル内部: `BoundedChannel<T>`、`UnboundedChannel<T>`、およびそのリーダー/ライター型

これらの型はジェネリックファクトリメソッド（`ValkarnTaskPool<T>.GetOrCreate`）を通じてインスタンス化されます。静的解析コールグラフはジェネリックメソッド呼び出しから始まり、すべての具体的な`T`インスタンス化を確実に追跡できないため、`link.xml`なしではこれらの型のいずれかがストリップされる可能性があります。

### ValkarnTaskPool\<T\>

```csharp
internal sealed class ValkarnTaskPool<T> : IPoolInfo where T : class, IPoolNode<T>
```

プールはその要素型に対してジェネリックです。各プロミスクラスは独自の静的プールフィールドを持ちます。特定のプロミス型がシーンで未使用の場合、プールとプロミスクラスが両方一緒にストリップされる可能性があります。

### ValkarnTaskCompletionCore\<T\>

内部完了コアはすべてのプロミス内で使用される値型です。コンティニュエーションコールバック、トークン、完了状態を保持します。ライブラリの外部から名前で参照されることはありません。

---

## ジェネリック型制約とIL2CPP

カスタム非同期メソッドビルダーはステートマシン型に対してジェネリックです：

```csharp
public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
    ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : ICriticalNotifyCompletion
    where TStateMachine : IAsyncStateMachine
```

そしてランナーはステートマシンに対してジェネリックです：

```csharp
internal sealed class AsyncValkarnTaskRunner<TStateMachine>
    : IStateMachineRunnerPromise, IPoolNode<AsyncValkarnTaskRunner<TStateMachine>>
    where TStateMachine : IAsyncStateMachine
```

IL2CPP下では、各異なる`TStateMachine`（ステートマシンは構造体なので、フルジェネリック特化が必要）に対して新しい具体型が生成されます。これは以下を意味します：

- プロジェクト内の各`async ValkarnTask`メソッドは別個の`AsyncValkarnTaskRunner<TYourStateMachine>`ネイティブ型を生成します。
- ストリッパーがすべてのインスタンス化を確認する前に`AsyncValkarnTaskRunner<T>`を削除すると、一部の非同期メソッドがランタイムでクラッシュする可能性があります。
- `link.xml`の`preserve="all"`がこれを防ぎます。

---

## マネージドコードストリッピングレベル

Unityのストリッピングレベルは**プレーヤー設定 → その他の設定 → マネージドストリッピングレベル**で設定します。

| レベル | Valkarn Tasksでの状態 |
|-------------|----------------------------------------------------------------------------------|
| 無効 | 安全。ストリッピングは発生しない。 |
| 低 | 安全。明らかに未使用のアセンブリのみ削除。 |
| 中 | **`link.xml`あり**で安全。含まれる`link.xml`がすべてのランタイム型を保持。 |
| 高 | **`link.xml`あり**で安全。同じカバレッジ；`link.xml`が保護レイヤー。 |

**高**までのすべてのストリッピングレベルは、`link.xml`ファイルが存在して適用されている限り安全です。どの内部型をストリッパーに露出させるかを理解せずに`link.xml`を削除または変更しないでください。

---

## [Preserve]属性の使用

Runtimeソースを検索しても、個々のメンバーに`[UnityEngine.Scripting.Preserve]`属性が適用されていません。選択されたアプローチは`link.xml`によるアセンブリレベルの保持であり、メンバーごとの属性ではありません。これは意図的です：

- メンバーごとの`[Preserve]`は、将来の追加を含むすべてのプールされたクラスに個別にアノテーションする必要があります。
- `link.xml`のアセンブリレベルの`preserve="all"`はよりシンプルで、エラーが少なく、将来のバージョンで追加される型をカバーすることが保証されています。

パッケージにバンドルされたものではなく、より大きな`link.xml`ファイルにValkarn Tasksを統合する必要がある場合、同等のディレクティブは：

```xml
<assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
```

---

## IL2CPPファーストプール設計

オブジェクトプール（`ValkarnTaskPool<T>`）のドキュメントコメントには明示的な注記があります：

> IL2CPPファースト: メインスレッド操作はアトミック演算ゼロ。

プールは2つのアクセスパスを使用します：

- **ファストパス（メインスレッド）:** 単一の`fastItem`フィールドをプレーンフィールドアクセスで読み書き — `Volatile`や`Interlocked`操作なし。これにより、IL2CPPが常に最適化できるとは限らないメインスレッドでのアトミック操作のオーバーヘッドを回避します。
- **オーバーフローパス（任意のスレッド）:** バックグラウンドスレッドからの並行アクセス（`ValkarnTask.RunOnThreadPool`など）での正確性のために`Interlocked.CompareExchange`を使用するTreiberロックフリースタック。

メインスレッドのIDは`ValkarnTaskPoolShared.MainThreadId`の`volatile int`フィールドに格納され、起動時に`RuntimeInitializeOnLoadMethod`を通じて一度だけ発行されます。IL2CPPは`volatile`フィールドを正しく処理します。

---

## コンソールプラットフォームの考慮事項

コンソールプラットフォーム（PlayStation、Xbox、Nintendo Switch）はIL2CPPのみを使用します。以下が適用されます：

- `link.xml`のカバレッジは他のIL2CPPターゲットと同じです。コンソール固有の追加保持は不要です。
- `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`はUnityがコンソール認定のためにサポートするすべてのプラットフォームで正しく発火します。
- `Interlocked`と`Volatile`操作はすべてのコンソールIL2CPPターゲットでサポートされています。プールのTreiberスタックは安全です。
- 構造体`TStateMachine`インスタンス化のジェネリック共有はコンソールでも適用されます。各`async ValkarnTask`メソッドは独自のネイティブ型を生成します — これは期待される動作であり正しく処理されます。
- コンソールプラットフォームが追加のAOT要件を強制する場合、ビルドログで「Stripping assembly: UnaPartidaMas.Valkarn.Tasks」を確認して`link.xml`が正しく取得されているかを確認してください。アセンブリがストリップされている場合、`link.xml`が発見されていません。

---

## ストリップされていないことの確認

IL2CPPターゲット向けのビルド後：

1. **ビルドログを確認。** Unityはストリッピングの決定を出力します。`UnaPartidaMas.Valkarn.Tasks`を検索してください。内部Valkarn型の`Stripping class`メッセージが見られる場合、`link.xml`が適用されていません。

2. **スモークテストを実行。** `ValkarnTask.Delay`、`WhenAll`、カスタム`ValkarnTaskCompletionSource`を実行する最小限のテストは、最も頻繁にストリップされる型をインスタンス化します。これら3つのシナリオがビルドを通過すれば、コアは無傷です。

3. **「Strip Engine Code」を選択的に有効化。** 高ストリッピングを使用する必要があり、バンドルされた`link.xml`を使用できない場合は、インクリメンタルにストリッピングを有効にして、各ストリッピングレベルの増加後にテストを実行してください。

4. **開発ビルドを有効にしてIL2CPPビルド。** 開発ビルドには追加の診断が含まれます。型が見つからない場合、ランタイムは`TypeInitializationException`または見つからない型の呼び出し元でのnull参照を報告します。スタックトレースを上記の「内部型」セクションに記載されている型と照合してください。

---

## まとめチェックリスト

- `Runtime/link.xml`がパッケージに存在する — 誤って削除されていないことを確認。
- マネージドストリッピングレベルはどのレベルにも設定できる；高がサポートされている。
- アプリケーションコードに`[Preserve]`属性を追加する必要はない。
- AOTヒント属性やコード登録呼び出しは不要。
- コンソールビルドは同じ`link.xml`を使用；プラットフォーム固有の追加は不要。
- ソースをフォークする場合は`link.xml`も一緒に持っていくこと。
