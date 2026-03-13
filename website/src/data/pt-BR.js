// Português Brasileiro (pt-BR) — dados da página
const data = {
  hero: {
    pill:  'Unity 2023.1+ · MIT · Gratuito para todos',
    h1:    'Async/await',
    h1Grad:'sem alocações.',
    p1:    'Tasks baseadas em structs. Cancelamento gerado por código-fonte.',
    p2:    'Pronto para Burst & ECS.',
    em:    'Zero alocação no caminho feliz.',
    cta:   'Começar →',
  },

  stats: [
    { end: 0,  suffix: '',  label: 'alocações\ncaminho feliz' },
    { end: 16, suffix: '',  label: 'timings do\nPlayerLoop' },
    { end: 17, suffix: '',  label: 'regras do\nanalisador' },
    { end: 5,  suffix: 'x', label: 'pool mais rápido\nque UniTask' },
  ],

  featuresSection: {
    heading: "Construído para o modelo de execução do Unity.",
    sub:     "Não é uma portagem de padrões .NET — cada decisão é Unity em primeiro lugar.",
  },
  features: [
    { icon: '⚡', title: 'Zero Alocação',            desc: 'O ValkarnTask baseado em struct evita pressão no heap em todo caminho de sucesso. Tasks concluídas são gratuitas.' },
    { icon: '🔄', title: 'Cancelamento Automático',   desc: 'Marque a classe como partial. O gerador de código-fonte vincula o cancelamento ao tempo de vida do MonoBehaviour.' },
    { icon: '🧵', title: 'Pool Consciente de Thread',  desc: 'CAS sem bloqueio na thread principal, pilha Treiber em threads de trabalho. Sem operações atômicas desnecessárias.' },
    { icon: '🎯', title: '16 Timings do PlayerLoop',   desc: 'De Initialization a TimeUpdate — controle preciso sobre quando as continuações são retomadas.' },
    { icon: '📡', title: 'Canais Assíncronos',          desc: 'Filas produtor/consumidor limitadas e ilimitadas. WriteAsync, ReadAsync, TryRead — zero alocação.' },
    { icon: '🚀', title: 'Pronto para Burst & ECS',     desc: 'NativeTimerHeap, BurstSchedulerRunner, sistemas ECS assíncronos. Suporte de primeira classe ao Unity DOTS.' },
    { icon: '🔍', title: '17 Regras do Analisador',     desc: 'Loops zumbi, lifetimes misturados, tasks não aguardadas — detectados em tempo de compilação, não em produção.' },
    { icon: '🛡️', title: 'Seguro para IL2CPP',           desc: 'Genéricos explícitos, sem reflexão em runtime, proteção contra stripping via link.xml. Funciona em consoles.' },
  ],

  comparisonSection: {
    heading:    'Comparação de recursos.',
    sub:        '🟢 = melhor na linha · ✦ = exclusivo do Valkarn Tasks · ⓘ = passe o mouse para detalhes',
    featureCol: 'Recurso',
  },

  rows: [
    {
      feature: 'Alocação no sucesso',
      sub: 'Aguardar uma task concluída aloca?',
      cols: { task: 'Sim — Task<T> é uma classe', unitask: 'Não (struct)', awaitable: 'Não (struct)', valkarn: 'Não (struct)' },
      win: ['unitask', 'awaitable', 'valkarn'],
      note: { task: 'Todo método async que retorna Task<T> aloca um objeto no heap, mesmo quando retorna de forma síncrona — um imposto constante de GC em um loop de jogo a 60 Hz.' },
    },
    {
      feature: 'Alocação na falha',
      sub: 'Caminhos de exceção / cancelamento',
      cols: { task: 'Sim', unitask: 'Sim', awaitable: 'Sim', valkarn: 'Sim' },
      win: [],
      note: { unitask: 'O UniTask anuncia "~Zero alocação" — o til é importante. Exceções e cancelamentos ainda alocam, assim como todos os outros.' },
    },
    {
      feature: 'Cancelamento automático ao destruir',
      sub: 'Vinculado ao tempo de vida do MonoBehaviour',
      cols: { task: 'Manual', unitask: 'Manual', awaitable: 'Manual', valkarn: 'Geração de código ✦' },
      win: ['valkarn'],
      note: { valkarn: 'Marque a classe como partial. Um gerador de código conecta um CancellationToken ao OnDestroy — sem campo boilerplate, sem OnEnable/OnDisable, sem cancelamento esquecido.' },
    },
    {
      feature: 'Timings do PlayerLoop',
      sub: 'Precisão de agendamento',
      cols: { task: '1 (ThreadPool)', unitask: '16', awaitable: '6', valkarn: '16' },
      win: ['unitask', 'valkarn'],
      note: {
        task:     'As continuações são executadas no ThreadPool do .NET. Voltar para a thread principal exige marshalling explícito via UnitySynchronizationContext.',
        unitask:  'UniTask e Valkarn Tasks implementam o conjunto completo de 16 timings do PlayerLoop, de Initialization até TimeUpdate.',
        awaitable:'O Awaitable expõe 6 hooks: NextFrameAsync, FixedUpdateAsync, EndOfFrameAsync, WaitForSecondsAsync, MainThreadAsync, BackgroundThreadAsync — não o PlayerLoop completo.',
      },
    },
    {
      feature: 'Compatibilidade com ECS / Entities',
      sub: 'Funciona junto com Unity DOTS',
      cols: { task: 'N/A', unitask: '⚠ Quebra', awaitable: 'Parcial', valkarn: 'Completo ✦' },
      win: ['valkarn'],
      note: {
        unitask:  "O pacote Entities do Unity redefine o PlayerLoop na inicialização, o que limpa os runners registrados do UniTask. Qualquer task agendada antes desse ponto é silenciosamente perdida.",
        awaitable:'O Awaitable funciona em sistemas ECS, mas não tem NativeTimerHeap, nem BurstSchedulerRunner, nem helpers para sistemas ECS assíncronos.',
        valkarn:  'Suporte completo a DOTS: NativeTimerHeap para agendamento compatível com Burst, BurstSchedulerRunner, AsyncSystemUtilities para sistemas ECS, ponte para JobHandle.',
      },
    },
    {
      feature: 'Segurança de double-await',
      sub: 'Aguardar a mesma task duas vezes',
      cols: { task: '✓ Seguro', unitask: '✗ Indefinido', awaitable: '✗ Deadlock', valkarn: '✓ Seguro' },
      win: ['task', 'valkarn'],
      note: {
        unitask:  'Aguardar um UniTask mais de uma vez é explicitamente comportamento indefinido. Na prática causa deadlock ou corrompe o estado do pool.',
        awaitable:'Aguardar um Awaitable duas vezes causa deadlock ou lança exceção, dependendo se já foi concluído. Não há proteção.',
        valkarn:  'Proteções contra double-return em todos os caminhos de fonte. O segundo await em uma task concluída retorna o resultado em cache imediatamente.',
      },
    },
    {
      feature: 'Diagnósticos em tempo de compilação',
      sub: 'Regras do analisador Roslyn',
      cols: { task: '0 regras', unitask: '1 regra', awaitable: '0 regras', valkarn: '17 regras ✦' },
      win: ['valkarn'],
      note: {
        unitask: 'O UniTask inclui uma regra (UNITASK001): avisa quando você esquece de aguardar um valor de retorno UniTask. Sem detecção de loops zumbi, lifetimes misturados ou bugs estruturais.',
        valkarn: 'Detecta loops zumbi, lifetimes misturados, tasks não aguardadas, double-awaits, fire-and-forget inadequado, marcadores de auto-cancel ausentes — antes do build ser enviado.',
      },
    },
    {
      feature: 'Pool consciente de thread',
      sub: 'Sem bloqueio na thread principal',
      cols: { task: 'N/A', unitask: 'Interlocked (todas as threads)', awaitable: 'N/A', valkarn: 'CAS / Treiber ✦' },
      win: ['valkarn'],
      note: {
        unitask: "O pool do UniTask usa Interlocked.CompareExchange em cada Push e Pop — incluindo a thread principal onde nunca há contenção real. Overhead atômico desnecessário em cada conclusão de task.",
        valkarn: "CAS sem bloqueio na thread principal (sem overhead atômico onde não é necessário). Pilha Treiber para threads de background. Cada contexto recebe o algoritmo correto.",
      },
    },
    {
      feature: 'Canais assíncronos',
      sub: 'Produtor/consumidor integrado',
      cols: { task: 'BCL (baseado em classe)', unitask: 'Apenas ilimitado', awaitable: '✗', valkarn: 'Limitado + Ilimitado ✦' },
      win: ['valkarn'],
      note: {
        task:    "System.Threading.Channels é baseado em classe e aloca. Não integrado com o PlayerLoop do Unity — as continuações são executadas no ThreadPool.",
        unitask: 'O UniTask inclui apenas um canal ilimitado de consumidor único. Sem capacidade limitada, sem backpressure. Zero alocação nas leituras, mas API limitada.',
        valkarn: 'Canais limitados (com capacidade e backpressure) e ilimitados. WriteAsync, ReadAsync, TryRead, TryWrite, TryPeek — todos zero alocação no caminho rápido.',
      },
    },
    {
      feature: 'Combinadores WhenAll / WhenAny',
      sub: 'Coordenação paralela de tasks',
      cols: { task: 'Retorna Task[]', unitask: '✓ (apenas arrays)', awaitable: '✗', valkarn: 'Tupla até 8 ✦' },
      win: ['unitask', 'valkarn'],
      note: {
        task:    'Task.WhenAll retorna Task<T[]>, exigindo acesso por índice. Sem desestruturação de tupla.',
        unitask: 'UniTask.WhenAll suporta tuplas tipadas até aridade 15. Um recurso forte.',
        valkarn: 'var (tex, sfx, data) = await ValkarnTask.WhenAll(...) — até 8 resultados tipados. WhenAny retorna o primeiro resultado concluído com seu índice.',
      },
    },
    {
      feature: 'Supressão silenciosa de exceções',
      sub: 'Erros não tratados em async void / fire-and-forget',
      cols: { task: 'AppDomain.UnhandledException', unitask: 'UniTaskScheduler.UnobservedTaskException', awaitable: '⚠ Bug no Unity 6', valkarn: 'Handler configurável ✦' },
      win: ['valkarn'],
      note: {
        awaitable:'O Unity 6 tinha um bug confirmado onde exceções lançadas dentro de continuações do Awaitable eram suprimidas silenciosamente sem nenhuma saída de log. Corrigido no Unity 6000.0.5 — versões anteriores do 6.x são afetadas.',
        valkarn:  'ValkarnTaskSettings.UnobservedExceptionHandler é configurável pelo usuário. Padrão: registra em Debug.LogException para que nenhuma exceção seja silenciosa.',
      },
    },
  ],

  versusSection: {
    heading: 'Por que fazer a mudança?',
    sub:     'Razões concretas, não texto de marketing.',
  },

  versus: [
    {
      vs: 'System.Task',
      color: '#ef4444',
      problems: [
        { title: 'Todo await aloca',              body: "Task<T> é uma classe. Todo método async aloca um objeto no heap, mesmo quando retorna de forma síncrona. Em um loop de jogo a 60 Hz que executa milhares de operações assíncronas, você paga um imposto de GC em cada frame." },
        { title: 'ThreadPool por padrão',         body: "As continuações são retomadas no ThreadPool do .NET, não na thread principal do Unity. Qualquer interação com GameObjects, Transforms ou APIs do Unity requer marshalling explícito via UnitySynchronizationContext — propenso a erros e verboso." },
        { title: 'Sem vínculo ao MonoBehaviour',  body: "Nada impede que uma Task continue após seu MonoBehaviour ser destruído. O resultado são exceções fantasmas de referência nula, corrupção de estado e bugs que aparecem apenas quando cenas são descarregadas sob carga." },
        { title: 'Sem diagnósticos do Unity',     body: "O runtime do .NET não tem conceito do modelo de tempo de vida do Unity. Zero regras Roslyn detectam os padrões que quebram jogos: loops zumbi, acesso a objetos destruídos, uso indevido de fire-and-forget." },
      ],
      verdict: 'System.Task é a ferramenta certa para servidores .NET. É a ferramenta errada para um loop de jogo em tempo real.',
    },
    {
      vs: 'UniTask',
      color: '#f59e0b',
      problems: [
        { title: '~Zero não é Zero',                          body: 'A afirmação principal do UniTask é "~Zero alocação." O til é essencial. Caminhos de exceção e cancelamento ainda alocam — assim como todos os outros. O Valkarn Tasks faz a mesma troca honestamente e adiciona mais garantias por cima.' },
        { title: 'Cancelamento manual — sempre',              body: 'Vincular um UniTask a um MonoBehaviour requer: um campo CancellationTokenSource, um OnEnable para (re-)inicializá-lo, um OnDestroy para cancelar e descartá-lo, e passar o token por cada chamada async. O Valkarn Tasks gera esse padrão inteiro a partir de um único atributo [AutoCancel].' },
        { title: 'Quebra com Unity Entities',                 body: "O pacote Entities do Unity chama PlayerLoop.SetPlayerLoop() na inicialização, o que substitui os runners registrados do UniTask. Qualquer task em andamento naquele momento é silenciosamente descartada. Não há aviso. A integração ECS do Valkarn Tasks é especificamente projetada para sobreviver a redefinições do PlayerLoop." },
        { title: 'Interlocked na thread principal',           body: "O pool do UniTask chama Interlocked.CompareExchange em cada Push e Pop — incluindo a thread principal onde não há contenção real. Estas são operações atômicas desnecessárias no caminho de código mais frequente do seu jogo. O Valkarn Tasks usa CAS simples na thread principal e uma pilha Treiber em threads de trabalho." },
        { title: 'Uma regra de analisador, sem verificações estruturais', body: 'O UniTask inclui UNITASK001: detecção de await esquecido. Só isso. Loops zumbi (um loop que nunca sai porque o cancelamento nunca é verificado), tasks de lifetime misto, double-awaits — nada disso é detectado. O Valkarn Tasks inclui 17 regras que cobrem esses padrões estruturais.' },
        { title: 'Último lançamento: outubro de 2024',        body: "O GitHub do UniTask não mostra desenvolvimento ativo desde outubro de 2024. Unity 6, DOTS 1.x e futuras versões do editor trazem mudanças que um pacote sem manutenção não consegue acompanhar." },
      ],
      verdict: 'UniTask era estado da arte em 2020. Valkarn Tasks é construído para 2025 e além.',
    },
    {
      vs: 'Awaitable',
      color: '#3b82f6',
      problems: [
        { title: '6 timings, não 16',                          body: "O Awaitable do Unity expõe 6 hooks de agendamento: NextFrameAsync, FixedUpdateAsync, EndOfFrameAsync, WaitForSecondsAsync, MainThreadAsync e BackgroundThreadAsync. O Valkarn Tasks mapeia cada uma das 16 fases do PlayerLoop do Unity — PreUpdate, PostLateUpdate, TimeUpdate e mais — como pontos de await de primeira classe." },
        { title: 'Não é possível aguardar a mesma task duas vezes', body: "Aguardar um Awaitable que já foi concluído causa deadlock ou lança exceção, dependendo do estado interno. Não há proteção. O Valkarn Tasks tem proteção de double-return em todo caminho de fonte: o segundo await sempre retorna o resultado em cache imediatamente." },
        { title: 'Unity 6 suprime exceções silenciosamente',  body: 'Um bug confirmado do Unity 6 faz com que exceções lançadas dentro de continuações do Awaitable desapareçam sem saída de log, sem rastreamento de pilha, sem crash. Corrigido na versão 6000.0.5 — o que significa que todos os projetos Unity 6.0 a 6.0.4 são afetados. O Valkarn Tasks roteia todas as exceções não observadas por um handler configurável que tem como padrão Debug.LogException.' },
        { title: 'Sem WhenAll, WhenAny ou canais',              body: 'O Awaitable não tem API de combinadores. Executar três carregamentos em paralelo e coletar seus resultados requer uma máquina de estados manual. O Valkarn Tasks fornece WhenAll com desestruturação de tupla até aridade 8, WhenAny, e canais assíncronos limitados e ilimitados.' },
        { title: 'Sem vínculo ao ciclo de vida',                body: "O Awaitable não fornece mecanismo para vincular o tempo de vida de uma task a um GameObject. Todo token de cancelamento deve ser criado, armazenado, passado por chamadas e descartado manualmente." },
      ],
      verdict: 'Awaitable é um hook de agendamento simples. Valkarn Tasks é um runtime assíncrono completo.',
    },
  ],

  cta: {
    heading:    'Entregue mais rápido. Aloque menos.',
    p1:         'MIT open source — gratuito para todos. Licença comercial opcional disponível.',
    p2:         'Uma linha no seu manifest. Sem necessidade de conta.',
    btnPrimary: 'Começar →',
    btnGhost:   'Ver Licença',
  },
};

export default data;
