# ADR-0012: Serve combat reports as content-addressed static file artifacts

## Context

The post-combat Viewer needs battle data, game-rendered images and an optional replay video. A
loopback HTTP design was considered, including sharing the optional BazaarAgent listener. Release
inspection showed that this is the wrong default dependency: the production mod installs only the
four base assemblies, BazaarAgent is absent, and the separately installed Tauri/Axum overlay on
`127.0.0.1:17654` is not a report or BazaarAgent host.

The report must therefore remain usable while the Tauri app is closed and without opening a new
port from the mod.

## Decision

1. A combat report is a static HTML file opened by the default browser with `file://`.
2. Each report HTML embeds its immutable report JSON in a non-executable
   `<script type="application/json">` element. The Viewer never fetches a local JSON file.
3. Viewer code and styles are installed once as a content-addressed generation derived from the
   report schema plus the JavaScript and CSS digests. JavaScript is one classic IIFE bundle; it uses
   no module import, dynamic import, Worker or runtime network request.
4. Game-rendered images live in one append-only, content-addressed cache shared by every report.
   A render-key index is checked before Unity materialization; only misses reach Unity.
5. The report stores stable relative URLs to its exact Viewer generation, shared content objects and
   existing replay video. Moving only the HTML may break those links; portable export is a separate
   future feature.
6. F8 opens HistoryPanel, whose detailed-report action opens the physical report HTML in the default
   browser. The default path has no local listener, token, authentication, HTTP Range route or
   dependency on Tauri/BazaarAgent.
7. Viewer generations, reports and cache objects are immutable. Existing bytes are verified and
   reused; conflicting bytes fail closed instead of overwriting history. The current gate installs
   or verifies only the current generation: there is no numbered registry, mutable alias, historical
   fallback or rewrite migration.
8. The report path does not remove or change BazaarAgent's optional listener. The default report/main
   path simply has no listener and no Tauri/BazaarAgent dependency.
9. React, Tailwind, shadcn-style Radix primitives and Vite are build-time authoring tools only. The
   runtime artifact remains a classic IIFE, one stylesheet and relative static resources.

## Consequences

- A completed report remains viewable after the game and app exit.
- Per recording, the only new report artifact is a small HTML file plus genuine global cache misses.
- Browser behavior for sibling-directory scripts/styles/images and local MP4 seek is a release gate
  on Chrome, Safari and Edge.
- Viewer generations and cache objects cannot use blind LRU deletion because reports retain exact
  relative references. Garbage collection, if added, must trace live report references first.
- ADR-0006 remains unchanged: BazaarAgent is optional and does not participate in report delivery.
