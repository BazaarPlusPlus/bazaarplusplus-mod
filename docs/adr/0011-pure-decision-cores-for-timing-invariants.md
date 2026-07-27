# ADR-0011: Timing invariants live in pure decision cores, not MonoBehaviour glue

Status: Accepted, consolidated 2026-07-28 (architecture-deepening batch #161, PRs #170–#177)

## Context

The 2026-07 architecture review found one root cause repeated across features: the pure logic cores (filter engine, muxer, capture core…) were deep and well tested, but the MonoBehaviour orchestration glue that actually held the race-safety and timing invariants had no testable seam. The extreme symptom was "testing" runtime ownership by asserting source-text token order (`ReplayPlaybackRuntimeOwnershipTests`, deleted with this batch).

## Decision

Move the state algebra of orchestration glue into decision-output pure cores, following the `EndOfRunCaptureWorkflowCore` precedent: no Unity types, no held delegates, time passed in as a `float now` parameter, and methods that return decision values which the runtime — now a translation layer — executes. Cores landed with this batch:

- [`CollectionViewState`](../../src/BazaarPlusPlus/Game/CollectionPanel/CollectionViewState.cs) — all CollectionPanel filter/search/catalog state transitions (tests: `tests/CollectionViewState.Tests/`).
- [`CollectionCardFitMath`](../../src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionCardFitMath.cs) — grid cell fit math, split from the virtualizer, with [`NativeCardCellBoundsCache`](../../src/BazaarPlusPlus/Game/CollectionPanel/Grid/NativeCardCellBoundsCache.cs) eliminating per-frame remeasure on scroll (tests: `tests/CollectionGridLayout.Tests/`).
- [`SavedReplayLifecycle`](../../src/BazaarPlusPlus/Game/CombatReplay/SavedReplayLifecycle.cs) — saved-replay playback state algebra (tests: `tests/CombatReplayPlaybackLogging.Tests/SavedReplayLifecycleTests.cs`).
- [`IUploadFeedSession`](../../src/BazaarPlusPlus/Game/Upload/IUploadFeed.cs#L124) — upload feed activation behaviorized into a session object; arm requests travel as the [`UploadArmRequested`](../../src/BazaarPlusPlus/Game/Upload/UploadArmRequested.cs) bus event.

## Load-bearing contract points

Red-team verified during design (two rounds, 2 blockers / 6 majors folded in); do not simplify these away:

- `SavedReplayLifecycle` start flags commit in **three stages** (`OnStartBegun` → `OnBootstrapResolved` → `OnInjectionCommitted`, [`SavedReplayLifecycle.cs:53-76`](../../src/BazaarPlusPlus/Game/CombatReplay/SavedReplayLifecycle.cs#L53)); collapsing them into one call reintroduces the mid-start-exit race. Exit is a **two-phase decision**: `BeginReplayStateExit(now)` answers terminal ownership before the ended-publish; `OnReplayStateExited(now, publishSucceeded, …)` routes afterwards (latch reason, complete/menu-return/defer). The 15 s exit-suppression latch has a **single owner** covering the continue, bootstrapped-exit, and native-exit paths (`IsExitSuppressed`/`NoteProgrammaticExitLatched`, [`SavedReplayLifecycle.cs:243-250`](../../src/BazaarPlusPlus/Game/CombatReplay/SavedReplayLifecycle.cs#L243)). Pending menu return and start progress are **parallel states** — a new start may begin inside the pending window. ADR-0007 guardrails (including the absorbed ADR-0008) are unchanged: `TryContinueReplay` remains the only programmatic replay exit.
- `CollectionViewState` command methods return `null` for a **same-value no-op** — no debounce cancel, no render, no scroll reset. `AcceptCatalog` hero normalization must **not** write the hero preference (only the user's `ToggleHero` saves; otherwise a remembered hero absent from the current catalog silently overwrites the stored preference with Common). The grid port has a three-window contract: `Publish` returns `null` when the grid is unavailable (the reducer then skips normalization write-back and cache invalidation), and `Current` returns an explicit `CollectionGridProjection.Empty` before the first publish ([`ICollectionGridPort.cs:13-25`](../../src/BazaarPlusPlus/Game/CollectionPanel/ICollectionGridPort.cs#L13)). `ApplyOpenSelection` snapshots `currentRunDay` at panel open — do not "simplify" it into a live `IGameDataDayTierResolver` read inside `BuildModel`, or the Day badge renders a stale value during the cold-start multi-frame load window.
- `IUploadFeedSession` has a **two-point dispose contract** ([`BackgroundUploadPump.cs:82-104`](../../src/BazaarPlusPlus/Game/Upload/BackgroundUploadPump.cs#L82)): the pump's `OnDestroy` releases arm subscriptions first (unsubscribe → cancel), and `session.Dispose` — covering attempt resources only — runs only after the shutdown drain callback. One merged Dispose lets an in-flight `RunAttemptAsync` hit disposed resources or lets a half-torn-down pump receive callbacks.
- `HistoryPanelDependencies` keeps a single **guard-free** constructor ([`HistoryPanelDependencies.cs:11`](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelDependencies.cs#L11)): exe-runner tests construct it with positional nulls as pinned behavior anchors (ADR-0009); adding null guards breaks them at construction.

## Superseded surfaces

Deleted and absorbed with the batch — do not resurrect:

- `ReplayPlaybackStateExitCoordinator` and the source-text ownership tests (absorbed into `SavedReplayLifecycle` + `CombatReplayRuntime` private helpers).
- `UploadFeedActivation`, `UploadArmHook`, and the static `CurrentByFeed` registry (replaced by `IUploadFeedSession` + the `UploadArmRequested` bus event).
- `HistoryPanelRuntime` and the telescoping `HistoryPanelDependencies` constructors; `IHistoryPanelRuntime` narrowed to the read-only two-member [`IHistoryPanelRunState`](../../src/BazaarPlusPlus/Game/HistoryPanel/IHistoryPanelRunState.cs#L10).
- `EncounterOption`'s dead source-attribution members and the three same-shape preview result records, merged into the single `EventPreviewResult` ([`EncounterPreviewModule.cs:27`](../../src/BazaarPlusPlus/Game/EventPreview/EncounterPreviewModule.cs#L27)).
