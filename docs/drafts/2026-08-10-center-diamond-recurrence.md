# Center diamond recurrence

Status: built and installed; in-game validation after process restart pending

## Background

The intermittent small brown diamond near the center of the post-combat board has been fixed before.
The previous investigation identified it as the shared native auxiliary-tooltip background collapsed
around inactive header/body nodes, not a music-note socket marker. The shared paired-tooltip handoff
was changed to reactivate requested native text nodes, and runtime anomaly logging was added for
`requested_text_inactive` and `visible_without_text`.

## Current recurrence

On 2026-08-10 the artifact appeared again after the earlier fix. The supplied screenshot shows the
same small brown diamond over the board, left of the center item. Because the symptom is intermittent,
another unconditional `SetActive(false)` or background hide would risk breaking legitimate pooled
auxiliary tooltips without proving which lifecycle won the race.

## Candidate mechanisms

1. A native auxiliary `Show` path bypasses the paired-host handoff, so requested text remains inactive.
2. The handoff reactivates text, but a late hide/restore continuation deactivates it after the handoff
   while leaving the pooled background visible.
3. The frame audit detects the invalid state but only logs it; its recovery path is missing or loses a
   later race in the same visible episode.
4. The visible object is a different native background instance than the controller currently tracked
   by the paired host, indicating pooled-controller identity drift.

## Evidence to collect

- The matching `post_combat_impact.native_auxiliary.anomaly` entries and surrounding show/dismiss logs.
- Native controller, background, header/body, and paired-content instance identities at show handoff,
  hide/restore, and frame audit.
- Active/renderable state and Canvas alpha for the same identities, ordered by frame/revision.
- Decompiled native `Show`/`Hide` paths and every current patch target that can alter these nodes.

## Confirmed cause

Candidate 3 is confirmed by the 2026-08-10 runtime log and the decompiled native lifecycle:

- `LogOutput.log:5103`, `5255`, and `5280` report `requested_text_inactive` during the
  `show_handoff`, each with `recovered=true`. The previous handoff repair is executing.
- `LogOutput.log:5116`, `5271`, and `5291` later report `visible_without_text` during
  `frame_audit`, with both native text nodes inactive, paired content inactive, and
  `recovered=false`. The invalid frame is therefore created after a successful handoff and the
  audit observes it without changing state.
- `AuxiliaryTooltipController.cs:112-119` starts the native fade-in after an end-of-frame yield.
  `BaseTooltipController.cs:227-267` then animates `tooltipCanvasGroup.alpha` back to `1`, so a
  one-time write to that same CanvasGroup cannot reliably conceal the frame.
- `NativePairedTooltipHost.cs:169-173` documents the prefab boundary already established from the
  native asset: `auxParent` owns Background, TitleText, BodyText, and Divider below the animated
  native CanvasGroup. Its own CanvasGroup is therefore the stable concealment seam.
- The teardown path restored that stable gate even when `restoreNativeContent=false`, immediately
  after destroying paired content. This reopened the shell while the native fade could still raise
  its own CanvasGroup.

The repeated empty frame is not a second marker type or a missed native `Show`. It is a late native
fade exposing the pooled background during a frame in which neither native text nor paired content
is renderable, followed by an observation-only audit.

## Chosen recovery

Teardown without native-content restoration now keeps the existing `auxParent` CanvasGroup gate
closed while paired content is removed. The next native show handoff restores it, or the next paired
presentation reuses it and fades it open once content is ready. This prevents the known race at its
source.

The frame audit remains a fallback for any other ordering that reaches the exact invalid state. It
closes the same gate below the native fade, without cancelling and re-requesting a valid hover that
is still settling. A matching held gate is reused, so recovery does not introduce a
restore/recreate flash.

Deterministic coverage pins all three lifecycle edges: teardown must retain a closed gate, the
native show handoff must restore it, and the controller audit must still conceal and verify an empty
visible frame. The shared host must always gate `auxParent` and leave a newly created recovery gate
held after creation.

## Automated verification results

- `NativePairedTooltipHost.Tests`: 37 passed.
- `Architecture.Tests`: 177 passed, including the native new-day transition visibility contract.
- `PostCombatImpact.Tests`: 252 passed in the combined lifesteal, critical-attribution, and tooltip
  working tree.
- `MusicNoteLetterMath.Tests`: 26 passed, including suspension and automatic resumption of
  the Shift-toggle overlay around the native new-day transition.
- `ItemEnchantPreview.Tests`: passed with the hover-only Toggle refresh guard.
- `CombatReplayRecording.Tests`: passed after fast-forwarding PR #256's native recording cue.
- PR #256 initially left an architecture assertion pinned to its removed `PositionOverUI` path;
  the local combination updates that contract to require `UICueActivator` and prohibit reuse of
  the shared auxiliary tooltip.
- Release configuration was used for these checks so validation did not replace the currently
  installed combined development DLL before the two pending fixes were assembled together.
- `./run.sh build`: succeeded with 0 warnings and 0 errors, then copied the combined Debug DLL into
  `BepInEx/plugins`. The built and installed DLLs have the same SHA-256
  (`dd30a6425080cb78d032ab957f23886e10f81cd553751efde0ae8eadff10735f`).
- The game process was still running from before the replacement, so it must restart before this
  build can receive runtime acceptance.

## Verification

The fix is accepted only when:

- the captured failing ordering is covered by a deterministic lifecycle test;
- rapid Recap/Replay/Continue hover transitions cannot leave a visible background with no renderable
  native or paired content;
- legitimate native auxiliary tooltips still render their requested text;
- repeated in-game transitions produce no visible diamond and no unrecovered
  `visible_without_text` anomaly.
