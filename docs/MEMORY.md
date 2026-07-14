<!-- Curated by consolidation runs only. Do not hand-edit; write new knowledge to docs/drafts/. -->
<!-- Budget: <=200 lines / <=10KB. Merge, don't append. Boundaries: AGENTS.md=process, this=knowledge, truth/overview.md (docs/ARCHITECTURE.md here)=structure. -->

# BazaarPlusPlus — Durable Memory

Dense agent-facing index. Structure (`docs/ARCHITECTURE.md`) and rationale (`docs/adr/`) are linked, not restated. Code is the source of truth; `decompiled/` is read-only game reference.

## Rules

Domain constraints that must stay true in the system. Process/workflow rules live in `CLAUDE.md` (AGENTS.md symlink).

- MessagePack-serialized DTOs in the Unity/Mono runtime must keep their whole serialized graph `public`. [decision: CLAUDE.md project rules]
- Key game entities (cards, merchants, trainers) by their stable template GUID, never by display name or `ArtKey` substring. [`GameInterop/Cards/PackageIdentity.cs` | `CONTEXT.md`]
- Package-card identity is `EHiddenTag.Package` only, resolved via `PackageIdentity.IsPackage`; never name/`ArtKey` heuristics. The custom package-art replacement subsystem was removed (9ae1999b); only the Items-tab exclusion remains. [`src/BazaarPlusPlus/GameInterop/Cards/PackageIdentity.cs:16` | `Game/CollectionPanel/Data/CollectionCardClassifier.cs:71-72`]
- The local SQLite schema is versioned; bump `RunLogSchema` (db=17, row=11, upload payload=5) when the persisted graph changes. [`src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs:10-14`]
- The mod carries **no play policy**: it does transport + validation only; all agent strategy lives in the external `bazaarplusplus-agent`. [ADR-0006]
- BazaarAgent v1 wire field names (`stateName`, `availableActions`, `actionKind`, `cardInstanceId`, `targetSection`, `targetSockets`, `reason`) are a stable contract — do not rename. [ADR-0006 | ADR-0007]
- BazaarAgent pure core (`BazaarPlusPlus.BazaarAgent.dll`) must stay `System` + `Newtonsoft.Json` only — no Unity/BepInEx/Harmony/game refs (enforced by architecture tests). [ADR-0006]
- The four base assemblies (`ModApi`, `Storage`, `Localization`, and their consumers) keep zero game/Unity/BepInEx references. [`docs/ARCHITECTURE.md` §Assemblies]
- Harmony patches apply per patch class via `CreateClassProcessor(type).Patch()` — never revert to `PatchAll()`: one broken game target (game update / PTR branch) must degrade only its own feature, not abort the whole plugin. [`src/BazaarPlusPlus/Plugin.cs:250-285`]
- CJK text that renders as tofu must be routed through the mod's native-typography adapter (`NativeGameTypography` — applies the game's native serif/sans and extends BPP-owned text with a Noto CJK fallback chain), not "fixed" by editing copy. [`src/BazaarPlusPlus/GameInterop/Fonts/NativeGameTypography.cs:121-160` | CLAUDE.md]
- Mod-authored user-facing strings use `LocalizedTextSet` (en + zh-Hans, optional zh-Hant + de/pt/ko/it; anything else falls back to English). [`src/BazaarPlusPlus.Localization/LocalizedTextSet.cs:14-79`]

## Architecture decisions

One line each; full record in `docs/adr/`. This repo uses `docs/adr/`, not a `decisions/` tree.

- ADR-0001 (2026): Expose run/encounter state via on-demand `IEncounterStateProbe`, not an event-sourced timeline tracker. [adr/0001]
- ADR-0002: Mount MonoBehaviour features via one-line `IBppMountable`/`BppMountableRegistry`; generalized to `ComponentMount<T>`. [adr/0002]
- ADR-0003: Render HistoryPanel previews via `ScreenSpaceOverlay` Canvas, NOT offscreen RenderTexture (URP cannot render uGUI to RT). Do not re-propose the RT path. [adr/0003]
- ADR-0004: Enchant preview = three-state visibility (`Off`/`AutoOnPedestalChoice`/`Always`, default `Always`); upgrade preview reverted to hold-Shift only. [adr/0004]
- ADR-0006 (2026-06-05): BazaarAgent is its own BepInEx plugin (`BazaarAgentHost.dll`) depending on BazaarPlusPlus; dependency inverted, fixed loopback `127.0.0.1:47900`. Absorbs superseded ADR-0005 (transport-only core, no play policy; collapsed 2026-07-11). [adr/0006]
- ADR-0007: External battle video recording via three primitive replay-control HTTP endpoints (`record`/`context`/`continue`); only `CombatReplayRuntime.TryContinueReplay` exits ReplayState. [adr/0007]
- ADR-0008 (2026-06-22): Replay exit exposed to the agent as a first-class `Continue` Flow action, emitted only at `finishedAwaitingContinue`, routed to the same `TryContinueReplay`; BazaarAgent action schema 2.1.0→2.2.0. [adr/0008]
- Non-ADR (2026-06-19): C5 "retire the three Core seams" refactor REJECTED — keep the seams; do not re-propose. [archive/plans/2026-06-19-five-deepening-refactors-plan.md]
- Non-ADR (2026-07-11 architecture-review batch, all red-teamed — do NOT re-propose): unified `RunGuardedAsync` over HistoryPanelCoordinator's four async handlers (6 divergence axes = config record); "HistoryPanel two-writer state" (proxy setters were dead code; coordinator already sole writer); OnPanelShown symmetric flag reset (trigger unreachable post-OverlayPanelHost; `GhostBattleSync.Tests` deliberately pins the preserve behavior); unifying the two tooltip line-normalizers (char-level different transforms); moving the facet snapshot onto `CollectionCatalogBuildResult` (deliberate hot-path placement in `SetCatalogCards`, bf91a60a); interop-before-game feature registration order (not load-bearing). [archive/design/2026-07-11-*.md]

## Durable knowledge

Verified facts about how the system works.

- Entry: `Plugin.Awake()` resolves the game build channel, creates `BppComposition` (manual composition root, no DI container), applies patches per class, builds the online + BazaarDB-link HTTP clients, then mounts components. Wires features (`IBppFeature`), mountables (`IBppMountable`), settings rows (`ISettingsDockEntry`). [`src/BazaarPlusPlus/Plugin.cs:40-93` | `BppComposition.cs:99-159`]
- Six assemblies: 4 ship unconditionally (`BazaarPlusPlus`, `.ModApi`, `.Storage`, `.Localization`); 2 BazaarAgent ship only with `./run.sh build --with-bazaaragent`. [`docs/ARCHITECTURE.md` §Assemblies]
- Layers: `Core/` pure abstractions; `GameInterop/` game/Unity adapters; `Game/` feature workflows+UI+policy; `Patches/` Harmony (reach services via static `BppPatchHost`); `Infrastructure/` cross-cutting. [`docs/ARCHITECTURE.md` §Boundaries]
- Game assemblies are publicized at build (`<PublicizeAll>` via Krafs.Publicizer), so `internal` game members are accessible. [`docs/ARCHITECTURE.md`]
- Runtime data roots under game dir `BazaarPlusPlusV4/` (SQLite db, replay payloads, screenshots, replay videos). [`src/BazaarPlusPlus/Core/Paths/BepInExPathProvider.cs:17-41`]
- Game build channel: PTR is detected by the `-ptr` token in `Application.version` plus a `TheBazaar.Config.ServerOption` type probe; on disagreement resolve to **Ptr** (PTR-as-Online silently pollutes the production dataset, Online-as-Ptr only pauses uploads). Uploads are gated `channel != Ptr`; `BuildChannel` is stamped on run rows. [`src/BazaarPlusPlus/GameInterop/GameBuildInfoResolver.cs:27-63` | `Game/RunLogging/Upload/RunBundleUploadStore.cs:47,75`]
- CollectionPanel source filtering: embedded `collection-sources.json`, schema v4, `offerSegments` with typed per-enchantment rules; catalog locked to 71 sources / 49 merchants / 22 trainers. [`...CollectionPanel/Sources/CollectionSourceCatalog.cs:15,101` | `tests/CollectionSourceFiltering.Tests/Program.cs:298-311`]
- LiveBuildPanel recommendations are owned by `Game/LiveBuildPanel/Recommendations`; corpus is analyzer-v4 ten-win builds (bundled seed → cache → remote `tenwin_builds.json`). [`docs/ARCHITECTURE.md` §History Panel...]
- Settings dock entry order is centralized in `BppSettingsDockOrder` const ints (no inline literals): name-override 0, supporter-list 1, status-bar 2, bilingual-names 3, event-preview 4, quest-preview 5, screenshot 6, BazaarDB 7, enchant 8, legendary 9, chinese-locale 10, voice-subtitles 11–14, history 15. [`src/BazaarPlusPlus/Game/Settings/BppSettingsDockOrder.cs:7-22`]
- Uploads run through one shared background pump behind the `IUploadFeed` seam: `UploadPumpMount` mounts a `BackgroundUploadPump` each for BazaarDB snapshots and run bundles (replaced the per-feature upload controllers, 90f1adb0). [`src/BazaarPlusPlus/Game/Upload/UploadPumpMount.cs:22-29`]
- BazaarDB account link: `BazaarDbLinkClient` POSTs to the bazaardb.gg redeem endpoint on a dedicated 30s-timeout HttpClient (independent of the mod-api client); surfaced as a collapsible card in the HistoryPanel right rail with a manual "already linked" button and an online gate (`channel != Ptr`, Unknown-as-Online). [`src/BazaarPlusPlus/Plugin.cs:213-227` | `Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs:388`]
- Voice subtitles (BazaarLine integration): `Game/VoiceSubtitles` + `GameInterop/VoiceSubtitles` bridge render VO subtitles from an embedded `voice-lines.json` with background remote refresh; master toggle default OFF, persisted in `BazaarPlusPlus.cfg` `[VoiceSubtitles]`; the dock exposes a four-state Subtitle Mode row. [`src/BazaarPlusPlus/Game/VoiceSubtitles/VoiceSubtitlesGate.cs:8-11` | `Core/Config/BppConfig.cs:98-103`]
- Supporter attribution is a game-side display module fetching `bpp-static.bazaarplusplus.com/supporter-list.json` with bundled fallback; consumed by History/Collection/LiveBuild panels. [`src/BazaarPlusPlus/Game/Supporters/BPPSupporterCatalog.cs:14-25`]
- Cloud backend (uploads, ghost battles, BazaarDB snapshots) lives in separate repo `bazaarplusplus-server` (`mod-api-v4.bazaarplusplus.com`); mod side is `src/BazaarPlusPlus.ModApi/`. Server behavior is not code-verifiable from this repo. [README.md]

## Patterns

- Reuse the game's native UI components (e.g. `CardPreviewBase.SetUp`) and codebase prior-art instead of hand-rolling new render/upload chains. [CLAUDE.md]
- Shared runtime/prefab/static-data behavior needed by ≥2 features → extract an adapter to `GameInterop/<Concept>/`; don't import another feature's internals. [`docs/ARCHITECTURE.md` §layering rules]
- `OverlayPanelHost` is the single owner of main-overlay-panel lifecycle (mutual exclusion, scene policy, combat gating, hotkey/escape routing, per-frame tick); `OverlayPanelHostMount` registers before every panel mount and panels register through its accessor instead of re-implementing lifecycle. [`src/BazaarPlusPlus/BppComposition.cs:160-163` | `Game/OverlayPanels/OverlayPanelHost.cs:12-45`]
- Hero portraits: resolve via `HeroPortraitSpriteProvider` (`GameInterop/HeroPortraits/`) — has cache + in-flight dedup; do not build per-feature providers. [`src/BazaarPlusPlus/GameInterop/HeroPortraits/HeroPortraitSpriteProvider.cs`]
- Mod-appended tooltip text goes through `BppTooltipSections`: clones the tooltip's own passive-text block (native typography, 0.75 font scale), keyed per controller+purpose, inserted after a caller-supplied anchor — reuse it instead of hand-rolling sections. [`src/BazaarPlusPlus/Patches/Tooltips/BppTooltipSections.cs:8-16`]
- BPP settings dock buttons are clones of native dock buttons (`BppDockButtonVisuals`) that must avoid native-button bounds (`BppDockButtonBounds`) and re-sync geometry for several frames after a screen-size change. [`src/BazaarPlusPlus/Game/Settings/BppDockButtonVisuals.cs:72-98` | `Game/Settings/BppScreenResizeSyncTracker.cs:17-31`]
- Synthetic/unknown GUIDs must never reach `CollectionCardFactory.TryBind`'s template lookup — unknown GUID → per-frame SQLite query + Warn spam (game has no negative cache). Memoize failed binds. [archive/plans/2026-06-10-collection-panel-achievements-tab.md | `Grid/CollectionGridVirtualizer.cs`]
- New architecture boundaries the compiler can't enforce get an architecture test. [`tests/Architecture.Tests/CoreLayeringTests.cs`]
- Settings-dock rows are data specs via `CyclingSettingsDockEntry<T>`/`Toggle` factories — new rows contribute ladders/delegates, not classes; enchant preview is a plain `Off → AutoOnPedestalChoice → Always` ladder built by `ItemEnchantPreviewSettingsDockEntry.Create()`. [`src/BazaarPlusPlus/Game/Settings/CyclingSettingsDockEntry.cs` | `Game/ItemEnchantPreview/ItemEnchantPreviewSettingsDockEntry.cs:9-29`]
- Pure-module test pattern for Unity-adjacent logic: keep the file free of Unity/game types and Compile-Include it into a zero-ManagedPath test project (`OverlayLifecycleCore` → `HotkeyBindingPathCore` → `AsyncLoadCache` all follow it; no InternalsVisibleTo needed).
- `CombatReplayPayloadStore`/`GhostBattlePayloadStore` class/method names, ctor arity, and file suffixes are reflection-pinned by exe-runner tests — keep the named facades over `FileBackedPayloadStore<T>`; never rename. [`tests/CombatReplayRecording.Tests/Program.cs` | `tests/GhostBattleSync.Tests/Program.cs`]

## Gotchas

- Runtime `Card.HiddenTags` is NOT guaranteed to carry static template hidden tags (`DTOUtils.CreateCard` doesn't copy them; snapshot updates overwrite) — derive identity from `template.HiddenTags`/static data, not runtime card tags. [`decompiled/TheBazaarRuntime/TheBazaar/DTOUtils.cs:14-19` | `Game/CollectionPanel/Data/CollectionCardClassifier.cs:71-72`]
- Static-data lookups (`BppStaticDataAccess.TryGetReadyManagerObject`) throw in the xUnit host (game assembly absent) — wrap in try/catch returning a safe default. [archive/plans/2026-06-11-package-card-art-live-game-replacement-fix.md]
- A Harmony postfix on an `async Task` game method (e.g. `ItemVisualsController.Setup`) runs at the first await suspension, not completion — bind pre-state in a **prefix**. [`decompiled/TheBazaarRuntime/TheBazaar.Game.CardFrames/ItemVisualsController.cs:248-262`]
- HistoryPanel list rows are pooled with no `unbindItem`/`destroyItem`; async portrait loads must guard staleness via a token (`userData`). [`src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs:831-845` | `Ui/HistoryPanelUiToolkitView.Rows.cs:94-113`]
- BPP hotkey conflict check only compares BPP actions vs BPP actions, not native `Gameplay/*` bindings. [`src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs:243-257`]
- `PublicizeAll` makes `ConfigEntry<T>.SettingChanged` ambiguous (CS0229), so no BPP code subscribes to it — invalidate config-derived caches by raw-value compare instead. [`src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs:148-166`]
- Panel toggle hotkeys are filtered per registration by `HotkeyGuard` (returning false swallows the press); HistoryPanel deliberately won't close while a text field is focused — do not "fix" this. [`src/BazaarPlusPlus/Game/OverlayPanels/OverlayPanelHost.cs:80` | `Game/HistoryPanel/HistoryPanel.cs:458`]
- BazaarDB link: `OnPanelShown` must reset `AccountLinkInProgress` — `_session.Begin()` cancels in-flight redeems whose continuations bail on `!IsCurrent` without resetting state, and re-entrant opens skip `OnPanelHidden`; otherwise the link row goes permanently inert. [`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs:59-71`]
- VoiceSubtitles labels clone the donor label's font/material and must keep `TextWrappingModes.Normal` + `TextOverflowModes.Overflow` — NoWrap+Ellipsis at scaled font hits TMP's `m_characterCount == 0` branch and the whole subtitle block disappears. [`src/BazaarPlusPlus/Game/VoiceSubtitles/VoiceLineDisplay.cs:423-448`]
- Pooled native tooltip clones share one root-canvas `sortingOrder`, and `overrideSorting` cannot be set on a root canvas — layering a BPP preview above the other (locked) clone means raising this clone's root-canvas order while visible (refcounted per owner); sibling order inside the prefab is irrelevant. This bug class recurred 3×. [`src/BazaarPlusPlus/Patches/Tooltips/ItemEnchantPreviewTooltipLayerPatch.cs:53-96`]
- `DayTierSchedule` (Game/CollectionPanel/Data) survives the removed shop-probability feature and is still live via `CollectionFilterEngine`/`EncounterEventTooltipPatch` (tier-ceiling filter); it is a hardcoded approximation of `tierManager.json` that shifts with balance patches. [`src/BazaarPlusPlus/Game/CollectionPanel/Data/DayTierSchedule.cs`]
- Tests split per-feature: some are xUnit (`dotnet test`, has `Microsoft.NET.Test.Sdk`), others exe-runners (`dotnet run --project`). Check the csproj before running. [CLAUDE.md]
- exe-runner test csprojs pin source files via explicit Compile-Include — moving/removing shared files breaks them silently. [`tests/HistoryPanelPreview.Tests/HistoryPanelPreview.Tests.csproj`]
- Portrait providers negative-cache exceptions past their service-readiness gates: a transient asset-load exception permanently caches a null portrait until restart (shared latent behavior, preserved by design in the `AsyncLoadCache` migration — fix would be a behavior change). [`GameInterop/HeroPortraits/` | `GameInterop/EncounterPortraits/`]
- Never `Instantiate` a GameObject that already hosts a BPP MonoBehaviour controller: the clone's `OnEnable` runs synchronously inside `Instantiate`, and cloned MonoBehaviours skip C# field initializers → null fields → NRE (often swallowed by patch try/catch, so the UI just silently fails to appear). Clone the controller-free native object first, then `AddComponent`. [`src/BazaarPlusPlus/Patches/Settings/BppKeybindSettingsPatch.cs:97` prior art | git-history: archive/superpowers 2026-06-02-settings-dock-clone-button]
