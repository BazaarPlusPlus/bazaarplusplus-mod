# Combat Report Viewer frontend

This directory owns browser-level behavior tests for the static `file://` Viewer.
The release contract remains one installed stylesheet plus one installed IIFE script
(ECharts followed by the generated Viewer payload).

## Run

```bash
npm ci
npm run viewer:browsers:install
npm run test:behavior
```

From the repository root, the equivalent project command is:

```bash
./run.sh viewer-test
```

Chromium covers Chrome/Edge-compatible behavior and WebKit covers Safari-compatible
behavior. The fixture deliberately loads from `file://`, blocks network access through
CSP, and exercises the stable `data-bpp-test-id` contract.

## Visual system

- `src/styles/theme.css` is the single owner of fonts, type scale, colors, radii,
  shadows, and spacing tokens.
- React components consume semantic Tailwind utilities backed by those tokens; they
  do not choose stock palette or typography values directly.
- Canvas and ECharts renderers read the same CSS custom properties through
  `src/styles/theme.ts`.
- `src/styles/app.css` is reserved for structural behavior that utilities do not
  express cleanly, such as sticky timeline layers, canvas geometry, and the resizable
  recording window.
- The production build rejects literal colors, typography declarations, stock
  Tailwind palettes, and arbitrary typography outside the theme source.
