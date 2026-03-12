// Japanese (ja) — locale page data
const data = {
  hero: {
    pill:  'Unity 2023.1以降 · インディー向け無料',
    h1:    '非同期/待機を',
    h1Grad:'ガベージなしで。',
    p1:    '構造体ベースのタスク。ソース生成によるキャンセル。',
    p2:    'Burst & ECS対応。',
    em:    '正常系パスでアロケーションゼロ。',
    cta:   'はじめる →',
  },

  stats: [
    { end: 0,  suffix: '',  label: 'アロケーション\n正常系パス' },
    { end: 16, suffix: '',  label: 'PlayerLoop\nタイミング' },
    { end: 17, suffix: '',  label: 'アナライザー\nルール' },
    { end: 5,  suffix: 'x', label: 'プール高速化\nvs UniTask' },
  ],

  featuresSection: {
    heading: 'Unityの実行モデルのために構築。',
    sub:     '.NETパターンの移植ではなく、すべての判断がUnityファースト。',
  },
  features: [
    { icon: '⚡', title: 'ゼロアロケーション',        desc: '構造体ベースのVlkTaskはすべての成功パスでヒープ負荷を回避します。完了済みタスクはコストゼロです。' },
    { icon: '🔄', title: 'Destroy時の自動キャンセル', desc: 'クラスをpartialとして宣言するだけ。ソースジェネレーターがキャンセルをMonoBehaviourのライフサイクルに紐付けます。' },
    { icon: '🧵', title: 'スレッド対応プール',         desc: 'メインスレッドではロックフリーCAS、ワーカースレッドではTreiberスタック。不要なアトミック操作なし。' },
    { icon: '🎯', title: '16のPlayerLoopタイミング',  desc: 'InitializationからTimeUpdateまで — コンティニュエーションが再開するタイミングを精密に制御できます。' },
    { icon: '📡', title: '非同期チャネル',              desc: '有界・無界のプロデューサー/コンシューマーキュー。WriteAsync、ReadAsync、TryRead — ゼロアロケーション。' },
    { icon: '🚀', title: 'Burst & ECS対応',             desc: 'NativeTimerHeap、BurstSchedulerRunner、非同期ECSシステム。Unity DOTSの一級サポート。' },
    { icon: '🔍', title: '17のアナライザールール',       desc: 'ゾンビループ、混在ライフタイム、未待機タスク — コンパイル時に検出。本番環境ではなく。' },
    { icon: '🛡️', title: 'IL2CPP安全',                  desc: '明示的ジェネリクス、実行時リフレクションなし、link.xmlストリッピング保護。コンソール向けに出荷可能。' },
  ],

  comparisonSection: {
    heading:    '機能比較。',
    sub:        '🟢 = 行内最優 · ✦ = Valkarn Tasks独自 · ⓘ = ホバーで詳細',
    featureCol: '機能',
  },

  rows: [
    {
      feature: '成功時のアロケーション',
      sub: '完了済みタスクのawaitでアロケーションが発生するか？',
      cols: { task: 'あり — Task<T>はクラス', unitask: 'なし（構造体）', awaitable: 'なし（構造体）', valkarn: 'なし（構造体）' },
      win: ['unitask', 'awaitable', 'valkarn'],
      note: { task: 'Task<T>を返す非同期メソッドは、同期的に返す場合でも常にヒープオブジェクトをアロケーションします — 60 Hzゲームループにおける恒常的なGCコストです。' },
    },
    {
      feature: '失敗時のアロケーション',
      sub: '例外/キャンセルのパス',
      cols: { task: 'あり', unitask: 'あり', awaitable: 'あり', valkarn: 'あり' },
      win: [],
      note: { unitask: 'UniTaskは「〜ゼロアロケーション」を謳っています — チルダが重要です。例外とキャンセルは他と同様にアロケーションが発生します。' },
    },
    {
      feature: 'Destroy時の自動キャンセル',
      sub: 'MonoBehaviourのライフタイムに紐付け',
      cols: { task: '手動', unitask: '手動', awaitable: '手動', valkarn: 'ソース生成 ✦' },
      win: ['valkarn'],
      note: { valkarn: 'クラスをpartialとして宣言するだけ。ソースジェネレーターがCancellationTokenをOnDestroyに紐付けます — ボイラープレートフィールドなし、OnEnable/OnDisableなし、キャンセル忘れなし。' },
    },
    {
      feature: 'PlayerLoopタイミング',
      sub: 'スケジューリングの精度',
      cols: { task: '1（ThreadPool）', unitask: '16', awaitable: '6', valkarn: '16' },
      win: ['unitask', 'valkarn'],
      note: {
        task:     'コンティニュエーションは.NET ThreadPool上で実行されます。メインスレッドへの復帰にはUnitySynchronizationContext経由の明示的なマーシャリングが必要で、エラーが起きやすく冗長です。',
        unitask:  'UniTaskとValkarn Tasksはどちらも、InitializationからTimeUpdateまで16のPlayerLoopタイミングのフルセットを実装しています。',
        awaitable:'Awaitableが公開するフックは6つのみです：NextFrameAsync、FixedUpdateAsync、EndOfFrameAsync、WaitForSecondsAsync、MainThreadAsync、BackgroundThreadAsync — PlayerLoopの全体ではありません。',
      },
    },
    {
      feature: 'ECS / Entitiesの互換性',
      sub: 'Unity DOTSとの併用',
      cols: { task: 'N/A', unitask: '⚠ 破損', awaitable: '部分対応', valkarn: '完全対応 ✦' },
      win: ['valkarn'],
      note: {
        unitask:  "UnityのEntitiesパッケージは初期化時にPlayerLoopをリセットし、UniTaskの登録済みランナーをクリアします。その時点でスケジュールされたタスクは無音で失われます。",
        awaitable:'AwaitableはECSシステムで動作しますが、NativeTimerHeap、BurstSchedulerRunner、非同期ECSシステムヘルパーはありません。',
        valkarn:  '完全なDOTSサポート：Burst互換スケジューリング用NativeTimerHeap、BurstSchedulerRunner、ECSシステム用AsyncSystemUtilities、JobHandleブリッジ。',
      },
    },
    {
      feature: 'ダブルawait安全性',
      sub: '同じタスクを二度awaitする',
      cols: { task: '✓ 安全', unitask: '✗ 未定義', awaitable: '✗ デッドロック', valkarn: '✓ 安全' },
      win: ['task', 'valkarn'],
      note: {
        unitask:  'UniTaskを複数回awaitすることは明示的に未定義動作です。実際にはデッドロックやプール状態の破損を引き起こします。',
        awaitable:'Awaitableを二度awaitすると、既に完了しているかどうかによってデッドロックまたは例外が発生します。ガードはありません。',
        valkarn:  'すべてのソースパスにダブルリターンガードを実装。完了済みタスクの2回目のawaitは即座にキャッシュされた結果を返します。',
      },
    },
    {
      feature: 'コンパイル時診断',
      sub: 'Roslynアナライザールール',
      cols: { task: '0ルール', unitask: '1ルール', awaitable: '0ルール', valkarn: '17ルール ✦' },
      win: ['valkarn'],
      note: {
        unitask: 'UniTaskには1つのルール（UNITASK001）があります：UniTaskの戻り値のawait忘れを警告します。ゾンビループ、混在ライフタイム、構造的なバグの検出はありません。',
        valkarn: 'ゾンビループ、混在ライフタイム、未待機タスク、ダブルawait、不適切なfire-and-forget、auto-cancelマーカー忘れを — ビルドが出荷される前に検出します。',
      },
    },
    {
      feature: 'スレッド対応プール',
      sub: 'メインスレッドでロックフリー',
      cols: { task: 'N/A', unitask: 'Interlocked（全スレッド）', awaitable: 'N/A', valkarn: 'CAS / Treiber ✦' },
      win: ['valkarn'],
      note: {
        unitask: "UniTaskのプールはすべてのPushとPopでInterlocked.CompareExchangeを使用します — 実際には競合がないメインスレッドでも同様です。タスク完了のたびに不要なアトミックオーバーヘッドが発生します。",
        valkarn: "メインスレッドではロックフリーCAS（不要なアトミックオーバーヘッドなし）。バックグラウンドスレッドにはTreiberスタック。各コンテキストに適切なアルゴリズムを使用。",
      },
    },
    {
      feature: '非同期チャネル',
      sub: '組み込みプロデューサー/コンシューマー',
      cols: { task: 'BCL（クラスベース）', unitask: '無界のみ', awaitable: '✗', valkarn: '有界 + 無界 ✦' },
      win: ['valkarn'],
      note: {
        task:    "System.Threading.Channelsはクラスベースでアロケーションがあります。UnityのPlayerLoopとは統合されておらず、コンティニュエーションはThreadPool上で実行されます。",
        unitask: 'UniTaskには無界のシングルコンシューマーチャネルのみがあります。容量制限なし、バックプレッシャーなし。読み取りはゼロアロケーションですが、APIが限られています。',
        valkarn: '有界（容量とバックプレッシャーあり）と無界の両チャネル。WriteAsync、ReadAsync、TryRead、TryWrite、TryPeek — すべて正常系パスでゼロアロケーション。',
      },
    },
    {
      feature: 'WhenAll / WhenAnyコンビネーター',
      sub: '並行タスクの協調',
      cols: { task: 'Task[]を返す', unitask: '✓（配列のみ）', awaitable: '✗', valkarn: '最大8のタプル ✦' },
      win: ['unitask', 'valkarn'],
      note: {
        task:    'Task.WhenAllはTask<T[]>を返し、インデックスベースのアクセスが必要です。タプルの分解はできません。',
        unitask: 'UniTask.WhenAllはアリティ15までの型付きタプルをサポートしています。強力な機能です。',
        valkarn: 'var (tex, sfx, data) = await VlkTask.WhenAll(...) — 最大8つの型付き結果。WhenAnyは最初に完了した結果とそのインデックスを返します。',
      },
    },
    {
      feature: '例外の暗黙的な握りつぶし',
      sub: 'async void / fire-and-forgetにおける未処理エラー',
      cols: { task: 'AppDomain.UnhandledException', unitask: 'UniTaskScheduler.UnobservedTaskException', awaitable: '⚠ Unity 6のバグ', valkarn: '設定可能なハンドラー ✦' },
      win: ['valkarn'],
      note: {
        awaitable:'Unity 6には確認済みのバグがあり、Awaitableコンティニュエーション内でスローされた例外がログ出力なしに暗黙的に握りつぶされます。6000.0.5で修正済み — それ以前の6.xリリースが影響を受けます。',
        valkarn:  'VlkTaskSettings.UnobservedExceptionHandlerはユーザーが設定可能です。デフォルトはDebug.LogExceptionへのログ記録なので、例外が暗黙的に失われることはありません。',
      },
    },
  ],

  versusSection: {
    heading: 'なぜ切り替えるのか？',
    sub:     '具体的な理由。マーケティングコピーではありません。',
  },

  versus: [
    {
      vs: 'System.Task',
      color: '#ef4444',
      problems: [
        { title: 'すべてのawaitでアロケーション',  body: "Task<T>はクラスです。すべての非同期メソッドは、同期的に返す場合でもヒープオブジェクトをアロケーションします。数千の非同期操作を実行する60 Hzゲームループでは、フレームごとにGCコストを支払い続けることになります。" },
        { title: 'デフォルトでThreadPool',           body: "コンティニュエーションはUnityのメインスレッドではなく.NET ThreadPool上で再開されます。GameObjects、Transforms、Unity APIとのやり取りにはUnitySynchronizationContext経由の明示的なマーシャリングが必要で、エラーが起きやすく冗長です。" },
        { title: 'MonoBehaviourとの紐付けなし',      body: "TaskはMonoBehaviourが破棄された後も継続するのを何も防いでくれません。その結果、ファントムのnull参照例外、状態の破損、シーンがアンロードされる際にのみ現れるバグが発生します。" },
        { title: 'Unity診断なし',                    body: ".NETランタイムはUnityのライフタイムモデルを知りません。ゲームを破壊するパターンを検出するRoslynルールはゼロです：ゾンビループ、破棄済みオブジェクトへのアクセス、fire-and-forgetの誤用。" },
      ],
      verdict: 'System.Taskは.NETサーバーに適したツールです。リアルタイムゲームループには適していません。',
    },
    {
      vs: 'UniTask',
      color: '#f59e0b',
      problems: [
        { title: '〜ゼロはゼロではない',                    body: 'UniTaskのキャッチフレーズは「〜ゼロアロケーション」です。チルダが重要です。例外パスとキャンセルは他と同様にアロケーションが発生します。Valkarn Tasksは同じトレードオフを正直に示し、さらに多くの保証を追加します。' },
        { title: '常に手動キャンセル',                      body: 'UniTaskをMonoBehaviourに紐付けるには：CancellationTokenSourceフィールド、OnEnableでの（再）初期化、OnDestroyでのキャンセルとdispose、そしてすべての非同期呼び出しへのトークンの受け渡しが必要です。Valkarn Tasksは単一の[AutoCancel]属性からこのパターン全体をソース生成します。' },
        { title: 'Unity Entitiesで破損',                    body: "UnityのEntitiesパッケージは初期化時にPlayerLoop.SetPlayerLoop()を呼び出し、UniTaskの登録済みランナーを上書きします。その時点で飛行中のタスクは暗黙的に破棄されます。警告はありません。Valkarn TasksのECS統合はPlayerLoopリセットを乗り越えるよう特別に設計されています。" },
        { title: 'メインスレッドでのInterlocked',            body: "UniTaskのプールは、実際には競合がないメインスレッドでも、すべてのPushとPopでInterlocked.CompareExchangeを呼び出します。これはゲームの最も重要なコードパスで不要なアトミック操作です。Valkarn TasksはメインスレッドでプレーンなCASを使用し、ワーカースレッドにはTreiberスタックを使用します。" },
        { title: '1つのアナライザールール、構造チェックなし', body: 'UniTaskにはUNITASK001があります：await忘れの検出だけです。ゾンビループ（キャンセルがチェックされないため決して終了しないループ）、混在ライフタイムタスク、ダブルawait — これらはどれも検出されません。Valkarn Tasksはこれらの構造的パターンをカバーする17のルールを搭載しています。' },
        { title: '最終リリース：2024年10月',                 body: "UniTaskのGitHubは2024年10月以降、アクティブな開発を示していません。Unity 6、DOTS 1.x、および将来のエディターバージョンは、メンテナンスされていないパッケージが追跡できない破壊的変更をもたらします。" },
      ],
      verdict: 'UniTaskは2020年の最先端でした。Valkarn Tasksは2025年以降のために構築されています。',
    },
    {
      vs: 'Awaitable',
      color: '#3b82f6',
      problems: [
        { title: '16ではなく6のタイミング',              body: "UnityのAwaitableが公開するスケジューリングフックは6つです：NextFrameAsync、FixedUpdateAsync、EndOfFrameAsync、WaitForSecondsAsync、MainThreadAsync、BackgroundThreadAsync。Valkarn TasksはUnityの16のPlayerLoopフェーズのすべてをファーストクラスのawaitポイントとしてマッピングします。" },
        { title: '同じタスクを二度awaitできない',          body: "既に完了しているAwaitableをawaitすると、内部状態によってデッドロックまたは例外が発生します。ガードはありません。Valkarn Tasksはすべてのソースパスにダブルリターン保護があります：2回目のawaitは常にキャッシュされた結果を即座に返します。" },
        { title: 'Unity 6は例外を暗黙的に握りつぶす',    body: '確認済みのUnity 6バグにより、Awaitableコンティニュエーション内でスローされた例外がログ出力なし、スタックトレースなし、クラッシュなしで消えてしまいます。6000.0.5で修正済み — Unity 6.0から6.0.4のすべてのプロジェクトが影響を受けます。Valkarn Tasksはすべての未処理例外を、Debug.LogExceptionをデフォルトとする設定可能なハンドラーを通じてルーティングします。' },
        { title: 'WhenAll、WhenAny、チャネルなし',         body: 'AwaitableにはコンビネーターのAPIがありません。3つのロードを並行して実行してその結果を収集するには手動のステートマシンが必要です。Valkarn Tasksはアリティ8までのタプル分解を持つWhenAll、WhenAny、有界・無界の非同期チャネルを提供します。' },
        { title: 'ライフサイクル紐付けなし',               body: "AwaitableはタスクのライフタイムをGameObjectに紐付けるメカニズムを提供しません。すべてのキャンセルトークンを手動で作成、保存、呼び出しに渡し、disposeする必要があります。" },
      ],
      verdict: 'Awaitableは薄いスケジューリングフックです。Valkarn Tasksは完全な非同期ランタイムです。',
    },
  ],

  cta: {
    heading:    '出荷を速く。アロケーションを少なく。',
    p1:         '個人および年間収益100万ドル未満のスタジオは無料。',
    p2:         'マニフェストに1行追記するだけ。アカウント不要。',
    btnPrimary: 'はじめる →',
    btnGhost:   'ライセンスを見る',
  },
};

export default data;
