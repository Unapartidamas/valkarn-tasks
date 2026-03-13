// English — default locale page data
const data = {
  hero: {
    pill:  'Unity 2023.1+ · MIT · Free for everyone',
    h1:    'Async/await',
    h1Grad:'without garbage.',
    p1:    'Struct-based tasks. Source-generated cancellation.',
    p2:    'Burst & ECS ready.',
    em:    'Zero allocation on the happy path.',
    cta:   'Get Started →',
  },

  stats: [
    { end: 0,  suffix: '',  label: 'allocs\nhappy path' },
    { end: 16, suffix: '',  label: 'PlayerLoop\ntimings' },
    { end: 17, suffix: '',  label: 'analyzer\nrules' },
    { end: 5,  suffix: 'x', label: 'faster pool\nvs UniTask' },
  ],

  featuresSection: {
    heading: "Built for Unity's execution model.",
    sub:     "Not a port of .NET patterns — every decision is Unity-first.",
  },
  features: [
    { icon: '⚡', title: 'Zero Allocation',        desc: 'Struct-based ValkarnTask avoids heap pressure on every success path. Completed tasks are free.' },
    { icon: '🔄', title: 'Auto-Cancel on Destroy', desc: 'Mark the class partial. The source generator binds cancellation to the MonoBehaviour lifetime.' },
    { icon: '🧵', title: 'Thread-Aware Pool',       desc: 'Lock-free CAS on the main thread, Treiber stack on worker threads. No unnecessary atomics.' },
    { icon: '🎯', title: '16 PlayerLoop Timings',   desc: 'From Initialization to TimeUpdate — precise control over when continuations resume.' },
    { icon: '📡', title: 'Async Channels',           desc: 'Bounded and unbounded producer/consumer queues. WriteAsync, ReadAsync, TryRead — zero-alloc.' },
    { icon: '🚀', title: 'Burst & ECS Ready',        desc: 'NativeTimerHeap, BurstSchedulerRunner, async ECS systems. First-class Unity DOTS support.' },
    { icon: '🔍', title: '17 Analyzer Rules',        desc: 'Zombie loops, mixed lifetimes, unawaited tasks — caught at compile time, not in production.' },
    { icon: '🛡️', title: 'IL2CPP Safe',              desc: 'Explicit generics, no runtime reflection, link.xml stripping protection. Ships to consoles.' },
  ],

  comparisonSection: {
    heading:    'Feature comparison.',
    sub:        '🟢 = best in row · ✦ = unique to Valkarn Tasks · ⓘ = hover for detail',
    featureCol: 'Feature',
  },

  rows: [
    {
      feature: 'Allocation on success',
      sub: 'Does awaiting a completed task allocate?',
      cols: { task: 'Yes — Task<T> is a class', unitask: 'No (struct)', awaitable: 'No (struct)', valkarn: 'No (struct)' },
      win: ['unitask', 'awaitable', 'valkarn'],
      note: { task: 'Every async method returning Task<T> allocates a heap object, even when it returns synchronously — a constant GC tax in a 60 Hz game loop.' },
    },
    {
      feature: 'Allocation on failure',
      sub: 'Exception / cancellation paths',
      cols: { task: 'Yes', unitask: 'Yes', awaitable: 'Yes', valkarn: 'Yes' },
      win: [],
      note: { unitask: 'UniTask advertises "~Zero allocation" — the tilde matters. Exceptions and cancellations still allocate, same as everyone else.' },
    },
    {
      feature: 'Auto-cancel on Destroy',
      sub: 'Tied to MonoBehaviour lifetime',
      cols: { task: 'Manual', unitask: 'Manual', awaitable: 'Manual', valkarn: 'Source-gen ✦' },
      win: ['valkarn'],
      note: { valkarn: 'Mark the class partial. A source generator wires a CancellationToken to OnDestroy — no boilerplate field, no OnEnable/OnDisable, no forgotten cancellation.' },
    },
    {
      feature: 'PlayerLoop timings',
      sub: 'Scheduling precision',
      cols: { task: '1 (ThreadPool)', unitask: '16', awaitable: '6', valkarn: '16' },
      win: ['unitask', 'valkarn'],
      note: {
        task:     'Continuations run on the .NET ThreadPool. Moving back to the main thread requires explicit marshalling via UnitySynchronizationContext.',
        unitask:  'UniTask and Valkarn Tasks both implement the full set of 16 PlayerLoop timings, from Initialization through TimeUpdate.',
        awaitable:'Awaitable exposes 6 hooks: NextFrameAsync, FixedUpdateAsync, EndOfFrameAsync, WaitForSecondsAsync, MainThreadAsync, BackgroundThreadAsync — not the full PlayerLoop.',
      },
    },
    {
      feature: 'ECS / Entities compatibility',
      sub: 'Works alongside Unity DOTS',
      cols: { task: 'N/A', unitask: '⚠ Breaks', awaitable: 'Partial', valkarn: 'Full ✦' },
      win: ['valkarn'],
      note: {
        unitask:  "Unity's Entities package resets the PlayerLoop on initialization, which clears UniTask's registered runners. Any task scheduled before that point is silently lost.",
        awaitable:'Awaitable works in ECS systems but has no NativeTimerHeap, no BurstSchedulerRunner, and no async ECS system helpers.',
        valkarn:  'Full DOTS support: NativeTimerHeap for Burst-compatible scheduling, BurstSchedulerRunner, AsyncSystemUtilities for ECS systems, JobHandle bridge.',
      },
    },
    {
      feature: 'Double-await safety',
      sub: 'Awaiting the same task twice',
      cols: { task: '✓ Safe', unitask: '✗ Undefined', awaitable: '✗ Deadlock', valkarn: '✓ Safe' },
      win: ['task', 'valkarn'],
      note: {
        unitask:  'Awaiting a UniTask more than once is explicitly undefined behavior. In practice it causes a deadlock or corrupts pool state.',
        awaitable:'Awaiting an Awaitable twice deadlocks or throws, depending on whether it has already completed. There is no guard.',
        valkarn:  'Double-return guards on all source paths. The second await on a completed task returns the cached result immediately.',
      },
    },
    {
      feature: 'Compile-time diagnostics',
      sub: 'Roslyn analyzer rules',
      cols: { task: '0 rules', unitask: '1 rule', awaitable: '0 rules', valkarn: '17 rules ✦' },
      win: ['valkarn'],
      note: {
        unitask: 'UniTask ships one rule (UNITASK001): warns when you forget to await a UniTask return value. No detection of zombie loops, mixed lifetimes, or structural bugs.',
        valkarn: 'Catches zombie loops, mixed lifetimes, unawaited tasks, double-awaits, improper fire-and-forget, missing auto-cancel markers — before the build ships.',
      },
    },
    {
      feature: 'Thread-aware pool',
      sub: 'Lock-free on main thread',
      cols: { task: 'N/A', unitask: 'Interlocked (all threads)', awaitable: 'N/A', valkarn: 'CAS / Treiber ✦' },
      win: ['valkarn'],
      note: {
        unitask: "UniTask's pool uses Interlocked.CompareExchange on every Push and Pop — including the main thread where there is never any real contention. Unnecessary atomic overhead on every task completion.",
        valkarn: "Lock-free CAS on the main thread (no atomic overhead where it's not needed). Treiber stack for background threads. Each context gets the correct algorithm.",
      },
    },
    {
      feature: 'Async channels',
      sub: 'Built-in producer/consumer',
      cols: { task: 'BCL (class-based)', unitask: 'Unbounded only', awaitable: '✗', valkarn: 'Bounded + Unbounded ✦' },
      win: ['valkarn'],
      note: {
        task:    "System.Threading.Channels is class-based and allocates. Not integrated with Unity's PlayerLoop — continuations run on the ThreadPool.",
        unitask: 'UniTask ships an unbounded single-consumer channel only. No bounded capacity, no backpressure. Zero-alloc on reads, but limited API.',
        valkarn: 'Both bounded (with capacity and backpressure) and unbounded channels. WriteAsync, ReadAsync, TryRead, TryWrite, TryPeek — all zero-alloc on the fast path.',
      },
    },
    {
      feature: 'WhenAll / WhenAny combinators',
      sub: 'Parallel task coordination',
      cols: { task: 'Returns Task[]', unitask: '✓ (arrays only)', awaitable: '✗', valkarn: 'Tuple up to 8 ✦' },
      win: ['unitask', 'valkarn'],
      note: {
        task:    'Task.WhenAll returns Task<T[]>, requiring index-based access. No tuple destructuring.',
        unitask: 'UniTask.WhenAll supports typed tuples up to arity 15. A strong feature.',
        valkarn: 'var (tex, sfx, data) = await ValkarnTask.WhenAll(...) — up to 8 typed results. WhenAny returns the first completed result with its index.',
      },
    },
    {
      feature: 'Silent exception swallowing',
      sub: 'Unhandled errors in async void / fire-and-forget',
      cols: { task: 'AppDomain.UnhandledException', unitask: 'UniTaskScheduler.UnobservedTaskException', awaitable: '⚠ Unity 6 bug', valkarn: 'Configurable handler ✦' },
      win: ['valkarn'],
      note: {
        awaitable:'Unity 6 had a confirmed bug where exceptions thrown inside Awaitable continuations were silently swallowed with no log output. Fixed in Unity 6000.0.5 — earlier 6.x releases are affected.',
        valkarn:  'ValkarnTaskSettings.UnobservedExceptionHandler is user-configurable. Default: logs to Debug.LogException so no exception is ever silent.',
      },
    },
  ],

  versusSection: {
    heading: 'Why make the switch?',
    sub:     'Concrete reasons, not marketing copy.',
  },

  versus: [
    {
      vs: 'System.Task',
      color: '#ef4444',
      problems: [
        { title: 'Every await allocates',      body: "Task<T> is a class. Every async method allocates a heap object, even when it returns synchronously. In a 60 Hz game loop that runs thousands of async operations, you are paying a GC tax on every single frame." },
        { title: 'ThreadPool by default',      body: "Continuations resume on the .NET ThreadPool, not on Unity's main thread. Any interaction with GameObjects, Transforms, or Unity APIs requires explicit marshalling through UnitySynchronizationContext — error-prone and verbose." },
        { title: 'No MonoBehaviour binding',   body: "Nothing prevents a Task from continuing after its owning MonoBehaviour is destroyed. The result is phantom null-reference exceptions, state corruption, and bugs that only appear when scenes are unloaded under load." },
        { title: 'No Unity diagnostics',       body: "The .NET runtime has no concept of Unity's lifetime model. Zero Roslyn rules catch the patterns that break games: zombie loops, destroyed-object access, fire-and-forget misuse." },
      ],
      verdict: 'System.Task is the right tool for .NET servers. It is the wrong tool for a real-time game loop.',
    },
    {
      vs: 'UniTask',
      color: '#f59e0b',
      problems: [
        { title: '~Zero is not Zero',                      body: 'UniTask\'s headline claim is "~Zero allocation." The tilde is load-bearing. Exception paths and cancellation still allocate — same as everyone else. Valkarn Tasks makes the same trade-off honestly and adds more guarantees on top.' },
        { title: 'Manual cancellation — always',           body: 'Binding a UniTask to a MonoBehaviour requires: a CancellationTokenSource field, an OnEnable to (re-)initialize it, an OnDestroy to cancel and dispose it, and threading the token through every async call. Valkarn Tasks source-generates this entire pattern from a single [AutoCancel] attribute.' },
        { title: 'Breaks with Unity Entities',             body: "Unity's Entities package calls PlayerLoop.SetPlayerLoop() on initialization, which overwrites UniTask's registered runners. Any task in flight at that moment is silently discarded. There is no warning. Valkarn Tasks' ECS integration is specifically designed to survive PlayerLoop resets." },
        { title: 'Interlocked on the main thread',         body: "UniTask's pool calls Interlocked.CompareExchange on every Push and Pop — including the main thread where there is no actual contention. These are unnecessary atomic operations on the hottest code path in your game. Valkarn Tasks uses plain CAS on the main thread and a Treiber stack on worker threads." },
        { title: 'One analyzer rule, no structural checks',body: 'UniTask ships UNITASK001: forget-to-await detection. That\'s it. Zombie loops (a loop that never exits because cancellation is never checked), mixed-lifetime tasks, double-awaits — none of these are caught. Valkarn Tasks ships 17 rules that cover these structural patterns.' },
        { title: 'Last release: October 2024',             body: "UniTask's GitHub shows no active development since October 2024. Unity 6, DOTS 1.x, and future editor versions bring breaking changes that an unmaintained package cannot track." },
      ],
      verdict: 'UniTask was state-of-the-art in 2020. Valkarn Tasks is built for 2025 and beyond.',
    },
    {
      vs: 'Awaitable',
      color: '#3b82f6',
      problems: [
        { title: '6 timings, not 16',                   body: "Unity's Awaitable exposes 6 scheduling hooks: NextFrameAsync, FixedUpdateAsync, EndOfFrameAsync, WaitForSecondsAsync, MainThreadAsync, and BackgroundThreadAsync. Valkarn Tasks maps every one of Unity's 16 PlayerLoop phases — PreUpdate, PostLateUpdate, TimeUpdate, and more — as first-class await points." },
        { title: 'Cannot await the same task twice',    body: "Awaiting an Awaitable that has already completed either deadlocks or throws, depending on internal state. There is no guard. Valkarn Tasks has double-return protection on every source path: the second await always returns the cached result immediately." },
        { title: 'Unity 6 silently swallows exceptions',body: 'A confirmed Unity 6 bug causes exceptions thrown inside Awaitable continuations to disappear with no log output, no stack trace, no crash. Fixed in 6000.0.5 — meaning every Unity 6.0 through 6.0.4 project is affected. Valkarn Tasks routes all unobserved exceptions through a configurable handler that defaults to Debug.LogException.' },
        { title: 'No WhenAll, WhenAny, or channels',    body: 'Awaitable has no combinator API. Running three loads in parallel and collecting their results requires a manual state machine. Valkarn Tasks provides WhenAll with tuple destructuring up to arity 8, WhenAny, and both bounded and unbounded async channels.' },
        { title: 'No lifecycle binding',                body: "Awaitable provides no mechanism to tie a task's lifetime to a GameObject. Every cancellation token must be created, stored, threaded through calls, and disposed manually." },
      ],
      verdict: 'Awaitable is a thin scheduling hook. Valkarn Tasks is a complete async runtime.',
    },
  ],

  cta: {
    heading:    'Ship faster. Allocate less.',
    p1:         'MIT open source — free for everyone. Optional commercial license available.',
    p2:         'One line in your manifest. No account required.',
    btnPrimary: 'Get Started →',
    btnGhost:   'View License',
  },
};

export default data;
