// Arabic — locale page data
const data = {
  hero: {
    pill:  'Unity 2023.1+ · مجاني للمستقلين',
    h1:    'Async/await',
    h1Grad:'بدون نفايات.',
    p1:    'مهام مبنية على البنى. إلغاء مُولَّد تلقائيًا.',
    p2:    'جاهز لـ Burst وECS.',
    em:    'صفر تخصيص على المسار الناجح.',
    cta:   'ابدأ الآن →',
  },

  stats: [
    { end: 0,  suffix: '',  label: 'تخصيص\nالمسار الناجح' },
    { end: 16, suffix: '',  label: 'توقيتات\nPlayerLoop' },
    { end: 17, suffix: '',  label: 'قواعد\nالمحلل' },
    { end: 5,  suffix: 'x', label: 'مجموعة موارد أسرع\nمقارنةً بـ UniTask' },
  ],

  featuresSection: {
    heading: "مبني لنموذج تنفيذ Unity.",
    sub:     "ليس منفذًا لأنماط .NET — كل قرار يضع Unity أولًا.",
  },
  features: [
    { icon: '⚡', title: 'صفر تخصيص',             desc: 'يتجنب VlkTask المبني على البنية الضغط على كومة الذاكرة في كل مسار ناجح. المهام المكتملة لا تُكلف شيئًا.' },
    { icon: '🔄', title: 'إلغاء تلقائي عند التدمير', desc: 'اجعل الفئة partial. يربط مولّد الكود الإلغاءَ بعمر MonoBehaviour.' },
    { icon: '🧵', title: 'مجموعة موارد واعية بالخيوط',  desc: 'CAS خالٍ من الأقفال في الخيط الرئيسي، ومكدس Treiber في خيوط العمال. لا عمليات ذرية غير ضرورية.' },
    { icon: '🎯', title: '16 توقيت PlayerLoop',    desc: 'من Initialization إلى TimeUpdate — تحكم دقيق في وقت استئناف الاستمرارات.' },
    { icon: '📡', title: 'قنوات غير متزامنة',       desc: 'طوابير منتج/مستهلك محدودة وغير محدودة. WriteAsync وReadAsync وTryRead — صفر تخصيص.' },
    { icon: '🚀', title: 'جاهز لـ Burst وECS',      desc: 'NativeTimerHeap وBurstSchedulerRunner وأنظمة ECS غير متزامنة. دعم أول درجة لـ Unity DOTS.' },
    { icon: '🔍', title: '17 قاعدة محلل',           desc: 'حلقات الزومبي، وعمار الحياة المختلطة، والمهام غير المنتظَرة — تُكتشف عند الترجمة وليس في الإنتاج.' },
    { icon: '🛡️', title: 'آمن مع IL2CPP',            desc: 'جينيريك صريح، لا انعكاس وقت تشغيل، حماية استئصال link.xml. يعمل على منصات الكونسول.' },
  ],

  comparisonSection: {
    heading:    'مقارنة الميزات.',
    sub:        '🟢 = الأفضل في الصف · ✦ = فريد في Valkarn Tasks · ⓘ = مرر لرؤية التفاصيل',
    featureCol: 'الميزة',
  },

  rows: [
    {
      feature: 'التخصيص عند النجاح',
      sub: 'هل انتظار مهمة مكتملة يُخصص ذاكرة؟',
      cols: { task: 'نعم — Task<T> فئة', unitask: 'لا (بنية)', awaitable: 'لا (بنية)', valkarn: 'لا (بنية)' },
      win: ['unitask', 'awaitable', 'valkarn'],
      note: { task: 'كل طريقة غير متزامنة تُرجع Task<T> تُخصص كائنًا في الكومة، حتى عند الإرجاع المتزامن — ضريبة دائمة على جامع النفايات في حلقة لعبة بـ 60 إطار/ثانية.' },
    },
    {
      feature: 'التخصيص عند الفشل',
      sub: 'مسارات الاستثناء والإلغاء',
      cols: { task: 'نعم', unitask: 'نعم', awaitable: 'نعم', valkarn: 'نعم' },
      win: [],
      note: { unitask: 'يُعلن UniTask عن "~صفر تخصيص" — التيلدا مهمة. مسارات الاستثناءات والإلغاء لا تزال تُخصص، مثل الجميع.' },
    },
    {
      feature: 'إلغاء تلقائي عند التدمير',
      sub: 'مرتبط بعمر MonoBehaviour',
      cols: { task: 'يدوي', unitask: 'يدوي', awaitable: 'يدوي', valkarn: 'مُولَّد ✦' },
      win: ['valkarn'],
      note: { valkarn: 'اجعل الفئة partial. يربط مولّد الكود CancellationToken بـ OnDestroy تلقائيًا — لا حقول بويلربلايت، لا OnEnable/OnDisable، لا إلغاء منسي.' },
    },
    {
      feature: 'توقيتات PlayerLoop',
      sub: 'دقة الجدولة',
      cols: { task: '1 (ThreadPool)', unitask: '16', awaitable: '6', valkarn: '16' },
      win: ['unitask', 'valkarn'],
      note: {
        task:     'تعمل الاستمرارات على .NET ThreadPool. العودة إلى الخيط الرئيسي تتطلب نقلًا صريحًا عبر UnitySynchronizationContext.',
        unitask:  'يُطبّق كلٌّ من UniTask وValkarn Tasks المجموعة الكاملة من 16 توقيت PlayerLoop، من Initialization حتى TimeUpdate.',
        awaitable:'تعرض Awaitable 6 خطافات: NextFrameAsync وFixedUpdateAsync وEndOfFrameAsync وWaitForSecondsAsync وMainThreadAsync وBackgroundThreadAsync — وليس PlayerLoop الكامل.',
      },
    },
    {
      feature: 'توافق ECS / Entities',
      sub: 'يعمل جنبًا إلى جنب مع Unity DOTS',
      cols: { task: 'غير متاح', unitask: '⚠ يتعطل', awaitable: 'جزئي', valkarn: 'كامل ✦' },
      win: ['valkarn'],
      note: {
        unitask:  'حزمة Entities في Unity تُعيد تعيين PlayerLoop عند التهيئة، مما يمحو مشغّلات UniTask المسجّلة. أي مهمة جُدولت قبل تلك اللحظة تُفقد بصمت.',
        awaitable:'تعمل Awaitable في أنظمة ECS لكن لا تمتلك NativeTimerHeap أو BurstSchedulerRunner أو مساعدات أنظمة ECS غير المتزامنة.',
        valkarn:  'دعم DOTS كامل: NativeTimerHeap لجدولة متوافقة مع Burst، وBurstSchedulerRunner، وAsyncSystemUtilities لأنظمة ECS، وجسر JobHandle.',
      },
    },
    {
      feature: 'أمان الانتظار المزدوج',
      sub: 'انتظار نفس المهمة مرتين',
      cols: { task: '✓ آمن', unitask: '✗ غير محدد', awaitable: '✗ إغلاق', valkarn: '✓ آمن' },
      win: ['task', 'valkarn'],
      note: {
        unitask:  'انتظار UniTask أكثر من مرة سلوك غير محدد بشكل صريح. في الممارسة يسبب إغلاقًا أو يُفسد حالة مجموعة الموارد.',
        awaitable:'انتظار Awaitable مرتين يُسبب إغلاقًا أو رميًا، بحسب ما إذا كانت قد اكتملت. لا توجد حماية.',
        valkarn:  'حراس ضد الإرجاع المزدوج على جميع المسارات. الانتظار الثاني لمهمة مكتملة يُرجع النتيجة المخبأة فورًا.',
      },
    },
    {
      feature: 'تشخيصات وقت الترجمة',
      sub: 'قواعد محلل Roslyn',
      cols: { task: '0 قاعدة', unitask: '1 قاعدة', awaitable: '0 قاعدة', valkarn: '17 قاعدة ✦' },
      win: ['valkarn'],
      note: {
        unitask: 'يُشحن UniTask بقاعدة واحدة (UNITASK001): تحذر عند نسيان انتظار قيمة UniTask. لا كشف لحلقات الزومبي أو عمار الحياة المختلطة أو الأخطاء الهيكلية.',
        valkarn: 'يكشف حلقات الزومبي، وعمار الحياة المختلطة، والمهام غير المنتظَرة، والانتظار المزدوج، وإساءة استخدام fire-and-forget، وعلامات الإلغاء التلقائي المفقودة — قبل الشحن.',
      },
    },
    {
      feature: 'مجموعة موارد واعية بالخيوط',
      sub: 'خالية من الأقفال في الخيط الرئيسي',
      cols: { task: 'غير متاح', unitask: 'Interlocked (جميع الخيوط)', awaitable: 'غير متاح', valkarn: 'CAS / Treiber ✦' },
      win: ['valkarn'],
      note: {
        unitask: 'مجموعة موارد UniTask تستخدم Interlocked.CompareExchange في كل Push وPop — بما في ذلك الخيط الرئيسي حيث لا يوجد تنافس حقيقي. تكلفة ذرية غير ضرورية عند كل اكتمال مهمة.',
        valkarn: 'CAS خالٍ من الأقفال في الخيط الرئيسي (لا تكلفة ذرية حيث لا حاجة لها). مكدس Treiber لخيوط الخلفية. كل سياق يحصل على الخوارزمية الصحيحة.',
      },
    },
    {
      feature: 'قنوات غير متزامنة',
      sub: 'منتج/مستهلك مدمج',
      cols: { task: 'BCL (مبني على فئة)', unitask: 'غير محدود فقط', awaitable: '✗', valkarn: 'محدود + غير محدود ✦' },
      win: ['valkarn'],
      note: {
        task:    'System.Threading.Channels مبني على فئات ويُخصص ذاكرة. غير متكامل مع PlayerLoop في Unity — الاستمرارات تعمل على ThreadPool.',
        unitask: 'يُشحن UniTask بقناة واحدة غير محدودة أحادية المستهلك فقط. لا سعة محدودة، لا ضغط عكسي. صفر تخصيص عند القراءة لكن API محدود.',
        valkarn: 'قنوات محدودة (بسعة وضغط عكسي) وغير محدودة. WriteAsync وReadAsync وTryRead وTryWrite وTryPeek — جميعها صفر تخصيص على المسار السريع.',
      },
    },
    {
      feature: 'مُجمِّعات WhenAll / WhenAny',
      sub: 'تنسيق المهام المتوازية',
      cols: { task: 'يُرجع Task[]', unitask: '✓ (مصفوفات فقط)', awaitable: '✗', valkarn: 'صف حتى 8 ✦' },
      win: ['unitask', 'valkarn'],
      note: {
        task:    'Task.WhenAll يُرجع Task<T[]>، مما يتطلب وصولًا بالفهرس. لا تفكيك صفوف.',
        unitask: 'UniTask.WhenAll يدعم صفوف مكتوبة حتى 15 عنصرًا. ميزة قوية.',
        valkarn: 'var (tex, sfx, data) = await VlkTask.WhenAll(...) — حتى 8 نتائج مكتوبة. WhenAny يُرجع أول نتيجة مكتملة مع فهرسها.',
      },
    },
    {
      feature: 'ابتلاع الاستثناءات بصمت',
      sub: 'أخطاء غير معالجة في async void / fire-and-forget',
      cols: { task: 'AppDomain.UnhandledException', unitask: 'UniTaskScheduler.UnobservedTaskException', awaitable: '⚠ خطأ Unity 6', valkarn: 'معالج قابل للتكوين ✦' },
      win: ['valkarn'],
      note: {
        awaitable:'كان Unity 6 يحتوي على خطأ مؤكد يُسبب ابتلاع الاستثناءات المرمية داخل استمرارات Awaitable بصمت دون أي مخرجات سجل. تم إصلاحه في 6000.0.5 — الإصدارات الأقدم من 6.x متأثرة.',
        valkarn:  'VlkTaskSettings.UnobservedExceptionHandler قابل للتكوين من قِبل المستخدم. الافتراضي: يُسجل إلى Debug.LogException حتى لا يضيع أي استثناء.',
      },
    },
  ],

  versusSection: {
    heading: 'لماذا تنتقل؟',
    sub:     'أسباب ملموسة، ليست دعاية تسويقية.',
  },

  versus: [
    {
      vs: 'System.Task',
      color: '#ef4444',
      problems: [
        { title: 'كل انتظار يُخصص ذاكرة',      body: 'Task<T> فئة. كل طريقة غير متزامنة تُخصص كائنًا في الكومة، حتى عند الإرجاع المتزامن. في حلقة لعبة بـ 60 إطار/ثانية تُنفّذ آلاف العمليات غير المتزامنة، تدفع ضريبة جامع النفايات في كل إطار.' },
        { title: 'ThreadPool افتراضيًا',      body: 'تستأنف الاستمرارات على .NET ThreadPool، وليس على الخيط الرئيسي لـ Unity. أي تفاعل مع GameObjects أو Transforms أو واجهات برمجة Unity يتطلب نقلًا صريحًا عبر UnitySynchronizationContext — عرضة للأخطاء ومطوّلة.' },
        { title: 'لا ربط بـ MonoBehaviour',   body: 'لا شيء يمنع Task من الاستمرار بعد تدمير MonoBehaviour المالكة. النتيجة هي استثناءات null-reference وهمية وتلف الحالة وأخطاء تظهر فقط عند تفريغ المشاهد.' },
        { title: 'لا تشخيصات Unity',       body: 'وقت تشغيل .NET لا يفهم نموذج حياة Unity. لا قواعد Roslyn تكشف الأنماط التي تُفسد الألعاب: حلقات الزومبي، والوصول إلى كائنات مدمرة، وإساءة استخدام fire-and-forget.' },
      ],
      verdict: 'System.Task الأداة المثلى لخوادم .NET. وهي الأداة الخاطئة لحلقة لعبة في الوقت الفعلي.',
    },
    {
      vs: 'UniTask',
      color: '#f59e0b',
      problems: [
        { title: '~صفر ليس صفرًا',                      body: 'الادعاء الرئيسي لـ UniTask هو "~صفر تخصيص". التيلدا جوهرية. مسارات الاستثناءات والإلغاء لا تزال تُخصص — مثل الجميع. Valkarn Tasks يُجري نفس المقايضة بصدق ويُضيف ضمانات أكثر.' },
        { title: 'إلغاء يدوي — دائمًا',           body: 'ربط UniTask بـ MonoBehaviour يتطلب: حقل CancellationTokenSource، وOnEnable لإعادة تهيئته، وOnDestroy لإلغائه والتخلص منه، وتمرير الرمز عبر كل استدعاء غير متزامن. Valkarn Tasks يُولّد هذا النمط كاملًا من سمة [AutoCancel] واحدة.' },
        { title: 'يتعطل مع Unity Entities',             body: 'حزمة Entities في Unity تستدعي PlayerLoop.SetPlayerLoop() عند التهيئة، مما يُستبدل مشغّلات UniTask المسجّلة. أي مهمة قيد التشغيل في تلك اللحظة تُهمل بصمت. لا تحذير. تكامل ECS في Valkarn Tasks مصمم خصيصًا للنجاة من إعادة تعيين PlayerLoop.' },
        { title: 'Interlocked في الخيط الرئيسي',         body: 'مجموعة موارد UniTask تستدعي Interlocked.CompareExchange في كل Push وPop — بما في ذلك الخيط الرئيسي حيث لا تنافس فعلي. هذه عمليات ذرية غير ضرورية على أكثر مسار كود حرارةً في لعبتك. Valkarn Tasks يستخدم CAS عاديًا في الخيط الرئيسي ومكدس Treiber في خيوط العمال.' },
        { title: 'قاعدة محلل واحدة، لا فحوصات هيكلية',body: 'يُشحن UniTask بـ UNITASK001: كشف نسيان الانتظار. هذا كل شيء. حلقات الزومبي (حلقة لا تخرج أبدًا لأن الإلغاء لا يُفحص)، والمهام ذات عمار حياة مختلطة، والانتظار المزدوج — لا شيء من هذا يُكشف. Valkarn Tasks يُشحن بـ 17 قاعدة تغطي هذه الأنماط الهيكلية.' },
        { title: 'آخر إصدار: أكتوبر 2024',             body: 'لا يُظهر GitHub لـ UniTask أي تطوير نشط منذ أكتوبر 2024. Unity 6 وDOTS 1.x والإصدارات المستقبلية من المحرر تُحضر تغييرات جذرية لا يستطيع مسايرتها مشروع غير مُصان.' },
      ],
      verdict: 'كان UniTask الأحدث عام 2020. Valkarn Tasks مبني لعام 2025 وما بعده.',
    },
    {
      vs: 'Awaitable',
      color: '#3b82f6',
      problems: [
        { title: '6 توقيتات وليس 16',                   body: 'تعرض Awaitable في Unity 6 خطافات جدولة: NextFrameAsync وFixedUpdateAsync وEndOfFrameAsync وWaitForSecondsAsync وMainThreadAsync وBackgroundThreadAsync. Valkarn Tasks يُعيّن كل مرحلة من المراحل الـ 16 لـ PlayerLoop في Unity — PreUpdate وPostLateUpdate وTimeUpdate وأكثر — كنقاط انتظار من الدرجة الأولى.' },
        { title: 'لا يمكن انتظار نفس المهمة مرتين',    body: 'انتظار Awaitable مكتملة بالفعل إما يُسبب إغلاقًا أو رميًا، بحسب الحالة الداخلية. لا توجد حماية. Valkarn Tasks لديه حماية من الإرجاع المزدوج على كل مسار مصدر: الانتظار الثاني دائمًا يُرجع النتيجة المخبأة فورًا.' },
        { title: 'Unity 6 يبتلع الاستثناءات بصمت',body: 'خطأ مؤكد في Unity 6 يُسبب اختفاء الاستثناءات المرمية داخل استمرارات Awaitable دون أي مخرجات أو تتبع مكدس أو تعطل. تم إصلاحه في 6000.0.5 — بمعنى أن كل مشروع من Unity 6.0 إلى 6.0.4 متأثر. Valkarn Tasks يُوجّه جميع الاستثناءات غير الملاحظة عبر معالج قابل للتكوين يُرسل افتراضيًا إلى Debug.LogException.' },
        { title: 'لا WhenAll ولا WhenAny ولا قنوات',    body: 'لا تمتلك Awaitable واجهة مُجمِّعات. تشغيل ثلاثة أحمال بالتوازي وجمع نتائجها يتطلب آلة حالة يدوية. Valkarn Tasks يوفر WhenAll مع تفكيك صفوف حتى 8 عناصر، وWhenAny، وقنوات غير متزامنة محدودة وغير محدودة.' },
        { title: 'لا ربط بدورة الحياة',                body: 'لا توفر Awaitable آلية لربط عمر المهمة بـ GameObject. كل رمز إلغاء يجب إنشاؤه وتخزينه وتمريره عبر الاستدعاءات والتخلص منه يدويًا.' },
      ],
      verdict: 'Awaitable خطاف جدولة رفيع. Valkarn Tasks وقت تشغيل غير متزامن كامل.',
    },
  ],

  cta: {
    heading:    'اشحن أسرع. خصّص أقل.',
    p1:         'مجاني للأفراد والاستوديوهات بإيرادات أقل من مليون دولار/سنة.',
    p2:         'سطر واحد في ملف الحزمة. لا حساب مطلوب.',
    btnPrimary: 'ابدأ الآن →',
    btnGhost:   'عرض الترخيص',
  },
};

export default data;
