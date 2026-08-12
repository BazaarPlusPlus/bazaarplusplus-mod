# Superfan critical attribution incident

## Reported failure

- Runtime sample: latest local PvP battle `46213828c49b491fab36edb789824905`, recorded
  `2026-08-12T10:18:14Z`, local The Dragons versus waddoop / The Dragons.
- Replay payload: `BazaarPlusPlusV5/CombatReplays/46213828c49b491fab36edb789824905.payload.mpack.gz`.
- Recap card: Superfan, instance `itm__itn3d5`, template
  `109af4f3-b1c5-41c3-bf28-a4551ea3108a`; the runtime hover log matches that template.
- User-visible result: 10 uses, Damage `x10 · 150`, Shield `x10 · 300`, but no critical marker.
  The user interpreted a doubled value during playback as a critical hit.

## Same-replay finding (reopened after user correction)

The first diagnosis was incomplete: it treated a false health-adjustment `IsCrit` as proof that no
critical trigger occurred. That is invalid for non-health effects. Boombox's active abilities use
`TTriggerOnCardCritted`, so an execution of those abilities is independent native evidence that its
`TriggerSource` critted.

Facts established so far:

- All `CombatSimPlayerHealthAdjustment.IsCrit` values in battle `46213828...` are false.
- Superfan deals 10 damage and grants 25 shield for frames 130–150, five outcomes each.
- At frame 161 Boombox (`itm_wyXZ2rG`), triggered by Smoke Machine (`itm_WEHRQuV`), changes
  Superfan's `DamageAmount` from 10 to 20 and `ShieldApplyAmount` from 25 to 35.
- Superfan then deals 20 damage and grants 35 shield for frames 250–270, five outcomes each.
- Therefore the native totals reconcile exactly without criticals:
  - Damage: `5 × 10 + 5 × 20 = 150`.
  - Shield: `5 × 25 + 5 × 35 = 300`.
- The screenshot timestamp (18:19 local) and replay payload timestamp (18:18 local) match this
  battle, and the runtime hover log identifies the same Superfan template.

The frame-161 trigger proves Smoke Machine critted; it does not prove Superfan critted. A second
same-deck replay (`53a9cca6...`) provides the control case: when Superfan crits, Boombox's
executions name Superfan itself as `TriggerSource`. The missing marker in the reported battle
therefore belongs on Smoke Machine's originating Burn effect.

## Problem boundary used for diagnosis

The failure can originate in one of four layers and must be isolated in order:

1. The replay payload did not preserve the native critical evidence.
2. `CombatImpactProjector` failed to attach criticality to Superfan's damage execution.
3. `CombatImpactAggregator` dropped a projected critical count/value.
4. The tooltip model contains the critical aggregate but the view omitted it.

Do not special-case the Superfan template id. The fix must apply to the proven event shape.

## Root cause

The projector recognized critical outcomes from health adjustments and from exact doubled status
amounts, but it did not treat an executed `TTriggerOnCardCritted` ability as native critical
evidence. That loses non-health criticals when their amount cannot be inferred exactly from a
same-frame player-attribute delta. Smoke Machine is such a case: other Burn applications share its
frame, while all health adjustments legitimately keep `IsCrit == false`.

The native trigger ability is low priority, so its executed action can appear in the following
frame. The implementation retains that priority along with the effect id. Low-priority evidence
prefers the preceding frame unless the trigger source performs a new self-triggered action in the
evidence frame; immediate evidence prefers the same frame. This prevents a delayed trigger from
being assigned to a later activation of the same card.

## Implemented fix

- Snapshot each active `TTriggerOnCardCritted` ability's effect id and native priority.
- Use the executed ability's exact `TriggerSource` as the critted card identity.
- Attach one critical occurrence to the card activation's representative explicit effect,
  preferring Damage, Burn, Poison, Heal, Shield, then attribute effects. This matches the tooltip's
  count model: one native critical roll per activation, not one roll per effect row. Do not create a
  duplicate Crit row and do not invent a critical amount.
- Keep existing native `IsCrit` evidence authoritative and deduplicate when both evidence paths
  describe the same activation.
- Read the active base/tier abilities and the current enchantment abilities through one shared
  snapshot path, so enchantments that add `TTriggerOnCardCritted` participate in the same evidence
  chain.

## Proven replay boundary

This fix closes the known `TTriggerOnCardCritted` evidence chain; it does not make every critical
outcome observable. A full scan of the replay message graph found only one direct critical field:
`CombatSimPlayerHealthAdjustment.IsCrit`. `ECardStats` has no critical counter, and `VfxIndex` is
only an index into the VFX catalog. Non-health criticals therefore have only three usable evidence
paths:

1. a health adjustment carrying native `IsCrit`;
2. an exact doubled amount that can be reconstructed without ambiguity;
3. an executed active `TTriggerOnCardCritted` ability whose `TriggerSource` identifies the card.

If a critical touches no health, its amount cannot be reconstructed exactly, and no active
on-card-critted listener executes, the replay contains no fact that can distinguish it from a
non-critical outcome. The projector must leave that outcome unknown rather than guess.

The corpus now asserts the useful invariant available for path 3: every listener execution that
can be resolved to an originating displayed activation must leave that origin carrying critical
evidence. This prevents regression of known resolvable evidence, but cannot prove completeness for
unobservable outcomes.

## Verification performed / retained acceptance

1. The exact replay was decoded and its effect executions, card attribute updates, health
   adjustments, and native `IsCrit` flags were compared frame by frame.
2. The production projector was rerun against the exact payload plus its recorded opening
   snapshots and current `GameData.db`. Result: Smoke Machine Burn `crit=1`; Superfan Damage and
   Shield remain `crit=0`.
3. Focused regressions cover next-frame and same-frame trigger evidence, wrong trigger source, a
   later same-frame activation of the same card, and current-enchantment listeners.
4. The complete `PostCombatImpact.Tests` suite passes: 281 / 281 tests.
5. The local replay corpus passes across 398 battles and 130,745 frames, with zero card-attribute
   conservation failures. Its resolved-trigger invariant also passes: every path-3 origin that can
   be identified without guessing received critical evidence.
