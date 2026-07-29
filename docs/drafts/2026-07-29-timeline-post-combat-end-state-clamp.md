# Timeline post-combat end-state clamp

## Background

The report has three related but non-identical end points:

- the final CombatSim frame and its state samples;
- the Timeline display domain, which includes the complete duration of that
  final frame;
- the recording media, which may continue after the combat clock stops so the
  final board can settle and remain visible before the replay transitions away.

Earlier fixes stopped the right edge from wrapping to media time zero and
stopped terminal seek from selecting the clearing transition. The current
mapping therefore treats the repeated terminal combat-time anchors as a
platform and selects its first media sample.

## Current problem

The latest screenshot shows the cursor at the Timeline right edge and the
recording at `14.041s`, but the visible board is still in the final damage
animation. It does not yet show the settled terminal fact represented by the
Timeline: one combatant has crossed zero health and the defeat marker has
already been emitted.

The expected behavior is:

- the last in-combat interval remains seekable normally;
- once the pointer enters display time after the last combat event/state
  transition, the recording preview is clamped to a settled terminal board
  frame;
- that terminal preview must still be before board clearing or replay
  transition, and must visibly agree with the report's defeat state.

This is the same end-of-combat synchronization boundary that has failed in
several forms, so another endpoint-only clamp is not sufficient without
validating the full data path.

## Candidate approaches

### A. Continue using the first terminal platform sample

This preserves the earliest media frame whose combat time equals the final
CombatSim time. It avoids transitions but can land before health UI and final
damage animations have settled. The screenshot demonstrates that this is not a
valid product definition of "final state".

### B. Use the last terminal platform sample

This is simple and likely reaches the settled board in recordings that stop
before `FlipBoard`. It is unsafe for older recordings whose anchors continue
through board clearing or post-combat transition under the same frozen combat
time.

### C. Derive a terminal-state anchor from the stable pre-transition window

Treat the repeated terminal combat-time anchors as a media interval rather than
a single point. Select a sample late enough for the board UI to settle while
remaining inside the known pre-transition hold. Prefer explicit recording
metadata when available; for compatible reports, derive a conservative point
from anchor cadence and the terminal platform without crossing a discontinuity
or media end.

This is the preferred direction if the existing recording/report metadata can
prove the safe interval. If it cannot, the producer must publish an explicit
terminal preview anchor instead of making the Viewer guess from video pixels.

## Evidence and selected behavior

The current recorder replaces the native final-blow slowdown with a one-second
terminal presentation hold before `CombatEnded`. In report
`edec33acd04444c89966c08693bf1509`, the repeated `12.25s` combat anchor spans
media `14.05s` through `15.483s`. The first sample is still in the damage
animation; at `14.80s` the board is intact and player health visibly reads
`-202`; clearing begins after roughly `15.2s`.

The older report `b505aa0e39054c17b49778490edc6382` has only a `450ms`
terminal platform and does not contain the full modern hold. An inventory of
the local historical reports also found legacy native-slowdown platforms from
`4.584s` through `9.584s`, clearly distinct from the modern `1.433s` platform.
Therefore:

- exact final-combat time maps to the first terminal anchor, preserving the
  event's impact frame;
- display time after combat maps to the first terminal anchor at or after
  `+750ms`, but only when the platform spans `1,000–2,000ms`;
- shorter incomplete captures and longer legacy slow-motion platforms keep the
  first-anchor behavior instead of guessing inside animation or board clearing;
- normal playback stops at the same settled terminal anchor.

## Verification method

1. Identify the exact report behind the screenshot and inspect its final combat
   events, health samples, media duration, and repeated terminal anchors.
2. Decode contact frames across the complete terminal anchor platform and mark:
   final damage begins, health visibly crosses zero, board becomes stable, and
   board clearing begins.
3. Add pure mapping tests for:
   in-combat seek, the last combat transition, post-combat display time, old
   reports without terminal metadata, and terminal platforms that include an
   unsafe tail.
4. Add Chromium and WebKit behavior coverage that moves from the last combat
   event to the Timeline right edge and asserts a monotonic media time that
   settles on the same terminal preview frame rather than wrapping or choosing
   the impact frame.
5. Refresh the real localhost report and verify the rightmost preview visibly
   shows the defeated combatant at zero or below while the board remains intact.
