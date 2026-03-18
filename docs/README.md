---
title: Docs README
---

# Documentation site (VitePress)

This directory contains the Clockworks documentation site, built with VitePress 1.x.

## Local development

1) Install dependencies:

```bash
cd docs
npm install
```

2) Copy the logo into `docs/public/`:

```bash
cp ../assets/clockworks-display-resized.png public/logo.png
```

3) Start the dev server:

```bash
npm run docs:dev
```

## Notes on GitHub Pages

The site is deployed as a GitHub Pages **project site** under `/Clockworks/`, so the VitePress `base` is set to:

- `base: '/Clockworks/'`

In CI, the workflow copies `assets/clockworks-display-resized.png` to `docs/public/logo.png` before running the build. The generated `logo.png` is intentionally excluded from git.

