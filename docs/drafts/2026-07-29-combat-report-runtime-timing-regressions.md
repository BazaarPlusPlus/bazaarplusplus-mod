# Combat report runtime timing regressions

## Background

Draft PR #132 links a CombatSim timeline to a recorded replay. Two timing fixes
were previously treated as complete after model/policy tests:

- report duration now includes the complete terminal CombatSim frame;
- replay recording has a readiness gate intended to delay simulation until the
  presented board is visible, face-up, and idle.

Real runtime feedback shows that neither user-visible workflow is closed.

## Current problems

### A. Terminal hover resets the recording

Verified user observation:

- the recording itself can seek to and display the final frame;
- pointer hover/drag at the final timeline frame jumps the recording back to
  the first frame.

The previous validation only proved that a 295-frame report exposes a 14.75s
timeline domain. It did not validate the complete pointer → combat time →
recording sync → `HTMLVideoElement.currentTime` path at the upper boundary.

Candidate mechanisms to distinguish:

1. the recovered report duration (14.75s) extends past the final sync anchor
   (14.70s), and upper-bound mapping falls through to zero instead of clamping;
2. assigning the exact media duration triggers `ended`/reset behavior while a
   slightly earlier seek works;
3. the scrub proxy and full recording have different terminal durations, so
   the hover path accepts a time the proxy cannot seek;
4. a seek completion or playback-state callback overwrites the latest hover
   target with the initial pinned time.

Confirmed root cause in the localhost review surface:

- the temporary preview was Python `http.server`, which answered a byte-range
  request with `200 OK` and the complete file instead of `206 Partial Content`;
- both videos therefore buffered only their opening segment while paused, so a
  direct hover seek to an unbuffered terminal frame stayed at `0`;
- the same report served by Vite returned `206 Partial Content` with
  `Accept-Ranges: bytes`; terminal hover then produced combat `14.75s`, scrub
  media `21.433s`, and never reset to zero.

This failure is in the review server, not the Viewer mapping. Do not add a
Viewer fallback that hides a server incapable of random media access.

### B. Replay simulation still starts before presentation is ready

Verified user observation:

- after choosing Record, combat starts while some items are not fully shown.

The previous validation covered the pure readiness predicate but did not prove
that the live Replay path reaches that gate, samples the complete expected item
set, or delays the actual first `CombatSimHandler.Simulate` call.

Candidate mechanisms to distinguish:

1. the Harmony target does not intercept the runtime overload/call path used by
   this replay;
2. expected item discovery under-counts controllers while cards are still being
   created, allowing a false-ready empty/partial set;
3. active/visible/face-up/idle probes report ready before the final presentation
   animation or layout completes;
4. the one-shot re-entry permit or timeout releases simulation before two
   genuinely stable rendered frames;
5. recording start and simulation release are ordered correctly in policy but
   wired to different runtime events.

Confirmed root cause:

- the existing presentation gate was only enabled for the native
  `CurrentNative` recording path;
- Record from History uses the `local_saved`/`imported_ghost` managed replay
  path, so its `ReplayState.Replay()` call reached
  `CombatSimHandler.Simulate(...)` without that gate;
- an initial attempted fix put the strict face-up/idle predicate in
  `WaitForPresentationReadyAsync`, before `Replay()` performs the native board
  flip. The live path correctly disproved that placement by timing out during
  bootstrap;
- the bootstrap gate and simulation gate are distinct: bootstrap waits only
  for the complete controller set and presentation managers to mount; the
  Harmony `Simulate` gate runs after the native flip and waits for every combat
  item to be visible, positioned, face-up, non-transitioning, and in
  `Card_Idle_Faceup_A`.

## Candidate approach

### Terminal hover

- Serve review reports with a Range-capable server.
- Verify the response contract before UI testing with a non-zero byte range:
  it must return `206 Partial Content`, `Content-Range`, and `Accept-Ranges`.
- Reproduce on the current real report while reading the computed combat time,
  scrub/full video duration, and completed media time for the same pointer
  sample.
- Keep the existing terminal mapping contract: timeline `14.75s` clamps to the
  final sync anchor at media `21.433s`.

### Replay readiness

- Use the main Replay/recording path and existing structured logging rather than
  a standalone probe.
- Confirm the exact runtime `Simulate` method and call order against decompiled
  source after capturing the failing runtime log.
- Preserve bootstrap as the controller-mount gate, then apply the strict
  presentation predicate at the intercepted `Simulate` boundary for both
  native-current and managed saved recordings.
- Gate against the complete expected combat item set, not merely currently
  discovered controllers. Require every item to be visible, face-up,
  positioned, non-transitioning, and idle for consecutive rendered frames
  before simulation release. Native-current recording also begins at that
  boundary; managed saved recording begins earlier so the report retains the
  board-reveal lead-in.
- Keep timeout as an explicit degraded path with counts and reason; a timeout
  must never be reported as readiness success.

## Verification

### Terminal hover acceptance

- On a real report, drag continuously through the final 250ms and hover the
  terminal frame.
- The preview server proves byte-range support before the interaction check.
- Combat preview time and the active scrub video's `currentTime` remain near
  the end; neither becomes zero.
- The terminal frame remains visible after seek completion and after leaving
  hover.
- Retain pure upper-bound sync coverage and the behavior tests for the
  pointer-to-video path; use a real Range-capable server for manual media
  acceptance.

### Replay readiness acceptance

- Launch The Bazaar through Steam and Record a replay with the current Debug
  DLL.
- Structured logs show
  `combat_replay.saved_recording.presentation_gate_resolved` with
  `outcome=ready`, `expected_items=15`, `visible_items=15`,
  `face_up_items=15`, `settled_items=15`, and `elapsed_ms=1613`.
- The generated recording
  `a4bd8d94683a468e87c0a5153ddf05bc.html` maps the first combat frame advance
  to media `1.966s`. Full-resolution review at media `1.800s` shows all 15
  items fully presented; combat effects begin only afterward.
- Deterministic tests cover incomplete item counts, presentation regression
  between samples, and gate selection for native-current, recorded saved, and
  non-recorded saved replay paths. The structured log schema is locked for
  both gate kinds; live validation covers the real one-shot Harmony release.
