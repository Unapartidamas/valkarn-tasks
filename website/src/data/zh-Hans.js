// 简体中文 — zh-Hans 本地化页面数据
const data = {
  hero: {
    pill:  'Unity 2023.1+ · 独立开发者免费',
    h1:    '异步/等待',
    h1Grad:'零垃圾回收。',
    p1:    '基于结构体的任务。源码生成的取消机制。',
    p2:    'Burst 与 ECS 就绪。',
    em:    '快速路径零内存分配。',
    cta:   '立即开始 →',
  },

  stats: [
    { end: 0,  suffix: '',  label: '内存分配\n快速路径' },
    { end: 16, suffix: '',  label: 'PlayerLoop\n时机点' },
    { end: 17, suffix: '',  label: '分析器\n规则' },
    { end: 5,  suffix: 'x', label: '对象池更快\n对比 UniTask' },
  ],

  featuresSection: {
    heading: '专为 Unity 执行模型而生。',
    sub:     '不是 .NET 模式的移植版本——每一个决策都以 Unity 为优先。',
  },
  features: [
    { icon: '⚡', title: '零内存分配',        desc: '基于结构体的 VlkTask 在每条成功路径上均无堆内存压力。已完成的任务完全免费。' },
    { icon: '🔄', title: '销毁时自动取消', desc: '将类声明为 partial，源码生成器即可将取消绑定到 MonoBehaviour 的生命周期。' },
    { icon: '🧵', title: '线程感知对象池',       desc: '主线程使用无锁 CAS，工作线程使用 Treiber 栈。无不必要的原子操作。' },
    { icon: '🎯', title: '16 个 PlayerLoop 时机点',   desc: '从 Initialization 到 TimeUpdate——精确控制续体在何时恢复。' },
    { icon: '📡', title: '异步通道',           desc: '有界与无界的生产者/消费者队列。WriteAsync、ReadAsync、TryRead——快速路径零分配。' },
    { icon: '🚀', title: 'Burst 与 ECS 就绪',        desc: 'NativeTimerHeap、BurstSchedulerRunner、异步 ECS 系统。对 Unity DOTS 的一级支持。' },
    { icon: '🔍', title: '17 条分析器规则',        desc: '僵尸循环、混合生命周期、未等待任务——在编译时捕获，而非在生产环境中暴露。' },
    { icon: '🛡️', title: 'IL2CPP 安全',              desc: '显式泛型、无运行时反射、link.xml 剥离保护。可发布到主机平台。' },
  ],

  comparisonSection: {
    heading:    '功能对比。',
    sub:        '🟢 = 当行最佳 · ✦ = Valkarn Tasks 独有 · ⓘ = 悬停查看详情',
    featureCol: '功能',
  },

  rows: [
    {
      feature: '成功路径内存分配',
      sub: '等待一个已完成的任务是否会分配内存？',
      cols: { task: '是——Task<T> 是引用类型', unitask: '否（结构体）', awaitable: '否（结构体）', valkarn: '否（结构体）' },
      win: ['unitask', 'awaitable', 'valkarn'],
      note: { task: '每个返回 Task<T> 的异步方法都会在堆上分配一个对象，即使它同步返回也不例外——在 60Hz 的游戏循环中，这是一笔持续的 GC 税。' },
    },
    {
      feature: '失败路径内存分配',
      sub: '异常 / 取消路径',
      cols: { task: '是', unitask: '是', awaitable: '是', valkarn: '是' },
      win: [],
      note: { unitask: 'UniTask 宣称"约零分配"——那个"约"字至关重要。异常和取消路径仍然会分配，与其他所有方案相同。' },
    },
    {
      feature: '销毁时自动取消',
      sub: '绑定到 MonoBehaviour 生命周期',
      cols: { task: '手动', unitask: '手动', awaitable: '手动', valkarn: '源码生成 ✦' },
      win: ['valkarn'],
      note: { valkarn: '将类声明为 partial。源码生成器会将 CancellationToken 连接到 OnDestroy——无需样板字段、无需 OnEnable/OnDisable、不会遗忘取消。' },
    },
    {
      feature: 'PlayerLoop 时机点',
      sub: '调度精度',
      cols: { task: '1 个（线程池）', unitask: '16 个', awaitable: '6 个', valkarn: '16 个' },
      win: ['unitask', 'valkarn'],
      note: {
        task:     '续体在 .NET 线程池上运行。切换回主线程需要通过 UnitySynchronizationContext 进行显式编组——既容易出错又繁琐。',
        unitask:  'UniTask 和 Valkarn Tasks 均实现了完整的 16 个 PlayerLoop 时机点，从 Initialization 到 TimeUpdate。',
        awaitable:'Awaitable 提供 6 个钩子：NextFrameAsync、FixedUpdateAsync、EndOfFrameAsync、WaitForSecondsAsync、MainThreadAsync、BackgroundThreadAsync——并非完整的 PlayerLoop。',
      },
    },
    {
      feature: 'ECS / Entities 兼容性',
      sub: '与 Unity DOTS 协同工作',
      cols: { task: '不适用', unitask: '⚠ 会损坏', awaitable: '部分支持', valkarn: '完全支持 ✦' },
      win: ['valkarn'],
      note: {
        unitask:  'Unity 的 Entities 包在初始化时会重置 PlayerLoop，这会清除 UniTask 已注册的运行器。在此之前调度的任何任务都会被静默丢失。',
        awaitable:'Awaitable 可以在 ECS 系统中工作，但没有 NativeTimerHeap、没有 BurstSchedulerRunner，也没有异步 ECS 系统辅助工具。',
        valkarn:  '完整 DOTS 支持：用于 Burst 兼容调度的 NativeTimerHeap、BurstSchedulerRunner、用于 ECS 系统的 AsyncSystemUtilities、JobHandle 桥接。',
      },
    },
    {
      feature: '重复等待安全性',
      sub: '对同一任务等待两次',
      cols: { task: '✓ 安全', unitask: '✗ 未定义', awaitable: '✗ 死锁', valkarn: '✓ 安全' },
      win: ['task', 'valkarn'],
      note: {
        unitask:  '对 UniTask 等待超过一次属于明确的未定义行为。实际上会导致死锁或破坏对象池状态。',
        awaitable:'对已完成的 Awaitable 等待两次会死锁或抛出异常，取决于其内部状态。没有任何保护措施。',
        valkarn:  '所有源路径均有双重返回防护。对已完成任务的第二次等待会立即返回缓存结果。',
      },
    },
    {
      feature: '编译时诊断',
      sub: 'Roslyn 分析器规则',
      cols: { task: '0 条规则', unitask: '1 条规则', awaitable: '0 条规则', valkarn: '17 条规则 ✦' },
      win: ['valkarn'],
      note: {
        unitask: 'UniTask 附带一条规则（UNITASK001）：当你忘记 await UniTask 返回值时发出警告。无法检测僵尸循环、混合生命周期或结构性 bug。',
        valkarn: '可捕获僵尸循环、混合生命周期、未等待任务、重复等待、不当的即发即弃用法、缺失的自动取消标记——在构建发布之前。',
      },
    },
    {
      feature: '线程感知对象池',
      sub: '主线程无锁',
      cols: { task: '不适用', unitask: 'Interlocked（所有线程）', awaitable: '不适用', valkarn: 'CAS / Treiber ✦' },
      win: ['valkarn'],
      note: {
        unitask: 'UniTask 的对象池在每次 Push 和 Pop 时都使用 Interlocked.CompareExchange——包括主线程，而主线程根本不存在真正的竞争。在每次任务完成时都有不必要的原子开销。',
        valkarn: '主线程使用无锁 CAS（无需原子操作的地方不产生额外开销）。后台线程使用 Treiber 栈。每种上下文都获得正确的算法。',
      },
    },
    {
      feature: '异步通道',
      sub: '内置生产者/消费者',
      cols: { task: 'BCL（基于引用类型）', unitask: '仅无界通道', awaitable: '✗', valkarn: '有界 + 无界 ✦' },
      win: ['valkarn'],
      note: {
        task:    'System.Threading.Channels 基于引用类型且会分配内存。未与 Unity 的 PlayerLoop 集成——续体在线程池上运行。',
        unitask: 'UniTask 仅提供无界单消费者通道。无有界容量，无背压。读取零分配，但 API 有限。',
        valkarn: '同时提供有界（含容量和背压）和无界通道。WriteAsync、ReadAsync、TryRead、TryWrite、TryPeek——快速路径全部零分配。',
      },
    },
    {
      feature: 'WhenAll / WhenAny 组合器',
      sub: '并行任务协调',
      cols: { task: '返回 Task[]', unitask: '✓（仅数组）', awaitable: '✗', valkarn: '元组最多 8 个 ✦' },
      win: ['unitask', 'valkarn'],
      note: {
        task:    'Task.WhenAll 返回 Task<T[]>，需要基于索引的访问。不支持元组解构。',
        unitask: 'UniTask.WhenAll 支持最多 15 个类型化元组。这是一个强大的功能。',
        valkarn: 'var (tex, sfx, data) = await VlkTask.WhenAll(...)——最多 8 个类型化结果。WhenAny 返回第一个完成的结果及其索引。',
      },
    },
    {
      feature: '异常静默吞噬',
      sub: 'async void / 即发即弃中的未处理错误',
      cols: { task: 'AppDomain.UnhandledException', unitask: 'UniTaskScheduler.UnobservedTaskException', awaitable: '⚠ Unity 6 bug', valkarn: '可配置处理器 ✦' },
      win: ['valkarn'],
      note: {
        awaitable:'Unity 6 存在一个已确认的 bug，导致在 Awaitable 续体中抛出的异常被静默吞噬，没有任何日志输出。已在 Unity 6000.0.5 中修复——较早的 6.x 版本均受影响。',
        valkarn:  'VlkTaskSettings.UnobservedExceptionHandler 可由用户配置。默认值：记录到 Debug.LogException，确保任何异常都不会被静默。',
      },
    },
  ],

  versusSection: {
    heading: '为什么要切换？',
    sub:     '具体原因，而非营销话术。',
  },

  versus: [
    {
      vs: 'System.Task',
      color: '#ef4444',
      problems: [
        { title: '每次等待都会分配内存',      body: 'Task<T> 是引用类型。每个异步方法都会在堆上分配一个对象，即使它同步返回也不例外。在以 60Hz 运行、每帧执行数千次异步操作的游戏循环中，你在每一帧都在缴纳 GC 税。' },
        { title: '默认在线程池上运行',      body: '续体在 .NET 线程池上恢复，而非 Unity 的主线程。与 GameObject、Transform 或 Unity API 的任何交互都需要通过 UnitySynchronizationContext 进行显式编组——既容易出错又繁琐。' },
        { title: '不绑定 MonoBehaviour 生命周期',   body: '没有任何机制阻止 Task 在其所属 MonoBehaviour 被销毁后继续运行。结果是幽灵般的空引用异常、状态损坏，以及只有在场景卸载时才会出现的 bug。' },
        { title: '没有 Unity 诊断',       body: '.NET 运行时没有 Unity 生命周期模型的概念。零条 Roslyn 规则能捕获那些会破坏游戏的模式：僵尸循环、已销毁对象访问、即发即弃误用。' },
      ],
      verdict: 'System.Task 是适合 .NET 服务器的正确工具。它是实时游戏循环的错误工具。',
    },
    {
      vs: 'UniTask',
      color: '#f59e0b',
      problems: [
        { title: '"约零"不等于零',                      body: 'UniTask 的主打宣传是"约零分配"。那个"约"字是关键。异常路径和取消路径仍然会分配——与其他所有方案相同。Valkarn Tasks 诚实地做出同样的取舍，并在此基础上提供更多保证。' },
        { title: '始终需要手动取消',           body: '将 UniTask 绑定到 MonoBehaviour 需要：一个 CancellationTokenSource 字段、一个 OnEnable 来（重新）初始化它、一个 OnDestroy 来取消并释放它，以及将 token 贯穿每次异步调用。Valkarn Tasks 通过单个 [AutoCancel] 特性源码生成整个模式。' },
        { title: '与 Unity Entities 不兼容',             body: 'Unity 的 Entities 包在初始化时调用 PlayerLoop.SetPlayerLoop()，这会覆盖 UniTask 已注册的运行器。此时正在运行的任何任务都会被静默丢弃。没有任何警告。Valkarn Tasks 的 ECS 集成专门设计为能在 PlayerLoop 重置后存活。' },
        { title: '主线程上的 Interlocked 操作',         body: 'UniTask 的对象池在每次 Push 和 Pop 时都调用 Interlocked.CompareExchange——包括主线程，而主线程根本不存在真正的竞争。这些是游戏中最热门代码路径上不必要的原子操作。Valkarn Tasks 在主线程上使用普通 CAS，在工作线程上使用 Treiber 栈。' },
        { title: '一条分析器规则，无结构性检查',body: 'UniTask 附带 UNITASK001：忘记等待检测。仅此而已。僵尸循环（因从未检查取消而永不退出的循环）、混合生命周期任务、重复等待——这些都不会被捕获。Valkarn Tasks 附带 17 条覆盖这些结构性模式的规则。' },
        { title: '最后一次发布：2024 年 10 月',             body: 'UniTask 的 GitHub 自 2024 年 10 月以来没有活跃开发记录。Unity 6、DOTS 1.x 以及未来的编辑器版本带来了破坏性变更，一个无人维护的包无法持续跟进。' },
      ],
      verdict: 'UniTask 是 2020 年的业界标杆。Valkarn Tasks 是为 2025 年及未来而生的。',
    },
    {
      vs: 'Awaitable',
      color: '#3b82f6',
      problems: [
        { title: '6 个时机点，而非 16 个',                   body: 'Unity 的 Awaitable 提供 6 个调度钩子：NextFrameAsync、FixedUpdateAsync、EndOfFrameAsync、WaitForSecondsAsync、MainThreadAsync 和 BackgroundThreadAsync。Valkarn Tasks 将 Unity 的全部 16 个 PlayerLoop 阶段——PreUpdate、PostLateUpdate、TimeUpdate 等——作为一级 await 点。' },
        { title: '无法对同一任务等待两次',    body: '对已完成的 Awaitable 再次等待，要么死锁要么抛出异常，取决于内部状态。没有任何保护措施。Valkarn Tasks 在每条源路径上都有双重返回保护：第二次等待始终立即返回缓存结果。' },
        { title: 'Unity 6 会静默吞噬异常',body: '一个已确认的 Unity 6 bug 导致在 Awaitable 续体中抛出的异常消失，没有日志输出、没有堆栈跟踪、不会崩溃。已在 6000.0.5 中修复——意味着每一个 Unity 6.0 到 6.0.4 的项目都受此影响。Valkarn Tasks 将所有未观察到的异常路由到一个可配置的处理器，默认为 Debug.LogException。' },
        { title: '没有 WhenAll、WhenAny 或通道',    body: 'Awaitable 没有组合器 API。并行运行三个加载任务并收集结果需要手写状态机。Valkarn Tasks 提供支持最多 8 个参数元组解构的 WhenAll、WhenAny，以及有界和无界异步通道。' },
        { title: '没有生命周期绑定',                body: 'Awaitable 没有提供任何机制将任务的生命周期绑定到 GameObject。每个取消 token 都必须手动创建、存储、传递到每次调用中并在最后释放。' },
      ],
      verdict: 'Awaitable 是一个轻量级的调度钩子。Valkarn Tasks 是一个完整的异步运行时。',
    },
  ],

  cta: {
    heading:    '更快发布。更少分配。',
    p1:         '对年收入低于 100 万美元的个人和工作室免费。',
    p2:         '在 manifest 中添加一行即可。无需注册账号。',
    btnPrimary: '立即开始 →',
    btnGhost:   '查看许可',
  },
};

export default data;
