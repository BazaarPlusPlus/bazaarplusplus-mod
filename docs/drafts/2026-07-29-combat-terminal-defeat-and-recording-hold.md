# Combat terminal defeat marker and recording hold

## Background

Draft PR #132 reconstructs a defeat marker in the Viewer from
`combatant-died`, the previous Health metric, and ordered same-frame
`Health:*` settlements. For uniquely attributable direct damage, the defeat
marker replaces the original Timeline damage marker and keeps its source.

The recording path also intentionally suppresses the game's final-blow
slow-motion sequence while a recording lease is active. Earlier fixes aligned
the terminal combat timestamp with the first media sample of the repeated
terminal anchor plateau so Timeline hover does not seek into the post-combat
transition.

## Current problems

1. Newly recorded report
   `b505aa0e39054c17b49778490edc6382.html` does not visibly show a defeat
   marker.
2. Its recording ends abruptly. The desired result is a short, stable hold on
   the final board state after defeat, without restoring the native
   slow-motion/transition tail.

These are related terminal-boundary problems but must be diagnosed
independently:

- the report may lack the raw death/health evidence required by the current
  Viewer reconstruction, or the marker may be outside the rendered endpoint;
- the recorder may stop at the simulation terminal signal before a deliberate
  stable-frame hold is captured, even though playback mapping correctly avoids
  the later transition.

## Root cause

### Missing defeat marker

The report data is complete. Frame 245 contains:

- `effect-executed / PlayerDamage`, from Fang to the player, value `828`;
- `combatant-died`, targeting the player;
- `health / Health:Damage`, targeting the player, value `-828`.

The preceding player Health metric is `626`, so the committed Viewer
reconstruction produces one exact `Defeated by direct damage` event sourced by
Fang.

The report instead references shared Viewer bundle
`7e1bde1957aa1ff81ca85b89138c633942a222ed5014c8b5af5fb26c21c047a7`.
That installed bundle predates the defeat-marker implementation. Refreshing
only `__codex-tailwind-theme-review.html` did not replace the production DLL or
the shared Viewer used by newly generated reports.

### Abrupt recording end

Native `ReplayState.Replay()` awaits `CombatSimHandler.Simulate()`, then
immediately calls `FlipBoard()` and rebuilds the pre-combat board. Normally,
`Simulate()` holds the terminal presentation in
`FinalBlowSlowDownController.ReturnToNormalSpeed()` before it returns.

The recording-only Harmony patch currently replaces that entire method with
`Task.CompletedTask` to suppress native slow motion. It therefore removes both
the slow motion and the terminal presentation hold. In the affected MP4 the
terminal frame begins near 14.03s, the board starts clearing near 14.15s, and
the video ends at 14.50s.

## Candidate mechanisms to verify

### Missing defeat marker

- `combatant-died` is absent or targets a different entity ID;
- the terminal Health metric is already zero, so there is no previous positive
  sample from which the Viewer can identify the crossing;
- health settlement ordering differs from the earlier fixture;
- the report duration/last-frame boundary clips the marker;
- the marker exists but is visually hidden by endpoint or dense-marker layout.

### Abrupt recording end

- the recorder is stopped directly from simulation completion;
- the recording contains repeated terminal anchors but the final published
  media is intentionally clamped to the plateau's first sample;
- suppression of native final-blow slow motion also removed the only existing
  terminal hold;
- a hold exists in wall-clock state but is not represented by captured video
  frames or sync anchors.

## Candidate approaches

1. Prefer fixing the report producer if a canonical death event is missing.
2. If the producer evidence is complete but the Viewer assumption is too
   narrow, broaden reconstruction only with evidence-backed terminal rules.
3. Replace the recording-only completed task at the native terminal boundary
   with a one-second real-time hold. It should:
   - begin only after the terminal combat state is fully rendered;
   - capture a short bounded duration of unchanged final frames;
   - keep combat time pinned to the terminal frame;
   - avoid native slow motion, board clear, and scene transition;
   - not delay non-recorded replay behavior.
4. Do not solve either issue by extending the Timeline duration into
   post-combat transition frames.

## Verification

- Decode the report envelope and enumerate terminal metrics/events by frame,
  sequence, target, and source.
- Inspect recording metadata, FFprobe duration/frame count, and combat/media
  anchors at the terminal plateau.
- Trace the recorder stop boundary and native final-blow sequence against
  decompiled game source before selecting a hook.
- Add pure tests for terminal defeat reconstruction variants.
- Add recording lifecycle tests for a bounded final hold scoped to active
  recording only.
- Run the full frontend suite and relevant CombatReplay recording/report
  executable tests.
- Rebuild and replace the local DLL, then record a new Steam replay and verify:
  one defeat marker, correct cause/source policy, and a stable final-board hold
  without slow motion or transition footage.
