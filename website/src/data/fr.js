// Français — locale fr
const data = {
  hero: {
    pill:  'Unity 2023.1+ · MIT · Gratuit pour tous',
    h1:    'Async/await',
    h1Grad:'sans garbage.',
    p1:    'Tâches basées sur des structs. Annulation générée par la source.',
    p2:    'Prêt pour Burst & ECS.',
    em:    'Zéro allocation sur le chemin nominal.',
    cta:   'Commencer →',
  },

  stats: [
    { end: 0,  suffix: '',  label: 'allocs\nchemin nominal' },
    { end: 16, suffix: '',  label: 'timings\nPlayerLoop' },
    { end: 17, suffix: '',  label: 'règles\nd\'analyseur' },
    { end: 5,  suffix: 'x', label: 'pool plus rapide\nvs UniTask' },
  ],

  featuresSection: {
    heading: "Conçu pour le modèle d'exécution d'Unity.",
    sub:     "Pas un portage de patterns .NET — chaque décision est orientée Unity en premier.",
  },
  features: [
    { icon: '⚡', title: 'Zéro Allocation',               desc: 'ValkarnTask basé sur une struct évite la pression sur le tas sur chaque chemin de succès. Les tâches terminées sont gratuites.' },
    { icon: '🔄', title: 'Auto-Annulation à la Destruction', desc: 'Déclarez la classe partial. Le générateur de source lie l\'annulation à la durée de vie du MonoBehaviour.' },
    { icon: '🧵', title: 'Pool Conscient des Threads',    desc: 'CAS sans verrou sur le thread principal, pile Treiber sur les threads de travail. Pas d\'atomiques inutiles.' },
    { icon: '🎯', title: '16 Timings PlayerLoop',         desc: 'De l\'Initialization au TimeUpdate — contrôle précis du moment où les continuations reprennent.' },
    { icon: '📡', title: 'Canaux Asynchrones',             desc: 'Files producteur/consommateur bornées et non bornées. WriteAsync, ReadAsync, TryRead — zéro allocation.' },
    { icon: '🚀', title: 'Prêt pour Burst & ECS',          desc: 'NativeTimerHeap, BurstSchedulerRunner, systèmes ECS asynchrones. Support Unity DOTS de première classe.' },
    { icon: '🔍', title: '17 Règles d\'Analyseur',         desc: 'Boucles zombie, durées de vie mixtes, tâches non attendues — détectées à la compilation, pas en production.' },
    { icon: '🛡️', title: 'Sûr pour IL2CPP',               desc: 'Génériques explicites, pas de réflexion à l\'exécution, protection contre le stripping link.xml. Compatible avec les consoles.' },
  ],

  comparisonSection: {
    heading:    'Comparaison des fonctionnalités.',
    sub:        '🟢 = meilleur de la ligne · ✦ = unique à Valkarn Tasks · ⓘ = survoler pour le détail',
    featureCol: 'Fonctionnalité',
  },

  rows: [
    {
      feature: 'Allocation en cas de succès',
      sub: "L'attente d'une tâche terminée alloue-t-elle ?",
      cols: { task: 'Oui — Task<T> est une classe', unitask: 'Non (struct)', awaitable: 'Non (struct)', valkarn: 'Non (struct)' },
      win: ['unitask', 'awaitable', 'valkarn'],
      note: { task: "Chaque méthode async retournant Task<T> alloue un objet sur le tas, même lorsqu'elle retourne de manière synchrone — une taxe GC constante dans une boucle de jeu à 60 Hz." },
    },
    {
      feature: "Allocation en cas d'échec",
      sub: "Chemins d'exception / annulation",
      cols: { task: 'Oui', unitask: 'Oui', awaitable: 'Oui', valkarn: 'Oui' },
      win: [],
      note: { unitask: 'UniTask revendique "~Zéro allocation" — le tilde est important. Les exceptions et les annulations allouent toujours, comme tout le monde.' },
    },
    {
      feature: 'Auto-annulation à la destruction',
      sub: 'Liée à la durée de vie du MonoBehaviour',
      cols: { task: 'Manuelle', unitask: 'Manuelle', awaitable: 'Manuelle', valkarn: 'Générée par la source ✦' },
      win: ['valkarn'],
      note: { valkarn: 'Déclarez la classe partial. Un générateur de source câble un CancellationToken à OnDestroy — pas de champ passe-partout, pas de OnEnable/OnDisable, pas d\'annulation oubliée.' },
    },
    {
      feature: 'Timings PlayerLoop',
      sub: 'Précision de planification',
      cols: { task: '1 (ThreadPool)', unitask: '16', awaitable: '6', valkarn: '16' },
      win: ['unitask', 'valkarn'],
      note: {
        task:     'Les continuations s\'exécutent sur le ThreadPool .NET. Revenir au thread principal nécessite un marshalling explicite via UnitySynchronizationContext.',
        unitask:  'UniTask et Valkarn Tasks implémentent tous les deux l\'ensemble complet de 16 timings PlayerLoop, de l\'Initialization au TimeUpdate.',
        awaitable:'Awaitable expose 6 hooks : NextFrameAsync, FixedUpdateAsync, EndOfFrameAsync, WaitForSecondsAsync, MainThreadAsync, BackgroundThreadAsync — pas l\'intégralité du PlayerLoop.',
      },
    },
    {
      feature: 'Compatibilité ECS / Entities',
      sub: 'Fonctionne avec Unity DOTS',
      cols: { task: 'N/A', unitask: '⚠ Plante', awaitable: 'Partiel', valkarn: 'Complet ✦' },
      win: ['valkarn'],
      note: {
        unitask:  "Le package Unity Entities réinitialise le PlayerLoop à l'initialisation, ce qui efface les runners enregistrés d'UniTask. Toute tâche planifiée avant ce point est silencieusement perdue.",
        awaitable:"Awaitable fonctionne dans les systèmes ECS mais n'a pas de NativeTimerHeap, ni de BurstSchedulerRunner, ni d'assistants de système ECS asynchrone.",
        valkarn:  'Support DOTS complet : NativeTimerHeap pour la planification compatible Burst, BurstSchedulerRunner, AsyncSystemUtilities pour les systèmes ECS, pont JobHandle.',
      },
    },
    {
      feature: 'Sécurité double-await',
      sub: 'Attendre la même tâche deux fois',
      cols: { task: '✓ Sûr', unitask: '✗ Indéfini', awaitable: '✗ Deadlock', valkarn: '✓ Sûr' },
      win: ['task', 'valkarn'],
      note: {
        unitask:  "Attendre un UniTask plus d'une fois est explicitement un comportement indéfini. En pratique, cela cause un deadlock ou corrompt l'état du pool.",
        awaitable:"Attendre un Awaitable deux fois cause un deadlock ou lève une exception, selon qu'il a déjà été terminé. Il n'y a pas de garde.",
        valkarn:  'Gardes contre le double-retour sur tous les chemins sources. Le second await sur une tâche terminée retourne le résultat mis en cache immédiatement.',
      },
    },
    {
      feature: 'Diagnostics à la compilation',
      sub: "Règles d'analyseur Roslyn",
      cols: { task: '0 règle', unitask: '1 règle', awaitable: '0 règle', valkarn: '17 règles ✦' },
      win: ['valkarn'],
      note: {
        unitask: "UniTask inclut une règle (UNITASK001) : avertit quand vous oubliez d'attendre une valeur de retour UniTask. Aucune détection de boucles zombie, de durées de vie mixtes, ou de bugs structurels.",
        valkarn: 'Détecte les boucles zombie, les durées de vie mixtes, les tâches non attendues, les doubles-awaits, le mauvais usage du fire-and-forget, les marqueurs auto-cancel manquants — avant que le build soit publié.',
      },
    },
    {
      feature: 'Pool conscient des threads',
      sub: 'Sans verrou sur le thread principal',
      cols: { task: 'N/A', unitask: 'Interlocked (tous les threads)', awaitable: 'N/A', valkarn: 'CAS / Treiber ✦' },
      win: ['valkarn'],
      note: {
        unitask: "Le pool d'UniTask utilise Interlocked.CompareExchange à chaque Push et Pop — y compris sur le thread principal où il n'y a jamais de vraie contention. Surcharge atomique inutile à chaque achèvement de tâche.",
        valkarn: "CAS sans verrou sur le thread principal (pas de surcharge atomique où ce n'est pas nécessaire). Pile Treiber pour les threads en arrière-plan. Chaque contexte obtient l'algorithme correct.",
      },
    },
    {
      feature: 'Canaux asynchrones',
      sub: 'Producteur/consommateur intégré',
      cols: { task: 'BCL (basé sur des classes)', unitask: 'Non borné uniquement', awaitable: '✗', valkarn: 'Borné + Non borné ✦' },
      win: ['valkarn'],
      note: {
        task:    "System.Threading.Channels est basé sur des classes et alloue. Pas intégré avec le PlayerLoop d'Unity — les continuations s'exécutent sur le ThreadPool.",
        unitask: 'UniTask inclut uniquement un canal non borné à consommateur unique. Pas de capacité bornée, pas de contre-pression. Zéro allocation en lecture, mais API limitée.',
        valkarn: 'Canaux bornés (avec capacité et contre-pression) et non bornés. WriteAsync, ReadAsync, TryRead, TryWrite, TryPeek — tous zéro allocation sur le chemin rapide.',
      },
    },
    {
      feature: 'Combinateurs WhenAll / WhenAny',
      sub: 'Coordination de tâches parallèles',
      cols: { task: 'Retourne Task[]', unitask: '✓ (tableaux uniquement)', awaitable: '✗', valkarn: 'Tuple jusqu\'à 8 ✦' },
      win: ['unitask', 'valkarn'],
      note: {
        task:    'Task.WhenAll retourne Task<T[]>, nécessitant un accès par index. Pas de déstructuration de tuple.',
        unitask: "UniTask.WhenAll supporte les tuples typés jusqu'à l'arité 15. Une fonctionnalité solide.",
        valkarn: 'var (tex, sfx, data) = await ValkarnTask.WhenAll(...) — jusqu\'à 8 résultats typés. WhenAny retourne le premier résultat complété avec son index.',
      },
    },
    {
      feature: 'Avalement silencieux des exceptions',
      sub: 'Erreurs non gérées dans async void / fire-and-forget',
      cols: { task: 'AppDomain.UnhandledException', unitask: 'UniTaskScheduler.UnobservedTaskException', awaitable: '⚠ Bug Unity 6', valkarn: 'Gestionnaire configurable ✦' },
      win: ['valkarn'],
      note: {
        awaitable:"Unity 6 avait un bug confirmé où les exceptions levées dans les continuations Awaitable étaient silencieusement avalées sans aucune sortie de log. Corrigé dans Unity 6000.0.5 — les versions 6.x antérieures sont affectées.",
        valkarn:  "ValkarnTaskSettings.UnobservedExceptionHandler est configurable par l'utilisateur. Par défaut : enregistre dans Debug.LogException afin qu'aucune exception ne soit jamais silencieuse.",
      },
    },
  ],

  versusSection: {
    heading: 'Pourquoi faire le changement ?',
    sub:     'Des raisons concrètes, pas du marketing.',
  },

  versus: [
    {
      vs: 'System.Task',
      color: '#ef4444',
      problems: [
        { title: 'Chaque await alloue',         body: "Task<T> est une classe. Chaque méthode async alloue un objet sur le tas, même lorsqu'elle retourne de manière synchrone. Dans une boucle de jeu à 60 Hz qui exécute des milliers d'opérations async, vous payez une taxe GC à chaque frame." },
        { title: 'ThreadPool par défaut',        body: "Les continuations reprennent sur le ThreadPool .NET, pas sur le thread principal d'Unity. Toute interaction avec des GameObjects, des Transforms, ou les APIs Unity nécessite un marshalling explicite via UnitySynchronizationContext — source d'erreurs et verbeux." },
        { title: 'Pas de liaison MonoBehaviour', body: "Rien n'empêche une Task de continuer après la destruction de son MonoBehaviour propriétaire. Le résultat est des MissingReferenceException fantômes, de la corruption d'état, et des bugs qui n'apparaissent que lorsque les scènes sont déchargées sous charge." },
        { title: 'Pas de diagnostics Unity',     body: "Le runtime .NET n'a aucun concept du modèle de durée de vie d'Unity. Zéro règle Roslyn ne détecte les patterns qui cassent les jeux : boucles zombie, accès à des objets détruits, mauvais usage du fire-and-forget." },
      ],
      verdict: "System.Task est le bon outil pour les serveurs .NET. C'est le mauvais outil pour une boucle de jeu en temps réel.",
    },
    {
      vs: 'UniTask',
      color: '#f59e0b',
      problems: [
        { title: "~Zéro ce n'est pas Zéro",                       body: "La promesse phare d'UniTask est \"~Zéro allocation.\" Le tilde est essentiel. Les chemins d'exception et d'annulation allouent toujours — comme tout le monde. Valkarn Tasks fait le même compromis honnêtement et ajoute plus de garanties par-dessus." },
        { title: 'Annulation manuelle — toujours',                 body: "Lier un UniTask à un MonoBehaviour nécessite : un champ CancellationTokenSource, un OnEnable pour l'(ré-)initialiser, un OnDestroy pour annuler et disposer, et propager le token à travers chaque appel async. Valkarn Tasks génère ce pattern entier par la source depuis un seul attribut [AutoCancel]." },
        { title: 'Plante avec Unity Entities',                     body: "Le package Unity Entities appelle PlayerLoop.SetPlayerLoop() à l'initialisation, ce qui écrase les runners enregistrés d'UniTask. Toute tâche en cours à ce moment est silencieusement abandonnée. Il n'y a pas d'avertissement. L'intégration ECS de Valkarn Tasks est spécifiquement conçue pour survivre aux réinitialisations de PlayerLoop." },
        { title: 'Interlocked sur le thread principal',            body: "Le pool d'UniTask appelle Interlocked.CompareExchange à chaque Push et Pop — y compris sur le thread principal où il n'y a pas de vraie contention. Ce sont des opérations atomiques inutiles sur le chemin de code le plus sollicité de votre jeu. Valkarn Tasks utilise un CAS simple sur le thread principal et une pile Treiber sur les threads de travail." },
        { title: "Une seule règle d'analyseur, pas de vérifications structurelles", body: "UniTask inclut UNITASK001 : détection d'oubli d'await. C'est tout. Les boucles zombie (une boucle qui ne se termine jamais parce que l'annulation n'est jamais vérifiée), les tâches à durées de vie mixtes, les doubles-awaits — rien de tout cela n'est détecté. Valkarn Tasks inclut 17 règles couvrant ces patterns structurels." },
        { title: 'Dernière version : octobre 2024',                body: "Le GitHub d'UniTask ne montre aucun développement actif depuis octobre 2024. Unity 6, DOTS 1.x et les futures versions de l'éditeur apportent des changements incompatibles qu'un package non maintenu ne peut pas suivre." },
      ],
      verdict: 'UniTask était à la pointe en 2020. Valkarn Tasks est conçu pour 2025 et au-delà.',
    },
    {
      vs: 'Awaitable',
      color: '#3b82f6',
      problems: [
        { title: '6 timings, pas 16',                          body: "L'Awaitable d'Unity expose 6 hooks de planification : NextFrameAsync, FixedUpdateAsync, EndOfFrameAsync, WaitForSecondsAsync, MainThreadAsync et BackgroundThreadAsync. Valkarn Tasks mappe chacune des 16 phases du PlayerLoop d'Unity — PreUpdate, PostLateUpdate, TimeUpdate, et plus — comme des points d'await de première classe." },
        { title: "Impossible d'attendre la même tâche deux fois", body: "Attendre un Awaitable qui s'est déjà terminé cause soit un deadlock, soit une exception, selon l'état interne. Il n'y a pas de garde. Valkarn Tasks a une protection contre le double-retour sur chaque chemin source : le second await retourne toujours le résultat mis en cache immédiatement." },
        { title: 'Unity 6 avale silencieusement les exceptions', body: "Un bug confirmé d'Unity 6 fait disparaître les exceptions levées dans les continuations Awaitable sans sortie de log, sans trace de pile, sans crash. Corrigé dans 6000.0.5 — ce qui signifie que chaque projet Unity 6.0 à 6.0.4 est affecté. Valkarn Tasks achemine toutes les exceptions non observées via un gestionnaire configurable qui utilise Debug.LogException par défaut." },
        { title: 'Pas de WhenAll, WhenAny, ni de canaux',       body: "Awaitable n'a pas d'API combinatoire. Exécuter trois chargements en parallèle et collecter leurs résultats nécessite une machine d'état manuelle. Valkarn Tasks fournit WhenAll avec déstructuration de tuple jusqu'à l'arité 8, WhenAny, et des canaux asynchrones bornés et non bornés." },
        { title: 'Pas de liaison au cycle de vie',              body: "Awaitable ne fournit aucun mécanisme pour lier la durée de vie d'une tâche à un GameObject. Chaque token d'annulation doit être créé, stocké, propagé à travers les appels, et disposé manuellement." },
      ],
      verdict: 'Awaitable est un simple hook de planification. Valkarn Tasks est un runtime async complet.',
    },
  ],

  cta: {
    heading:    'Publiez plus vite. Allouez moins.',
    p1:         "MIT open source — gratuit pour tous. Licence commerciale optionnelle disponible.",
    p2:         'Une ligne dans votre manifest. Aucun compte requis.',
    btnPrimary: 'Commencer →',
    btnGhost:   'Voir la licence',
  },
};

export default data;
