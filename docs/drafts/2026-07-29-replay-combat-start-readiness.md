# Replay combat start readiness

## Background

Draft PR #132 records a managed/current replay by entering the native replay presentation, waiting for required runtime assets, and then starting the combat simulation. Earlier fixes prevented `Replay()` from running before the expected player/opponent card controllers were mounted and removed an encounter chest that could be mistaken for the opponent hero.

The remaining user-visible failure is different: the expected entities now exist, but combat actions can begin while the board is still black or while item cards are still appearing/flipping. This has reproduced after multiple readiness changes, so another fixed delay is not an acceptable fix.

## Current problem

Verified by user observation:

- starting a recording can enter combat before every item is fully visible;
- presentation and simulation overlap, so the recording begins with an incomplete board;
- the existing controller-existence gate is therefore necessary but not sufficient.

Verified against current code and `decompiled/`:

- `ReplayState.Replay()` triggers `Events.ReplayStarted` before `FlipBoard()`, so the existing
  `OnNativeReplayStarted` subscription starts video capture while the board transition is still
  beginning;
- after one fixed `StorageAnimationTime` delay, native replay updates combat attributes, calls
  `FlipSocketsAndCarpet()`, and immediately awaits `CombatSimHandler.Simulate()` without a rendered
  frame or per-card presentation-complete barrier;
- the normal live-combat handler waits for `CombatRevealCompleted`,
  `!BoardManager.IsUpdatingBoard`, and `CombatReadyToStart` before calling `Simulate()`, but the
  replay path calls `Simulate()` directly and bypasses those gates;
- replay cards are native `ItemController`s whose shared face-up idle state is
  `Card_Idle_Faceup_A`; `CardController.IsCardVisible`, `PositionedInSocket`, the animator's
  `FaceUp` parameter, transition state, and face-up idle state together expose the required
  aggregate readiness signal.

## Candidate approaches

### A. Gate the outer `Replay()` call on mounted and visually ready controllers

Wait for every expected player/opponent item controller to be active, front-facing, visible, and no longer transitioning before invoking the native replay action.

Risk: if the reveal animation only starts *after* `Replay()`, this gate can never observe completion and would deadlock.

### B. Gate the native presentation-to-simulation transition — selected

Allow `Replay()` to begin presentation, then prevent the transition to combat simulation until the native presentation completion signal is true for all expected cards.

Risk: patching the wrong continuation could duplicate or skip a native state transition. The exact method and ownership must be proven from decompiled source.

### C. Separate recording readiness from simulation readiness — selected as part of B

Let native presentation run normally, start video capture at the intended visual boundary, and start simulation only after presentation completes.

Risk: this is broader than the reported bug and may alter synchronization anchors. Use only if source evidence shows capture and simulation currently share one incorrectly coupled gate.

The implementation keeps `ReplayStarted` as the native lifecycle acknowledgement, but defers the
recording-start publication until the aggregate presentation gate resolves. It then captures one
complete ready-board render boundary before permitting the original `Simulate()` call. The
original method is invoked exactly once through a one-shot Harmony re-entry permit; all other
combat simulations retain their existing path.

### Rejected: add another fixed delay

A fixed number of milliseconds cannot cover machine/GPU variance and has already failed in practice. Any timeout may only be a fail-safe degradation path around a state-based primary gate.

## Verification method

1. Trace the current managed replay path through the outer readiness gate, native replay presentation, and the exact call that starts simulation.
2. Identify a native, observable presentation-complete condition from current code/decompiled source.
3. Add a temporary structured log on the main path only, recording:
   - expected and ready controller counts;
   - presentation-complete state;
   - recorder start;
   - simulation start;
   - timeout/degradation category, if any.
4. Add deterministic tests around the extracted readiness policy/state transition.
5. Build and replace the local DLL.
6. Record a fresh battle through the actual Record flow and verify:
   - every expected item is visible and front-facing before the first simulated combat action;
   - no black/incomplete-board frames occur at combat start;
   - combat/video synchronization anchors remain monotonic;
   - timeout degradation still exits safely if a native visual never reports completion.

## Completion criterion

The fix is complete only when the actual Record flow starts simulation after the native board reveal is complete on a fresh recording. Passing unit tests or merely delaying the call is insufficient.
