# BazaarPlusPlus Architecture

This is the living architecture document for the current repository state. The code remains the source of truth; this document summarizes only behavior verified against current source paths during the 2026-06-10 documentation calibration.

## Runtime Shape

BazaarPlusPlus is a BepInEx 5 plugin. `Plugin.Awake()` creates `BppComposition`, installs static facades such as `BppLog`, `BppPatchHost`, localization, settings dock entries, hotkeys, and run-log readers, applies Harmony patches, starts feature modules, then mounts Unity components onto the plugin `GameObject` (`src/BazaarPlusPlus/Plugin.cs:34-67`, `src/BazaarPlusPlus/Plugin.cs:102-111`).

`BppComposition` is the manual composition root. It wires feature modules through `BppFeatureRegistry`, Unity components through `BppMountableRegistry`, and in-game settings rows through `SettingsDockEntryRegistry` (`src/BazaarPlusPlus/BppComposition.cs:83-123`). It also publishes the passive BazaarAgent game facades through `BazaarAgentGameBridge` so the optional host plugin can consume them without the main plugin referencing the agent core (`src/BazaarPlusPlus/BppComposition.cs:125-135`).

The main plugin project targets `netstandard2.1`, uses C# 12, publicizes game assemblies, embeds data resources, and references the three unconditional child assemblies: `BazaarPlusPlus.ModApi`, `BazaarPlusPlus.Storage`, and `BazaarPlusPlus.Localization` (`src/BazaarPlusPlus/BazaarPlusPlus.csproj:7-10`, `src/BazaarPlusPlus/BazaarPlusPlus.csproj:23-32`, `src/BazaarPlusPlus/BazaarPlusPlus.csproj:107-109`, `src/BazaarPlusPlus/BazaarPlusPlus.csproj:164-166`). The shared version is `BppVersion` in `Directory.Build.props` (`Directory.Build.props:3`).

## Assemblies And Boundaries

The unconditional runtime assemblies are:

- `BazaarPlusPlus.dll`: the BepInEx plugin, Unity/game integration, feature modules, Harmony patches, embedded resources, and composition root.
- `BazaarPlusPlus.ModApi.dll`: HTTP client, routes, DTOs, and MessagePack/gzip helpers for the mod backend.
- `BazaarPlusPlus.Storage.dll`: SQLite schema, repositories, path interfaces, and local persistence models.
- `BazaarPlusPlus.Localization.dll`: localization resolution engine and language/mode helpers.

The optional BazaarAgent assemblies are:

- `BazaarPlusPlus.BazaarAgent.dll`: pure HTTP transport, action DTOs, validation, queues, and runtime controller.
- `BazaarPlusPlus.BazaarAgentHost.dll`: a separate BepInEx plugin that depends on BazaarPlusPlus, reads `BazaarAgentGameBridge`, and pumps the agent controller from Unity lifecycle methods (`src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentHostPlugin.cs:16-63`).

The main source tree follows these boundaries:

- `Core/`: pure mod abstractions such as config, event bus, runtime service interfaces, run context, and path contracts.
- `GameInterop/`: adapters over game, Unity, or publicized runtime surfaces, including client cache reads, static card data reads, encounter status probes, live card snapshots, item-board preview adapters, and BazaarAgent cross-plugin facades.
- `Game/`: feature workflows, UI, user-facing policy, filtering, upload orchestration, storage use, and BPP-owned domain modules such as PvP battle evidence.
- `Patches/`: Harmony patches. Patches reach services through `BppPatchHost`, not constructor injection (`src/BazaarPlusPlus/Patches/BppPatchHost.cs:7-24`).
- `Infrastructure/`: cross-cutting logging, font loading, UI tokens, stable text helpers, and shared non-feature utilities.

Architecture tests ratchet these boundaries, including `Core/` layering, shared item-board preview ownership, BazaarAgent isolation, and source layout (`tests/Architecture.Tests/CoreLayeringTests.cs:29-74`, `tests/Architecture.Tests/CoreLayeringTests.cs:287-346`, `tests/Architecture.Tests/CoreLayeringTests.cs:931-998`).

## Data And File Locations

Runtime data that BazaarPlusPlus owns is rooted under the game directory's `BazaarPlusPlusV4` folder. `BepInExPathProvider.Initialize()` sets the SQLite database path, combat replay payload directory, screenshot directory, combat replay video directory, custom card-art directory, and plugin directory (`src/BazaarPlusPlus/Core/Paths/BepInExPathProvider.cs:20-48`).

The local SQLite schema is versioned in `RunLogSchema`. The current local database schema version is `16`, row schema version is `11`, and upload payload schema version is `5` (`src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs:10-18`). The schema creates runs, run events, battles, battle snapshots, run screenshots, combat replay videos, sync cursors, run sync state, and BazaarDB snapshot upload state plus indexes (`src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs:57-263`).

## Event Flow

The plugin uses an in-memory event bus for decoupled runtime coordination. `RunLifecycleModule`, `CombatReplayModule`, and `CombatStatusBarModule` are feature modules registered at startup (`src/BazaarPlusPlus/BppComposition.cs:79-86`). The combat status bar subscribes to `CombatSimObserved` and `CombatFrameAdvanced`; it stores the latest message id/outcome in run context and advances the HUD frame count from replay events (`src/BazaarPlusPlus/Game/CombatStatusBar/CombatStatusBarModule.cs:25-55`).

PvP battle evidence is a shared `Game/PvpBattles` module, not a `GameInterop` adapter. It captures local/opponent identities and board snapshots from live game state and net messages (`src/BazaarPlusPlus/Game/PvpBattles/PvpBattleSnapshotCollector.cs:16-66`). Run logging subscribes to `PvpBattleRecorded`, attaches the battle to the current run when possible, and passes a replay event into the run-log core (`src/BazaarPlusPlus/Game/RunLogging/RunLoggingModule.cs:92-100`, `src/BazaarPlusPlus/Game/RunLogging/RunLoggingModule.cs:185-217`).

## Collection Panel

CollectionPanel is a full-screen Item/Skill catalog mounted by `CollectionPanelMount` (`src/BazaarPlusPlus/BppComposition.cs:99`). Its filter state is pure mutable data: active card type, single-select hero set, tiers, tags, hidden keyword tags, Any/All facet modes, sizes, selected source key, packages-only mode, run-day filter, and sort priority (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterState.cs:14-42`).

Filtering is code-owned and deterministic. `CollectionFilterEngine.FilterAndSort()` applies active card type, packages-only handling, source offer pool membership, hero, tier, day, tag, keyword, and size filters, then sorts by tier/size priority plus localized display name (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:40-86`). Tag and keyword facets support `Any` and `All` matching (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:105-135`), and the UI exposes mode toggles in the tag and keyword headers (`src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:173-199`).

Merchant and trainer source filtering is driven by an embedded `collection-sources.json` catalog. The current `CollectionSourceCatalog.ExpectedSchemaVersion` is `4`; mismatched JSON versions are rejected during build/parse (`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:15`, `src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:96-104`). Each source has `offerSegments`, and each segment parses a typed rule with hero mode, starting tier, sizes, tags, hidden tags and groups, enchantability, and enchantment facets (`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:239-330`).

Card VMs project base card tags and per-enchantment facets from game templates. Lifesteal is derived when an item has positive Lifesteal base value at any tier even if the template lacks the hidden tag (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardVm.From.cs:24-45`, `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionDerivedKeywordFacts.cs:20-37`).

## History Panel, Item Boards, And Live Build

HistoryPanel is mounted through `HistoryPanelMount`, configured with runtime dependencies, data services, replay services, and an item-board preview source (`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.cs:129-143`). It renders battle previews by asking the shared item-board preview renderer to render a projected board and handling preview phases (`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.cs:318-340`).

HistoryPanel's right rail contains local database status, remote server health probing, runs/ghost tabs, ghost filters, delete/replay controls, and status text (`src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs:333-405`). Server health display state is derived from the mod API health probe result without changing storage state (`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelServerHealth.cs:51-81`).

`GameInterop/ItemBoardPreview` is the shared board-rendering adapter for HistoryPanel and LiveBuildPanel. `BppItemBoardSlotPlanner` plans cards onto a 10-socket board, with special handling for selectable shop/container boards (`src/BazaarPlusPlus/GameInterop/ItemBoardPreview/BppItemBoardSlotPlanner.cs:10-24`).

LiveBuildPanel reads live shop, board, and stash cards through `LiveCardSnapshotReader` (`src/BazaarPlusPlus/GameInterop/LiveCards/LiveCardSnapshotReader.cs:21-35`). Its recommendation backend is owned by `Game/LiveBuildPanel/Recommendations`: `BuildRecommendationRepository` loads the analyzer-v4 ten-win build corpus from a bundled/cache/remote source and answers local recommendation queries (`src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendationRepository.cs:20-29`, `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendationRepository.cs:52-89`). The panel exposes a manual final-build refresh action and recomputes recommendations from candidate state (`src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs:217-247`, `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs:319-329`).

## Combat Replay And Video

Combat replay records local PvP battle replay payloads and lets HistoryPanel start playback when a local or downloaded ghost replay payload is available. Video recording is optional and gated by platform support, an FFmpeg binary, and output directory configuration (`src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs:145-170`).

When recording starts, `CombatReplayVideoRecorder` creates a capture session, stores temp/final output paths, starts audio taps, suppresses selected BPP chrome, saves start metadata, and starts the capture coroutine (`src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs:465-493`). Audio capture is additive: capture failure does not abort video recording. The recorder derives a WAV path, starts the platform capture tap if available, and logs/cleans up on failure (`src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs:496-529`). Stop logic only passes WAV files that captured samples to the muxer (`src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs:259-294`).

BPP UI chrome suppression is centralized in `Game/OverlayPanels`. Screenshot mode hides CollectionPanel dock, settings dock, and combat status bar; replay recording mode hides CollectionPanel dock and settings dock while intentionally leaving the combat status bar visible (`src/BazaarPlusPlus/Game/OverlayPanels/BppUiChromeSuppression.cs:13-25`).

## Screenshots, Uploads, And Ghost Battles

End-of-run screenshot capture initializes from the screenshot and run-log paths, creates a screenshot service, and writes screenshot metadata to SQLite when the database path is available (`src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs:60-73`). It suppresses BPP UI chrome during capture (`src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs:411-414`).

BazaarDB snapshot upload is optional. When enabled and the database/screenshots paths are valid, `BazaarDbSnapshotUploadController` creates API routes, a SQLite-backed upload store, an HTTP client, and an upload service keyed by the current profile account id (`src/BazaarPlusPlus/Game/Screenshots/Upload/BazaarDbSnapshotUploadController.cs:50-82`).

Run and replay uploads use the mod API client and local storage state. Ghost battle sync imports remote battle data into local history storage; server-side behavior is outside this repository and should not be treated as code-verified here.

## Tooltip Preview, Settings, And Hotkeys

Enchant preview visibility is a three-state setting controlled by `PreviewVisibilityMode`: `Off`, `AutoOnPedestalChoice`, and `Always`. `BppSettingsDockCatalog.NextPreviewVisibilityMode()` cycles those states and resolves localized status labels for dock display (`src/BazaarPlusPlus/Game/Settings/BppSettingsDockCatalog.cs:32-61`).

`TooltipPreviewModePolicy` owns preview priority. Upgrade hotkey wins first, then enchant hotkey, then enchant `Always`, then enchant `AutoOnPedestalChoice` only when the current choice pedestal is an enchant pedestal (`src/BazaarPlusPlus/Game/Tooltips/TooltipPreviewModePolicy.cs:30-43`). Default BPP tooltip hotkeys are Ctrl for enchant preview and Shift for upgrade preview (`src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs:47-51`).

The current BPP hotkey conflict check compares BPP actions against other BPP actions (`src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs:207-209`). Native game binding conflicts, including sell-hotkey interactions, remain an active investigation item in `docs/plans/`.

## Localization And Fonts

`BazaarPlusPlus.Localization` is the localization resolution engine. The `L` facade is installed at plugin startup with language and locale-mode providers, then resolves `LocalizedTextSet` values against current language and Chinese locale mode (`src/BazaarPlusPlus.Localization/L.cs:11-30`, `src/BazaarPlusPlus/Plugin.cs:107-109`).

CJK text in BPP-owned UI is handled with an embedded LXGW WenKai font. `BppUiFont` extracts `LXGWWenKai-Regular.ttf` from embedded resources to a BepInEx cache path and loads it as a Unity `Font` (`src/BazaarPlusPlus/Infrastructure/Fonts/BppUiFont.cs:14-40`). The font and license are embedded by the main project (`src/BazaarPlusPlus/BazaarPlusPlus.csproj:31-32`). The font file is subset by Unicode range rather than by the current source strings so BPP-owned Chinese UI keeps broad glyph coverage (`src/BazaarPlusPlus/Resources/Fonts/README.md:1-15`).

## Supporter Attribution

Supporter attribution is a game-side display module, not a generic sampling utility. `BPPSupporters.SampleMany()` and `Sample()` delegate to `BPPSupporterCatalog.GetCurrentEntries()` and `BPPSupporterSampler` (`src/BazaarPlusPlus/Game/Supporters/BPPSupporters.cs:13-31`). The catalog fetches `https://bpp-static.bazaarplusplus.com/supporter-list.json`, caches it in temp storage, and falls back to bundled placeholder entries if remote/cache reads fail (`src/BazaarPlusPlus/Game/Supporters/BPPSupporterCatalog.cs:14-25`, `src/BazaarPlusPlus/Game/Supporters/BPPSupporterCatalog.cs:47-82`).

HistoryPanel, CollectionPanel, and LiveBuildPanel consume supporter samples for attribution rows. This module should stay separate from feature-specific UI behavior.

## BazaarAgent Optional Host

BazaarAgent is optional. The host plugin declares `[BepInDependency(BppPluginMetadata.Guid)]`, reads `BazaarAgentGameBridge.Current`, creates a pure runtime controller, pumps it from `Update()`, and disposes it on destroy (`src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentHostPlugin.cs:16-63`).

The host listens only on loopback. The default port is fixed at `127.0.0.1:47900`, with a 32 MiB cap for raw replay-record request bodies (`src/BazaarPlusPlus.BazaarAgent/Contract/BazaarAgentPorts.cs:8-22`, `src/BazaarPlusPlus.BazaarAgent/Transport/BazaarAgentHttpServer.cs:57-58`). Current routes are:

- `GET /v1/context`
- `POST /v1/actions`
- `POST /v1/replay/record`
- `POST /v1/replay/continue`

The action set (`POST /v1/actions`) adds `ReturnToMenu` (schema `2.1.0`): emitted only in
`EndRunVictory`/`EndRunDefeat` while the scene loader is not transitioning, it advances the
end-of-run screen back to hero-select via `RunManager.LoadMainMenu()` — the same call the native
"return to menu" button makes — closing the unattended `run → next run` loop. `isClientBusy` now
reflects the real client state (`AppState.IsWaitingForServerResponse || AppState.BlockInput`) instead
of a constant `false`.

Those routes are dispatched in `BazaarAgentHttpServer.HandleContextAsync()` (`src/BazaarPlusPlus.BazaarAgent/Transport/BazaarAgentHttpServer.cs:132-160`). Replay record accepts a raw binary GhostBattlePayload msgpack+gzip body and optional battle id from header/query before queueing a start command; replay continue queues an explicit continue command (`src/BazaarPlusPlus.BazaarAgent/Transport/BazaarAgentHttpServer.cs:245-276`).

## Decision Records

This repository already uses `docs/adr/` for decision records. The documentation restructure keeps that convention instead of creating a parallel `docs/decisions/` tree.

Current decision records cover encounter status probes, the mountable registry, HistoryPanel preview overlay rendering, preview visibility mode, BazaarAgent isolation, BazaarAgent as a separate plugin, and external replay video control. See [adr/](adr/).
