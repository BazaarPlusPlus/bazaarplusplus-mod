<!-- Curated by consolidation runs only. Do not hand-edit; write new knowledge to docs/drafts/. -->
<!-- Budget: <=200 lines / <=10KB. Merge, don't append. Boundaries: AGENTS.md=process, this=knowledge, truth/overview.md (docs/ARCHITECTURE.md here)=structure. -->

# BazaarPlusPlus — Durable Memory

Dense agent-facing index. Structure (`docs/ARCHITECTURE.md`) and rationale (`docs/adr/`) are linked, not restated. Code is the source of truth; `decompiled/` is read-only game reference.

## Rules

Domain constraints that must stay true in the system. Process/workflow rules live in `CLAUDE.md` (AGENTS.md symlink).

- MessagePack-serialized DTOs in the Unity/Mono runtime must keep their whole serialized graph `public`. [decision: CLAUDE.md project rules]
- Key game entities (cards, merchants, trainers) by their stable template GUID, never by display name or `ArtKey` substring. [`GameInterop/Cards/PackageIdentity.cs` | `CONTEXT.md`]
- Package-card identity is `EHiddenTag.Package` only, resolved via `PackageIdentity`/`CardArtInjector.IsPackageCard`; never name/`ArtKey` heuristics. [`src/BazaarPlusPlus/GameInterop/CardArtReplacement/CardArtInjector.cs`]
- The local SQLite schema is versioned; bump `RunLogSchema` (db=16, row=11, upload payload=5) when the persisted graph changes. [`src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs:10-18`]
- The mod carries **no play policy**: it does transport + validation only; all agent strategy lives in the external `bazaarplusplus-agent`. [ADR-0005, ADR-0006]
- BazaarAgent v1 wire field names (`stateName`, `availableActions`, `actionKind`, `cardInstanceId`, `targetSection`, `targetSockets`, `reason`) are a stable contract — do not rename. [ADR-0005 | ADR-0007]
- BazaarAgent pure core (`BazaarPlusPlus.BazaarAgent.dll`) must stay `System` + `Newtonsoft.Json` only — no Unity/BepInEx/Harmony/game refs (enforced by architecture tests). [ADR-0006]
- The four base assemblies (`ModApi`, `Storage`, `Localization`, and their consumers) keep zero game/Unity/BepInEx references. [`docs/ARCHITECTURE.md` §Assemblies]
- CJK text that renders as tofu must be routed to the embedded LXGW WenKai font, not "fixed" by editing copy. [`src/BazaarPlusPlus/Infrastructure/Fonts/BppUiFont.cs` | CLAUDE.md]
- Mod-authored user-facing strings use `LocalizedTextSet` (en + zh-Hans + zh-TW/zh-HK, others fall back to English). [`src/BazaarPlusPlus.Localization/LocalizedTextSet.cs:11-26`]

## Architecture decisions

One line each; full record in `docs/adr/`. This repo uses `docs/adr/`, not a `decisions/` tree.

- ADR-0001 (2026): Expose run/encounter state via on-demand `IEncounterStateProbe`, not an event-sourced timeline tracker. [adr/0001]
- ADR-0002: Mount MonoBehaviour features via one-line `IBppMountable`/`BppMountableRegistry`; generalized to `ComponentMount<T>` (~9 features). [adr/0002]
- ADR-0003: Render HistoryPanel previews via `ScreenSpaceOverlay` Canvas, NOT offscreen RenderTexture (URP cannot render uGUI to RT). Do not re-propose the RT path. [adr/0003]
- ADR-0004: Enchant preview = three-state visibility (`Off`/`AutoOnPedestalChoice`/`Always`, default `Always`); upgrade preview reverted to hold-Shift only. [adr/0004]
- ADR-0005 (superseded by 0006): AutoBazaar isolated as transport-only core behind host ports. [adr/0005]
- ADR-0006 (2026-06-05): BazaarAgent is its own BepInEx plugin (`BazaarAgentHost.dll`) depending on BazaarPlusPlus; dependency inverted, fixed loopback `127.0.0.1:47900`, release `4.1.0`. [adr/0006]
- ADR-0007: External battle video recording via three primitive replay-control HTTP endpoints (`record`/`context`/`continue`); only `CombatReplayRuntime.TryContinueReplay` exits ReplayState. [adr/0007]

## Durable knowledge

Verified facts about how the system works.

- Entry: `Plugin.Awake()` → `BppComposition` (manual composition root, no DI container). Wires features (`IBppFeature`), mountables (`IBppMountable`), settings rows (`ISettingsDockEntry`). [`src/BazaarPlusPlus/BppComposition.cs:83-135`]
- Six assemblies: 4 ship unconditionally (`BazaarPlusPlus`, `.ModApi`, `.Storage`, `.Localization`); 2 BazaarAgent ship only with `./run.sh build --with-bazaaragent`. [`docs/ARCHITECTURE.md` §Assemblies]
- Layers: `Core/` pure abstractions; `GameInterop/` game/Unity adapters; `Game/` feature workflows+UI+policy; `Patches/` Harmony (reach services via static `BppPatchHost`); `Infrastructure/` cross-cutting. [`docs/ARCHITECTURE.md` §Boundaries]
- Game assemblies are publicized at build (`<PublicizeAll>` via Krafs.Publicizer), so `internal` game members are accessible. [`docs/ARCHITECTURE.md`]
- Runtime data roots under game dir `BazaarPlusPlusV4/` (SQLite db, replay payloads, screenshots, replay videos, custom card art). [`src/BazaarPlusPlus/Core/Paths/BepInExPathProvider.cs:20-48`]
- CollectionPanel source filtering: embedded `collection-sources.json`, schema v4, `offerSegments` with typed per-enchantment rules; catalog locked to 69 sources / 47 merchants / 22 trainers (Zurphin's Safari excluded). [`...CollectionPanel/Sources/CollectionSourceCatalog.cs:15,96-104` | `tests/CollectionSourceFiltering.Tests/Program.cs:289-311`]
- Package custom-art replacement runs on 3 surfaces: live `ItemVisualsController.SetCardFrameMaterial`, `RewardController.Setup` (encounter-choice cards are runtime `EncounterStep` w/ package template id), and CollectionPanel `CardPreviewItem` (marker-gated). Live hover/tooltip preview is intentionally out of scope. [archive/plans/2026-06-11-package-card-art-live-game-replacement-fix.md | `src/BazaarPlusPlus/Patches/CardArtReplacement/`]
- LiveBuildPanel recommendations are owned by `Game/LiveBuildPanel/Recommendations`; corpus is analyzer-v4 ten-win builds (bundled seed → cache → remote `tenwin_builds.json`). [`docs/ARCHITECTURE.md` §History Panel...]
- Settings dock entry order is `int Order` per feature-owned `ISettingsDockEntry`; current literals: history 0, name-override 1, legendary 2, enchant 3, package-art 4, status-bar 5, chinese-locale 6, BazaarDB 7. [`src/BazaarPlusPlus/Game/Settings/`]
- Supporter attribution is a game-side display module fetching `bpp-static.bazaarplusplus.com/supporter-list.json` with bundled fallback; consumed by History/Collection/LiveBuild panels. [`src/BazaarPlusPlus/Game/Supporters/BPPSupporterCatalog.cs:14-25`]
- Cloud backend (uploads, ghost battles, BazaarDB snapshots) lives in separate repo `bazaarplusplus-server` (`mod-api-v4.bazaarplusplus.com`); mod side is `src/BazaarPlusPlus.ModApi/`. Server behavior is not code-verifiable from this repo. [README.md]

## Patterns

- Reuse the game's native UI components (e.g. `CardPreviewBase.SetUp`) and codebase prior-art instead of hand-rolling new render/upload chains. [CLAUDE.md]
- Shared runtime/prefab/static-data behavior needed by ≥2 features → extract an adapter to `GameInterop/<Concept>/`; don't import another feature's internals. [`docs/ARCHITECTURE.md` §layering rules]
- Hero portraits: resolve via `HeroPortraitSpriteProvider` (`GameInterop/HeroPortraits/`) — has cache + in-flight dedup; do not build per-feature providers. [`src/BazaarPlusPlus/GameInterop/HeroPortraits/HeroPortraitSpriteProvider.cs`]
- Synthetic/unknown GUIDs must never reach `CollectionCardFactory.TryBind`'s template lookup — unknown GUID → per-frame SQLite query + Warn spam (game has no negative cache). Memoize failed binds. [archive/plans/2026-06-10-collection-panel-achievements-tab.md | `Grid/CollectionGridVirtualizer.cs`]
- New architecture boundaries the compiler can't enforce get an architecture test. [`tests/Architecture.Tests/CoreLayeringTests.cs`]

## Gotchas

- Runtime `Card.HiddenTags` is NOT guaranteed to carry static template hidden tags (`DTOUtils.CreateCard` doesn't copy them; snapshot updates overwrite). Resolve package identity via `card.HiddenTags` → `card.Template.HiddenTags` → ready static data. [`src/BazaarPlusPlus/GameInterop/CardArtReplacement/CardArtInjector.cs` | archive/plans/2026-06-11-package-card-art-live-game-replacement-fix.md]
- Static-data lookups (`BppStaticDataAccess.TryGetReadyManagerObject`) throw in the xUnit host (game assembly absent) — wrap in try/catch returning a safe default. [archive/plans/2026-06-11-package-card-art-live-game-replacement-fix.md]
- `ItemVisualsController.Setup` is `async Task`; a postfix runs at the first await suspension, AFTER `SetCardFrameMaterial` when awaits complete synchronously. Bind identity in a **prefix**. [archive/plans/2026-06-11-package-card-art-live-game-replacement-fix.md]
- HistoryPanel list rows are pooled with no `unbindItem`/`destroyItem`; async portrait loads must guard staleness via a token (`userData`). [docs/plans/history-panel-hero-portrait-badge.md C7]
- `NativeKeybindLabelPatch` reflects `_keybindAction`, which no longer exists on the installed game (rows moved to `InputActionReference _action`) — stale, emits log warnings. [docs/plans/sell-hotkey-regression-debug-plan.md]
- BPP hotkey conflict check only compares BPP actions vs BPP actions, not native `Gameplay/*` bindings. [`src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs:207-209`]
- Tests split per-feature: some are xUnit (`dotnet test`, has `Microsoft.NET.Test.Sdk`), others exe-runners (`dotnet run --project`). Check the csproj before running. [CLAUDE.md]
- exe-runner test csprojs pin source files via explicit Compile-Include — moving/removing shared files breaks them silently. [docs/plans/achievement-ui-local-mvp.md]
