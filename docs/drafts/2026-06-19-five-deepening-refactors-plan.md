# Master Plan — Five Deepening Refactors (confirmation-ready)

Status: **CONFIRMED by user 2026-06-19 — ready to implement.** Decisions locked (see §6). Implementation is delegated to an executor agent; see the companion handoff prompt `2026-06-19-five-deepening-refactors-EXECUTOR-PROMPT.md`.
Produced 2026-06-19 by a design + adversarial red-team workflow (5 designs, 5 red-teams, 1 synthesis). All load-bearing red-team claims re-verified against HEAD (`master`, `BppVersion 4.2.1`).

Source of the candidates: architecture review HTML report (5 deepening candidates C1–C5).

---

## 1. Executive summary

| # | Candidate | Decision | One-line reason |
|---|---|---|---|
| **C1** | `BackgroundUploadPump` (collapse two upload controllers) | **DO** | Deletes ~345 lines of byte-identical MonoBehaviour shell; genuine deepening. |
| **C2** | `CollectionQuery` (lift filter pipeline out of 1040-line panel) | **DO** | Consolidates normalize+resolve+policy into one pure module. |
| **C3** | HistoryPanel testable core (trimmed) | **DO, TRIMMED** | Keep `DeleteConfirmation` + chip de-dup + `HistoryPanelButtonModel` + `PreviewSource` inline. **Drop** ghost-filter "extraction" (no-op) and DataService inlining (reflection-pinned). |
| **C4** | Move replay-video output decision into the muxer | **DO, REVISED** | Muxer owns the 4-way decision via `Resolve`; **muxer RECEIVES a resolved ffmpeg path** (kills static-cache poisoning / PATH non-determinism). |
| **C5** | Retire shallow Core seams | **DROP** | Red-team verified `TestGameStateProbe` is **actively exercised** (`CombatStatusBarStateTests.cs:208-217` via `CombatStatusBar.State.cs:129`). The "free deletion / no test fake" premise is **false**; per C5's own decision rule → **do nothing, keep all three interfaces.** |

**Net: do C1, C2, C3 (trimmed), C4 (revised). Drop C5.**

---

## 2. Recommended sequence

**Dependency reasoning**
- **C5 dropped** removes the only `services.RunContext`/`services.Config` retype risk; with C5 gone, nothing collides with C1 (which reads both).
- **Only contended file is `BppComposition.cs`** — C1 edits the mountable-registration block (`:109-135`); C4's mount (`:114`) sits adjacent but untouched. C2/C3 don't touch composition.
- **C2, C3, C4 are mutually independent** (disjoint dirs: `CollectionPanel/`, `HistoryPanel/`, `CombatReplay/`).

**Order (one merge at a time, `master` stays green):** **C1 → C4 → C2 → C3.** Each candidate = a sequence of build+test-green commits. Every commit passes `./run.sh build` and `./run.sh test`.

---

## 3. Per-candidate revised design

### C1 — `BackgroundUploadPump`
**Target:** one `BackgroundUploadPump : MonoBehaviour` parameterized by `IUploadFeed` (in `Game/Upload/`). Lifecycle (Awake/Update/OnDestroy + `Time.unscaledTime`) in the pump; per-feature differences are data on the feed.

```csharp
internal interface IUploadFeed {
    UploadFeedDescriptor Descriptor { get; }
    UploadFeedActivation? Activate(IBppServices services); // null => disarm (blank paths / route fail)
}
internal readonly record struct UploadFeedDescriptor(
    string LogScope, string SkipLiveRunMessage, string StartMessage, string FailureMessage);
internal sealed class UploadFeedActivation {
    public required Func<CancellationToken, Task> UploadInBackgroundAsync { get; init; }
    public Func<bool> IsEnabled { get; init; } = static () => true;
    public IDisposable? Disposable { get; init; }
    public UploadArmHook? ExtraArmHook { get; init; }   // SINGLE optional hook, not a list
}
internal sealed record UploadArmHook(Func<IBppServices, Action, IDisposable> Subscribe);
```

**Red-team fixes folded in:** (1) settings-toggle path still emits the unconditional `BppLog.Info` whether or not a pump is registered (dock lambda logs, then null-safe `ArmImmediate`). (2) shared scope const `BazaarDbSnapshotScope` — no triplicated magic string. (3) explicit OnDestroy order: unsubscribe → `CTS.Cancel()` → `CTS.Dispose()` → `Disposable?.Dispose()` → drop activation; remove registry entry only under `ReferenceEquals`. (4) `ExtraArmHook` is one nullable hook (run-bundle's `CombatReplayPersistenceDrained`); `IsEnabled` is the snapshot-only difference. (5) only blank-path→null disarm + `IsEnabled` are headless-testable; rest is in-game. (6) optional do-not-restore `Fact` for the two deleted controllers.

**Files — Added:** `Game/Upload/IUploadFeed.cs`, `BackgroundUploadPump.cs`, `UploadPumpMount.cs` (`IBppMountable` that tracks both `AddComponent` instances + `DestroyImmediate`s each — avoids `ComponentMount`'s single-`GetComponent` hazard at `ComponentMount.cs:30`), `RunLogging/Upload/RunBundleUploadFeed.cs`, `Screenshots/Upload/BazaarDbSnapshotUploadFeed.cs`.
**Changed:** `BppComposition.cs` (two `ComponentMount` regs `:109-111`,`:133-135` → one `UploadPumpMount(PvpBattleCatalog)`); `BazaarDbSnapshotUploadSettingsDockEntry.cs:18`.
**Deleted:** `RunUploadController.cs`, `BazaarDbSnapshotUploadController.cs`.
**Pinned (unchanged):** `StartupUploadAttemptGate/Runner`, both services, both stores. No csproj edits.

**Migration:** (1) seam+pump+mount unregistered (build-only). (2) both feeds reproduce each `InitializeCore` verbatim, unregistered. (3) flip wiring + rewire dock entry + delete both controllers in one commit (no parallel paths).

### C2 — `CollectionQuery`
**Target:** `static CollectionQuery.Run(...)` in `Game/CollectionPanel/Data/CollectionQuery.cs` — pure: reads filter (never mutates), returns ordered cards + offer-match map + a `CollectionFilterNormalization` the panel adopts. Offer-pool cache injected as `ICollectionOfferPoolResolver` (panel keeps ownership for `InvalidateCatalog`). Source catalog behind `ICollectionSourceCatalog`.

**Red-team fixes:** (1) keep `PruneInvisibleSourceSelections` at `:527,:538,:623` — `Run` does NOT subsume Prune (different query: roster visibility vs `TryGetBySourceKey`); load path `:758` keeps no-Prune behaviour. (2) `ICollectionSourceCatalog` seam mandatory; exe-runner test compile-includes only the dictionary fake — **never** `CollectionSourceCatalog.cs`/`StaticCollectionSourceCatalog.cs` (pull `Newtonsoft.Json`+`Infrastructure`, absent from the test project). (3) exact compile-include additions to `CollectionFilterEngine.Tests.csproj`: `CollectionQuery.cs`, `CollectionSourceEntry.cs`, `CollectionSourceDtos.cs`, `CollectionSourceOfferRule.cs`, `CollectionSourceOfferResult.cs`, `CollectionSourceOfferPoolResolver.cs` — verify each is `Newtonsoft`/`Infrastructure`-free first. (4) null-vs-empty: `RetainedTags`/`RetainedKeywords` = `null` when the profile gate is off OR set empty (no mutation); non-null only when a trim ran, only then `AdoptNormalization` does `Clear()`+`UnionWith`. (5) policy cases (1–7) pending confirmation the harness can fabricate `CollectionCardVm` (game-coupled); headless-pure cases are normalization (8–12) + purity guard.

**Files — Added:** `Data/CollectionQuery.cs` (+`CollectionQueryResult`, `CollectionFilterNormalization`, `ICollectionOfferPoolResolver`, `ICollectionSourceCatalog`), `Sources/StaticCollectionSourceCatalog.cs`.
**Changed:** `CollectionSourceOfferPoolCache.cs` (`: ICollectionOfferPoolResolver`, zero body change); `CollectionPanel.cs` (`ApplyFilters` → `Run` + `AdoptNormalization`; delete `TrimUnavailableFacetSelections`/`TrimSet`/`ResolveSelectedSourceEntry`); `CollectionFilterEngine.Tests.csproj` (additions only). Keep `CollectionFilterContext`.

### C3 — HistoryPanel testable core (trimmed)
**Keep four pieces:**
- `DeleteConfirmation` struct (clock-injected) — **`RunId + ExpiresAt` ONLY** (drop `StatusActive`: it's an effect of `SetStatusMessage:695`, stays a `_state` field). `IsActiveFor(runId, now)`/`HasExpired(now)` take the clock as a param.
- `CanDeleteRun` + `ResolveDatabaseChip` de-dup — collapse the two-site database-chip drift (`GetDatabaseChipText:459-467` + `ResolveDatabaseChipSeverity:213-218`) into one `HistoryPanelDatabaseChip(Text, Severity)`.
- `HistoryPanelButtonModel` + builder — single button-state computation; pass `replayActionLabel` unconditionally; cover all four arms of `GetReplayButtonLabel:200-208` + the `_replayActionInProgress` short-circuit (`:161`).
- `PreviewSource` inline (commit 1, safe).

**Dropped:** ghost-filter `ProjectFilteredGhostBattles` (no-op over already-pure `HistoryPanelGhostBattleFilter.Matches:16-25` — test `Matches` directly); DataService inlining (reflection-pinned by `GhostBattleSync.Tests:711-718`).
**Also:** delete dead 2-field `ClearDeleteRunConfirmation` at `HistoryPanelController.cs:97-101` (no caller); read clock at coordinator boundary (`TryDeleteSelectedRun:420`) + `BuildUiModel` refresh, NOT through the `HistoryPanelController.cs:103` forwarder; new `HistoryPanelDecisions.Tests.csproj` must `Compile Include ../Shared/LocalizationTestBootstrap.cs`.

**Files — Added:** `HistoryPanelDecisions.cs`, `DeleteConfirmation.cs`, `HistoryPanelButtonModel.cs`, `tests/HistoryPanelDecisions.Tests/`.
**Changed:** `HistoryPanelCoordinator.cs` (delegate; keep `ResolveGhostBattleOutcome` shim + nested enum — reflection-pinned), `HistoryPanel.UiToolkit.cs`, `HistoryPanel.cs` (inline preview), `HistoryPanelController.cs` (delete dead clear).
**Deleted:** `HistoryPanelPreviewSource.cs`. **`HistoryPanelDataService.cs` KEPT.**

### C4 — Muxer owns the output decision (revised)
**Target:** `internal ReplayVideoAudioMuxer.Resolve(...)` owning the 4-way "what is the final video file" decision: (a) not-Completed → delete/promote temp; (b) no usable audio → `PromoteSilentToFinal`; (c) no ffmpeg/no AAC → promote; (d) usable audio + ffmpeg → `DispatchAsync`.

**Critical revision:** muxer **receives a resolved `string? ffmpegExecutable`**; it does NOT call `FfmpegLocator.Resolve` internally (recorder resolves once at `Initialize`). "No ffmpeg" becomes a deterministic `null` the test controls — kills static-cache poisoning (`FfmpegLocator.cs:12-22,41`) and avoids real dispatch in unit tests.

```csharp
internal readonly struct MuxResolution {
    public bool Dispatched { get; init; }
    public Task? Task { get; init; }              // non-null iff Dispatched
    public MuxResult? Synchronous { get; init; }  // non-null iff !Dispatched
}
internal MuxResolution Resolve(
    ReplayVideoCaptureStatus status, string tempVideoPath, string finalPath,
    IReadOnlyList<string> usableWavPaths, string? ffmpegExecutable, Action<MuxResult> onResolved);
```

**Red-team fixes:** branch (a) still invokes `onResolved` (collapses four `TrySaveFinishMetadataFor` sites to one); `AbortActiveSession` (from `OnDisable:88`/`OnDestroy:93`, can fire before `Initialize`) **kept on the static `PromoteSilentToFinal`/delete path** — never dispatches, no null-deref; tap usable/delete rule extracted as a **pure static helper** `IsUsable(bool capturedAnySamples, string wavPath)` in `ReplayAudioTapStopper` (NOT an `IReplayAudioCaptureTap` mock seam).

**Files — Added:** `Audio/ReplayAudioCaptureResult.cs`, `Audio/ReplayAudioTapStopper.cs`.
**Changed:** `ReplayVideoAudioMuxer.cs` (add `MuxResolution`+`Resolve`; delete dead single-WAV `DispatchAsync`/`MuxOrPromote`/`Mux` overloads); `CombatReplayVideoRecorder.cs` (`StopAudioTaps`→stopper; success → `_muxer.Resolve(..., FfmpegLocator.Resolve(pluginsDir), ...)`; delete `DispatchMuxOrPromote`, `_muxTasks`; OnDestroy drain → `TryDrainPendingForShutdown`; abort stays static). **Recorder file must not move** (architecture pin `CoreLayeringTests.cs:806-822`).

---

## 4. Cross-cutting

**Version bump:** all four are internal-only (no wire/serialization/public-API/on-disk change). The "bump major for breaking change" rule targets public/wire surfaces — none qualify. **Recommend `BppVersion 4.2.1 → 4.3.0`** once at the end of the batch (one "Improved" internal-architecture release).

**Architecture tests:** no required changes — every refactor stays inside its `Game/` feature dir; no Core↔GameInterop crossing; C4 recorder stays put. C1 do-not-restore `Fact` is an optional ratchet (propose in PR, don't inline). C5 dropped → `IBppServices` allowlist (`CoreLayeringTests.cs:21-27`) untouched.

**Verification per commit:** `./run.sh build` then `./run.sh test` — **grep full output for `"Failed test projects:"`** (exe-runner exit code is insufficient; MEMORY `project_runsh_test_exit_code`). Finish with `./run.sh format` (csharpier); reformatting of files outside the change → separate scoped commit.

**USER must validate in-game** (build + reload; Steam App ID 1617400):
- C1: both `...armed.` startup lines; BazaarDB toggle before AND after pump mount; completed run + replay drain → run-bundle upload; no disposed-HttpClient exception on quit mid-upload.
- C2: Prune+Apply deselects now-invisible source chip; unknown/kind/hero-mismatch source deselects; badges render after locale change; no tab-switch flicker.
- C3: delete-confirm 5s arm→re-click→delete→auto-expire; button enable/label parity (run/ghost, in-run, replay-in-progress); chip color/text; preview against-me orientation after inline.
- C4: real recording with audio → muxed MP4; taps feed `CapturedAnySamples`; drain on quit-during-mux; abort leaves correct file and never background-dispatches.

---

## 5. Risk register (ranked, all mitigated)

1. **C4 ffmpeg static-cache / PATH non-determinism** — muxer receives resolved `ffmpegExecutable`; tests pass `null` for branch (c).
2. **C1 two MonoBehaviours on one GameObject** — dedicated `UploadPumpMount` tracks both instances; never reuse `ComponentMount<BackgroundUploadPump>` twice.
3. **C1 settings-toggle log/arm parity + teardown ordering** — log in dock lambda (pump-presence-independent); shared scope const; explicit Cancel→Dispose→Disposable.Dispose→drop order.
4. **C2 de-mutation ordering / null-vs-empty** — `AdoptNormalization` runs where old mutations were; null=no-trim contract; purity-guard test; preserve `PruneInvisibleSourceSelections`.
5. **C2 test won't compile if static catalog pulled in** — compile-include only the dictionary fake.
6. **C3 button-model parity (replay 4-arm)** — pass `replayActionLabel` unconditionally + cover all arms + short-circuit.
7. **C3 `StatusActive` mismodeling** — keep it a `_state` field; struct is `RunId + ExpiresAt` only.
8. **Async teardown disposed-resource races** — Cancel-before-dispose; C4 abort on synchronous static path.

---

## 6. Confirmed decisions (locked 2026-06-19)

1. **Version:** bump `BppVersion 4.2.1 → 4.3.0` **once at the end of the batch** (`Directory.Build.props`). Release Notes: one `Improved` bullet (internal architecture).
2. **C5: DROPPED.** Keep `IGameStateProbe` / `IRunContext` / `IBppConfig`. Reason (do not re-litigate): `TestGameStateProbe` is a live test fake exercised by `CombatStatusBarStateTests.cs:208-217` via `CombatStatusBar.State.cs:129` — the seam has a second adapter, so it is a real seam, not a hypothetical one. Removal would break a test for zero leverage gain.
3. **C3 scope: TRIMMED** to the four pieces in §3 (DeleteConfirmation, chip de-dup, button-model, PreviewSource inline). Ghost-filter extraction and DataService inlining are NOT done.
4. **C4 abort path:** keep abort on the static `PromoteSilentToFinal`/delete path (never dispatches).
5. **Cadence:** one branch per candidate, **C1 → C4 → C2 → C3**, each build+test-green before the next. A **review gate** precedes each merge to `master` (the user reviews; the executor does not self-merge unreviewed).
