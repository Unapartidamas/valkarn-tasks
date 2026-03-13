/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
  docsSidebar: [
    {
      type: 'category',
      label: 'Getting Started',
      collapsed: false,
      items: [
        'installation',
        'quick-start',
        'why-valkarn',
        'features',
        'architecture',
        'migration-from-unitask',
      ],
    },
    {
      type: 'category',
      label: 'Core Concepts',
      items: [
        'concepts/struct-tasks',
        'concepts/pooling',
        'concepts/player-loop',
        'concepts/auto-cancel',
      ],
    },
    {
      type: 'category',
      label: 'API Reference',
      items: [
        'api/vlk-task',
        'api/vlk-task-t',
        'api/completion-source',
        'api/channels',
        'api/semaphore',
        'api/player-loop-timing',
        'api/settings',
      ],
    },
    {
      type: 'category',
      label: 'Advanced',
      items: [
        'advanced/burst-ecs',
        'advanced/job-bridge',
        'advanced/analyzers',
        'advanced/il2cpp',
        'advanced/testing',
      ],
    },
    {
      type: 'category',
      label: 'Legal',
      items: [
        'license',
      ],
    },
  ],
};

module.exports = sidebars;
