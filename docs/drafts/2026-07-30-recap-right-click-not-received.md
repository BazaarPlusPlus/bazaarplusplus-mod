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

### Candidate approaches

1. **Retune layout/background creation** — rejected: the screenshot predates content attachment and
   contains none of those nodes.
2. **Hide only the header text** — rejected: the native frame/background could still flash, and it
   leaves the cancellation lifecycle split across individual children.
3. **Keep the complete prepared Auxiliary Tooltip concealed until native teardown** — selected:
   cancel restores reusable geometry but does not restore native content visibility; it sets the
   controller-level CanvasGroup to zero before handing teardown back to the game.

### Verification

- Add a lifecycle regression that locks `CancelPreparedAuxiliary` to conceal-before-restore
  behavior.
- Run the focused PostCombatImpact/architecture tests, format check, Debug and Release builds.
- Manual acceptance: rapidly enter/exit several Recap items and skills, including leaving before
  the auxiliary tooltip settles; no standalone `Combat Impact` title may appear. Confirm ordinary
  native auxiliary tooltips still render after the stale request is dismissed.
