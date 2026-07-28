# Recorded replay starts combat before the board presentation settles

Date: 2026-07-28

## Symptom

After choosing **Record**, the replay begins while the board is still flipping/revealing.
The captured video therefore opens on a transient board and the first combat frames can
advance before the cards and carpet are visually settled.

This has been reported after multiple readiness changes, so the next change must target
the actual replay/simulation boundary instead of adding another pre-replay delay.

## Verified facts

### Current code only waits before `Replay()`

`ReplayBootstrap.InjectSavedReplayAsync` waits for active card controllers and several
negative board-busy flags before it removes the loading scene and calls
`ReplayState.Replay()`. The current-recording path similarly closes Recap and prepares
assets before invoking the native Replay action.

That does not cover the animation started *inside* `ReplayState.Replay()`:

1. `Events.ReplayStarted.Trigger()`
2. `TransitionIn.OnReplayPlaybackStarted()` (starts encounter-carpet presentation)
3. `BoardManager.FlipBoard()`
4. fixed `StorageAnimationTime` delay (the current game value is 0.25 s)
5. `BoardManager.FlipSocketsAndCarpet()`
6. `CombatSimHandler.Simulate(...)`

Source of truth:

- `decompiled/TheBazaarRuntime/TheBazaar/ReplayState.cs:246-271`
- `decompiled/TheBazaarRuntime/BoardManager.cs:245-250`
- `decompiled/TheBazaarRuntime/TheBazaar.SequenceFramework.VisualSequences/TransitionInComponent.cs:199-209`

The recorder subscribes to `ReplayStarted`, so it correctly captures the lead-in, but
the native fixed 250 ms delay—not the mod's pre-Replay readiness check—controls when the
combat simulation begins.

### New recording proves the boundary is early

Recording/report:

- report `75b5aab6ad424d0289042e4834d02d0a`
- video `7045a8f969a24fa1ba2f149e371ff93f.20260728-191329.75b5aab6ad424d0289042e4834d02d0a.mp4`

Read-only frame extraction at 0, 100, 200, 280, 350, 500, and 1,000 ms shows:

- 0–200 ms: storage board/card transition is in progress;
- 280 ms: card faces are still materializing/settling;
- 350–500 ms: the board is substantially settled;
- sync metadata advances from repeated combat frame 0 anchors to combat frame 1 at
  approximately 300 ms.

Therefore this is not only a Viewer seek bug: the simulation boundary itself occurs
while the presentation is still settling.

## Rejected repairs

1. **Wait longer before calling `Replay()`.** Rejected because `Replay()` starts a new
   flip/carpet transition; a pre-call wait cannot settle work that does not exist yet.
2. **Trim or remap only the first video frame.** Useful for Viewer initialization, but it
   cannot prevent combat events from advancing underneath an unfinished presentation.
3. **Increase `BoardManager.StorageAnimationTime`.** Rejected because it changes a native
   global timing value and also affects Replay's exit animation. It is a fixed guess rather
   than a readiness signal.
4. **Block the Unity main thread.** Rejected because the presentation itself needs render
   frames to advance.

## Revised implementation plan

Intercept only the recorded-replay call to `CombatSimHandler.Simulate` and return an
asynchronous wrapper Task:

1. Preserve the existing `CombatSimObserved` publication exactly once.
2. If no video recording is active, invoke the native method unchanged.
3. For an active recording, yield on Unity's main synchronization context until the
   post-flip presentation is ready:
   - ReplayState is still active and replaying;
   - BoardManager is not updating/moving/revealing/unrolling;
   - player/opponent skill presentation managers are idle;
   - expected active card controllers still exist;
   - relevant board/card transforms remain unchanged for a short stability window.
4. Apply a hard upper bound so a missing/animated decorative subtree degrades to the
   native replay instead of deadlocking it.
5. Invoke the original `Simulate` exactly once and return/await its original Task.

The sampler must exclude particle/VFX subtrees so ambient effects cannot keep the gate
closed. The gate applies only while the recorder has an active operation; normal Replay
behavior is unchanged.

Separately, the Viewer should map combat time zero to the last anchor in the initial
equal-combat-time run. That removes the captured pre-roll from Timeline-driven seeks but
does not replace the simulation gate above.

## Verification

Automated:

- pure gate-state tests: baseline, moving, stable, busy, timeout, Replay exit;
- patch source/architecture test: no blocking wait, original `Simulate` exactly once,
  non-recorded replay bypass;
- sync test: duplicate initial combat-zero anchors map time zero to the final initial
  anchor, while later exact frame anchors keep their first occurrence;
- existing recording, report, and full Viewer test suites.

Live:

1. build and replace the local DLL;
2. Record a current battle from Recap;
3. inspect 0/100/200/300/500/1,000 ms frames and sync anchors;
4. confirm the recording still contains the native flip lead-in;
5. confirm the first combat-frame advance occurs only after the cards/carpet have visibly
   settled;
6. confirm audio/video remain synchronized and report Timeline time zero seeks to the
   settled combat baseline.
