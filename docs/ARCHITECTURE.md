# BazaarPlusPlus Architecture

This is the living architecture document for the current repository state. The code remains the source of truth; this document summarizes only behavior verified against current source paths during the 2026-07-10 documentation calibration, amended 2026-07-11 for the architecture-review batch (PRs #22–#28, #30, #31) and 2026-07-13 for native game-font ownership.

## Runtime Shape

BazaarPlusPlus is a BepInEx 5 plugin. `Plugin.Awake()` resolves the game build channel via `GameBuildInfoResolver`, creates `BppComposition`, installs static facades such as `BppLog`, `BppPatchHost`, localization, settings dock entries, supporters, and hotkeys, applies Harmony patches per patch class (so one broken game target degrades only its own feature, never the whole plugin), starts feature modules, builds the online and BazaarDB-link HTTP clients, then mounts Unity components onto the plugin `GameObject` (`src/BazaarPlusPlus/Plugin.cs:43-100`, `src/BazaarPlusPlus/Plugin.cs:185-196`). Teardown releases the native game-font handles and cloned font assets through `NativeGameFonts.Reset()` (`src/BazaarPlusPlus/Plugin.cs:198-210`).

`BppComposition` is the manual composition root; it receives the resolved `IGameBuildInfo`. It wires feature modules through `BppFeatureRegistry`, Unity components through `BppMountableRegistry`, and in-game settings rows through `SettingsDockEntryRegistry` (`src/BazaarPlusPlus/BppComposition.cs:102-162`). It also publishes the passive BazaarAgent game facades through `BazaarAgentGameBridge` so the optional host plugin can consume them without the main plugin referencing the agent core (`src/BazaarPlusPlus/BppComposition.cs:164-174`).

The main plugin project targets `netstandard2.1`, uses C# 12, publicizes game assemblies, embeds data resources (including `Data\VoiceSubtitles\voice-lines.json`), and references the three unconditional child assemblies: `BazaarPlusPlus.ModApi`, `BazaarPlusPlus.Storage`, and `BazaarPlusPlus.Localization` (`src/BazaarPlusPlus/BazaarPlusPlus.csproj:22-31`, `src/BazaarPlusPlus/BazaarPlusPlus.csproj:110-112`, `src/BazaarPlusPlus/BazaarPlusPlus.csproj:167-170`). The shared version is `BppVersion` in `Directory.Build.props` (`Directory.Build.props:3`).

## Game Build Channel And PTR Isolation

`GameBuildInfoResolver` classifies the running client as Online/Ptr/Unknown from the bundleVersion `-ptr` token plus a corroborating `TheBazaar.Config.ServerOption` type probe, treating disagreement as Ptr to protect the production dataset (`src/BazaarPlusPlus/GameInterop/GameBuildInfoResolver.cs:29-63`). The channel is injected into the composition (`src/BazaarPlusPlus/Plugin.cs:49-50`), stamped onto recorded runs when `RunLoggingController` assembles the create request (`src/BazaarPlusPlus/Game/RunLogging/RunLoggingController.cs:120-133`), and PTR runs are excluded from the upload feed (`src/BazaarPlusPlus/Game/RunLogging/Upload/RunBundleUploadStore.cs:47`, `:75`). Premise tests live in `tests/PtrCompatibility.Tests/`.

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

- `Core/`: pure mod abstractions such as config, event bus, runtime service interfaces, run context, path contracts, and the run-snapshot contract (`IRunSnapshotProbe` plus its `RunBasicsSnapshot`/`PlayerStatsSnapshot`/`RankSnapshot` DTOs in `Core/GameState/`).
- `GameInterop/`: adapters over game, Unity, or publicized runtime surfaces, including client cache reads, static card data reads, encounter status probes, the run snapshot probe (`GameInterop/RunSnapshot/` — the only place `Data.Run`/rank/leaderboard reads happen for RunLogging and Screenshots), live card snapshots, item-board preview adapters, native game-font access (`GameInterop/Fonts/`), and BazaarAgent cross-plugin facades.
- `Game/`: feature workflows, UI, user-facing policy, filtering, upload orchestration, storage use, and BPP-owned domain modules such as PvP battle evidence. Record construction for run logs and screenshots is pure mappers (`RunLogRecordMapper`, `RunScreenshotRecordMapper`) consuming probe snapshots.
- `Patches/`: Harmony patches. Patches reach services through `BppPatchHost`, not constructor injection (`src/BazaarPlusPlus/Patches/BppPatchHost.cs:7-24`).
- `Infrastructure/`: cross-cutting logging, UI tokens, stable text helpers, and shared non-feature utilities, including the generic seams `AtomicFileWriter`, `AsyncLoadCache<TKey,TValue>` (in-flight-deduped negative-caching async loads; hero/encounter portrait providers are thin facades over it), and `FileBackedPayloadStore<T>` (battleId-keyed atomic MessagePack payload files; the combat-replay and ghost payload stores are name-pinned facades over it).

Architecture tests ratchet these boundaries, including `Core/` layering, shared item-board preview ownership, BazaarAgent isolation, voice-subtitle guards, and source layout (`tests/Architecture.Tests/CoreLayeringTests.cs`; Core layering starts at `:30`). Behavioral guarantees of the composition registries (feature start/stop fault isolation, mount ordering) are pinned by `tests/CompositionRuntime.Tests/`; `BppMountableRegistry.MountAll` deliberately has no per-item fault isolation (pinned as current behavior, unlike `BppFeatureRegistry`).

## Data And File Locations

Runtime data that BazaarPlusPlus owns is rooted under the game directory's `BazaarPlusPlusV4` folder. `BepInExPathProvider.Initialize()` sets the SQLite database path, combat replay payload directory, screenshot directory, combat replay video directory, and plugin directory (`src/BazaarPlusPlus/Core/Paths/BepInExPathProvider.cs:18-41`).

The local SQLite schema is versioned in `RunLogSchema`. The current local database schema version is `17` (v17 added the `build_channel` run column for PTR isolation), row schema version is `11`, and upload payload schema version is `5` (`src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs:10-14`). The schema creates runs, run events, battles, battle snapshots, run screenshots, combat replay videos, sync cursors, run sync state, and BazaarDB snapshot upload state plus indexes (`src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs:57-263`).

## Event Flow

The plugin uses an in-memory event bus for decoupled runtime coordination. `RunLifecycleModule`, `CombatReplayModule`, `CombatStatusBarModule`, `VoiceSubtitlesInteropModule`, and `VoiceSubtitlesModule` are feature modules registered at startup (`src/BazaarPlusPlus/BppComposition.cs:105-109`). The combat status bar subscribes to `CombatSimObserved` and `CombatFrameAdvanced`; it stores the latest message id/outcome in run context and advances the HUD frame count from replay events (`src/BazaarPlusPlus/Game/CombatStatusBar/CombatStatusBarModule.cs:25-55`).

PvP battle evidence is a shared `Game/PvpBattles` module, not a `GameInterop` adapter. It captures local/opponent identities and board snapshots from live game state and net messages (`src/BazaarPlusPlus/Game/PvpBattles/PvpBattleSnapshotCollector.cs:16-66`). Run logging subscribes to `PvpBattleRecorded`, attaches the battle to the current run when possible, and passes a replay event into the run-log core (`src/BazaarPlusPlus/Game/RunLogging/RunLoggingModule.cs:92-100`, `src/BazaarPlusPlus/Game/RunLogging/RunLoggingModule.cs:185-217`).

## Overlay Panels

`Game/OverlayPanels/OverlayPanelHost.cs` is the single Unity adapter that owns main-overlay-panel lifecycle: it reads per-frame input (escape, panel toggle hotkeys, combat state, scene changes), runs the pure `OverlayLifecycleCore` rules, and dispatches open/close/tick to registered panels (`src/BazaarPlusPlus/Game/OverlayPanels/OverlayPanelHost.cs:12-45`). It mounts before every panel mount, and CollectionPanel, HistoryPanel, and LiveBuildPanel all register through its accessor, enforcing at-most-one-open-panel (`src/BazaarPlusPlus/BppComposition.cs:126-149`). Lifecycle tests live in `tests/OverlayPanelLifecycle.Tests/`.

## Collection Panel

CollectionPanel is a full-screen Item/Skill catalog mounted by `CollectionPanelMount`, registered with the shared Overlay Panel Host (`src/BazaarPlusPlus/BppComposition.cs:126-130`). Its filter state is pure mutable data: active tab/card type, single-select hero set, tiers, tags, hidden keyword tags, Any/All facet modes, sizes, selected source key, run-day filter, and sort priority (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterState.cs:24-49`). The tab model contains only `Items` and `Skills` (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionTabKind.cs:6-15`).

Filtering is code-owned and deterministic. `CollectionFilterEngine.Apply()` applies active card type, excludes package cards from normal results, applies source offer pool membership, hero, tier, day, tag, keyword, and size filters, then sorts by tier/size priority plus localized display name (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:15-80`). Tag and keyword facets support `Any` and `All` matching (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:99-135`), and the UI exposes mode toggles in the tag and keyword headers (`src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:173-199`).

Merchant and trainer source filtering is driven by an embedded `collection-sources.json` catalog. The current `CollectionSourceCatalog.ExpectedSchemaVersion` is `4`; mismatched JSON versions are rejected during build/parse (`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:15`, `src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:96-104`). Each source has `offerSegments`, and each segment parses a typed rule with hero mode, starting tier, sizes, tags, hidden tags and groups, enchantability, and enchantment facets (`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:239-330`).

Card VMs project base card tags and per-enchantment facets from game templates. Lifesteal is derived when an item has positive Lifesteal base value at any tier even if the template lacks the hidden tag (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardVm.From.cs:24-45`, `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionDerivedKeywordFacts.cs:20-37`).

## History Panel, Item Boards, And Live Build

HistoryPanel is mounted through `HistoryPanelMount`, configured with runtime dependencies, data services, replay services, and an item-board preview source (`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.cs:129-143`). It renders battle previews by asking the shared item-board preview renderer to render a projected board and handling preview phases (`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.cs:318-340`).

HistoryPanel's right rail contains local database status, remote server health probing, runs/ghost tabs, ghost filters, delete/replay controls, status text, and a collapsible BazaarDB account-link card (`src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs:356-410`). The account-link card POSTs a one-time code to the bazaardb.gg redeem endpoint through `BazaarDbLinkClient`, built at startup on a dedicated 30s-timeout HttpClient independent of the mod-api base URL (`src/BazaarPlusPlus/Plugin.cs:181-195`), with link state persisted by `Game/HistoryPanel/AccountLink/BazaarDbAccountLinkStore.cs`. Server health display state is derived from the mod API health probe result without changing storage state (`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelServerHealth.cs:51-81`).

`GameInterop/ItemBoardPreview` is the shared board-rendering adapter for HistoryPanel and LiveBuildPanel. `BppItemBoardSlotPlanner` plans cards onto a 10-socket board, with special handling for selectable shop/container boards (`src/BazaarPlusPlus/GameInterop/ItemBoardPreview/BppItemBoardSlotPlanner.cs:10-24`).

LiveBuildPanel reads live shop, board, and stash cards through `LiveCardSnapshotReader` (`src/BazaarPlusPlus/GameInterop/LiveCards/LiveCardSnapshotReader.cs:21-35`). Its recommendation backend is owned by `Game/LiveBuildPanel/Recommendations`: `BuildRecommendationRepository` loads the analyzer-v4 ten-win build corpus from a bundled/cache/remote source and answers local recommendation queries (`src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendationRepository.cs:20-29`, `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendationRepository.cs:52-89`). The panel exposes a manual final-build refresh action and recomputes recommendations from candidate state (`src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs:217-247`, `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs:319-329`).

## Combat Replay And Video

Combat replay records local PvP battle replay payloads and lets HistoryPanel start playback when a local or downloaded ghost replay payload is available. Video recording is optional and gated by platform support, an FFmpeg binary, and output directory configuration (`src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs:145-170`).

When recording starts, `CombatReplayVideoRecorder` creates a capture session, stores temp/final output paths, starts audio taps, suppresses selected BPP chrome, saves start metadata, and starts the capture coroutine (`src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs:465-493`). Audio capture is additive: capture failure does not abort video recording. The recorder derives a WAV path, starts the platform capture tap if available, and logs/cleans up on failure (`src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs:496-529`). Stop logic only passes WAV files that captured samples to the muxer (`src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs:259-294`).

BPP UI chrome suppression is centralized in `Game/OverlayPanels`. Screenshot mode hides CollectionPanel dock, settings dock, and combat status bar; replay recording mode hides CollectionPanel dock and settings dock while intentionally leaving the combat status bar visible (`src/BazaarPlusPlus/Game/OverlayPanels/BppUiChromeSuppression.cs:13-25`).

## Screenshots, Uploads, And Ghost Battles

End-of-run screenshot capture initializes from the screenshot and run-log paths, then arms from the native `EndOfRunScreenInitializing` lifecycle (`src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs:58-74`, `src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs:85-139`). A `DisplayCardsAsync` prefix records when the native summary display actually starts; normal capture requires the outer transition to finish, every loaded card to be face-up, and the native skill scale sequence to settle (`src/BazaarPlusPlus/Patches/EndOfRun/EndOfRunSummaryRevealPatch.cs:8-18`, `src/BazaarPlusPlus/Game/Screenshots/EndOfRunCaptureReadinessDetector.cs:25-65`, `src/BazaarPlusPlus/Game/Screenshots/EndOfRunSummaryRevealDetector.cs:39-150`). Capture is automatic and does not advance the page; BPP chrome is suppressed while pixels are read, screenshot/metadata waits are bounded, and the gate fails open rather than trapping `Continue` (`src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs:181-239`, `src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs:241-426`).

Uploads are consolidated behind an `IUploadFeed` seam: `UploadPumpMount` mounts two `BackgroundUploadPump` components, one draining `BazaarDbSnapshotUploadFeed` (optional, keyed by the current profile account id) and one draining `RunBundleUploadFeed` (`src/BazaarPlusPlus/Game/Upload/UploadPumpMount.cs:22-29`), with `StartupUploadAttemptGate`/`StartupUploadAttemptRunner` handling the startup catch-up attempt (`src/BazaarPlusPlus/Game/Upload/StartupUploadAttemptGate.cs`).

Run and replay uploads use the mod API client and local storage state; PTR runs are excluded at the feed level (see §Game Build Channel). Ghost battle sync imports remote battle data into local history storage; server-side behavior is outside this repository and should not be treated as code-verified here.

## Tooltip Preview, Settings, And Hotkeys

Enchant preview visibility is a three-state setting controlled by `PreviewVisibilityMode`: `Off`, `AutoOnPedestalChoice`, and `Always`. `BppSettingsDockCatalog.NextPreviewVisibilityMode()` supplies its cycle order and status labels (`src/BazaarPlusPlus/Game/Settings/BppSettingsDockCatalog.cs`).

Settings-dock rows themselves are data: every cycling or boolean row is a `CyclingSettingsDockEntry<T>` (with a `Toggle` factory for the bool case) built by a per-feature static factory supplying order, key, value ladder, read/write delegates, highlight predicate, status text, and optional `nextOverride`/`onChanged` hooks (`src/BazaarPlusPlus/Game/Settings/CyclingSettingsDockEntry.cs`). Only the history-panel action button and the force-lockable end-of-run screenshot toggle remain hand-built definitions.

`TooltipPreviewModePolicy` owns preview priority. Upgrade hotkey wins first, then enchant hotkey, then enchant `Always`, then enchant `AutoOnPedestalChoice` only when the current choice pedestal is an enchant pedestal (`src/BazaarPlusPlus/Game/Tooltips/TooltipPreviewModePolicy.cs:30-43`). A `TooltipModifierRefreshController` mountable re-resolves open preview tooltips when the Ctrl/Shift hold state changes (`src/BazaarPlusPlus/Game/Tooltips/TooltipModifierRefreshController.cs:17`). The tooltip patch surface also covers encounter/event tooltips (gated by the `Game/EventPreview` settings toggle), quest reward previews, aggregate-item missing types, and hero level rewards (`src/BazaarPlusPlus/Patches/Tooltips/`), with mod-appended text rendered through the shared `BppTooltipSections` helper. Sections keep the cloned native donor typography and attach the game's own zh-CN fallback chain only when BPP-authored content contains CJK (`src/BazaarPlusPlus/Patches/Tooltips/BppTooltipSections.cs:12-15`, `src/BazaarPlusPlus/Patches/Tooltips/BppTooltipSections.cs:55-76`).

BPP hotkeys are user-rebindable and persisted in config per action. `BppHotkeyActionId` covers five actions: the two hold-preview hotkeys (Ctrl enchant / Shift upgrade defaults) plus toggles for CollectionPanel, LiveBuildPanel, and HistoryPanel (`src/BazaarPlusPlus/Game/Input/BppHotkeyActionId.cs`); rebinding rows are cloned from native settings rows (`src/BazaarPlusPlus/Game/Input/BppKeyBindRowController.cs`), and panel toggle presses are resolved per frame by the Overlay Panel Host. Binding-path normalization, ctrl/shift alias expansion, conflict detection, and the default/display data tables are the pure `HotkeyBindingPathCore` (`src/BazaarPlusPlus/Game/Input/HotkeyBindingPathCore.cs`, compile-linked into the zero-ManagedPath `tests/HotkeyBindingPath.Tests/`); `BppHotkeyService` remains the Unity/config facade. Binding paths in `BazaarPlusPlus.cfg` `[Hotkeys]` are untrusted input: junk normalizes to empty and falls back to the action default. The conflict check compares BPP actions only against other BPP actions, not native `Gameplay/*` bindings.

The settings dock registers all feature rows through `SettingsDockEntryRegistry` with order constants centralized in `BppSettingsDockOrder` (`src/BazaarPlusPlus/Game/Settings/BppSettingsDockOrder.cs`); the roster spans history, name override, legendary position, enchant preview, event and quest-reward previews, combat status bar, Chinese locale, supporter list, voice subtitles, end-of-run screenshot, and BazaarDB upload (`src/BazaarPlusPlus/BppComposition.cs:118-130`). There is no BPP font selector because all BPP-owned game UI follows the game's font assets.

## Localization And Fonts

`BazaarPlusPlus.Localization` is the localization resolution engine. The `L` facade is installed at plugin startup with language and locale-mode providers, then resolves `LocalizedTextSet` values against current language and Chinese locale mode (`src/BazaarPlusPlus.Localization/L.cs:11-30`, `src/BazaarPlusPlus/Plugin.cs:185-196`).

BPP does not embed, extract, select, or create an operating-system font. `NativeGameFonts` retains the game's `zh-CN` TMP addressable references for the plugin session. Native TMP labels retain their donor primary font and material while a cloned primary receives the corresponding game fallback chain; BPP-created uGUI and UI Toolkit surfaces use the packaged `UnityEngine.Font` from the game's dynamic sans asset (`src/BazaarPlusPlus/GameInterop/Fonts/NativeGameFonts.cs:20-24`, `src/BazaarPlusPlus/GameInterop/Fonts/NativeGameFonts.cs:56-96`, `src/BazaarPlusPlus/GameInterop/Fonts/NativeGameFonts.cs:96-148`).

UI Toolkit panels receive a panel-local `PanelTextSettings` instance with OS and emoji fallback disabled so global game text settings are not mutated (`src/BazaarPlusPlus/GameInterop/Fonts/NativeGameFonts.cs:178-223`, `src/BazaarPlusPlus/GameInterop/Fonts/NativeGameFonts.cs:225-242`). The adapter owns every loaded addressable handle and cloned binding until teardown, then restores donors and releases those resources (`src/BazaarPlusPlus/GameInterop/Fonts/NativeGameFonts.cs:264-299`). User-supplied supporter names are coverage-checked before display; unsupported text is rejected instead of causing tofu or silently selecting an external font (`src/BazaarPlusPlus/GameInterop/Fonts/NativeGameFonts.cs:150-176`).

## Voice Subtitles

`Game/VoiceSubtitles` renders in-game voice-over subtitles (the BazaarLine integration): `VoiceSubtitlesModule` plus a `GameInterop/VoiceSubtitles` bridge module observe game VO playback (`src/BazaarPlusPlus/BppComposition.cs:106-116`), and `VoiceLineDisplayDispatcher`/`VersionLabelScanner` mountables render cues (`src/BazaarPlusPlus/BppComposition.cs:178-180`). The line catalog is an embedded `voice-lines.json` (`src/BazaarPlusPlus/BazaarPlusPlus.csproj:25`) with a background remote refresh (`src/BazaarPlusPlus/Game/VoiceSubtitles/VoiceLinesRepository.cs:16-17`). The feature is gated by a master toggle (default OFF) in `BazaarPlusPlus.cfg` `[VoiceSubtitles]` (`src/BazaarPlusPlus/Game/VoiceSubtitles/VoiceSubtitlesGate.cs:8-11`, `src/BazaarPlusPlus/Core/Config/BppConfig.cs:98-103`), with a four-state Subtitle Mode dock row registered via `VoiceSubtitlesSettingsDockEntry.RegisterAll` (`src/BazaarPlusPlus/BppComposition.cs:118-121`). Subtitle labels preserve the version-label donor font/material when it already covers Chinese. If it does not, mounting waits until the game's Chinese font configuration exists, then the Chinese uGUI label uses the dynamic game font (`src/BazaarPlusPlus/Game/VoiceSubtitles/VersionLabelScanner.cs:43-69`, `src/BazaarPlusPlus/Game/VoiceSubtitles/FontDiagnostics.cs:18-28`).

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

The action set (`POST /v1/actions`, current schema `2.2.0`, `src/BazaarPlusPlus.BazaarAgent/Contract/BazaarAgentDecision.cs:11`) includes two Flow actions beyond the basics: `ReturnToMenu`, emitted only in `EndRunVictory`/`EndRunDefeat` while the scene loader is not transitioning, advances the end-of-run screen back to hero-select via `RunManager.LoadMainMenu()` — closing the unattended `run → next run` loop; and `Continue` (ADR-0008), emitted only when a replay sits at `finishedAwaitingContinue`, routed to the same `CombatReplayRuntime.TryContinueReplay` facade so the agent stays replay-agnostic. `isClientBusy` reflects the real client state (`AppState.IsWaitingForServerResponse || AppState.BlockInput`).

Those routes are dispatched in `BazaarAgentHttpServer.HandleContextAsync()` (`src/BazaarPlusPlus.BazaarAgent/Transport/BazaarAgentHttpServer.cs:132-160`). Replay record accepts a raw binary GhostBattlePayload msgpack+gzip body and optional battle id from header/query before queueing a start command; replay continue queues an explicit continue command (`src/BazaarPlusPlus.BazaarAgent/Transport/BazaarAgentHttpServer.cs:245-276`).

## Decision Records

This repository already uses `docs/adr/` for decision records. The documentation restructure keeps that convention instead of creating a parallel `docs/decisions/` tree.

Current decision records cover encounter status probes, the mountable registry, HistoryPanel preview overlay rendering, preview visibility mode, BazaarAgent isolation, BazaarAgent as a separate plugin, external replay video control, and replay continue as an agent action. See [adr/](adr/).
