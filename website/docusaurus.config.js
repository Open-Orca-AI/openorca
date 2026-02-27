// @ts-check

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'OpenOrca',
  tagline: 'A local-first AI coding assistant that runs entirely on your machine',
  favicon: 'img/favicon.png',

  url: 'https://open-orca-ai.github.io',
  baseUrl: '/openorca/',

  organizationName: 'open-orca-ai',
  projectName: 'openorca',

  onBrokenLinks: 'throw',

  markdown: {
    format: 'md',
    hooks: {
      onBrokenMarkdownLinks: 'warn',
    },
  },

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          sidebarPath: './sidebars.js',
          editUrl: 'https://github.com/open-orca-ai/openorca/wiki/',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      }),
    ],
  ],

  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      colorMode: {
        defaultMode: 'dark',
        respectPrefersColorScheme: true,
      },
      navbar: {
        title: 'OpenOrca',
        logo: {
          alt: 'OpenOrca Mascot',
          src: 'img/orca_mascot.png',
        },
        items: [
          {
            type: 'docSidebar',
            sidebarId: 'docsSidebar',
            position: 'left',
            label: 'Docs',
          },
          {
            href: 'https://github.com/open-orca-ai/openorca',
            label: 'GitHub',
            position: 'right',
          },
          {
            href: 'https://github.com/open-orca-ai/openorca/releases/latest',
            label: 'Download',
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
              { label: 'Getting Started', to: '/docs/Installation' },
              { label: 'Quick Start', to: '/docs/Quick-Start' },
              { label: 'Configuration', to: '/docs/Configuration' },
            ],
          },
          {
            title: 'Community',
            items: [
              {
                label: 'GitHub Discussions',
                href: 'https://github.com/open-orca-ai/openorca/discussions',
              },
              {
                label: 'Issues',
                href: 'https://github.com/open-orca-ai/openorca/issues',
              },
            ],
          },
          {
            title: 'More',
            items: [
              {
                label: 'GitHub',
                href: 'https://github.com/open-orca-ai/openorca',
              },
              {
                label: 'Releases',
                href: 'https://github.com/open-orca-ai/openorca/releases',
              },
            ],
          },
        ],
        copyright: `Copyright \u00a9 ${new Date().getFullYear()} OpenOrca Contributors. Built with Docusaurus.`,
      },
      prism: {
        theme: require('prism-react-renderer').themes.github,
        darkTheme: require('prism-react-renderer').themes.dracula,
        additionalLanguages: ['csharp', 'bash', 'json'],
      },
    }),
};

module.exports = config;
