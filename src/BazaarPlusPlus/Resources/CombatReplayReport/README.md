# Combat report Viewer

`viewer.js` and `viewer.css` are generated artifacts. Author React/TypeScript/Tailwind code only
under `frontend/`, then run:

```sh
cd src/BazaarPlusPlus/Resources/CombatReplayReport/frontend
npm run viewer:build
npm run viewer:browsers:install
npm test
```

The build is intentionally constrained to one classic IIFE script and one stylesheet so generated
reports can open directly through `file://`. It rejects extra chunks, source maps, remote resources,
`fetch`, dynamic imports, and workers. React and Tailwind are bundled into `viewer.js` /
`viewer.css`; the pinned ECharts UMD file under `vendor/echarts/` is prepended by
`ViewerArtifactBundle` at installation time.

UI controls use the local shadcn/new-york primitive layer under `frontend/src/components/ui/`.
Feature components compose those primitives rather than maintaining parallel button, tab, card,
popover, accordion, scroll-area, or table styles. `frontend/components.json` is the shadcn source
configuration.

Design values live in `frontend/src/styles/theme.css`. Concrete `:root` variables are the runtime
source of truth so Canvas and ECharts can read them under `file://`; the `@theme inline` block only
maps those values to Tailwind utilities. Do not place a Canvas-only value exclusively inside
`@theme`, because Tailwind may omit it from the generated stylesheet.

The plugin installs each Viewer build as an immutable, content-addressed generation:

```text
BazaarPlusPlusV4/
  reports/<recording-id>.html
  report-viewer/objects/<viewer-bundle-id>/viewer.js
  report-viewer/objects/<viewer-bundle-id>/viewer.css
  report-assets/objects/<sha-prefix>/<sha>.png
  CombatReplayVideos/<recording>.mp4
```

Each report HTML embeds its report JSON and references exactly one Viewer generation. Publishing
repairs or completes that generation before the report becomes visible, so an interrupted update
cannot mix JavaScript and CSS from different builds. There is no legacy Viewer fallback or mutable
stable alias. Game image assets remain immutable and content-addressed.

Run the repository-level browser suite with `./run.sh viewer-test`. It uses direct `file://` reports
in Chromium and WebKit.

## Release packaging

`./run.sh publish` runs the frontend release gate before the production `BuildAll`: clean dependency
install, Chromium/WebKit installation, typecheck, deterministic artifact comparison, pure tests,
and browser behavior tests. The generated Viewer files, pinned ECharts build, and license notices
are embedded resources in `BazaarPlusPlus.dll`.

The production MSBuild target then copies that DLL into both platform payload trees and rebuilds:

```text
bazaarplusplus-installer/src-tauri/resources/BepInExSource/macos/BepInEx.zip
bazaarplusplus-installer/src-tauri/resources/BepInExSource/windows/BepInEx.zip
```

The Tauri installer bundles the platform ZIP as `BepInExSource/BepInEx.zip`. No loose Viewer files
belong in the installer resource manifest; the installed plugin extracts the immutable Viewer
generation from its own embedded resources when a report is published.
