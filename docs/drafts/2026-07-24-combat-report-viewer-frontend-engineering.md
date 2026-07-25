# Combat Report Viewer React/shadcn/Tailwind replacement

Status: confirmed. This PR replaces the current Viewer in place; it is not a compatibility migration.

## Product boundary

The report remains a static, offline post-combat analysis tool:

- each report HTML embeds immutable report JSON;
- the browser loads one classic `report-viewer/viewer.js` and one
  `report-viewer/viewer.css` through `file://`;
- shared game assets and the optional recording use stable relative paths;
- runtime network access, `fetch`, workers, dynamic imports, and a local HTTP server are forbidden;
- dense combat events remain Canvas-rendered rather than becoming thousands of DOM nodes.

The current implementation is not retained. The finished PR contains no handwritten DOM Viewer, no
`legacy/viewer.js`, no monolithic accumulated override stylesheet, and no v1–v26 compatibility
resources or registry entries.

Because historical Viewer compatibility is explicitly out of scope, reports use the one stable
unversioned Viewer URL. Plugin updates atomically replace that current Viewer after validating its
source-controlled integrity pins. No `report-viewer/vN/` directory, version meta tag, immutable
multi-generation registry, or report-rewrite migration remains.

## Framework decision

Use the same frontend family as `bazaarplusplus.com`:

- React 19 and TypeScript;
- Vite for the authored Viewer build;
- Tailwind CSS 4;
- shadcn/ui `new-york` component sources with the Radix primitive base;
- a committed `components.json` with CSS variables enabled and explicit `@/*` aliases;
- `clsx` plus `tailwind-merge` for class composition;
- `class-variance-authority` for shadcn variants and no second variant helper;
- Lucide for generic application controls;
- game-native cached assets for combat semantics;
- the existing pinned ECharts global for statistical charts.

shadcn is source code committed under `src/components/ui/`, not a runtime service, CDN, or visual
reference. The CLI is used to initialize and add the selected primitives; those generated sources
are then adapted to the Viewer-owned theme and reviewed like application code. What ships is
shadcn structure, accessibility behavior, slots, and variants with Viewer-owned Tailwind classes;
it is not a verbatim copy of shadcn's stock palette, typography, radius, or density. The initial
inventory is deliberately bounded to `button`, `button-group`, `tabs`, `badge`, `card`, `tooltip`,
`separator`, `toggle-group`, `accordion`, `scroll-area`, and `table`. Add another primitive only
when a concrete Viewer interaction needs it.

Do not import the website at runtime or create a cross-repository package in this PR. Copy the
relevant theme tokens into an explicitly owned Viewer theme and document their origin. This keeps a
static report independent while aligning its visual language with the website.

shadcn owns ordinary application chrome and accessibility behavior: buttons, grouped controls,
tabs, badges, cards, tooltips, disclosure sections, scroll containers, and table structure.
Tailwind owns their layout, spacing, typography, responsive behavior, and Viewer-specific variants.
A small semantic CSS layer is allowed only for Canvas stacking, sticky timeline geometry, PiP
drag/resize behavior, scrollbars, and browser behavior that utilities cannot express clearly.
There is no parallel legacy styling system or hand-built replacement for an installed shadcn
primitive.

Domain surfaces stay custom where shadcn would be the wrong abstraction:

- state, ruler, and event rendering remain Canvas renderers;
- timeline hit targets and hover previews remain imperative renderer behavior;
- the frame inspector remains a width-reserving dock, not a shadcn `Sheet` overlay;
- recording drag/resize geometry remains Viewer logic, while its buttons, badge, tooltip, and card
  chrome use shadcn;
- shadcn `ScrollArea` is allowed only inside inspector/statistics panes and must never wrap the
  native synchronized timeline scroller;
- the raw CombatSim payload disclosure remains a documented native `details` leaf; only grouped
  event disclosures become shadcn accordions;
- game-semantic icons continue to use cached game assets rather than Lucide or shadcn decoration.

## Runtime architecture

```text
main.tsx
  └─ <ReportApp>
       ├─ <ReportHeader>
       ├─ <TimelinePage>
       │    ├─ <CombatantStateBand>  Canvas
       │    ├─ <TimelineViewport>
       │    │    ├─ <LaneLabels>     DOM, virtualized if required
       │    │    └─ <EventCanvas>    Canvas
       │    ├─ <FrameInspector>      docked layout, not overlaying the timeline
       │    └─ <RecordingPip>        draggable/resizable portal
       └─ <StatisticsPage>
            ├─ <CombatSummary>
            ├─ <TopContributors>
            └─ <EntityActivityTable>
```

One `useReducer` store owns:

- selected tab;
- locale;
- time zoom and horizontal viewport;
- committed playhead;
- selected frame/cluster/event;
- inspector open state;
- recording playback/follow/rate/PiP geometry;
- statistics sort key, metric, and direction.

Derived report data is produced by pure selectors and memoized at component boundaries. React does
not own per-frame drawing commands. Canvas components receive typed render models and draw inside
layout/effect hooks using refs. Pointer hit testing uses the existing spatial index and dispatches
semantic actions back to the reducer.

Hover preview, pointer coordinates, transient hit-test results, video animation frames, and Canvas
redraw scheduling live in refs and imperative renderer controllers. Pointer movement must not
dispatch React state. Only committed actions such as selection, tab changes, zoom, and inspector
navigation enter the reducer.

## Source layout

```text
Resources/CombatReplayReport/frontend/
  components.json
  package.json
  package-lock.json
  tsconfig.json
  vite.config.ts
  src/
    main.tsx
    app/
      ReportApp.tsx
      report-reducer.ts
      report-state.ts
      ErrorBoundary.tsx
    model/
      contracts.ts
      normalize.ts
      selectors.ts
      asset-paths.ts
    i18n/
      catalog.ts
      format.ts
      use-copy.ts
    components/
      shell/
      timeline/
      inspector/
      recording/
      statistics/
      semantic/
        EntityArt.tsx
        SemanticIcon.tsx
      ui/
        button.tsx
        button-group.tsx
        tabs.tsx
        badge.tsx
        card.tsx
        tooltip.tsx
        separator.tsx
        toggle-group.tsx
        accordion.tsx
        scroll-area.tsx
        table.tsx
    lib/
      utils.ts
    timeline/
      canvas.ts
      state-band-renderer.ts
      timeline-renderer.ts
      geometry.ts
      clusters.ts
      state-scale.ts
    statistics/
      aggregate.ts
    styles/
      index.css
      theme.css
      semantic.css
  tests/
```

Default limit: 400 authored lines per React component and 600 lines per pure renderer. A larger
Canvas renderer needs a documented reason. Stable `data-bpp-test-id` values are public test
contracts and do not depend on Tailwind classes.

## Build contract

Vite must be configured as a library build with exactly one IIFE entry:

- `formats: ["iife"]`;
- no code splitting or dynamic imports;
- one deterministic JavaScript artifact;
- `cssCodeSplit: false`;
- one deterministic stylesheet;
- stable explicit filenames `viewer.js` and `viewer.css`;
- React and Tailwind are bundled locally;
- shadcn sources and Radix primitives are bundled locally;
- ECharts is external and resolves only to the already prepended `window.echarts`;
- source maps and external font/image URLs are disabled;
- the committed artifacts are reproducible from the lockfile.

The build gate scans the complete temporary output directory and accepts exactly `viewer.js` and
`viewer.css`; any chunk, source map, font, image, `assets/` directory, or other file fails the build.
It rejects `fetch`, workers, dynamic imports, HTTP(S) URLs, CSS `url(...)`, and an embedded second
ECharts distribution. It retains byte-for-byte reproducibility checks and committed-artifact
freshness checks. Ordinary .NET builds continue consuming committed artifacts without requiring
Node.

The theme gate treats generated shadcn source as ordinary production source. Before a primitive is
accepted, its stock typography, palette, radius, and density classes are translated to owned
tokens. `theme.css` defines the complete shadcn semantic contract (`background`, `foreground`,
`card`, `popover`, `primary`, `secondary`, `muted`, `accent`, `destructive`, `border`, `input`, and
`ring`) as aliases of the Viewer gold/dark palette through Tailwind 4 `@theme inline`. A browser
acceptance assertion checks that every semantic token used by a component resolves to a concrete
computed value.

The source gate also enforces the replacement boundary: `control-styles.ts` must not exist; ordinary
raw `button` and `table` elements are forbidden outside the shadcn primitive directory; and
superseded component selectors such as `.bpp-activity-table` must not survive their migration.
Documented native exceptions are limited to domain Canvas/media behavior and the raw CombatSim
payload disclosure.

Rollup explicitly maps external `echarts` to the global name `echarts`. Browser acceptance tests
load the exact installed concatenation—pinned ECharts UMD, separator, Viewer IIFE—and prove that an
ECharts Canvas is painted on the statistics page.

## Replacement checklist

### 1. Establish the React/shadcn/Tailwind boundary

- [ ] Pin React, Vite, Tailwind 4, TypeScript, shadcn/Radix dependencies, and the small class/icon
      utilities.
- [ ] Commit `components.json`, `@/*` TypeScript/Vite aliases, and the single shadcn `cn` helper.
- [ ] Add the bounded shadcn primitive inventory through the CLI and map every generated semantic
      token to the Viewer-owned gold/dark theme; do not accept the stock shadcn palette, font,
      radius, or sizing as product defaults.
- [ ] Move `EntityArt` and `SemanticIcon` to `components/semantic/`; they remain game-domain
      components and are not shadcn primitives.
- [ ] Add all new Radix/CVA dependencies to the committed lockfile and prove `viewer:check`
      reproduces the artifacts byte for byte.
- [ ] Add the single-IIFE Vite build and deterministic artifact/freshness checks.
- [ ] Scan the complete Vite output directory and reject every artifact except `viewer.js` and
      `viewer.css`, including CSS local URLs and hidden chunks.
- [ ] Reproduce the existing inert JSON bootstrap and fatal-error boundary in React.
- [ ] Prove direct `file://` startup in Chromium and WebKit before moving UI.

### 2. Preserve and type pure domain logic

- [ ] Keep the existing normalization, typed sibling URL validation, formatting, localization,
      clustering,
      state-axis math, recording sync, and statistics aggregation as framework-free TypeScript.
- [ ] Add unit tests for all exported selectors and aggregations, including items and skills,
      amount/count pairs, exact attribution, and extreme values.
- [ ] Define one typed view model consumed by React components.

### 3. Replace the application shell and pages

- [ ] Implement the compact header, time zoom, and help controls with shadcn `Button`,
      `ButtonGroup`, `Tabs`, `Badge`, and `Tooltip`.
- [ ] Preserve the current compact one-button locale cycle using a shadcn `Button`; changing it to
      a three-segment ToggleGroup is a separate product decision.
- [ ] Implement the statistics page with shadcn table structure, sortable amount/count cells, and
      unframed game-semantic icons.
- [ ] Implement the docked frame inspector with shadcn `Card`, `Accordion`, `Separator`, and
      `ScrollArea`, while preserving the width-reserving layout rather than using an overlay Sheet.
- [ ] Keep the raw CombatSim record as a native leaf disclosure rather than forcing it into the
      grouped event Accordion.
- [ ] Implement the draggable/resizable recording PiP with shadcn card/button/badge/tooltip chrome
      and Viewer-owned geometry plus playback-rate behavior.

### 4. Replace the timeline

- [ ] Implement the shared-Y combatant state Canvas using the same horizontal viewport as events.
- [ ] Implement lane labels with correctly oriented cached item/skill/hero assets.
- [ ] Implement the event Canvas with reversible aggregation, status ranges, right interaction
      gutter, hover hit targets, frame selection, and playhead.
- [ ] Keep horizontal scroll, time zoom, video seek, state band, ruler, labels, and event Canvas
      synchronized from one viewport model.
- [ ] Assert no shadcn/Radix `ScrollArea` owns or wraps the synchronized timeline scroller.
- [ ] Confirm 60 fps interaction at the dense fixture and no React render per pointer-move frame.
- [ ] Expose a test-only React commit counter; dispatch repeated pointer moves and assert it stays
      unchanged while the Canvas hover preview still moves.

### 5. Delete superseded implementation

- [ ] Delete `legacy/viewer.js`, transitional `app/report.js`, handwritten DOM helpers, and the
      monolithic `styles/index.css`.
- [ ] Delete `components/ui/control-styles.ts` and replace every ordinary raw control/table/details
      implementation with the selected shadcn source primitive; keep only the documented
      Canvas/media and raw-payload disclosure exceptions.
- [ ] Delete the superseded `.bpp-activity-table` and equivalent component-chrome selectors after
      their shadcn replacements land.
- [ ] Remove all remaining versioned Viewer paths, version metadata, registry/version arguments,
      resource declarations, compatibility tests, and stale README instructions.
- [ ] Emit reports with stable `../report-viewer/viewer.js` and `.css` URLs and atomically replace
      those current installed artifacts on plugin update.
- [ ] Ensure no alternate file://, server, or old-DOM fallback remains.

### 6. Verification and delivery

- [ ] Unit tests: reducer, selectors, clustering, aggregation, sync, formatting, and URL safety.
- [ ] Playwright Chromium/WebKit through direct `file://`.
- [ ] Viewports: 1300×857, 838×857, and 480×857; DPR 1 and 2.
- [ ] Fixtures: dense/sparse events, missing assets, extreme numbers, exact/no video, three locales.
- [ ] Interactions: zoom/scroll/hover/select/inspector/PiP resize+drag/rate/stat sort/language.
- [ ] Assert one JS, one CSS, zero runtime network, and deterministic build output.
- [ ] Assert the production component tree imports the committed shadcn primitives and contains no
      duplicate control-style helper or second component system.
- [ ] Assert every used shadcn semantic CSS variable has a concrete computed value and no component
      source reintroduces stock color, typography, or radius tokens.
- [ ] Assert statistics paints an ECharts Canvas from the exact installed concatenated script.
- [ ] Assert emitted report HTML resolves only the stable unversioned Viewer URLs.
- [ ] Run the report bundle tests, architecture tests, and main mod build.
- [ ] Review the complete diff as one PR-sized delivery; do not retain transition code.

## Acceptance invariants

- At 1300×857 the timeline is the primary flexible region and at least ten normal lanes are visible.
- State and event canvases have equal logical width, one time transform, and synchronized scroll.
- Every raw visible event is reachable through a reversible cluster and frame inspector.
- Opening the inspector reduces the timeline column width; it never overlays hidden content.
- Recording PiP does not consume document-flow height and can be resized within viewport bounds.
- All Canvas backing stores match clamped DPR and stay below browser dimension/area limits.
- Every item and skill from both sides appears in statistics, including zero-activity entities.
- Quantitative statistics show cumulative amount and occurrence count together.
- Missing or unsafe resources produce explicit fallbacks without runtime network access.
- Ordinary application controls come from the committed shadcn primitive layer; timeline Canvas,
  media, and width-reserving workspace geometry remain explicit domain components.
- The shadcn semantic variables resolve only through the Viewer-owned Tailwind theme, including the
  offline CJK-capable system font stack.
- Production source contains React components and pure renderers only; the old Viewer does not ship.
