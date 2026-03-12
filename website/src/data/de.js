// Deutsch — Übersetzung der Seitendaten
const data = {
  hero: {
    pill:  'Unity 2023.1+ · Kostenlos für Indie',
    h1:    'Async/await',
    h1Grad:'ohne Garbage.',
    p1:    'Struct-basierte Tasks. Quellgenerierte Abbruchlogik.',
    p2:    'Burst & ECS bereit.',
    em:    'Null Allokationen auf dem Erfolgspfad.',
    cta:   'Jetzt starten →',
  },

  stats: [
    { end: 0,  suffix: '',  label: 'Allokationen\nErfolgspfad' },
    { end: 16, suffix: '',  label: 'PlayerLoop-\nZeitpunkte' },
    { end: 17, suffix: '',  label: 'Analyzer-\nRegeln' },
    { end: 5,  suffix: 'x', label: 'schnellerer Pool\nvs UniTask' },
  ],

  featuresSection: {
    heading: "Entwickelt für Unitys Ausführungsmodell.",
    sub:     "Kein Port von .NET-Mustern — jede Entscheidung ist Unity-first.",
  },
  features: [
    { icon: '⚡', title: 'Null Allokationen',          desc: 'Struct-basierte VlkTask vermeidet Heap-Druck auf jedem Erfolgspfad. Abgeschlossene Tasks sind kostenlos.' },
    { icon: '🔄', title: 'Auto-Abbruch bei Destroy',   desc: 'Klasse als partial markieren. Der Quellgenerator bindet den Abbruch an die MonoBehaviour-Lebensdauer.' },
    { icon: '🧵', title: 'Thread-bewusster Pool',       desc: 'Sperrfreies CAS auf dem Haupt-Thread, Treiber-Stack auf Worker-Threads. Keine unnötigen Atomics.' },
    { icon: '🎯', title: '16 PlayerLoop-Zeitpunkte',    desc: 'Von Initialization bis TimeUpdate — präzise Kontrolle darüber, wann Fortsetzungen fortgesetzt werden.' },
    { icon: '📡', title: 'Asynchrone Channels',          desc: 'Begrenzte und unbegrenzte Producer/Consumer-Warteschlangen. WriteAsync, ReadAsync, TryRead — null Allokationen.' },
    { icon: '🚀', title: 'Burst & ECS bereit',           desc: 'NativeTimerHeap, BurstSchedulerRunner, asynchrone ECS-Systeme. Erstklassige Unity DOTS-Unterstützung.' },
    { icon: '🔍', title: '17 Analyzer-Regeln',           desc: 'Zombie-Schleifen, gemischte Lebensdauern, nicht abgewartete Tasks — zur Kompilierzeit erkannt, nicht in der Produktion.' },
    { icon: '🛡️', title: 'IL2CPP-sicher',               desc: 'Explizite Generics, keine Laufzeit-Reflexion, link.xml-Stripping-Schutz. Lieferbar auf Konsolen.' },
  ],

  comparisonSection: {
    heading:    'Funktionsvergleich.',
    sub:        '🟢 = Bestes in der Zeile · ✦ = Einzigartig in Valkarn Tasks · ⓘ = Hover für Details',
    featureCol: 'Funktion',
  },

  rows: [
    {
      feature: 'Allokation bei Erfolg',
      sub: 'Alloziert das Abwarten einer abgeschlossenen Task?',
      cols: { task: 'Ja — Task<T> ist eine Klasse', unitask: 'Nein (Struct)', awaitable: 'Nein (Struct)', valkarn: 'Nein (Struct)' },
      win: ['unitask', 'awaitable', 'valkarn'],
      note: { task: 'Jede asynchrone Methode, die Task<T> zurückgibt, alloziert ein Heap-Objekt, selbst wenn sie synchron zurückkehrt — eine konstante GC-Steuer in einer 60-Hz-Spielschleife.' },
    },
    {
      feature: 'Allokation bei Fehler',
      sub: 'Ausnahme- / Abbruchpfade',
      cols: { task: 'Ja', unitask: 'Ja', awaitable: 'Ja', valkarn: 'Ja' },
      win: [],
      note: { unitask: 'UniTask wirbt mit „~Null Allokationen" — die Tilde ist wichtig. Ausnahmen und Abbrüche allozieren weiterhin, wie bei allen anderen.' },
    },
    {
      feature: 'Auto-Abbruch bei Destroy',
      sub: 'An die MonoBehaviour-Lebensdauer gebunden',
      cols: { task: 'Manuell', unitask: 'Manuell', awaitable: 'Manuell', valkarn: 'Quellgeneriert ✦' },
      win: ['valkarn'],
      note: { valkarn: 'Klasse als partial markieren. Ein Quellgenerator verbindet ein CancellationToken mit OnDestroy — kein Boilerplate-Feld, kein OnEnable/OnDisable, kein vergessener Abbruch.' },
    },
    {
      feature: 'PlayerLoop-Zeitpunkte',
      sub: 'Planungsgenauigkeit',
      cols: { task: '1 (ThreadPool)', unitask: '16', awaitable: '6', valkarn: '16' },
      win: ['unitask', 'valkarn'],
      note: {
        task:     'Fortsetzungen laufen auf dem .NET ThreadPool. Das Zurückkehren zum Haupt-Thread erfordert explizites Marshalling über UnitySynchronizationContext.',
        unitask:  'UniTask und Valkarn Tasks implementieren beide den vollständigen Satz von 16 PlayerLoop-Zeitpunkten, von Initialization bis TimeUpdate.',
        awaitable:'Awaitable bietet 6 Hooks: NextFrameAsync, FixedUpdateAsync, EndOfFrameAsync, WaitForSecondsAsync, MainThreadAsync, BackgroundThreadAsync — nicht den vollständigen PlayerLoop.',
      },
    },
    {
      feature: 'ECS / Entities-Kompatibilität',
      sub: 'Funktioniert neben Unity DOTS',
      cols: { task: 'N/A', unitask: '⚠ Bricht', awaitable: 'Teilweise', valkarn: 'Vollständig ✦' },
      win: ['valkarn'],
      note: {
        unitask:  "Unitys Entities-Paket setzt den PlayerLoop bei der Initialisierung zurück, was UniTasks registrierte Runner löscht. Jeder Task, der vor diesem Zeitpunkt geplant wurde, geht lautlos verloren.",
        awaitable:'Awaitable funktioniert in ECS-Systemen, hat aber keinen NativeTimerHeap, keinen BurstSchedulerRunner und keine asynchronen ECS-System-Hilfsmethoden.',
        valkarn:  'Vollständige DOTS-Unterstützung: NativeTimerHeap für Burst-kompatible Planung, BurstSchedulerRunner, AsyncSystemUtilities für ECS-Systeme, JobHandle-Bridge.',
      },
    },
    {
      feature: 'Doppeltes-Await-Sicherheit',
      sub: 'Dieselbe Task zweimal abwarten',
      cols: { task: '✓ Sicher', unitask: '✗ Undefiniert', awaitable: '✗ Deadlock', valkarn: '✓ Sicher' },
      win: ['task', 'valkarn'],
      note: {
        unitask:  'Eine UniTask mehr als einmal abzuwarten ist ausdrücklich undefiniertes Verhalten. In der Praxis verursacht es einen Deadlock oder korrumpiert den Pool-Zustand.',
        awaitable:'Ein Awaitable zweimal abzuwarten führt zu einem Deadlock oder einem Fehler, je nachdem ob es bereits abgeschlossen ist. Es gibt keine Schutzmaßnahme.',
        valkarn:  'Doppeltes-Rückgabe-Schutz auf allen Quellpfaden. Das zweite Await auf einer abgeschlossenen Task gibt sofort das zwischengespeicherte Ergebnis zurück.',
      },
    },
    {
      feature: 'Kompilierzeitdiagnose',
      sub: 'Roslyn-Analyzer-Regeln',
      cols: { task: '0 Regeln', unitask: '1 Regel', awaitable: '0 Regeln', valkarn: '17 Regeln ✦' },
      win: ['valkarn'],
      note: {
        unitask: 'UniTask liefert eine Regel (UNITASK001): warnt, wenn das Abwarten eines UniTask-Rückgabewerts vergessen wird. Keine Erkennung von Zombie-Schleifen, gemischten Lebensdauern oder strukturellen Fehlern.',
        valkarn: 'Erkennt Zombie-Schleifen, gemischte Lebensdauern, nicht abgewartete Tasks, doppelte Awaits, unsachgemäßes Fire-and-Forget, fehlende Auto-Abbruch-Markierungen — vor dem Build-Abschluss.',
      },
    },
    {
      feature: 'Thread-bewusster Pool',
      sub: 'Sperrfrei auf dem Haupt-Thread',
      cols: { task: 'N/A', unitask: 'Interlocked (alle Threads)', awaitable: 'N/A', valkarn: 'CAS / Treiber ✦' },
      win: ['valkarn'],
      note: {
        unitask: "UniTasks Pool verwendet Interlocked.CompareExchange bei jedem Push und Pop — auch auf dem Haupt-Thread, wo nie echte Konkurrenz besteht. Unnötiger Atomic-Overhead bei jedem Task-Abschluss.",
        valkarn: "Sperrfreies CAS auf dem Haupt-Thread (kein Atomic-Overhead dort, wo er nicht benötigt wird). Treiber-Stack für Hintergrund-Threads. Jeder Kontext erhält den richtigen Algorithmus.",
      },
    },
    {
      feature: 'Asynchrone Channels',
      sub: 'Eingebautes Producer/Consumer',
      cols: { task: 'BCL (klassenbasiert)', unitask: 'Nur unbegrenzt', awaitable: '✗', valkarn: 'Begrenzt + Unbegrenzt ✦' },
      win: ['valkarn'],
      note: {
        task:    "System.Threading.Channels ist klassenbasiert und alloziert. Nicht mit Unitys PlayerLoop integriert — Fortsetzungen laufen auf dem ThreadPool.",
        unitask: 'UniTask liefert nur einen unbegrenzten Einzelkonsumenten-Channel. Keine begrenzte Kapazität, kein Gegendruck. Null Allokationen beim Lesen, aber begrenzte API.',
        valkarn: 'Sowohl begrenzte (mit Kapazität und Gegendruck) als auch unbegrenzte Channels. WriteAsync, ReadAsync, TryRead, TryWrite, TryPeek — alle null Allokationen auf dem schnellen Pfad.',
      },
    },
    {
      feature: 'WhenAll / WhenAny-Kombinatoren',
      sub: 'Parallele Task-Koordination',
      cols: { task: 'Gibt Task[] zurück', unitask: '✓ (nur Arrays)', awaitable: '✗', valkarn: 'Tupel bis zu 8 ✦' },
      win: ['unitask', 'valkarn'],
      note: {
        task:    'Task.WhenAll gibt Task<T[]> zurück und erfordert indexbasierten Zugriff. Kein Tupel-Destructuring.',
        unitask: 'UniTask.WhenAll unterstützt typisierte Tupel bis zu Arität 15. Eine starke Funktion.',
        valkarn: 'var (tex, sfx, data) = await VlkTask.WhenAll(...) — bis zu 8 typisierte Ergebnisse. WhenAny gibt das erste abgeschlossene Ergebnis mit seinem Index zurück.',
      },
    },
    {
      feature: 'Stilles Schlucken von Ausnahmen',
      sub: 'Unbehandelte Fehler in async void / Fire-and-Forget',
      cols: { task: 'AppDomain.UnhandledException', unitask: 'UniTaskScheduler.UnobservedTaskException', awaitable: '⚠ Unity 6-Fehler', valkarn: 'Konfigurierbarer Handler ✦' },
      win: ['valkarn'],
      note: {
        awaitable:'Unity 6 hatte einen bestätigten Fehler, bei dem Ausnahmen innerhalb von Awaitable-Fortsetzungen lautlos verschluckt wurden ohne Log-Ausgabe. In Unity 6000.0.5 behoben — frühere 6.x-Versionen sind betroffen.',
        valkarn:  'VlkTaskSettings.UnobservedExceptionHandler ist benutzerkonfigurierbar. Standard: Protokolliert über Debug.LogException, sodass keine Ausnahme jemals lautlos ist.',
      },
    },
  ],

  versusSection: {
    heading: 'Warum wechseln?',
    sub:     'Konkrete Gründe, kein Marketing-Text.',
  },

  versus: [
    {
      vs: 'System.Task',
      color: '#ef4444',
      problems: [
        { title: 'Jedes Await alloziert',           body: "Task<T> ist eine Klasse. Jede asynchrone Methode alloziert ein Heap-Objekt, selbst wenn sie synchron zurückkehrt. In einer 60-Hz-Spielschleife, die Tausende von asynchronen Operationen ausführt, zahlen Sie bei jedem einzelnen Frame eine GC-Steuer." },
        { title: 'ThreadPool standardmäßig',         body: "Fortsetzungen werden auf dem .NET ThreadPool fortgesetzt, nicht auf Unitys Haupt-Thread. Jede Interaktion mit GameObjects, Transforms oder Unity-APIs erfordert explizites Marshalling über UnitySynchronizationContext — fehleranfällig und ausführlich." },
        { title: 'Keine MonoBehaviour-Bindung',      body: "Nichts verhindert, dass ein Task weiterläuft, nachdem das zugehörige MonoBehaviour zerstört wurde. Das Ergebnis sind Phantom-NullReferenceExceptions, Zustandskorruption und Fehler, die nur auftreten, wenn Szenen unter Last entladen werden." },
        { title: 'Keine Unity-Diagnose',             body: "Die .NET-Laufzeit kennt Unitys Lebensdauermodell nicht. Null Roslyn-Regeln fangen die Muster ab, die Spiele brechen: Zombie-Schleifen, Zugriff auf zerstörte Objekte, Fire-and-Forget-Missbrauch." },
      ],
      verdict: 'System.Task ist das richtige Werkzeug für .NET-Server. Es ist das falsche Werkzeug für eine Echtzeit-Spielschleife.',
    },
    {
      vs: 'UniTask',
      color: '#f59e0b',
      problems: [
        { title: '~Null ist nicht Null',                          body: 'UniTasks Hauptanspruch ist „~Null Allokationen." Die Tilde ist wichtig. Ausnahme- und Abbruchpfade allozieren weiterhin — wie bei allen anderen. Valkarn Tasks macht denselben Kompromiss ehrlich und fügt obendrein weitere Garantien hinzu.' },
        { title: 'Manueller Abbruch — immer',                    body: 'Das Binden einer UniTask an ein MonoBehaviour erfordert: ein CancellationTokenSource-Feld, ein OnEnable zum (Neu-)Initialisieren, ein OnDestroy zum Abbrechen und Entsorgen sowie das Durchfädeln des Tokens durch jeden asynchronen Aufruf. Valkarn Tasks quellgeneriert dieses gesamte Muster aus einem einzigen [AutoCancel]-Attribut.' },
        { title: 'Bricht mit Unity Entities',                     body: "Unitys Entities-Paket ruft PlayerLoop.SetPlayerLoop() bei der Initialisierung auf, was UniTasks registrierte Runner überschreibt. Jeder Task, der zu diesem Zeitpunkt läuft, wird lautlos verworfen. Es gibt keine Warnung. Die ECS-Integration von Valkarn Tasks ist speziell dafür ausgelegt, PlayerLoop-Resets zu überleben." },
        { title: 'Interlocked auf dem Haupt-Thread',              body: "UniTasks Pool ruft Interlocked.CompareExchange bei jedem Push und Pop auf — auch auf dem Haupt-Thread, wo keine echte Konkurrenz besteht. Dies sind unnötige atomare Operationen auf dem heißesten Codepfad in Ihrem Spiel. Valkarn Tasks verwendet einfaches CAS auf dem Haupt-Thread und einen Treiber-Stack auf Worker-Threads." },
        { title: 'Eine Analyzer-Regel, keine strukturellen Prüfungen', body: 'UniTask liefert UNITASK001: Vergessen-zu-awaiten-Erkennung. Das war\'s. Zombie-Schleifen (eine Schleife, die nie beendet wird, weil der Abbruch nie geprüft wird), gemischte-Lebensdauer-Tasks, doppelte Awaits — nichts davon wird erkannt. Valkarn Tasks liefert 17 Regeln, die diese strukturellen Muster abdecken.' },
        { title: 'Letztes Release: Oktober 2024',                 body: "UniTasks GitHub zeigt seit Oktober 2024 keine aktive Entwicklung. Unity 6, DOTS 1.x und zukünftige Editor-Versionen bringen Breaking Changes, die ein nicht gewartetes Paket nicht verfolgen kann." },
      ],
      verdict: 'UniTask war 2020 Stand der Technik. Valkarn Tasks ist für 2025 und darüber hinaus gebaut.',
    },
    {
      vs: 'Awaitable',
      color: '#3b82f6',
      problems: [
        { title: '6 Zeitpunkte, nicht 16',                    body: "Unitys Awaitable bietet 6 Planungs-Hooks: NextFrameAsync, FixedUpdateAsync, EndOfFrameAsync, WaitForSecondsAsync, MainThreadAsync und BackgroundThreadAsync. Valkarn Tasks bildet alle 16 PlayerLoop-Phasen von Unity ab — PreUpdate, PostLateUpdate, TimeUpdate und mehr — als erstklassige Await-Punkte." },
        { title: 'Dieselbe Task kann nicht zweimal abgewartet werden', body: "Ein Awaitable abzuwarten, das bereits abgeschlossen ist, führt entweder zu einem Deadlock oder einem Fehler, abhängig vom internen Zustand. Es gibt keine Schutzmaßnahme. Valkarn Tasks hat Doppelrückgabe-Schutz auf jedem Quellpfad: das zweite Await gibt immer sofort das zwischengespeicherte Ergebnis zurück." },
        { title: 'Unity 6 verschluckt Ausnahmen lautlos',     body: 'Ein bestätigter Unity 6-Fehler verursacht, dass Ausnahmen innerhalb von Awaitable-Fortsetzungen ohne Log-Ausgabe, ohne Stack-Trace, ohne Absturz verschwinden. In 6000.0.5 behoben — das heißt, jedes Unity 6.0 bis 6.0.4-Projekt ist betroffen. Valkarn Tasks leitet alle nicht beobachteten Ausnahmen durch einen konfigurierbaren Handler, der standardmäßig Debug.LogException verwendet.' },
        { title: 'Kein WhenAll, WhenAny oder Channels',       body: 'Awaitable hat keine Kombinator-API. Drei Ladevorgänge parallel auszuführen und ihre Ergebnisse zu sammeln erfordert eine manuelle Zustandsmaschine. Valkarn Tasks bietet WhenAll mit Tupel-Destructuring bis zu Arität 8, WhenAny sowie begrenzte und unbegrenzte asynchrone Channels.' },
        { title: 'Keine Lebensdauer-Bindung',                  body: "Awaitable bietet keinen Mechanismus, um die Lebensdauer einer Task an ein GameObject zu binden. Jedes Abbruch-Token muss manuell erstellt, gespeichert, durch Aufrufe gefädelt und entsorgt werden." },
      ],
      verdict: 'Awaitable ist ein dünner Planungs-Hook. Valkarn Tasks ist eine vollständige asynchrone Laufzeit.',
    },
  ],

  cta: {
    heading:    'Schneller liefern. Weniger allozieren.',
    p1:         'Kostenlos für Einzelpersonen und Studios mit weniger als 1 Mio. USD Jahresumsatz.',
    p2:         'Eine Zeile in Ihrer Manifest-Datei. Kein Konto erforderlich.',
    btnPrimary: 'Jetzt starten →',
    btnGhost:   'Lizenz ansehen',
  },
};

export default data;
