import { defineConfig } from 'vitepress'

export default defineConfig({
  base: '/Clockworks/',
  title: 'Clockworks',
  description: 'Deterministic, fully controllable time for distributed-system simulations and testing.',

  head: [
    ['meta', { property: 'og:title', content: 'Clockworks' }],
    ['meta', { property: 'og:description', content: 'Deterministic, fully controllable time for distributed-system simulations and testing.' }],
    ['meta', { property: 'og:type', content: 'website' }],
  ],

  lastUpdated: true,

  markdown: {
    theme: { light: 'github-light', dark: 'github-dark' },
    lineNumbers: true,
  },

  themeConfig: {
    logo: '/Clockworks/logo.png',
    siteTitle: 'Clockworks',

    nav: [
      { text: 'Guide', link: '/guide/' },
      { text: 'Concepts', link: '/concepts/why-clockworks' },
      {
        text: 'API Reference',
        link: 'https://www.nuget.org/packages/Clockworks',
        target: '_blank',
      },
      { text: 'Changelog', link: '/changelog' },
      {
        text: 'v1.3.0',
        link: 'https://github.com/dexcompiler/Clockworks/releases',
        target: '_blank',
      },
    ],

    sidebar: {
      '/guide/': [
        {
          text: 'Guide',
          items: [
            { text: 'Getting Started', link: '/guide/' },
            { text: 'Installation', link: '/guide/#installation' },
            { text: 'Simulated Time', link: '/guide/simulated-time' },
            { text: 'Timeouts', link: '/guide/timeouts' },
            { text: 'UUIDv7 Generation', link: '/guide/uuidv7' },
            { text: 'Hybrid Logical Clock', link: '/guide/hlc' },
            { text: 'Vector Clock', link: '/guide/vector-clock' },
            { text: 'Instrumentation', link: '/guide/instrumentation' },
          ],
        },
      ],
      '/concepts/': [
        {
          text: 'Concepts',
          items: [
            { text: 'Why Clockworks?', link: '/concepts/why-clockworks' },
            { text: 'HLC vs Vector Clocks', link: '/concepts/hlc-vs-vector' },
            { text: 'Determinism Model', link: '/concepts/determinism' },
            { text: 'Security Considerations', link: '/concepts/security' },
          ],
        },
      ],
    },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/dexcompiler/Clockworks' },
    ],

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © 2024–present Dexter Ajoku (CloudyBox)',
    },

    search: {
      provider: 'local',
    },

    editLink: {
      pattern: 'https://github.com/dexcompiler/Clockworks/edit/main/docs/:path',
      text: 'Edit this page on GitHub',
    },
  },
})
