// Español — datos de la página en locale español
const data = {
  hero: {
    pill:  'Unity 2023.1+ · Gratuito para indie',
    h1:    'Async/await',
    h1Grad:'sin basura.',
    p1:    'Tareas basadas en struct. Cancelación generada por código fuente.',
    p2:    'Preparado para Burst y ECS.',
    em:    'Cero asignaciones en el camino feliz.',
    cta:   'Comenzar →',
  },

  stats: [
    { end: 0,  suffix: '',  label: 'asignaciones\ncamino feliz' },
    { end: 16, suffix: '',  label: 'timings de\nPlayerLoop' },
    { end: 17, suffix: '',  label: 'reglas del\nanalizador' },
    { end: 5,  suffix: 'x', label: 'grupo más rápido\nvs UniTask' },
  ],

  featuresSection: {
    heading: "Construido para el modelo de ejecución de Unity.",
    sub:     "No es un port de patrones .NET — cada decisión es Unity primero.",
  },
  features: [
    { icon: '⚡', title: 'Cero Asignaciones',          desc: 'ValkarnTask basado en struct evita la presión en el montón en cada camino de éxito. Las tareas completadas son gratuitas.' },
    { icon: '🔄', title: 'Auto-Cancel al Destruir',    desc: 'Marca la clase como partial. El generador de código fuente vincula la cancelación al tiempo de vida del MonoBehaviour.' },
    { icon: '🧵', title: 'Grupo Consciente de Hilos',  desc: 'CAS sin bloqueo en el hilo principal, pila Treiber en hilos de trabajo. Sin operaciones atómicas innecesarias.' },
    { icon: '🎯', title: '16 Timings de PlayerLoop',   desc: 'Desde Initialization hasta TimeUpdate — control preciso sobre cuándo se reanudan las continuaciones.' },
    { icon: '📡', title: 'Canales Asíncronos',          desc: 'Colas de productor/consumidor acotadas y no acotadas. WriteAsync, ReadAsync, TryRead — cero asignaciones.' },
    { icon: '🚀', title: 'Preparado para Burst y ECS', desc: 'NativeTimerHeap, BurstSchedulerRunner, sistemas ECS asíncronos. Soporte de primera clase para Unity DOTS.' },
    { icon: '🔍', title: '17 Reglas del Analizador',   desc: 'Bucles zombie, tiempos de vida mixtos, tareas sin esperar — detectados en tiempo de compilación, no en producción.' },
    { icon: '🛡️', title: 'Seguro para IL2CPP',          desc: 'Genéricos explícitos, sin reflexión en tiempo de ejecución, protección contra eliminación con link.xml. Se envía a consolas.' },
  ],

  comparisonSection: {
    heading:    'Comparación de características.',
    sub:        '🟢 = mejor en la fila · ✦ = exclusivo de Valkarn Tasks · ⓘ = pasar el cursor para detalles',
    featureCol: 'Característica',
  },

  rows: [
    {
      feature: 'Asignación en éxito',
      sub: '¿Esperar una tarea completada asigna memoria?',
      cols: { task: 'Sí — Task<T> es una clase', unitask: 'No (struct)', awaitable: 'No (struct)', valkarn: 'No (struct)' },
      win: ['unitask', 'awaitable', 'valkarn'],
      note: { task: 'Cada método async que devuelve Task<T> asigna un objeto en el montón, incluso cuando retorna de forma síncrona — un impuesto de GC constante en un bucle de juego a 60 Hz.' },
    },
    {
      feature: 'Asignación en fallo',
      sub: 'Caminos de excepción / cancelación',
      cols: { task: 'Sí', unitask: 'Sí', awaitable: 'Sí', valkarn: 'Sí' },
      win: [],
      note: { unitask: 'UniTask anuncia "~Cero asignaciones" — la tilde importa. Las excepciones y cancelaciones siguen asignando, igual que todos los demás.' },
    },
    {
      feature: 'Auto-cancel al Destruir',
      sub: 'Vinculado al tiempo de vida del MonoBehaviour',
      cols: { task: 'Manual', unitask: 'Manual', awaitable: 'Manual', valkarn: 'Generado por código fuente ✦' },
      win: ['valkarn'],
      note: { valkarn: 'Marca la clase como partial. Un generador de código fuente conecta un CancellationToken a OnDestroy — sin campo boilerplate, sin OnEnable/OnDisable, sin cancelación olvidada.' },
    },
    {
      feature: 'Timings de PlayerLoop',
      sub: 'Precisión de programación',
      cols: { task: '1 (ThreadPool)', unitask: '16', awaitable: '6', valkarn: '16' },
      win: ['unitask', 'valkarn'],
      note: {
        task:     'Las continuaciones se ejecutan en el ThreadPool de .NET. Volver al hilo principal requiere marshalling explícito a través de UnitySynchronizationContext.',
        unitask:  'UniTask y Valkarn Tasks implementan el conjunto completo de 16 timings de PlayerLoop, desde Initialization hasta TimeUpdate.',
        awaitable:'Awaitable expone 6 hooks: NextFrameAsync, FixedUpdateAsync, EndOfFrameAsync, WaitForSecondsAsync, MainThreadAsync, BackgroundThreadAsync — no el PlayerLoop completo.',
      },
    },
    {
      feature: 'Compatibilidad con ECS / Entities',
      sub: 'Funciona junto a Unity DOTS',
      cols: { task: 'N/A', unitask: '⚠ Rompe', awaitable: 'Parcial', valkarn: 'Completo ✦' },
      win: ['valkarn'],
      note: {
        unitask:  "El paquete Entities de Unity restablece el PlayerLoop en la inicialización, lo que borra los ejecutores registrados de UniTask. Cualquier tarea programada antes de ese punto se pierde silenciosamente.",
        awaitable:'Awaitable funciona en sistemas ECS pero no tiene NativeTimerHeap, ni BurstSchedulerRunner, ni ayudantes para sistemas ECS asíncronos.',
        valkarn:  'Soporte DOTS completo: NativeTimerHeap para programación compatible con Burst, BurstSchedulerRunner, AsyncSystemUtilities para sistemas ECS, puente de JobHandle.',
      },
    },
    {
      feature: 'Seguridad ante doble espera',
      sub: 'Esperar la misma tarea dos veces',
      cols: { task: '✓ Seguro', unitask: '✗ Indefinido', awaitable: '✗ Bloqueo', valkarn: '✓ Seguro' },
      win: ['task', 'valkarn'],
      note: {
        unitask:  'Esperar un UniTask más de una vez es comportamiento explícitamente indefinido. En la práctica causa un bloqueo o corrompe el estado del grupo.',
        awaitable:'Esperar un Awaitable dos veces produce un bloqueo o lanza una excepción, dependiendo de si ya se completó. No hay protección.',
        valkarn:  'Protecciones de doble retorno en todos los caminos de fuente. La segunda espera en una tarea completada devuelve el resultado en caché inmediatamente.',
      },
    },
    {
      feature: 'Diagnósticos en tiempo de compilación',
      sub: 'Reglas del analizador Roslyn',
      cols: { task: '0 reglas', unitask: '1 regla', awaitable: '0 reglas', valkarn: '17 reglas ✦' },
      win: ['valkarn'],
      note: {
        unitask: 'UniTask incluye una regla (UNITASK001): advierte cuando olvidas esperar un valor de retorno UniTask. Sin detección de bucles zombie, tiempos de vida mixtos o errores estructurales.',
        valkarn: 'Detecta bucles zombie, tiempos de vida mixtos, tareas sin esperar, dobles esperas, uso incorrecto de fire-and-forget, marcadores de auto-cancel faltantes — antes de que se envíe la compilación.',
      },
    },
    {
      feature: 'Grupo consciente de hilos',
      sub: 'Sin bloqueo en el hilo principal',
      cols: { task: 'N/A', unitask: 'Interlocked (todos los hilos)', awaitable: 'N/A', valkarn: 'CAS / Treiber ✦' },
      win: ['valkarn'],
      note: {
        unitask: "El grupo de UniTask usa Interlocked.CompareExchange en cada Push y Pop — incluyendo el hilo principal donde nunca hay contención real. Sobrecarga atómica innecesaria en cada finalización de tarea.",
        valkarn: "CAS sin bloqueo en el hilo principal (sin sobrecarga atómica donde no se necesita). Pila Treiber para hilos en segundo plano. Cada contexto obtiene el algoritmo correcto.",
      },
    },
    {
      feature: 'Canales asíncronos',
      sub: 'Productor/consumidor integrado',
      cols: { task: 'BCL (basado en clases)', unitask: 'Solo no acotado', awaitable: '✗', valkarn: 'Acotado + No acotado ✦' },
      win: ['valkarn'],
      note: {
        task:    "System.Threading.Channels está basado en clases y asigna memoria. No está integrado con el PlayerLoop de Unity — las continuaciones se ejecutan en el ThreadPool.",
        unitask: 'UniTask incluye únicamente un canal no acotado de consumidor único. Sin capacidad acotada, sin contrapresión. Cero asignaciones en lecturas, pero API limitada.',
        valkarn: 'Canales acotados (con capacidad y contrapresión) y no acotados. WriteAsync, ReadAsync, TryRead, TryWrite, TryPeek — todos cero asignaciones en el camino rápido.',
      },
    },
    {
      feature: 'Combinadores WhenAll / WhenAny',
      sub: 'Coordinación de tareas en paralelo',
      cols: { task: 'Devuelve Task[]', unitask: '✓ (solo arrays)', awaitable: '✗', valkarn: 'Tupla hasta 8 ✦' },
      win: ['unitask', 'valkarn'],
      note: {
        task:    'Task.WhenAll devuelve Task<T[]>, requiriendo acceso por índice. Sin desestructuración de tuplas.',
        unitask: 'UniTask.WhenAll admite tuplas tipadas hasta aridad 15. Una característica sólida.',
        valkarn: 'var (tex, sfx, data) = await ValkarnTask.WhenAll(...) — hasta 8 resultados tipados. WhenAny devuelve el primer resultado completado con su índice.',
      },
    },
    {
      feature: 'Excepciones silenciosas',
      sub: 'Errores no manejados en async void / fire-and-forget',
      cols: { task: 'AppDomain.UnhandledException', unitask: 'UniTaskScheduler.UnobservedTaskException', awaitable: '⚠ Error en Unity 6', valkarn: 'Manejador configurable ✦' },
      win: ['valkarn'],
      note: {
        awaitable:'Unity 6 tuvo un error confirmado donde las excepciones lanzadas dentro de las continuaciones de Awaitable eran ignoradas silenciosamente sin salida de registro. Corregido en Unity 6000.0.5 — las versiones anteriores de 6.x están afectadas.',
        valkarn:  'ValkarnTaskSettings.UnobservedExceptionHandler es configurable por el usuario. Por defecto: registra en Debug.LogException para que ninguna excepción sea nunca silenciosa.',
      },
    },
  ],

  versusSection: {
    heading: '¿Por qué hacer el cambio?',
    sub:     'Razones concretas, no texto de marketing.',
  },

  versus: [
    {
      vs: 'System.Task',
      color: '#ef4444',
      problems: [
        { title: 'Cada await asigna memoria',        body: "Task<T> es una clase. Cada método async asigna un objeto en el montón, incluso cuando retorna de forma síncrona. En un bucle de juego a 60 Hz que ejecuta miles de operaciones async, pagas un impuesto de GC en cada fotograma." },
        { title: 'ThreadPool por defecto',            body: "Las continuaciones se reanudan en el ThreadPool de .NET, no en el hilo principal de Unity. Cualquier interacción con GameObjects, Transforms o APIs de Unity requiere marshalling explícito a través de UnitySynchronizationContext — propenso a errores y verboso." },
        { title: 'Sin vinculación a MonoBehaviour',   body: "Nada impide que una Task continúe después de que su MonoBehaviour propietario sea destruido. El resultado son excepciones fantasmas de referencia nula, corrupción de estado y errores que solo aparecen cuando las escenas se descargan bajo carga." },
        { title: 'Sin diagnósticos de Unity',         body: "El runtime de .NET no tiene concepto del modelo de tiempo de vida de Unity. Cero reglas Roslyn capturan los patrones que rompen los juegos: bucles zombie, acceso a objetos destruidos, uso incorrecto de fire-and-forget." },
      ],
      verdict: 'System.Task es la herramienta correcta para servidores .NET. Es la herramienta incorrecta para un bucle de juego en tiempo real.',
    },
    {
      vs: 'UniTask',
      color: '#f59e0b',
      problems: [
        { title: '~Cero no es Cero',                                          body: 'El titular de UniTask es "~Cero asignaciones." La tilde es significativa. Los caminos de excepción y cancelación siguen asignando — igual que todos los demás. Valkarn Tasks hace el mismo compromiso honestamente y añade más garantías encima.' },
        { title: 'Cancelación manual — siempre',                              body: 'Vincular un UniTask a un MonoBehaviour requiere: un campo CancellationTokenSource, un OnEnable para (re-)inicializarlo, un OnDestroy para cancelarlo y desecharlo, y pasar el token a través de cada llamada async. Valkarn Tasks genera todo este patrón desde un único atributo [AutoCancel].' },
        { title: 'Rompe con Unity Entities',                                  body: "El paquete Entities de Unity llama a PlayerLoop.SetPlayerLoop() en la inicialización, lo que sobreescribe los ejecutores registrados de UniTask. Cualquier tarea en vuelo en ese momento se descarta silenciosamente. No hay advertencia. La integración ECS de Valkarn Tasks está específicamente diseñada para sobrevivir los reinicios del PlayerLoop." },
        { title: 'Interlocked en el hilo principal',                          body: "El grupo de UniTask llama a Interlocked.CompareExchange en cada Push y Pop — incluyendo el hilo principal donde no hay contención real. Estas son operaciones atómicas innecesarias en el camino de código más caliente de tu juego. Valkarn Tasks usa CAS simple en el hilo principal y una pila Treiber en hilos de trabajo." },
        { title: 'Una regla del analizador, sin verificaciones estructurales', body: 'UniTask incluye UNITASK001: detección de olvidarse de esperar. Eso es todo. Bucles zombie (un bucle que nunca termina porque la cancelación nunca se verifica), tareas de tiempo de vida mixto, dobles esperas — ninguno de estos se detecta. Valkarn Tasks incluye 17 reglas que cubren estos patrones estructurales.' },
        { title: 'Último lanzamiento: octubre de 2024',                       body: "El GitHub de UniTask no muestra desarrollo activo desde octubre de 2024. Unity 6, DOTS 1.x y futuras versiones del editor traen cambios incompatibles que un paquete sin mantenimiento no puede rastrear." },
      ],
      verdict: 'UniTask era el estado del arte en 2020. Valkarn Tasks está construido para 2025 y más allá.',
    },
    {
      vs: 'Awaitable',
      color: '#3b82f6',
      problems: [
        { title: '6 timings, no 16',                               body: "El Awaitable de Unity expone 6 hooks de programación: NextFrameAsync, FixedUpdateAsync, EndOfFrameAsync, WaitForSecondsAsync, MainThreadAsync y BackgroundThreadAsync. Valkarn Tasks mapea cada una de las 16 fases del PlayerLoop de Unity — PreUpdate, PostLateUpdate, TimeUpdate y más — como puntos de espera de primera clase." },
        { title: 'No se puede esperar la misma tarea dos veces',   body: "Esperar un Awaitable que ya se ha completado provoca un bloqueo o lanza una excepción, dependiendo del estado interno. No hay protección. Valkarn Tasks tiene protección de doble retorno en cada camino de fuente: la segunda espera siempre devuelve el resultado en caché inmediatamente." },
        { title: 'Unity 6 ignora silenciosamente las excepciones', body: 'Un error confirmado de Unity 6 hace que las excepciones lanzadas dentro de las continuaciones de Awaitable desaparezcan sin salida de registro, sin seguimiento de pila, sin fallo. Corregido en 6000.0.5 — lo que significa que todos los proyectos de Unity 6.0 hasta 6.0.4 están afectados. Valkarn Tasks enruta todas las excepciones no observadas a través de un manejador configurable que por defecto usa Debug.LogException.' },
        { title: 'Sin WhenAll, WhenAny ni canales',                body: 'Awaitable no tiene API de combinadores. Ejecutar tres cargas en paralelo y recoger sus resultados requiere una máquina de estados manual. Valkarn Tasks proporciona WhenAll con desestructuración de tuplas hasta aridad 8, WhenAny, y canales asíncronos acotados y no acotados.' },
        { title: 'Sin vinculación de ciclo de vida',               body: "Awaitable no proporciona ningún mecanismo para vincular el tiempo de vida de una tarea a un GameObject. Cada token de cancelación debe crearse, almacenarse, pasarse a través de las llamadas y desecharse manualmente." },
      ],
      verdict: 'Awaitable es un gancho de programación delgado. Valkarn Tasks es un runtime async completo.',
    },
  ],

  cta: {
    heading:    'Envía más rápido. Asigna menos.',
    p1:         'Gratuito para individuos y estudios con ingresos anuales inferiores a $1M.',
    p2:         'Una línea en tu manifiesto. Sin cuenta requerida.',
    btnPrimary: 'Comenzar →',
    btnGhost:   'Ver Licencia',
  },
};

export default data;
