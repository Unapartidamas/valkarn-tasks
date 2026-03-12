// Hindi — hi locale page data
const data = {
  hero: {
    pill:  'Unity 2023.1+ · इंडी के लिए निःशुल्क',
    h1:    'Async/await',
    h1Grad:'बिना कचरे के।',
    p1:    'Struct-आधारित tasks। Source-generated रद्दीकरण।',
    p2:    'Burst & ECS तैयार।',
    em:    'सफल पथ पर शून्य मेमोरी आवंटन।',
    cta:   'शुरू करें →',
  },

  stats: [
    { end: 0,  suffix: '',  label: 'आवंटन\nसफल पथ' },
    { end: 16, suffix: '',  label: 'PlayerLoop\ntimings' },
    { end: 17, suffix: '',  label: 'analyzer\nनियम' },
    { end: 5,  suffix: 'x', label: 'तेज़ पूल\nUniTask से' },
  ],

  featuresSection: {
    heading: "Unity के निष्पादन मॉडल के लिए निर्मित।",
    sub:     "यह .NET पैटर्न का पोर्ट नहीं है — हर निर्णय Unity-प्रथम है।",
  },
  features: [
    { icon: '⚡', title: 'शून्य आवंटन',              desc: 'Struct-आधारित ValkarnTask हर सफल पथ पर हीप दबाव से बचता है। पूर्ण tasks निःशुल्क हैं।' },
    { icon: '🔄', title: 'नष्ट होने पर Auto-Cancel',  desc: 'क्लास को partial घोषित करें। source generator रद्दीकरण को MonoBehaviour जीवनचक्र से बाँधता है।' },
    { icon: '🧵', title: 'Thread-Aware Pool',          desc: 'मुख्य thread पर lock-free CAS, worker threads पर Treiber stack। कोई अनावश्यक atomics नहीं।' },
    { icon: '🎯', title: '16 PlayerLoop Timings',      desc: 'Initialization से TimeUpdate तक — continuations कब resume होती हैं, इस पर सटीक नियंत्रण।' },
    { icon: '📡', title: 'Async Channels',             desc: 'Bounded और unbounded producer/consumer queues। WriteAsync, ReadAsync, TryRead — शून्य-आवंटन।' },
    { icon: '🚀', title: 'Burst & ECS Ready',           desc: 'NativeTimerHeap, BurstSchedulerRunner, async ECS systems। Unity DOTS का प्रथम-श्रेणी समर्थन।' },
    { icon: '🔍', title: '17 Analyzer नियम',           desc: 'Zombie loops, mixed lifetimes, unawaited tasks — compile time पर पकड़े जाते हैं, production में नहीं।' },
    { icon: '🛡️', title: 'IL2CPP सुरक्षित',            desc: 'Explicit generics, कोई runtime reflection नहीं, link.xml stripping सुरक्षा। Consoles पर ship होता है।' },
  ],

  comparisonSection: {
    heading:    'फ़ीचर तुलना।',
    sub:        '🟢 = पंक्ति में सर्वश्रेष्ठ · ✦ = Valkarn Tasks में अनूठा · ⓘ = hover करें विवरण के लिए',
    featureCol: 'फ़ीचर',
  },

  rows: [
    {
      feature: 'सफलता पर आवंटन',
      sub: 'क्या पूर्ण task का await करने पर आवंटन होता है?',
      cols: { task: 'हाँ — Task<T> एक class है', unitask: 'नहीं (struct)', awaitable: 'नहीं (struct)', valkarn: 'नहीं (struct)' },
      win: ['unitask', 'awaitable', 'valkarn'],
      note: { task: 'Task<T> लौटाने वाला हर async method एक heap object आवंटित करता है, तब भी जब वह synchronously लौटता है — 60 Hz game loop में निरंतर GC कर।' },
    },
    {
      feature: 'विफलता पर आवंटन',
      sub: 'Exception / रद्दीकरण पथ',
      cols: { task: 'हाँ', unitask: 'हाँ', awaitable: 'हाँ', valkarn: 'हाँ' },
      win: [],
      note: { unitask: 'UniTask का headline claim है "~Zero allocation" — tilde महत्वपूर्ण है। Exceptions और cancellations अभी भी आवंटित करती हैं, सबकी तरह।' },
    },
    {
      feature: 'नष्ट होने पर Auto-cancel',
      sub: 'MonoBehaviour जीवनचक्र से बंधा',
      cols: { task: 'मैनुअल', unitask: 'मैनुअल', awaitable: 'मैनुअल', valkarn: 'Source-gen ✦' },
      win: ['valkarn'],
      note: { valkarn: 'क्लास को partial घोषित करें। एक source generator CancellationToken को OnDestroy से जोड़ता है — कोई boilerplate field नहीं, कोई OnEnable/OnDisable नहीं, कोई भूला हुआ रद्दीकरण नहीं।' },
    },
    {
      feature: 'PlayerLoop timings',
      sub: 'शेड्यूलिंग परिशुद्धता',
      cols: { task: '1 (ThreadPool)', unitask: '16', awaitable: '6', valkarn: '16' },
      win: ['unitask', 'valkarn'],
      note: {
        task:     'Continuations .NET ThreadPool पर resume होती हैं। मुख्य thread पर वापस जाने के लिए UnitySynchronizationContext के माध्यम से explicit marshalling की आवश्यकता है।',
        unitask:  'UniTask और Valkarn Tasks दोनों 16 PlayerLoop timings का पूरा सेट लागू करते हैं, Initialization से TimeUpdate तक।',
        awaitable:'Awaitable 6 hooks expose करता है: NextFrameAsync, FixedUpdateAsync, EndOfFrameAsync, WaitForSecondsAsync, MainThreadAsync, BackgroundThreadAsync — पूरा PlayerLoop नहीं।',
      },
    },
    {
      feature: 'ECS / Entities संगतता',
      sub: 'Unity DOTS के साथ काम करता है',
      cols: { task: 'N/A', unitask: '⚠ टूटता है', awaitable: 'आंशिक', valkarn: 'पूर्ण ✦' },
      win: ['valkarn'],
      note: {
        unitask:  "Unity का Entities package initialization पर PlayerLoop reset करता है, जो UniTask के registered runners को साफ़ करता है। उस बिंदु से पहले scheduled कोई भी task चुपचाप खो जाता है।",
        awaitable:'Awaitable ECS systems में काम करता है लेकिन इसमें NativeTimerHeap, BurstSchedulerRunner, और async ECS system helpers नहीं हैं।',
        valkarn:  'पूर्ण DOTS समर्थन: Burst-compatible scheduling के लिए NativeTimerHeap, BurstSchedulerRunner, ECS systems के लिए AsyncSystemUtilities, JobHandle bridge।',
      },
    },
    {
      feature: 'Double-await सुरक्षा',
      sub: 'एक ही task को दो बार await करना',
      cols: { task: '✓ सुरक्षित', unitask: '✗ अपरिभाषित', awaitable: '✗ Deadlock', valkarn: '✓ सुरक्षित' },
      win: ['task', 'valkarn'],
      note: {
        unitask:  'UniTask को एक से अधिक बार await करना स्पष्ट रूप से अपरिभाषित व्यवहार है। व्यवहार में यह deadlock का कारण बनता है या pool state दूषित करता है।',
        awaitable:'Awaitable को दो बार await करने पर deadlock होता है या exception फेंकता है, इस पर निर्भर करता है कि यह पहले से पूर्ण हुआ है या नहीं। कोई guard नहीं है।',
        valkarn:  'सभी source पथों पर double-return guards। पूर्ण task पर दूसरा await तुरंत cached result लौटाता है।',
      },
    },
    {
      feature: 'Compile-time diagnostics',
      sub: 'Roslyn analyzer नियम',
      cols: { task: '0 नियम', unitask: '1 नियम', awaitable: '0 नियम', valkarn: '17 नियम ✦' },
      win: ['valkarn'],
      note: {
        unitask: 'UniTask एक नियम (UNITASK001) ship करता है: जब आप UniTask return value await करना भूल जाते हैं। zombie loops, mixed lifetimes, या structural bugs का कोई detection नहीं।',
        valkarn: 'Zombie loops, mixed lifetimes, unawaited tasks, double-awaits, अनुचित fire-and-forget, missing auto-cancel markers — build ship होने से पहले पकड़े जाते हैं।',
      },
    },
    {
      feature: 'Thread-aware pool',
      sub: 'मुख्य thread पर lock-free',
      cols: { task: 'N/A', unitask: 'Interlocked (सभी threads)', awaitable: 'N/A', valkarn: 'CAS / Treiber ✦' },
      win: ['valkarn'],
      note: {
        unitask: "UniTask का pool हर Push और Pop पर Interlocked.CompareExchange का उपयोग करता है — मुख्य thread सहित जहाँ कभी कोई वास्तविक contention नहीं होता। हर task completion पर अनावश्यक atomic overhead।",
        valkarn: "मुख्य thread पर lock-free CAS (जहाँ इसकी आवश्यकता नहीं वहाँ कोई atomic overhead नहीं)। Background threads के लिए Treiber stack। हर context को सही algorithm मिलता है।",
      },
    },
    {
      feature: 'Async channels',
      sub: 'बिल्ट-इन producer/consumer',
      cols: { task: 'BCL (class-आधारित)', unitask: 'केवल Unbounded', awaitable: '✗', valkarn: 'Bounded + Unbounded ✦' },
      win: ['valkarn'],
      note: {
        task:    "System.Threading.Channels class-आधारित है और आवंटित करता है। Unity के PlayerLoop के साथ integrated नहीं — continuations ThreadPool पर चलती हैं।",
        unitask: 'UniTask केवल unbounded single-consumer channel ship करता है। कोई bounded capacity नहीं, कोई backpressure नहीं। Reads पर zero-alloc, लेकिन सीमित API।',
        valkarn: 'Bounded (capacity और backpressure के साथ) और unbounded दोनों channels। WriteAsync, ReadAsync, TryRead, TryWrite, TryPeek — सभी fast path पर zero-alloc।',
      },
    },
    {
      feature: 'WhenAll / WhenAny combinators',
      sub: 'समानांतर task समन्वय',
      cols: { task: 'Task[] लौटाता है', unitask: '✓ (केवल arrays)', awaitable: '✗', valkarn: 'Tuple 8 तक ✦' },
      win: ['unitask', 'valkarn'],
      note: {
        task:    'Task.WhenAll Task<T[]> लौटाता है, index-based access की आवश्यकता है। कोई tuple destructuring नहीं।',
        unitask: 'UniTask.WhenAll arity 15 तक typed tuples support करता है। एक मजबूत feature।',
        valkarn: 'var (tex, sfx, data) = await ValkarnTask.WhenAll(...) — 8 typed results तक। WhenAny अपने index के साथ पहला completed result लौटाता है।',
      },
    },
    {
      feature: 'Silent exception निगलना',
      sub: 'async void / fire-and-forget में अनुपचारित errors',
      cols: { task: 'AppDomain.UnhandledException', unitask: 'UniTaskScheduler.UnobservedTaskException', awaitable: '⚠ Unity 6 bug', valkarn: 'Configurable handler ✦' },
      win: ['valkarn'],
      note: {
        awaitable:'Unity 6 में एक confirmed bug था जहाँ Awaitable continuations के अंदर फेंकी गई exceptions बिना किसी log output के चुपचाप निगल ली जाती थीं। Unity 6000.0.5 में ठीक किया गया — पहले के 6.x releases प्रभावित हैं।',
        valkarn:  'ValkarnTaskSettings.UnobservedExceptionHandler user-configurable है। Default: Debug.LogException पर log करता है ताकि कोई exception कभी silent न हो।',
      },
    },
  ],

  versusSection: {
    heading: 'switch क्यों करें?',
    sub:     'ठोस कारण, marketing copy नहीं।',
  },

  versus: [
    {
      vs: 'System.Task',
      color: '#ef4444',
      problems: [
        { title: 'हर await आवंटित करता है',      body: "Task<T> एक class है। हर async method एक heap object आवंटित करता है, तब भी जब वह synchronously लौटता है। 60 Hz game loop में जो हजारों async operations चलाता है, आप हर single frame पर GC कर चुका रहे हैं।" },
        { title: 'Default रूप से ThreadPool',     body: "Continuations .NET ThreadPool पर resume होती हैं, Unity के मुख्य thread पर नहीं। GameObjects, Transforms, या Unity APIs के साथ कोई भी interaction के लिए UnitySynchronizationContext के माध्यम से explicit marshalling की आवश्यकता है — error-prone और verbose।" },
        { title: 'कोई MonoBehaviour binding नहीं', body: "कुछ भी एक Task को उसके owning MonoBehaviour के नष्ट होने के बाद continue करने से नहीं रोकता। परिणाम phantom null-reference exceptions, state corruption, और ऐसे bugs हैं जो केवल load के तहत scenes unload होने पर दिखाई देते हैं।" },
        { title: 'कोई Unity diagnostics नहीं',    body: ".NET runtime को Unity के lifetime model की कोई अवधारणा नहीं है। Zero Roslyn नियम उन patterns को पकड़ते हैं जो games तोड़ते हैं: zombie loops, destroyed-object access, fire-and-forget misuse।" },
      ],
      verdict: 'System.Task .NET servers के लिए सही tool है। यह real-time game loop के लिए गलत tool है।',
    },
    {
      vs: 'UniTask',
      color: '#f59e0b',
      problems: [
        { title: '~Zero, Zero नहीं है',                     body: 'UniTask का headline claim है "~Zero allocation।" tilde महत्वपूर्ण है। Exception paths और cancellation अभी भी आवंटित करती हैं — सबकी तरह। Valkarn Tasks यही trade-off ईमानदारी से करता है और ऊपर से अधिक guarantees जोड़ता है।' },
        { title: 'हमेशा मैनुअल cancellation',              body: 'UniTask को MonoBehaviour से bind करने के लिए: एक CancellationTokenSource field, उसे (re-)initialize करने के लिए OnEnable, cancel और dispose करने के लिए OnDestroy, और हर async call के माध्यम से token threading की आवश्यकता है। Valkarn Tasks एक [AutoCancel] attribute से इस पूरे pattern को source-generate करता है।' },
        { title: 'Unity Entities के साथ टूटता है',          body: "Unity का Entities package initialization पर PlayerLoop.SetPlayerLoop() call करता है, जो UniTask के registered runners को overwrite करता है। उस क्षण in-flight कोई भी task चुपचाप discard हो जाता है। कोई warning नहीं है। Valkarn Tasks की ECS integration विशेष रूप से PlayerLoop resets से बचने के लिए design की गई है।" },
        { title: 'मुख्य thread पर Interlocked',             body: "UniTask का pool हर Push और Pop पर Interlocked.CompareExchange call करता है — मुख्य thread सहित जहाँ कोई actual contention नहीं है। ये आपके game के hottest code path पर अनावश्यक atomic operations हैं। Valkarn Tasks मुख्य thread पर plain CAS और worker threads पर Treiber stack का उपयोग करता है।" },
        { title: 'एक analyzer नियम, कोई structural checks नहीं', body: 'UniTask UNITASK001 ship करता है: forget-to-await detection। बस। Zombie loops (एक loop जो कभी exit नहीं होता क्योंकि cancellation कभी check नहीं होता), mixed-lifetime tasks, double-awaits — इनमें से कोई भी नहीं पकड़ा जाता। Valkarn Tasks 17 नियम ship करता है जो इन structural patterns को cover करते हैं।' },
        { title: 'अंतिम release: अक्टूबर 2024',            body: "UniTask का GitHub अक्टूबर 2024 से कोई सक्रिय development नहीं दिखाता। Unity 6, DOTS 1.x, और भविष्य के editor versions breaking changes लाते हैं जिन्हें एक unmaintained package track नहीं कर सकता।" },
      ],
      verdict: 'UniTask 2020 में state-of-the-art था। Valkarn Tasks 2025 और उससे आगे के लिए बनाया गया है।',
    },
    {
      vs: 'Awaitable',
      color: '#3b82f6',
      problems: [
        { title: '16 नहीं, 6 timings',                       body: "Unity का Awaitable 6 scheduling hooks expose करता है: NextFrameAsync, FixedUpdateAsync, EndOfFrameAsync, WaitForSecondsAsync, MainThreadAsync, और BackgroundThreadAsync। Valkarn Tasks Unity के 16 PlayerLoop phases में से हर एक को map करता है — PreUpdate, PostLateUpdate, TimeUpdate, और अधिक — पहले-श्रेणी await points के रूप में।" },
        { title: 'एक ही task को दो बार await नहीं कर सकते',  body: "पहले से complete हो चुके Awaitable को await करने पर या तो deadlock होता है या exception फेंकती है, internal state पर निर्भर करता है। कोई guard नहीं है। Valkarn Tasks के हर source path पर double-return protection है: दूसरा await हमेशा तुरंत cached result लौटाता है।" },
        { title: 'Unity 6 चुपचाप exceptions निगलता है',      body: 'एक confirmed Unity 6 bug के कारण Awaitable continuations के अंदर फेंकी गई exceptions बिना किसी log output, stack trace, या crash के गायब हो जाती हैं। 6000.0.5 में ठीक किया गया — यानी हर Unity 6.0 से 6.0.4 project प्रभावित है। Valkarn Tasks सभी unobserved exceptions को एक configurable handler के माध्यम से route करता है जो default रूप से Debug.LogException करता है।' },
        { title: 'कोई WhenAll, WhenAny, या channels नहीं',   body: 'Awaitable के पास कोई combinator API नहीं है। तीन loads को समानांतर में चलाना और उनके results collect करना एक manual state machine की आवश्यकता है। Valkarn Tasks WhenAll tuple destructuring के साथ arity 8 तक, WhenAny, और bounded और unbounded दोनों async channels प्रदान करता है।' },
        { title: 'कोई lifecycle binding नहीं',                body: "Awaitable किसी task के जीवनचक्र को किसी GameObject से tie करने का कोई mechanism प्रदान नहीं करता। हर cancellation token को manually create, store, thread through calls, और dispose करना होता है।" },
      ],
      verdict: 'Awaitable एक thin scheduling hook है। Valkarn Tasks एक complete async runtime है।',
    },
  ],

  cta: {
    heading:    'तेज़ ship करें। कम आवंटित करें।',
    p1:         '$1M/वर्ष revenue से कम के व्यक्तियों और studios के लिए निःशुल्क।',
    p2:         'आपके manifest में एक line। कोई account आवश्यक नहीं।',
    btnPrimary: 'शुरू करें →',
    btnGhost:   'License देखें',
  },
};

export default data;
