# PR #190 Acceptance Checklist

This checklist is the sole completion standard for PR #190. A checkbox may be marked only from
the evidence named in that item. Code inspection is not visual evidence, and a diagnostic smoke is
not final acceptance.

## Scope

- Recap hover immediately presents the native card Tooltip and a separate adjacent Combat Impact
  Tooltip. No click or right-click is required.
- Pressing Shift toggles the Combat Impact perspective between effects caused and effects received;
  the selected perspective persists across hover changes for the rest of the current Recap.
- Combat Impact follows PR #132's replay facts and aggregation semantics; it does not infer effects
  from card text.
- Native Recap UI outside the paired Tooltip presentation is out of scope. In particular, this PR
  must not patch the native use-count `Text_MultiplySign` glyph or the game's locale-wide font chain.

## Interaction and lifecycle

- [ ] Hovering an item immediately shows both complete Tooltips; moving away dismisses both.
  Evidence: user runtime acceptance with item screenshot and matching shown/dismissed log events.
- [ ] Hovering a skill immediately shows both complete Tooltips; moving away dismisses both.
  Evidence: user runtime acceptance with skill screenshot and matching shown/dismissed log events.
- [ ] One Shift press toggles perspective exactly once. Repeated toggles never dismiss, recreate,
  detach, or move either Tooltip, and never expose a header-only Auxiliary Tooltip.
  Evidence: user performs at least ten deliberate press/release cycles on one stable hover; Debug
  interaction logs show one perspective transition per press and no orphan native Auxiliary
  transition.
  Current code evidence: the controller consumes the native left/right Shift `wasPressedThisFrame`
  edge and swaps the two already-built perspective roots in place; it no longer polls held state or
  maintains a frame-reset latch. A toggle is accepted only after preview loading and pair placement
  finish, then swaps content and repositions the already-active pair while concealed. The requested
  perspective survives native-host requeues, real pointer exits, and subsequent card/skill hovers;
  only `RecapEnded` resets it to caused. If the native Auxiliary node independently fades while the
  same focused hover remains valid, the controller lets that native node close and requeues the
  complete pair; pointer-exit, Recap-disable, and native application-focus dismissal never take that
  recovery path. Runtime lifecycle/position evidence is still required.
- [ ] Re-hovering and moving across cards never leaves multiple cards enlarged, causes card flicker,
  or repositions the board/card layout.
  Evidence: user runtime acceptance across at least five cards, including repeated hover on one card.
- [ ] Both Tooltips share the native show/fade lifecycle and remain top-aligned, adjacent, fully
  on-screen, and clear of the native cooldown ring.
  Evidence: user screenshots from both left- and right-constrained placements.

## Data semantics

- [x] Effects are attributed from recorded replay commands/updates with exact source and target
  identity; a self-targeted effect appears in both caused and received perspectives without target
  fan-out. Evidence: `CombatImpactProjectorTests` exact-target/ambiguity cases and
  `CombatImpactAggregatorTests.Self_targeted_effect_appears_in_both_perspectives_without_authoritative_leakage`.
- [x] Destroy and Flying/state changes are represented when present in replay facts, and player/card
  attribute changes use player-facing change labels rather than raw internal keys.
  Evidence: focused projector coverage for action commands/attribute updates plus
  `CombatImpactAttributeLabelTests`. The Aura attribute policy now preserves the modifier/economy
  classes projected by PR #132, including Flying/Destroy/Force Use target modifiers, and retains PR
  #132's duration/percentage units; final wording remains covered by visual acceptance below.
- [ ] Crit is nested into the relevant effect's event count and target detail instead of appearing
  as a standalone effect group. Damage, Heal, and Shield use the native health-adjustment `IsCrit`
  marker. Burn, Poison, and Regen are Crit-capable actions whose client player-attribute DTO drops
  that marker; their count is recovered only when the historical non-Crit application values and
  the native `ECardStats` total have exactly one possible Crit count. Ambiguous/incomplete equations
  remain unlabelled, and Crit Chance is never used as evidence. Ordinary Damage/Regen/Crit Chance
  Gain rows never participate. Focused tests include the real Zarlic sequence
  `5,5,6,7,7,8,9,9,10,10`: total 147 resolves to 10 applications with 9 Crits. Runtime evidence is
  still required.
- [x] Damage, Burn, Poison, Shield, Heal, duration effects, reload/charge, and attribute changes keep
  authoritative totals separate from partial/lower-bound observed values.
  Evidence: `CombatImpactAggregatorTests` coverage tests and `CombatImpactMetricFormatterTests`.
  Presentation never turns incomplete reconstruction into a mathematical `≥`/`≈` claim: game totals
  remain unqualified, reconstructed durations carry a trailing `*`, and a localized `*` footer
  appears only for reconstructed or missing-target detail.
- [ ] Runtime samples agree with PR #132 semantics, including Zarlic self-Haste, Orange Julian's
  Damage increase, Destroy/Flying, and caused/received source attribution.
  Evidence: user runtime screenshots/logs for every named regression sample.

## Visual and content

- [ ] Typography follows native Tooltip rules: serif `Combat Impact` title (capital I), native body
  family, weight, size, leading, and hierarchy; no leaked rich-text/style tags.
  Evidence: user comparison against the adjacent native Tooltip.
  Current code evidence: entity names discard both a separate native enchantment-prefix line and
  same-line TMP rich-text wrappers before rendering. Metric strings no longer inject nested TMP
  font/material tags; Chinese BPP-owned text uses the stable native owned-font preparation and fails
  the complete presentation before construction if that typography is unavailable. Runtime
  typography comparison is still missing.
- [ ] Header is compact and contains perspective label, source name/summary, and concise
  `Press Shift ...` hint without a footer instruction row.
  The localized caused/received state and Shift key keep the donor's ordinary weight and are
  distinguished only by color. Crit counts use the native Crit sprite rather than a text label.
  The received perspective proceeds directly from its summary to effect groups without a redundant
  `Sources that affected this card` context row. Evidence: user screenshot.
- [ ] Effect groups, dividers, rows, and right-aligned metrics match the approved mockup hierarchy;
  left/right outer padding is equal and there is no unexplained ellipsis row.
  Evidence: user screenshots for compact and dense content.
  Current code evidence: BPP no longer adds asymmetric horizontal root padding; the native
  `Tooltip_Aux_Content` prefab's serialized `0 / 0` horizontal padding remains authoritative.
- [ ] Effect headings without an icon start at the content edge; headings with an icon do not reserve
  extra blank space. Item/skill target icons are left-aligned with no leading padding, and names start
  immediately after the visible icon.
  Evidence: user screenshots containing icon and iconless groups.
  Current code evidence: iconless headings create no spacer; icon headings render the native sprite
  and label in one TMP element so both share the native text baseline instead of aligning two
  independent rectangles. Runtime evidence for item/skill rows and mixed card spans is still missing.
- [ ] Target previews show icon/art only—no native red/green stat badges, cooldown ring, or other
  card chrome—and remain sharp with correct aspect ratio.
  Evidence: user screenshots for item and skill rows.
  Current code evidence: the shared Tab/Collection `CardPreviewBase` path is used;
  `ShowArtworkOnly` enables native art/frame while keeping `CardGemGroupBase` supplemental values
  concealed. Runtime sharpness and skill rendering are still unverified.
- [ ] Small, Medium, and Large item artwork uses native previews with adaptive visible-art bounds;
  all visible left edges align and each following name begins at the same gap from the artwork's
  visible right edge. No per-size hard-coded offsets.
  Evidence: user screenshot containing all three sizes in one group.
  Current code evidence: fitting measures the active native artwork quad, aligns its world-space
  visible left edge, and derives the row column width from `CardPreviewBase._cardImage` rather than
  guessing from the frame or another `RawImage`; a missing authoritative artwork rect hides that
  row's preview instead of restoring the span-wide slot. Both perspective roots stay active but only
  the selected root participates in layout, so received previews can finish measurement before the
  first Shift. No span-specific offset table exists. Runtime mixed-span alignment is still
  unverified.
- [ ] Damage/Burn/Poison and equivalent player/opponent effects omit redundant target rows. Attribute
  changes state the concrete change type. Crit details appear parenthetically with the native Crit
  sprite within event/target metrics, never as text or a separate row.
  Evidence: user screenshots for these effect types.
- [ ] Every duration is shown in seconds. Fractional numbers show exactly two decimal places while
  integers remain unpadded; no `ms`, `≥`, `≈`, `estimated`, or `估算` appears. Reconstructed values
  carry a trailing `*`; the localized disclosure starts with `*` and appears only when applicable.
  Evidence: focused formatter tests plus user screenshots in caused and received perspectives.
- [ ] Empty caused/received states render a complete, correctly positioned Combat Impact Tooltip,
  not only a detached native header.
  Evidence: user screenshot from a zero-impact card in both perspectives.

## Replay readiness and release gates

- [ ] Replay never begins until every current board card front is fully rendered; no fixed delay is
  used as the readiness definition.
  Evidence: user starts several replays without any blank/partial card front; code inspection confirms
  the replay gate awaits native setup completion and a stable render boundary.
  Current code evidence: `ItemController.Setup(Card)`'s original returned task is wrapped and tracked;
  once no active setup remains, the gate crosses Unity's `WaitForEndOfFrame` render boundary and
  checks the active setup set again. It uses no fixed delay as readiness. Runtime evidence is still
  missing.
- [ ] No `post_combat_impact` render exception/degraded event occurs during the complete runtime matrix.
  Evidence: final `BepInEx/LogOutput.log` inspection after user acceptance.
  Normal hover, dismissal, and perspective lifecycle events are Debug-only; Release logs retain only
  projection and interaction degradation warnings.
- [x] `./run.sh format-check`, focused PostCombatImpact/native-preview tests, Architecture.Tests,
  full `./run.sh test`, Debug build, and `git diff --check` all pass on the accepted revision.
  Evidence: captured command results after the final code change.
  Final evidence after diagnostic/test cleanup on 2026-08-02: `format-check` checked 1098 files;
  PostCombatImpact passed 112/112, native preview passed 36/36, Architecture passed 127/127,
  HistoryPanelPreview passed, the complete `./run.sh test` suite passed, and the Debug build completed
  with zero warnings/errors. Installed Debug DLL `4.6.0.t20260802.172007.dev` matches the built
  SHA-256 `810e3c437992e59a3bba8d45812da923d5a98d07e25682528f769c49e52f2503`.
- [ ] User explicitly accepts the final in-game visuals and interaction. PR remains Draft until then.

## 2026-08-04 intermittent header-only orphan

### Background

Combat Impact requests the game's singleton Auxiliary Tooltip, conceals it while custom content is
built, and then reveals the complete paired presentation. The native request is asynchronous: the
controller writes the header before its end-of-frame positioning/fade coroutine completes.

### Current problem

Peng captured an intermittent white Auxiliary Tooltip containing only `Combat Impact` while the
native card Tooltip remained visible. This is the same header-only state prohibited by the
interaction acceptance criteria above.

### Evidence and root cause

- The screenshot is a header-only native Auxiliary Tooltip, not a partially rendered Combat Impact
  content tree: no custom summary, groups, rows, frame, or background clone are present.
- `AuxiliaryTooltipController.ShowAuxiliaryTooltipController` assigns the header synchronously and
  finishes positioning/fade after `WaitForEndOfFrame`.
- When a BPP-owned request becomes stale before takeover, `ResumeAfterNativeAuxiliaryShow` calls
  `CancelPreparedNativeAuxiliary` and then asks the native parent to hide it.
- The current cancel path restores the prepared host's header visibility and CanvasGroup alpha
  before that native hide begins. This creates a real frame window where the detached title is
  visible; if the native fade coroutine is interrupted, the window can persist.
- The current Debug log contains no `post_combat_impact` shown/degraded event for this screenshot,
  which is consistent with cancellation before the complete presentation reaches its reveal/log
  stage.

The first cancellation fix concealed the serialized root CanvasGroup immediately before teardown,
but runtime acceptance still reproduced the orphan. Re-reading the earlier part of the sequence
exposes a second window: `PrepareAuxiliary` gates only `auxParent`, while the native header and frame
are not proven to be descendants of that node. The native end-of-frame coroutine then starts its own
fade-in on the controller CanvasGroup before stale-request cancellation runs. Concealing only at
cancellation is therefore too late to prevent a rendered header/frame flash.

### Candidate approaches

1. **Retune layout/background creation** — rejected: the screenshot predates content attachment and
   contains none of those nodes.
2. **Hide only the header text** — rejected: the native frame/background could still flash, and it
   leaves the cancellation lifecycle split across individual children.
3. **Keep the complete prepared Auxiliary Tooltip concealed until native teardown** — selected:
   cancel restores reusable geometry but does not restore native content visibility; it sets the
   controller-level CanvasGroup to zero before handing teardown back to the game.
4. **Add an independent controller-root preparation gate** — selected after the first fix still
   reproduced: add a BPP-owned CanvasGroup before the native `Show` body runs. The native serialized
   CanvasGroup remains free to complete its own fade and `HasShown` lifecycle underneath; the BPP
   gate opens only when the complete paired content is ready. A stale cancellation deliberately
   keeps this outer gate closed until teardown or the next legitimate native show releases it.

### Verification

- Add a lifecycle regression that locks `CancelPreparedAuxiliary` to conceal-before-restore
  behavior.
- Run the focused PostCombatImpact/architecture tests, format check, Debug and Release builds.
- Manual acceptance: rapidly enter/exit several Recap items and skills, including leaving before
  the auxiliary tooltip settles; no standalone `Combat Impact` title may appear. Confirm ordinary
  native auxiliary tooltips still render after the stale request is dismissed.

## 2026-08-05 fast A→B switch: B's Combat Impact details never show

### Background

The 2026-08-04 fix added a BPP-owned controller-root shell gate created with `forceOwnedGroup:
true` (`AddComponent<CanvasGroup>` on `auxiliary.gameObject`) in both `PrepareAuxiliary` and
`TryOpen` (`NativePairedTooltipHost.cs`). The native Auxiliary Tooltip is not a persistent
singleton GameObject: it is a spawned node-sequence object, and
`TooltipParentComponent.HideAuxiliaryTooltipController` tears it down by completing the node
sequence (`decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/TooltipParentComponent.cs:339`).

### Current problem

Quickly moving hover from card A to card B in Recap sometimes leaves B with only the primary
tooltip; B's Combat Impact details never appear for the remainder of that hover.

### Evidence and root cause

- `LogOutput.log` from the 2026-08-05 00:28 session (DLL built with the shell-gate fix) contains
  two `post_combat_impact.interaction.degraded reason_code=tooltip_render_exception` events with a
  `NullReferenceException` at `CanvasGroupGate..ctor [0x00046]` called from `TryOpen [0x00127]`.
- IL disassembly of the installed DLL maps `TryOpen IL_0127` to the shell-gate
  `CanvasGroupGate.Create(auxiliary, auxiliary.gameObject, forceNonInteractive: true,
  forceOwnedGroup: true)` call, and ctor `IL_0046` to `_originalAlpha = _group.alpha`, immediately
  after `AddComponent<CanvasGroup>()`. The `forceOwnedGroup` path never calls `GetComponent`, so
  the dereferenced group can only be the `AddComponent` result.
- Unity rejects `AddComponent` on a GameObject whose `Object.Destroy` is pending and returns null,
  while `!= null` checks, `HasShown`, and property reads still pass until end of frame (fake-null
  materializes at frame end). Decompiled `CanvasGroup.get_alpha` throws `NullReferenceException`
  via `MarshalledUnityObject.MarshalNotNull` — matching the logged exception type exactly.
- In the fast-switch flow, A's stale-request teardown (`ResumeAfterNativeAuxiliaryShow` →
  `CancelPreparedNativeAuxiliary` + `HideAuxiliaryTooltipController`,
  `PostCombatImpactController.cs:826-856`) can land its pending destroy in the same frame in which
  B's `ShowWhenReady` reaches `_view.Show`. Every stale/ownership check keys on the controller
  reference (`ReferenceEquals`), which cannot distinguish A's request from B's when the spawned
  instance is reused, and no liveness check can see a pending destroy before frame end.
- The thrown exception is caught at `PostCombatImpactController.cs:471-480` and routed to
  `FailPendingShow(..., suppressUntilExit: true)`: the hover revision is suppressed, so B's details
  stay hidden until the pointer exits. Nothing requeues the show afterwards — the auxiliary-hiding
  recovery path does not fire because the session never became owner (`_activeAuxiliary` is set
  after the gate creation that threw). This matches "sometimes never shows for the whole hover".

### Candidate approaches

1. **Harden `CanvasGroupGate` alone** (return null / no-throw when `AddComponent` comes back dead,
   `TryOpen` returns false) — insufficient alone: the failure would then route to
   `AuxiliaryTooltipContentUnavailable`, still with `suppressUntilExit: true`, so B would still
   never show.
2. **Same-frame liveness re-check before `_view.Show`** (`SequenceProcessor.IsNodeActive` sampled
   contiguously with the Show call) — narrows but cannot close the window: node completion and the
   coroutine resume order within the frame are not guaranteed, and the pending destroy is invisible
   to managed checks.
3. **Fail soft + requeue + request-token identity** — selected direction:
   a. `CanvasGroupGate.Create` returns null when the owned `AddComponent` result is dead;
      `TryOpen` undoes partial work and returns false (no exception on the main path).
   b. Classify this failure as transient: instead of `suppressUntilExit: true`, requeue via
      `StartPendingShow` while the hover revision is still current, so the still-valid hover
      re-requests a fresh native auxiliary tooltip on the next frame.
   c. Remove the identity ambiguity that lets A's stale cancel/hide land on B's request: capture
      the hover revision (or an explicit request token) when `ResumeAfterNativeAuxiliaryShow`
      starts and make its stale-discard a no-op once the pending auxiliary request has been
      superseded. Controller-reference equality cannot carry request identity on a reused spawned
      instance.

### Verification

- Unit/architecture: lock `TryOpen` returning false (not throwing) when the gate target is dying,
  and lock the stale-discard no-op when the request token is superseded.
- Runtime: in Recap, sweep hover A→B→A across ≥5 cards for ≥20 fast transitions. Acceptance:
  B's details visible on every settled hover, zero `tooltip_render_exception` in the Debug log,
  and no header-only orphan regression (previous section's acceptance still holds).
- Post-session log audit: `stale_request_discarded` may appear only when the hover genuinely
  exited; no `reason_code=tooltip_render_exception` at all.

### 2026-08-05 implementation notes

- Implemented 3a (gate fail-soft: `CanvasGroupGate.Create` returns null on a dead `AddComponent`
  result; `TryOpen` rolls back and returns false) and 3b (bounded transient retry:
  `TryConsumeTransientShowRetry`, max 2 per hover revision, requeues via `StartPendingShow` and
  logs `auxiliary_tooltip_show_retried`; suppression remains the fallback).
- Dropped 3c (request token for `ResumeAfterNativeAuxiliaryShow`'s stale discard) after tracing
  every A→B entry: a new hover always passes `SetHoveredSource` →
  `CancelAndClearPresentation(preserveOutstandingAuxiliaryRequest: true)` → `StopPendingShow`,
  which stops the resume coroutine (it *is* `_pendingShow`) before any superseding flow starts
  (`PostCombatImpactController.cs:169`, `:950-953`). The `sameHover` early-return cannot switch
  cards, and the unmatched branch of `OnNativeAuxiliaryTooltipShowing` also reaps it. A token
  would be dead code today; revisit only if a new caller starts that coroutine outside
  `_pendingShow`.
- Locked by `Gate_creation_fails_soft_and_open_reports_a_dying_controller_as_unusable` and
  `Transient_show_failure_retries_before_suppressing_the_hover` in
  `NativePairedTooltipArchitectureTests`.

## 2026-08-05 (second report): orphan reappears, apparently only on Tracer Pistol

### Current problem

With the retry fix deployed (verified: game started 00:53, DLL deployed 00:50, hashes match), the
header-only `Combat Impact` orphan reappeared during combat-replay playback, reportedly only when
hovering Tracer Pistol (a DEADLY-enchanted weapon). The session log contains zero
`post_combat_impact` warnings for the occurrence.

### Evidence so far

- BepInEx `[Logging.Disk] LogLevels` excluded `Debug`, so every `LogInteraction` reason code
  (hover observed, retried, stale discarded, content unavailable…) was invisible on disk across
  ALL prior sessions. Debug has now been added to the disk log levels (config change; revert by
  removing `, Debug` from line 104 of `BepInEx/config/BepInEx.cfg`).
- Static analysis found the exposure mechanism: every failure rollback inside `TryOpen` called
  `Release(restoreNativeContent: false)` directly, and `Release` restores all gates to their
  visible originals. `CompleteAnimatedHide` and `CancelPreparedAuxiliary` conceal first, but the
  TryOpen rollbacks did not — so ANY TryOpen failure after the native fade-in exposed the native
  shell (header-only box) until the subsequent hide finished. The gate-death rollback exposes it
  with the header still active — matching the screenshot text.
- `Show`'s only silent-false sources are the locale typography check and TryOpen itself; TryOpen's
  branch was not observable from logs. Both are now logged at Warning through
  `InteractionDegraded` with new reason codes (`typography_unavailable`,
  `pair_open_missing_auxiliary_fields`, `pair_open_dying_controller`,
  `pair_open_missing_background`, `pair_open_background_clone_rejected`); TryOpen returns a
  `NativePairedTooltipOpenFailure` detail to the view, keeping the host log-free.
- Fix applied: each TryOpen rollback now calls `ConcealNativeAuxiliary(auxiliary)` before
  `Release`, mirroring the cancel-path precedent. Locked by the extended
  `Gate_creation_fails_soft_and_open_reports_a_dying_controller_as_unusable` test.
- Session log also shows `collection_panel.dock_layout.degraded blocker=…Tooltip_CardTooltip_LockMode_P(Clone)` —
  the native LOCK-MODE card tooltip prefab was alive during the session. Unverified lead: if the
  hovered card's primary tooltip resolves to the lock-mode clone, its background sprite/shape may
  differ and deterministically fail TryOpen's background checks for that card.

### To verify next (requires game restart for the config + DLL)

Reproduce on Tracer Pistol, then read `LogOutput.log`:

1. Which Warning appears — `pair_open_missing_background` / `pair_open_background_clone_rejected`
   would confirm the primary-background theory; `typography_unavailable` would point at the locale
   font gate.
2. `auxiliary_tooltip_show_retried` (Debug, now visible) should appear up to 2 times followed by
   `auxiliary_tooltip_content_unavailable` when the failure is persistent.
3. Visually: the header-only orphan must no longer appear even while the failure persists (conceal
   now precedes rollback); the remaining defect would be "details missing on this card", to be
   fixed once the reason code names the branch.

### 2026-08-05 (third round): card identified, exposure window found

Runtime facts from the instrumented session (Debug disk logging active, probe build verified):

- The failing card `4f4134b8-2799-4180-b665-eb809cfb101c` is Tracer Pistol — and it is the ONLY
  Legendary-tier card in the replayed battle (battle_snapshots for 63c5a5ca). The auxiliary frame
  is tier-assigned (`AssignTooltipFrame(tier)`) and the Legendary frame is light/white. "Only
  Tracer Pistol shows the white box" therefore most likely means "every card has the same exposure
  window, but only the Legendary frame is bright enough to notice".
- The user reproduced the white box while details rendered normally, and the full trail shows
  ZERO failure codes, zero retries, zero pair-open rejections, zero unmatched shows, zero
  blocked/aborted probes. The exposure lives in a path with no logging and no interaction-pipeline
  anomaly.
- The trail for Tracer Pistol shows rapid re-hover cycles (hover → dismissed → hover within
  ~1s). On a re-request the native singleton controller is reused while still alive, and
  `PrepareAuxiliary` did restore-then-recreate on the shell gate: `Restore()` sets the kept-closed
  owned CanvasGroup back to alpha 1 and defers `Object.Destroy` to end of frame, so during that
  frame's render the controller root is ungated with the header active — a one-frame header-only
  flash, white on Legendary.

Fix: `PrepareAuxiliary` now reuses a matching still-held gate (re-closing it explicitly) instead
of restore-then-recreate, for both the controller-root shell gate and the auxParent gate. Locked
by the existing prepare-order architecture test (assignment still precedes `auxiliary.auxParent`).

Remaining risk: the one-frame-flash mechanism explains a screenshot-able artifact only if capture
odds are favorable; if the box is ever observed PERSISTING for multiple seconds after this fix, the
next suspect is a superseded native hide (`HideAuxiliaryTooltipController` no-op when the node is
already completing) leaving a natively-visible controller — verify by capturing frames during
reproduction (scratchpad capture script + window id via CGWindowList).

### 2026-08-05 (fourth round): prefab ground truth — the shell gate never worked

Static inspection of the game's `tooltips_assets_all.bundle` (UnityPy, `Tooltip_Aux_P` prefab)
settles the mechanism with asset-level evidence:

```
Tooltip_Aux_P                     ← AuxiliaryTooltipController, world-space Transform + BoxCollider
└── Tooltip_Aux_Canvas            ← Canvas + CanvasGroup (= serialized tooltipCanvasGroup, DOFade target)
    └── ScalerOffset
        └── Tooltip_Aux_Parent_RectTransform
            └── Tooltip_Aux_Content   ← auxParent (serialized field, verified via typetree)
                ├── Background / TitleText / Divider / BodyText
```

- The 2026-08-04 premise "the title and frame can be siblings of auxParent" is false for this
  prefab: `auxParent` (Tooltip_Aux_Content) owns the COMPLETE visual tree.
- The controller root sits ABOVE the prefab's own nested Canvas; a CanvasGroup added there is
  outside canvas alpha propagation. The controller-root shell gate was therefore inert from the
  day it shipped — which is why every shell-gate-based fix "didn't work" at runtime.
- The only effective concealment is the auxParent gate — and `CancelPreparedAuxiliary`'s stale
  branch RESTORED it, leaving concealment to a one-shot serialized-alpha write that the in-flight
  native fade-in freely rewrites. That is the persistent white box (white = Legendary frame).

Fix (replaces the shell-gate subsystem entirely, per the no-fallback rule):

- Shell gate removed everywhere (field, prepare/open/release/fade paths, `forceOwnedGroup`).
- `CancelPreparedAuxiliary` stale branch now KEEPS the auxParent gate closed; it is handed back by
  the next `PrepareAuxiliary` reuse, by `ReleasePrepared` when a foreign show takes the
  controller, or dies with the despawned controller.
- Prepare reuses a matching held gate closed (`SetAlpha(0f)`) instead of restore-then-recreate.
- Architecture tests updated: `Preparing_an_auxiliary_gates_auxparent_and_reuses_a_held_gate`,
  cancel test now asserts `RestorePreparedAuxiliaryGate` is absent from the cancel path.

Verification: same acceptance as previous rounds (rapid re-hovers on the Legendary card, no white
box, details render; log must stay free of degradations). Known residual: if some native path
raises the auxiliary's visibility without an `AuxiliaryTooltipController.ShowAuxiliaryTooltipController`
call (which is what hands the gate back via ReleasePrepared), the native tooltip could stay blank —
no such path is known; if a blank NATIVE auxiliary tooltip is ever observed, start there.

### 2026-08-05 (fifth round): user correction — the box is a single-frame flash

User observation: the white box is not persistent; it is a sudden one-frame(-ish) flash. This
identifies the remaining mechanism as the deferred-destroy corpse class:

1. On every matched show for a controller that still carries prepared state, the feature handler
   first calls `ReleasePrepared` → `CanvasGroupGate.Restore()` sets the owned group back to
   alpha 1 and calls `Object.Destroy` — which only takes effect at END of frame.
2. The immediately following `PrepareAuxiliary` recreates the gate via `GetComponent`, which
   returns the still-attached corpse; the new gate closes it (alpha 0) for this frame.
3. At frame end the corpse is destroyed, silently taking the gate with it. From the next frame
   auxParent is UNGATED, the native fade-in raises the serialized alpha, and the tooltip shell
   (white Legendary frame + header) is visible until `TryOpen` takes over.

The removed shell gate had incidentally masked the misdiagnosis of this chain; removing it exposed
the corpse-adoption hole directly. Fix: owned gates now `Object.DestroyImmediate` on restore —
the corpse never survives into a GetComponent, which eliminates the whole deferred-destroy class
(corpse adoption, end-of-frame exposure after restore, same-frame duplicate groups). Locked by
architecture assertions (`Object.DestroyImmediate(_group)` required, deferred variant forbidden).
