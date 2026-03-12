// @ts-check
const { themes } = require('prism-react-renderer');

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'Valkarn Tasks',
  tagline: 'Zero-allocation async/await for Unity. Faster than UniTask.',
  favicon: 'img/favicon.ico',

  url: 'https://tasks.valkarn.com',
  baseUrl: '/',

  organizationName: 'unapartidamas',
  projectName: 'valkarn-tasks',
  deploymentBranch: 'gh-pages',
  trailingSlash: false,

  onBrokenLinks: 'throw',
  onBrokenMarkdownLinks: 'warn',

  i18n: {
    defaultLocale: 'en',
    locales: ['en', 'es', 'zh-Hans', 'fr', 'de', 'pt-BR', 'ja', 'ru', 'ar', 'hi'],
    localeConfigs: {
      en:        { label: 'English',        direction: 'ltr', htmlLang: 'en' },
      es:        { label: 'Español',        direction: 'ltr', htmlLang: 'es' },
      'zh-Hans': { label: '中文',           direction: 'ltr', htmlLang: 'zh-Hans' },
      fr:        { label: 'Français',       direction: 'ltr', htmlLang: 'fr' },
      de:        { label: 'Deutsch',        direction: 'ltr', htmlLang: 'de' },
      'pt-BR':   { label: 'Português (BR)', direction: 'ltr', htmlLang: 'pt-BR' },
      ja:        { label: '日本語',         direction: 'ltr', htmlLang: 'ja' },
      ru:        { label: 'Русский',        direction: 'ltr', htmlLang: 'ru' },
      ar:        { label: 'العربية',        direction: 'rtl', htmlLang: 'ar' },
      hi:        { label: 'हिन्दी',         direction: 'ltr', htmlLang: 'hi' },
    },
  },

  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          sidebarPath: './sidebars.js',
          editUrl: 'https://github.com/unapartidamas/valkarn-tasks/tree/main/website/',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
        sitemap: {
          changefreq: 'weekly',
          priority: 0.5,
        },
      }),
    ],
  ],

  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      image: 'img/social-card.png',
      colorMode: {
        defaultMode: 'dark',
        disableSwitch: false,
        respectPrefersColorScheme: true,
      },
      navbar: {
        title: 'Valkarn Tasks',
        logo: {
          alt: 'Valkarn Tasks Logo',
          src: 'img/logo.svg',
        },
        items: [
          {
            type: 'docSidebar',
            sidebarId: 'docsSidebar',
            position: 'left',
            label: 'Docs',
          },
          {
            to: '/docs/license',
            label: 'License',
            position: 'left',
          },
          {
            href: 'https://github.com/unapartidamas/valkarn-tasks',
            label: 'GitHub',
            position: 'right',
          },
          {
            type: 'localeDropdown',
            position: 'right',
          },
        ],
      },
      footer: {
        style: 'dark',
        links: [
          {
            title: 'Docs',
            items: [
              { label: 'Installation', to: '/docs/installation' },
              { label: 'Quick Start', to: '/docs/quick-start' },
              { label: 'API Reference', to: '/docs/api/vlk-task' },
            ],
          },
          {
            title: 'More',
            items: [
              { label: 'GitHub', href: 'https://github.com/unapartidamas/valkarn-tasks' },
              { label: 'License', to: '/docs/license' },
              { label: 'Una Partida Mas', href: 'https://unapartidamas.com' },
            ],
          },
        ],
        copyright: `Copyright © ${new Date().getFullYear()} Una Partida Mas. Built with Docusaurus.`,
      },
      prism: {
        theme: themes.vsLight,
        darkTheme: themes.vsDark,
        additionalLanguages: ['csharp', 'json', 'bash'],
      },
    }),
};

module.exports = config;
