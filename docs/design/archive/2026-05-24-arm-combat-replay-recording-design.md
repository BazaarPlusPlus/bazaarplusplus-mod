> **Status: ASPIRATIONAL - never implemented.** None of its named artifacts exist; the recorder still auto-records, which this doc aimed to replace. CONTRADICTS the living [combat-replay.md](../../features/combat-replay.md). Path refs use the stale BazaarPlusPlus/ dir (now BazaarPlusPlusV4/).

# Arm-Then-Record Combat Replay (History Panel Trigger)

**Status:** Draft for implementation planning
**Date:** 2026-05-24
**Owner:** BazaarPlusPlus mod

## 1. Background

### 1.1 Today: auto-record everything

When `CombatReplayVideo.Enabled = true`, [CombatReplayVideoRecorder](../../../Game/CombatReplay/Video/CombatReplayVideoRecorder.cs) subscribes to `CombatReplayPlaybackStarting` and **starts recording every replay the player opens, with no further opt-in**. See [docs/combat-replay-video-recording.md](../../combat-replay-video-recording.md) for the full pipeline.

### 1.2 The problem

Disk waste. The player has to watch any replay multiple times to know which ones are worth keeping, but the mod has already burned MP4s for all of them by the time they decide. There is no UI to say "I want this one, not the rest."

The MP4 directory (`<GameRoot>/BazaarPlusPlus/CombatReplayVideos/<yyyy-MM-dd>/`) accumulates files indiscriminately and players currently have to manually `rm` the ones they don't want.

### 1.3 Existing infrastructure we can reuse

- **History Panel** ([Game/HistoryPanel/HistoryPanel.cs](../../../Game/HistoryPanel/HistoryPanel.cs)) is a mod-added F8 panel that already lists runs and battles, and exposes a `Replay` button per-selection that triggers `runtime.ReplaySaved(battleId)` via [HistoryPanelReplayService.cs:76](../../../Game/HistoryPanel/HistoryPanelReplayService.cs:76). This is the existing playback entry point.
- **BppHotkeyService** ([Game/Input/BppHotkeyService.cs](../../../Game/Input/BppHotkeyService.cs)) handles configurable hotkeys with an in-game rebind UI ([BppKeyBindRowController.cs](../../../Game/Input/BppKeyBindRowController.cs)).
- The Replay button is rendered in [HistoryPanelUiToolkitView.Tree.cs:308](../../../Game/HistoryPanel/HistoryPanelUiToolkitView.Tree.cs:308) via `CreateButton(HistoryPanelText.Replay(), _replay, 140f, 36f)`.

## 2. Goals

1. Recording an MP4 is a **per-replay explicit choice**, not a global mode.
2. The choice happens at the point of action: the same UI surface the player uses to start the replay.
3. Default behavior produces **zero MP4 files** unless the player explicitly opts in.
4. No new top-level UI surface — extend what's already there.
5. Downstream pipeline (frame capture, ffmpeg, file path, showcase sidecar from the [companion design doc](2026-05-24-combat-replay-skill-showcase-design.md)) is unchanged.

## 3. Non-goals

- Mid-replay "start recording from now" (would lose the start of the replay and complicate the ffmpeg pipeline).
- Retroactive rendering of past replays (would require headless playback; out of scope for v1, may be revisited as a separate spec — see the showcase doc's Battle History page concept).
- Bulk "record next N replays" mode (yagni; if a player wants to record several, they click the second button each time).
- Settings UI overhaul (the existing `CombatReplayVideo.*` BepInEx config keys stay as-is).
- A separate "Recording History" panel (the existing MP4 directory layout is the answer for now).
- Per-battle MP4 deletion UI (separate feature).

## 4. UX design

### 4.1 Two-button row in History Panel

The existing single Replay button becomes a row of two:

```
  [ Replay ]   [ 🎥 Replay & Record ]
```

- **Replay** (existing): playback only, no MP4.
- **Replay & Record** (new): playback + record an MP4 for this one playback. Auto-disarms after this playback ends.

Both buttons are gated by the same `CanReplayBattle(battle, out reason)` check ([HistoryPanelReplayService.cs:33](../../../Game/HistoryPanel/HistoryPanelReplayService.cs:33)). If the battle can't be replayed at all, both buttons are disabled with the existing reason text.

If `CombatReplayVideo.Enabled = false`, the **Replay & Record** button is hidden entirely (the feature is off). The plain **Replay** button still works.

### 4.2 In-replay feedback

While recording is active, a small `● REC` corner indicator overlays the Unity Game View (bottom-right by default). This makes it unambiguous that recording is happening — the existing pipeline gave zero visual feedback.

Toggleable via a new config key `CombatReplayVideo.ShowRecIndicator` (default `true`).

When recording finishes (success), the recorder logs the MP4 path to the BepInEx console — verify the existing recorder already does this and, if not, add a single `BppLog.Info` line. No new toast / popup in v1 (console messages are observable enough for players debugging missing recordings; non-debugging players will just find the file in the directory they already know).

### 4.3 Hotkey (optional, included in v1)

Register `BppHotkeyActionId.ReplayAndRecordSelectedBattle` with default binding `<Keyboard>/f9` (single-key, no modifier, symmetric to the existing F8 = History Panel hotkey). When pressed while History Panel is visible and a battle is selected, behaves identically to clicking the **Replay & Record** button. Appears in the existing keybind UI so players can rebind it.

The hotkey is no-op when any of the following are true: History Panel is not visible; no battle is selected; `CombatReplayVideo.Enabled = false`; the selected battle fails `CanReplayBattle`. No silent surprise behavior.

If implementation pressure is high, this hotkey can ship in v1.1 — the spec marks it as P1, not P0.

## 5. Architecture

### 5.1 New component: recording intent gate

```
┌──────────────────────────────────────────────────────────┐
│ HistoryPanel: user clicks "Replay & Record" on battle X  │
└──────────────────────────────────────────────────────────┘
                              │
                              ▼
┌──────────────────────────────────────────────────────────┐
│ HistoryPanelReplayService.ReplayAndRecordBattleAsync(X)  │
│   1. validate CanReplayAndRecord(X)                      │
│   2. token = CombatReplayRecordingIntent.ArmFor(X)       │
│   3. await StartReplayInternalAsync(X, token)            │
│      → runtime.ReplaySaved(X, token)                     │
└──────────────────────────────────────────────────────────┘
                              │
                              ▼
              runtime.StartReplayAsync (fire-and-forget)
              eventually publishes
              CombatReplayPlaybackStarting{BattleId=X, IntentToken=token}
                              │
                              ▼
┌──────────────────────────────────────────────────────────┐
│ CombatReplayVideoRecorder.OnPlaybackStarting(evt)        │
│   if (Enabled                                            │
│       && evt.IntentToken is { } t                        │
│       && intent.ConsumeIfArmedFor(evt.BattleId, t))      │
│       StartRecording(evt)                                │
│   else                                                   │
│       skip   ← also the case for any non-R&R replay      │
│              (no token → never matches)                  │
└──────────────────────────────────────────────────────────┘
```

The single behavioral change in the recorder: it now requires `Enabled = true`, a non-null `IntentToken` on the event, AND a matching arm-intent (battleId + token) to start. Today's auto-everything behavior is removed; any playback initiated without a token (legacy `ReplayLatest`, plain Replay button, future code paths) gets no MP4 by construction.

### 5.2 New files

| File | Responsibility |
|---|---|
| `Game/CombatReplay/Video/CombatReplayRecordingIntent.cs` | Holds a single nullable `(battleId, token: Guid)` arm slot. Constructed with a `Func<bool> enabledAccessor` (reads `CombatReplayVideoEnabled` from config). `ArmFor(string battleId)` returns `Guid?`: under the single `lock`, first checks `enabledAccessor()`; if false, returns `null` (no slot mutation). Otherwise generates a fresh `Guid`, sets the slot (replacing any prior value), and returns the token. **The `Enabled`-check + slot-mutation is atomic** — this closes the narrow window where `Enabled` could flip between the service's pre-arm `CanReplayAndRecord` call and `ArmFor`. `ConsumeIfArmedFor(string battleId, Guid token)` returns true and clears iff both fields match — no time check, no TTL: correctness is the token, the slot is one field replaced by the next `ArmFor` or wiped by an explicit clear. `ClearIfArmedFor(string battleId)` clears iff battleId matches — used by the plain Replay path. **`ClearIfArmedFor(string battleId, Guid token)`** clears iff both match — used by R&R failure / catch paths so an older attempt's cleanup cannot wipe a newer attempt's arm. `ClearAll()` clears unconditionally — used by the `Enabled = false` path. Thread-safe via the single `lock`. **No lease / IDisposable** — the runtime's `ReplaySaved` is fire-and-forget (publishes `PlaybackStarting` asynchronously from inside `StartReplayAsync`, see [CombatReplayRuntime.cs:319](../../../Game/CombatReplay/CombatReplayRuntime.cs:319)), so any RAII pattern scoped to `ReplayAndRecordBattleAsync` would clear the arm before the recorder sees the event. Clearing is event-driven, not call-scoped. **No TTL** — earlier drafts had one as a "stale arm GC" backstop; with token correctness the slot can only be consumed by the originating R&R click, an unconsumed arm is at most ~50 bytes of memory replaced by the next `ArmFor`, and a TTL on the consume path re-introduces the deadline-as-correctness bug (Codex rounds 3 & 4). |
| `Game/CombatReplay/Video/RecordingIndicatorOverlay.cs` | MonoBehaviour that subscribes to a recorder-started / recorder-stopped event pair (added below) and draws a small `● REC` chip in the bottom-right of the Game View while active. Honors `CombatReplayVideo.ShowRecIndicator`. |

### 5.3 Modified files

| File | Change |
|---|---|
| `Game/CombatReplay/Video/CombatReplayVideoRecorder.cs` | `OnPlaybackStarting`: replace the existing recording-eligibility logic with `Enabled && evt.IntentToken is { } token && intent.ConsumeIfArmedFor(evt.BattleId, token)`. Publish two **new** events `CombatReplayRecordingStarted` / `CombatReplayRecordingStopped` on the existing event bus (for the indicator overlay to subscribe to). |
| `Game/CombatReplay/CombatReplayPlaybackStarting.cs` | **New nullable field** `Guid? IntentToken { get; init; }`. Defaults to `null` for all existing publish sites; only the R&R path threads a non-null value through. Adding the field is backward-compatible — no current subscriber other than the recorder reads it. |
| `Game/CombatReplay/CombatReplayRuntime.cs` | `ReplaySaved(string battleId)` gets a sibling overload `ReplaySaved(string battleId, Guid? intentToken)` (or the existing signature gains an optional parameter — either works, optional parameter preferred to avoid duplication). The token is captured into `StartReplayAsync` and passed to `PublishReplayPlaybackStarting`, which stamps it onto the `CombatReplayPlaybackStarting` event. All existing internal call sites (e.g., [CombatReplayRuntime.cs:293](../../../Game/CombatReplay/CombatReplayRuntime.cs:293) `ReplaySaved(latest.BattleId)`) pass `null`, preserving today's "no recording for these paths" behavior. Same change applies to `ReplayImportedBattle` for Ghost battles. |
| `Game/HistoryPanel/HistoryPanelReplayService.cs` | Refactor + add. (1) Extract the runtime-invocation part of `ReplayBattleAsync` into a **private** `InvokeRuntimeAsync(battle, intentToken)` that does only the `runtime.ReplaySaved(battleId, intentToken)` / `runtime.ReplayImportedBattle(manifest, payload, intentToken)` call. The Ghost-download step stays at the caller. **This private helper does not touch the intent slot.** (2) Modify the public `ReplayBattleAsync(battle, ct)` to call `_intent.ClearIfArmedFor(battle.BattleId)` first (battleId-only clear — "user changed their mind for this battle"), then do the Ghost download if needed, then `InvokeRuntimeAsync(battle, intentToken: null)`. (3) Add public `ReplayAndRecordBattleAsync(battle, ct)`: validate via `CanReplayAndRecord`; await Ghost download (failure → return Failure, no arm was set); **then `ct.ThrowIfCancellationRequested()` and re-run `CanReplayAndRecord` immediately before `ArmFor`** so that config flips (`Enabled → false`) or user cancellation during the download cannot result in an arm being set against current intent; only after that re-validation, `var token = _intent.ArmFor(battle.BattleId);` then `InvokeRuntimeAsync(battle, intentToken: token)`. If invocation returns Failure, call **`_intent.ClearIfArmedFor(battle.BattleId, token)`** (token-scoped — must not clobber a newer concurrent R&R attempt on the same battle). Wrap the post-arm section in `try { ... } catch { _intent.ClearIfArmedFor(battle.BattleId, token); throw; }`. **R&R must never delegate to the public `ReplayBattleAsync`** — otherwise the plain-Replay-clears guard would immediately undo the arm. Add `CanReplayAndRecord(battle, out reason)` returning `CanReplayBattle(battle, out reason) && config.CombatReplayVideoEnabled`. |
| `Game/HistoryPanel/HistoryPanelUiToolkitView.cs` | Accept a new `Action _replayAndRecord` delegate in the constructor. Cache a `_replayAndRecordButton` field. Bind `ReplayAndRecordButtonText` / `ReplayAndRecordButtonEnabled` / `ReplayAndRecordButtonVisible` from the model. |
| `Game/HistoryPanel/HistoryPanelUiToolkitView.Tree.cs` | After line 308 (`_replayButton = CreateButton(...)`), add a sibling `_replayAndRecordButton = CreateButton(HistoryPanelText.ReplayAndRecord(), _replayAndRecord, 200f, 36f)` in the same row. Width `200f` accommodates the longer label. |
| `Game/HistoryPanel/HistoryPanel.UiToolkit.cs` | Populate the new model fields by calling the new service methods. |
| `Game/HistoryPanel/HistoryPanelCoordinator.cs` | Add `OnReplayAndRecordButtonClicked` analogous to existing `OnReplayButtonClicked` (around [HistoryPanelCoordinator.cs:322](../../../Game/HistoryPanel/HistoryPanelCoordinator.cs:322)), calling `_replayService.ReplayAndRecordBattleAsync`. |
| `Game/HistoryPanel/HistoryPanelText.cs` | Add `ReplayAndRecord()` localized string for each supported language. |
| `Core/Config/IBppConfig.cs` + `Core/Config/BppConfig.cs` | Add `CombatReplayVideoShowRecIndicatorConfig` (bool, default `true`). Update the `CombatReplayVideo.Enabled` BepInEx description text to reflect the new "feature on, requires explicit click per battle" semantics. |
| `Game/Input/BppHotkeyActionId.cs` | Add `ReplayAndRecordSelectedBattle` enum value. |
| `Game/Input/BppHotkeyService.cs` | Register `ReplayAndRecordSelectedBattle` with default binding `<Keyboard>/f9`. |
| `Game/HistoryPanel/HistoryPanel.cs` | In Update, when History Panel is visible and a battle is selected, check `BppHotkeyService.WasPressedThisFrame(...)` for the new action; on press, route to the same coordinator handler as the button click. |
| `Plugin.cs` | Instantiate `CombatReplayRecordingIntent` once and pass to both the recorder and the replay service via DI ([BppComposition.cs](../../../BppComposition.cs)). Wire BepInEx `CombatReplayVideoEnabledConfig.SettingChanged` so that any transition to `false` calls `intent.ClearAll()` — see §6.1. |

### 5.4 Migration impact

**Players upgrading from a version with `CombatReplayVideo.Enabled = true`** will see their MP4 directory stop growing automatically. They must now click **Replay & Record** to produce an MP4 for any given replay.

This is intentional and aligned with the goal, but worth a release-notes line:

> Combat replay recording is now triggered per replay. Open the History Panel (F8), select a battle, and click **Replay & Record** to record that one playback. The previous "record everything when enabled" behavior is gone.

No config migration required; the meaning of `Enabled` changes from "auto-record everything" to "recording feature available."

## 6. Failure semantics

The arm slot is keyed by `(battleId, token: Guid)`. Correctness comes from **token match**: the recorder only consumes the arm when the published `PlaybackStarting` event carries the *exact* token that armed it. There is **no TTL** on the consume path — arbitrarily slow cold starts still produce a recording, because the originating token can only match the originating playback. Failure-path cleanup is **token-scoped** so an older attempt's catch handler cannot accidentally wipe a newer same-battle attempt's arm.

| Failure | Clearing mechanism | Behavior |
|---|---|---|
| `runtime.ReplaySaved(battleId, token)` returns false (battle not replayable) | **Token-scoped clear** in failure branch: `_intent.ClearIfArmedFor(battle.BattleId, token)` | Existing failure path surfaces the error. Only this attempt's arm is cleared; a concurrent newer R&R for the same battle (token₂) is untouched. |
| Ghost download throws / is cancelled / Task fails before runtime call | **No arm yet** — the arm is set after the await, only just before `ReplaySaved` | Existing failure path surfaces the error. No stale arm. |
| `Enabled` flips to `false` *during* the Ghost download await | **Post-download re-validation** — R&R re-runs `CanReplayAndRecord` after the await and before `ArmFor`; the re-check fails and the call returns Failure without ever setting an arm | No arm is created. Recorder is not invoked. `ClearAll` already ran (slot was empty so it was a no-op) and the post-await re-validation closes the window where an arm could be set against current intent. |
| `Enabled` flips to `false` *between* the post-download re-validation and `ArmFor` (narrow TOCTOU window — would require an unusual code-path that yielded between the two, since they're synchronous on the same thread) | **Atomic check in `ArmFor`** — the intent's `ArmFor` re-checks `enabledAccessor()` inside its lock and returns `null` if disabled; the service treats `null` as Failure | No arm is created. Defense in depth: even if the service's pre-arm re-validation were skipped (or a future refactor introduces an await), the atomic check inside the intent prevents arming-after-disable. |
| Player cancels the R&R operation (cancellation token) during the Ghost download | **`ct.ThrowIfCancellationRequested()` after the await** — raises `OperationCanceledException` before `ArmFor` | No arm is created. Standard async cancellation semantics. |
| Exception thrown between `ArmFor` and the end of `ReplayAndRecordBattleAsync` | **Token-scoped clear** in `try/catch`: `_intent.ClearIfArmedFor(battle.BattleId, token)` | Same property — only the failing attempt's own token is cleared. |
| Player clicks R&R then closes panel / switches scenes / walks away; `PlaybackStarting` never fires (or fires hours later) | None — the slot holds (A, token₁) until next `ArmFor`, `ClearIfArmedFor`, `ClearAll`, or process exit. Holding ~50 bytes is fine. | Inert without a matching token. Nothing other than that exact attempt's late `PlaybackStarting` can consume it; if that event eventually arrives, the recording is produced as originally requested. |
| Cold start: legitimate R&R, `PlaybackStarting` takes 45 s (or 5 min, or longer) | None needed — token match is time-independent | Records as expected. This is the bug TTL-as-correctness had in earlier drafts. |
| Player clicks R&R on A, then before A starts, clicks plain Replay on A | **`ClearIfArmedFor(A)`** (battleId-only) in modified `ReplayBattleAsync` | Plain Replay produces no MP4. A's eventual `PlaybackStarting(A, token₁)` finds no slot → no MP4 even if the event arrives after the plain replay. |
| Player clicks R&R on A, then plain Replay on B | Plain Replay calls `ClearIfArmedFor(B)` → no match, slot untouched. Recorder's `OnPlaybackStarting(B, null)` → no token → skip. | B does not record. A still armed; A records when A actually plays. |
| Player clicks R&R twice on same A within seconds | First: `ArmFor(A) → token₁`. Second: `ArmFor(A) → token₂` (replaces slot). Recorder's `OnPlaybackStarting(A, token₁)` from the first attempt: slot has token₂ → no match → no record. `OnPlaybackStarting(A, token₂)` from the second attempt: match → record. | Exactly one MP4, the **second** attempt's. The first attempt's late event is harmless. |
| Old R&R attempt on A fails (token₁) AFTER a new R&R on A succeeded (token₂ in slot) — the old attempt's catch handler runs | `ClearIfArmedFor(A, token₁)` → token mismatch → slot untouched | token₂ stays armed. The new attempt is not victimized by the old attempt's cleanup. This is the Codex round-4 fix. |
| Player clicks R&R on A; runtime's `_replayPlaybackStartingPublished` guard prevents a second publish ([CombatReplayRuntime.cs:412](../../../Game/CombatReplay/CombatReplayRuntime.cs:412)) | Only one consume happens. | Exactly one MP4. |
| Recorder fails to start ffmpeg | Token already consumed. Recorder logs error; playback continues without MP4. Retry requires another R&R click. | Correct — failures do not silently re-arm. |
| `CombatReplayVideo.Enabled` toggled to false while a slot is armed | **`ClearAll()`** via `SettingChanged` subscription | Old token is unmatchable on next event. Re-enabling and clicking R&R generates a fresh token. |
| `CombatReplayVideo.Enabled = false` and player triggers arm via stale UI state / hotkey | `ReplayAndRecordBattleAsync` short-circuits at `CanReplayAndRecord` | No arm is created. |
| Legacy code path (e.g., internal `ReplayLatest`) invokes runtime without a token | Recorder sees `evt.IntentToken == null` → skip without touching slot | No MP4, slot preserved for the actual R&R that's still pending (if any). |

### 6.1 Guards

With token match providing correctness, only three layers remain:

1. **Token match** (correctness, primary) — `ConsumeIfArmedFor(battleId, token)` requires both to match. Time-independent. The only path to a recording is: a specific R&R click generated this exact token, the runtime carried it through to `PlaybackStarting`, and the slot still holds it. No "stale arm gets silently consumed" by construction.
2. **`ClearIfArmedFor(battleId)` on plain Replay** (UX clarity + correctness on disable) — clearing the arm when the player explicitly chose plain Replay reflects intent ("I changed my mind"). Without it, A's late `PlaybackStarting(A, token₁)` could still record despite the user clicking plain Replay — token match alone wouldn't catch that.
3. **`ClearAll()` on toggle off** (correctness on disable) — wipes any pending arm when the feature is disabled, so a re-enable cannot resurface an old token match.

There is no TTL. There is no lease. The earlier drafts of this spec had both, and Codex caught the resulting bugs in rounds 3 and 4 (lease disposed before async event publish; TTL on consume rejecting legitimate slow starts). The final design has neither, and is simpler for it.

**Why not RAII / lease?** A `using var lease = ArmFor(...)` pattern would scope clearing to the synchronous lifetime of `ReplayAndRecordBattleAsync`, which **returns before `PlaybackStarting` fires** ([CombatReplayRuntime.cs:319](../../../Game/CombatReplay/CombatReplayRuntime.cs:319) is `_ = StartReplayAsync(...)`). The lease would clear the arm before the recorder observes the event, silently producing no MP4 on the normal path.

**Why not TTL on consume?** A TTL on `ConsumeIfArmedFor` re-introduces "deadline as correctness": a legitimate but slow cold start (per Codex's measurement, the runtime can wait ~45 s through bootstrap + injection + presentation-readiness) becomes a silent no-MP4 if the deadline misses. Token match is already exclusive; an unconsumed arm cannot harm anyone else, so there is nothing to GC against.

## 7. Testing

Per the project's test rules, only meaningful seams get tests. Two are in scope:

| Test | Justification |
|---|---|
| `CombatReplayRecordingIntentTests` — (a) `ArmFor` returns a distinct Guid each call when enabled; (b) **`ArmFor` returns `null` and does not mutate slot when enabled accessor returns false** (round-6 atomic-check); (c) consume with matching (battleId, token) succeeds and clears slot; (d) consume with matching battleId but wrong token returns false and leaves slot (token-match correctness); (e) `ClearIfArmedFor(matchingId)` (battleId-only) clears the slot; (f) `ClearIfArmedFor(nonMatchingId)` leaves the slot; (g) `ClearIfArmedFor(matchingId, matchingToken)` clears the slot; (h) `ClearIfArmedFor(matchingId, wrongToken)` leaves the slot (round-4 fix); (i) `ClearAll()` clears unconditionally; (j) re-arm replaces slot and returns a fresh token; (k) no TTL — consume succeeds regardless of elapsed time; (l) thread-safety smoke under concurrent arm + consume | Tests (b), (d), (h), (k) each pin a specific Codex-found regression class. Removing any of them would re-open a previously caught bug. |
| `CombatReplayVideoRecorderTests` — `OnPlaybackStarting` skips when `IntentToken == null` (legacy path); skips when token doesn't match slot; records when (battleId, token) match and `Enabled = true`; skips when `Enabled = false` regardless of token match | Smallest covering test for the gating change. Existing recorder is hard to unit-test end-to-end (it spawns ffmpeg); we test only the gating branch by stubbing the underlying capture session start. |
| `HistoryPanelReplayServiceTests` — (a) `ReplayBattleAsync(A)` calls `intent.ClearIfArmedFor(A)` (battleId-only) exactly once and invokes the stubbed runtime with `intentToken: null`; (b) `ReplayAndRecordBattleAsync(A)` arms once, captures the returned token, and invokes the stubbed runtime with that exact token; (c) `ReplayAndRecordBattleAsync(A)` with stubbed-runtime-returns-false calls `ClearIfArmedFor(A, token)` (the **token-scoped** overload, not battleId-only) before returning Failure; (d) thrown exception in stubbed runtime triggers the `catch` clearer with the token-scoped overload; (e) **concurrent same-battle test**: simulate R&R(A)→token₁ failing AFTER R&R(A)→token₂ replaces the slot; assert token₂ survives token₁'s cleanup; (f) **Enabled-flips-during-download test**: stub a Ghost download that completes only after `CombatReplayVideoEnabled` flips to false; assert no arm is created and the call returns Failure; (g) **cancellation-during-download test**: cancel the CT mid-download; assert `OperationCanceledException` propagates and no arm is created | These tests pin the wiring between service, intent, and runtime. Test (e) regression-guards Codex round-4 finding 1; test (f) regression-guards Codex round-5 finding 1; without them, future refactors could silently re-introduce those bugs. |

Out of scope (per project rules):
- UI button click → service call wiring (thin glue, not meaningful).
- Hotkey press → coordinator handler call (thin glue).
- ffmpeg integration (existing pipeline, unchanged).

### 7.1 Manual checks

| Check | Method | Pass |
|---|---|---|
| Default state produces no MP4 | Play a replay via plain Replay button on a fresh install | No file in `CombatReplayVideos/<date>/` |
| Replay & Record produces exactly one MP4 | Click Replay & Record on battle X | Exactly one `X.<ts>.mp4` appears |
| Replay & Record auto-disarms | Click Replay & Record on X, watch it finish, then click plain Replay on Y | Only X has an MP4; Y does not |
| Indicator visible while recording | Click Replay & Record, observe Game View during playback | `● REC` chip visible bottom-right; disappears when playback ends |
| Indicator off when disabled | Set `CombatReplayVideo.ShowRecIndicator = false`, repeat | No chip, MP4 still recorded |
| Feature off hides the button | Set `CombatReplayVideo.Enabled = false`, open History Panel | Only the plain Replay button is visible |
| Hotkey works | Press F9 with History Panel visible and battle selected | Same behavior as clicking the button |
| Hotkey no-op when panel closed | Press F9 with History Panel hidden | No action, no log noise |

## 8. Risks and rollback

| Risk | Mitigation |
|---|---|
| Players with `Enabled = true` upgrade and feel that "recording is broken" because MP4s stopped appearing automatically | Release notes call out the new model. The button label includes "Record" so the in-product affordance is discoverable. |
| The History Panel row gets visually crowded with two buttons | Single-line layout, both buttons live in the same action row. If it overflows on narrow viewports, the layout already wraps; defer responsive tuning unless reports come in. |
| F9 conflicts with a game default hotkey | The existing `BppHotkeyService.TryGetConflictingAction` only checks against other BPP actions, not game defaults. Manual smoke: confirm F9 is unbound in vanilla. If conflict, swap default to F10. |
| Recording indicator interferes with the player's view of combat replay UI | Indicator is small (~20px chip in a corner), opt-out via config. Can be repositioned in a follow-up if anyone complains. |
| `CombatReplayRecordingIntent` lock contention | The slot is touched ~once per replay click and once per playback start. No realistic contention. |

**Rollback path**: revert the recorder gate change (the single `intent.ConsumeIfArmedFor` line). Auto-record-everything resumes for anyone with `Enabled = true`. No data loss, no config migration, no orphaned files. The new button stays visible but every click also "auto" records as it would have anyway.

## 9. Out of scope, deferred to follow-up specs

- **Retroactive rendering from Battle History** (the showcase doc's eventual end state): user selects a past replay and renders an MP4 on demand without watching it. Requires headless playback or rendering from the persisted `.payload.mpack.gz`. Material engineering effort; deserves its own spec.
- **Bulk record-next-N or a sticky armed mode**: explicitly rejected for v1 (yagni).
- **In-game MP4 manager UI** (delete, rename, copy path to clipboard): separate spec. The current "open the folder" workflow is fine for the kind of player who records intentionally.
- **Recording quality presets** beyond the existing `Crf` / `Preset` config: leave as-is.

## 10. Open questions

None blocking implementation. The migration release-notes wording can be finalized at PR time.
