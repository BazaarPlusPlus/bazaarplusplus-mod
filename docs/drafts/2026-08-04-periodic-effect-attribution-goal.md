# Periodic Effect Attribution Goal Ledger

Outcome: Combat Impact shows per-item/skill Burn and Poison realized impact and Regen realized
healing on the existing status row. The model is deterministic and versioned. Values are labelled
by proof class: Exact and Constrained are mathematically unique under a complete observable ledger;
Proportional remains distinguishable in the internal model; Unknown is not presented as attributed
fact. The compact tooltip intentionally omits confidence punctuation. Final in-game presentation
requires user acceptance.

Appetite: Cross the real replay boundary and complete two substantial model rounds. Continue only
while a round adds goal-level evidence or improves Exact+Constrained coverage without weakening the
invariants below.

## Model contract

### Observations

- Normalize both replay messages, without Unity or Assembly-CSharp:
  - spawn `NetMessageGameSim` supplies card instance/template/type/owner through
    `GameSimEventCardSpawned`;
  - `NetMessageCombatSim` supplies ordered frames, effect executions, player attribute updates,
    health adjustments, card updates, and per-card aggregate stats.
- The normalizer records every source or state transition it cannot explain. Missing entity/source
  resolution is evidence against Exact, never evidence that no other source existed.
- Attribution is scoped by combatant, status, and a continuous state epoch. A reset, cleanse,
  removal, transform, unexplained delta, or unresolved source closes or contaminates an epoch; it
  must not silently carry debt across that boundary.

### Coupled accounting

The model keeps three coupled ledgers. Their raw units are not interchangeable, and passing only
one is insufficient for Exact/Constrained.

1. **Status-stock ledger**, per combatant × status × frame/epoch:

   `opening status + source applies + explained external gains - native decay - cleanse/removal = closing status`

   Every term must be supported by an observed transition or a versioned rule that P1 demonstrated
   against the current corpus. A net frame delta is not automatically an apply: multiple gains,
   ticks, and removals can occur in the same frame.

2. **Health-impact ledger**, per combatant × damage type × frame:

   `sum(source-attributed Health impact) = sum(combat-effective matching Health adjustments)`

   Burn and Poison debit actual Health; Regen credits actual Health. Typed adjustments are the
   authoritative post-modifier input, then the terminal policy removes movement after the combatant
   has already died and caps a lethal Health loss at the remaining positive Health. Poison must
   never consume or be mitigated by Shield. Overheal, overkill, prevented damage, and other
   counterfactual amounts that did not change the live combat state are not booked as realized
   Health impact.

3. **Shield ledger**, per combatant × frame:

   `opening Shield + observed Shield gains - Burn-attributed Shield consumption - other consumption = closing Shield`

   Burn can consume Shield, but Shield points consumed and Burn damage prevented are not assumed to
   be numerically equal. Typed Burn Shield adjustments are the authoritative realized consumption.
   Versioned bridge formulas remain counterexample-tested diagnostics, not admission gates or a
   substitute for observed pool movement.

The acceptance harness also reconciles the whole pool movement for each frame:

`opening pool + all observed gains/losses = closing pool`

This catches duplicated/missing `HealthAdjustment` consumption even when the per-status allocation
still sums to the desired headline number.

### Realized-impact definition

- The model version owns an explicit pool policy; it cannot inherit the existing Direct Damage
  matcher implicitly.
- P1 must first report actual corpus combinations of `EDamageType` and
  `EPlayerHealthChangeType` for Burn, Poison, and Regen.
- Candidate V1 policy, subject to P1 evidence:
  - Burn stores separate `HealthDamage`, `ShieldConsumed`, and, only if derivable under the accepted
    rule, `DamagePrevented`. These values are never added into one scalar merely because they are
    all integers.
  - Poison stores `HealthDamage` only. Any Poison-attributed Shield consumption is an invariant
    violation, not another supported pool.
  - Regen stores positive `HealthRestored` only; overheal that never became a health adjustment is
    not realized healing.
- `EPlayerHealthChangeType.Joy` still exists in the shared enum but Joy is not a current gameplay
  pool. Enum presence is legacy schema compatibility, not evidence that the model should book it.
- Legacy `BazaarBattleService` behavior is hypothesis evidence only; live server simulation
  mechanics must be inferred from current replay observations, not asserted from that code.
- A matching `CombatSimEventCombatantDied` makes the first non-positive ordered Health cursor
  terminal for that combatant. Pre-terminal effects still count; post-terminal Burn/Poison Health,
  Regen, and later periodic groups do not. A contiguous Burn Health/Shield pair is one atomic tick:
  if the Burn group began while alive, its Shield consumption remains realized even when its Health
  component is lethal.

### Current rule knowledge and uncertainty

- The current game has no Joy gameplay attribute. The model books Health and Shield only.
- Shield interacts with Burn but not Poison. The amount of Shield consumed is not assumed to equal
  the amount of Burn damage prevented.
- Burn's decay and Shield interaction have changed across recent patches. The old “decrement one per
  tick” rule is false for the current version; neither the legacy battle service nor the stale
  community keyword page is acceptable evidence for the replacement formula.
- Poison and Regen timing, decay/offset behavior, cleanse behavior, and same-frame ordering are also
  not model axioms merely because older documentation describes them.

P1 must infer the installed version's mechanics from ordered replay state updates and typed
Health/Shield adjustments. A rule becomes versioned model input only after it reconciles every
relevant observed frame and a counterexample scan finds none. Until then it remains Unknown.

### Proof classes

- **Exact:** all status-state changes in the epoch reconcile, every applying source is resolved, and
  the source allocation has exactly one feasible solution.
- **Constrained:** multiple candidate sources existed, but the complete observable ledger plus
  per-source apply totals leaves exactly one feasible allocation. This is the same uniqueness bar as
  Exact; the label records that the result was solved rather than directly observed.
- **Proportional:** more than one feasible allocation remains. Split the realized total with a
  deterministic documented weight and preserve sum and non-negativity. Keep the proof class in the
  model and tests, but do not add approximation punctuation to the compact display. The result is
  not claimed to be marginal contribution or ground truth.
- **Unknown:** the ledger cannot preserve its invariants or lacks a stable candidate set. Do not
  fabricate a per-source realized value.

`CardStats.BurnAdded`, `PoisonAdded`, and `RegenAdded` validate only the apply-side ledger. They do
not validate realized attribution. Realized totals and confidence must use new model fields; they
must not reuse `CombatImpactGroup.ObservedValue`.

## Boundaries

- Exact realized adjustment totals by target and affected pool: **verified** —
  `CombatSimPlayerHealthAdjustment` carries damage type, changed pool, amount, crit, and reduction.
- Effect executions expose candidate source, trigger source, target, and action type: **verified**.
- Native tick events preserve applying source: **failed** — replay health adjustments and the older
  battle-service tick constructors omit source identity.
- Damage-reduction modifier state/provenance: **partially observable** — replay player updates can
  carry `FlatDamageReduction` and `PercentDamageReduction`, but the latest corpus frequently omits
  the Player-side value even when periodic adjustments exhibit a stable 20%/25% reduction. Effect
  execution messages also omit the modified attribute and value, so modifier provenance cannot be
  reconstructed completely from `CombatSimEventEffectExecuted`.
- Per-card authoritative apply totals exist: **verified** — `CombatSim.CardStats` carries BurnAdded,
  PoisonAdded, and RegenAdded.
- Replay spawn data can describe normal item/skill instances without live Unity entities:
  **verified in schema** — `GameSimEventCardSpawned` carries instance, template, type, and owner.
- Existing local replay corpus: **verified present** — 53 payloads; current payload version is not a
  game-build stamp, so these are exploratory rather than cross-version golden fixtures.
- Analyzer/server replay corpus: **verified available** — the local Analyzer cache holds 120,137
  raw run bundles (46 GB), while its DuckDB projects 324,723 runs / 1,320,082 battles. The cache is
  stale at July 25 for decoded bundles, so a separate read-only production sample fetched the 100
  latest August 4 bundles into a temporary directory and decoded 1,110 battles with zero invalid
  payloads.
- Saved payloads deserialize through a BazaarGameShared-only host: **verified** — 53/53 payloads
  loaded; the host's dependency closure and runtime assembly scan contain neither Assembly-CSharp
  nor UnityEngine.
- The corpus contains the scenario floor needed to assess Shield/Health, overheal, unresolved
  source, and multi-source epochs: **partially verified** — it contains all three periodic effects,
  Burn with/without Shield, Poison while Shield is present, Regen reaching max Health, and
  multi-source battles. Removal/reset classification still needs to distinguish native decay from
  cleanse and unexplained mutation.
- Exact/constrained attribution reaches the observable uniqueness ceiling: **verified** — the
  latest sample has 19,990 Exact allocation decisions and zero Constrained decisions. Native ticks
  erase source identity, so every concurrently owned mixed stack admits multiple allocations and
  remains Proportional; no additional complete constraint exists on the retained surfaces.
- Proportional fallback is stable and useful without misleading the UI: **verified** — 61,580
  latest-sample allocation decisions conserve their measured ledgers, repeated reports are
  byte-identical, proof remains internal, and the compact UI renders no confidence punctuation.
- Existing-row bilingual UI remains readable across compact and dense tooltips: **unknown**; user
  owns closure in the real game.
- Terminal event shape: **verified on the latest 1,110-battle sample** — each battle has exactly one
  death event, no combatant has a second death event, and no frame follows the first death event.
  `CombatSimPlayerUpdate.IsPlayerDead` is not a reliable substitute in this sample.

## Acceptance gates

- **Deserialization:** P0 loads spawn and combat messages from at least one real payload through a
  pure BazaarGameShared host. The evaluator's assembly closure contains no Assembly-CSharp or
  UnityEngine reference.
- **Non-vacuity:** a corpus run fails if no relevant adjustment/application exists, if zero sources
  resolve across the corpus, or if any `CardStats` instance with periodic apply totals lacks a card
  identity record without being reported explicitly.
- **Conservation:** for every combatant × status × epoch, attributed plus Unknown Health impact
  equals the terminal-adjusted measurement, and attributed plus Unknown Shield consumption equals
  terminal-adjusted Burn Shield consumption. Raw adjustments independently partition into
  pre-terminal realized plus post-terminal/overkill excluded amounts. Exact equality, no tolerance,
  and no cross-ledger cancellation.
- **Coupled reconciliation:** measurement and ownership reconcile separately. Attributed plus
  Unknown equals every measured typed Burn/Poison Health and Burn Shield movement exactly; Regen
  equals ordered, capped realized healing. Formula diagnostics do not gate admission.
- **Poison bypass:** any Poison-attributed Shield consumption or Shield-dependent Poison formula is
  a hard invariant failure.
- **State reconciliation:** the status ledger reconciles frame-by-frame from previous value to
  current value. Any unexplained positive or negative state change contaminates the epoch and
  prevents Exact/Constrained classification.
- **Non-negative ledger:** no source balance, realized allocation, or residual becomes negative.
- **Uniqueness:** Exact/Constrained is emitted only when a second feasible allocation cannot be
  constructed. Candidate-count alone is insufficient.
- **Determinism:** two runs over the same sorted corpus produce byte-identical normalized reports;
  no dictionary/hash-set iteration order may affect allocation or display ordering.
- **Apply-side reconciliation:** normalized applications reconcile with per-card `CardStats` totals,
  with every mismatch surfaced by battle/source/status.
- **Fixture preservation:** checked-in deterministic fixtures cover single-source, ambiguous
  multi-source, removal/reset, unresolved source, Burn with and without Shield, Poison while Shield
  exists, Regen clamp, same-frame ordering, and modifier-adjusted typed amounts; proof labels and
  values are golden-tested. A genuinely unique multi-source fixture cannot be constructed from the
  retained tick surface because source identity is absent; that class reports Constrained = 0.
- **Corpus composition:** before coverage is used as a shipping argument, P1 must report whether the
  53-payload corpus contains Burn with partial/full/no Shield, Poison while Shield is present,
  Poison-to-Health, Regen clamp, removal/reset, unresolved-source, and multi-source cases. Missing
  classes remain explicit evidence gaps.
- **No exact regression:** later model versions must preserve every previously golden Exact case and
  may change one only by an explicit fixture/model-version migration.
- **Coverage:** report realized amounts and frame × status × source allocation-decision counts for
  Exact, Constrained, and Proportional, plus measured Unknown gap-frame counts and amounts. Never
  fold Proportional into correctness coverage. This finer-grained observer supersedes final-row
  counts, which conservatively taint an entire row after any Proportional allocation.
- **Presentation:** proof class is not rendered as punctuation; Unknown contributes no attributed
  value. Final geometry/readability is user-accepted.

## Evidence

- Realized total schema — decompiled current game types — Codex — **verified**.
- Apply source/target schema — decompiled current game types — Codex — **verified**.
- Missing tick provenance — current replay schema plus legacy tick constructors — Codex —
  **verified**.
- Headless entity identity from spawn events — current GameSim schema — Codex — **verified for
  normal spawns; transform/runtime completeness pending corpus evidence**.
- Existing projector unsuitable as the corpus normalizer — projector requires live-derived entity
  metadata and silently skips unresolved source/target/kind — CC red-team, independently verified.
- Candidate-count sole-source rule is unsound — unresolved/null/non-item sources and untracked
  attribute mutations can disappear from the candidate set — CC red-team, independently verified.
- Pool semantics must be empirical/versioned — adjustment schema distinguishes Health/Joy/Shield,
  but the current game uses Health/Shield and retains Joy only as legacy schema;
  legacy producer shows materially different Burn/Poison/Regen behavior but is not the live server —
  CC red-team, independently verified.
- Coupled accounting requirement — user decision — **accepted**: status-stock, Health-impact, and
  Shield ledgers must each reconcile before high-confidence attribution.
- Shield/Burn/Poison relationship — user decision — **accepted**: Shield affects Burn, never Poison,
  and Shield consumed must not be treated as a 1:1 proxy for Burn damage prevented.
- “Burn decrements by one” and community keyword-page Burn mechanics — **rejected by user as stale**;
  replaced by the corpus-verified current formula below.
- `ObservedValue` cannot store realized impact — it is compared to authoritative applied totals —
  CC red-team, independently verified.
- Pure replay boundary — retained corpus acceptance harness — **verified**: 53 battles, 18,414
  frames, 2,513 typed periodic adjustments, 0 invalid payloads, no forbidden runtime assemblies.
- P1 pool inventory — retained corpus acceptance harness — **verified**: Burn/Health loss 618
  adjustments (51,171), Burn/Shield loss 531 (95,986), Poison/Health loss 472 (13,452),
  Regen/Health gain 892 (117,786), and zero Poison/Shield adjustments.
- P1 scenario inventory — retained corpus acceptance harness — **verified**: 1,111 Burn ticks with
  opening Burn+Shield, 262 Poison-to-Health ticks while Shield was present, 52 Regen frames
  reaching max Health, and multi-source battle counts of Burn 27 / Poison 15 / Regen 33.
- Current Burn Shield bridge — executable corpus gate — **verified on 1,111/1,111
  observable ticks**:
  `ShieldConsumed = min(ShieldBefore, floor(BurnBefore / 2))` and
  `HealthDamage = max(0, BurnBefore - 2 * ShieldBefore)`.
- Expanded current Burn Shield bridge — latest 100 production bundles / 1,110 battles — **failed as
  a universal rule**: 25,510/26,073 observable ticks matched and 563 mismatched. The earlier 53-battle
  sample was insufficient to prove the bridge complete.
- Expanded Burn counterexample classification — **verified**: 454/563 bridge mismatches carry
  `IsDamageReduced` (240 Health-only, 191 Shield-only, 23 both); 109 do not. A formula mismatch is
  therefore not evidence that the typed Burn adjustment is invalid, and the bridge formula must not
  gate whether an observed pool movement is booked.
- Explicit damage-reduction correlation — latest 100 production bundles / 1,110 battles —
  **verified**: for every Burn mismatch with an observable 20%/25%
  `PercentDamageReduction` (89/89), applying the percentage separately to the pre-reduction Health
  and Shield adjustments and flooring each result reproduces the typed amounts. The same rule
  reproduces all 54/54 Poison ticks with observable 20%/25% reduction. Poison's
  `IsDamageReduced` flag remains false in these cases, so that flag is not a complete modifier
  signal.
- Hidden Player-side reduction — latest production sample — **inferred, not source-verifiable**:
  the remaining mismatches concentrate on Player frames whose reduction attributes are absent, but
  their repeated 75%/80% ratios match the explicit opponent-side behavior. The typed pool movement
  is still authoritative; the missing modifier value/source prevents a complete baseline-damage or
  prevented-damage reconstruction.
- Current clean Burn decay — executable corpus gate — **verified on 1,019/1,019 eligible ticks**
  (92 same-frame apply/heal cases excluded from formula admission):
  `decay = max(1, floor(BurnBefore * 0.03))`.
- Poison tick — executable corpus gate — **verified on 472/472 ticks**: use opening Poison, except
  frame zero applies Poison before its immediate first tick. Poison never touches Shield.
- Expanded current Poison tick — latest 100 production bundles / 1,110 battles — **failed as a
  universal rule**: 10,362/10,503 ticks matched and 141 mismatched; Poison still produced zero Shield
  adjustments.
- Regen tick — executable corpus gate — **verified on 820/820 source-status ticks**: attempted Regen
  equals opening HealthRegen. The 72 skipped `EDamageType.Regen` adjustments are all on the player,
  recur at the native 20-frame tick cadence, have zero status `HealthRegen`, and have no
  `PlayerRegenApply`; they are player intrinsic/base Regen rather than an item/skill status source.
  Their 536 attempted / 523 realized healing is excluded from both attribution and the 48,790
  source-status denominator.
- Expanded current Regen tick — latest 100 production bundles / 1,110 battles — **failed as a
  universal rule**: 11,616/11,627 evaluated source-status ticks matched and 11 mismatched. The new
  sample also contains substantially more status-absent native Regen.
- Typed adjustment pool semantics — current schema plus decompiled native UI consumers —
  **verified**: the game routes signed `CombatSimPlayerHealthAdjustment.Amount` directly to the
  `AttributeChanged` Health/Shield pool and preserves `DamageType`. Negative Burn/Poison Health and
  Burn Shield adjustments are authoritative applied pool movements even when a candidate formula
  fails. Positive Regen remains special because accepted healing may be capped by current MaxHealth,
  so realized Regen still requires the ordered Health ledger.
- Ordered Health ledger — executable corpus gate — **verified on 3,169/3,171 relevant frames**:
  apply MaxHealth delta first, replay typed Health adjustments in list order, never clamp losses,
  and cap each positive adjustment at current MaxHealth. The two unreconciled frames remain Unknown.
- Shield ledger — executable corpus gate — **verified on 3,171/3,171 relevant frames**.
- Model conservation — retained corpus acceptance harness — **verified**: attributed totals never
  exceed reconciled realized totals; Poison/Regen Shield attribution is hard-failed.
- Model determinism — repeated inventory/model build and a second process — **verified**:
  byte-identical normalized reports.
- Apply-side CardStats reconciliation — retained corpus inventory — **verified with surfaced
  residuals**: Poison reconciles exactly; inferred Regen is short 3 and Burn short 34 across the
  corpus, so those affected ownership epochs cannot be promoted to Exact.
- Focused rule preservation — PostCombatImpact tests — **verified**: single-source Burn bridge,
  first-tick Poison ordering, mixed-source proportional ownership, Regen clamp, same-frame
  damage/regen accounting, Unknown initial status, projector attachment, and bilingual formatting.
- Versioned model coverage — 53-payload corpus — **verified**: Burn Health 51,131/51,171 (99.92%),
  Burn Shield 95,986/95,986 (100%), Poison Health 13,450/13,452 (99.99%), and Regen realized Health
  46,197/48,790 (94.69%). V2 retrospectively assigns 4,950 opening-state realized healing only
  across item/skill sources independently observed applying Regen elsewhere in the completed combat;
  every such assignment remains Proportional in the internal model.
- Expanded versioned-model coverage — latest 100 production bundles / 1,110 battles — **failed the
  existing acceptance gate**: Burn Health 1,243,804/1,310,646 (94.90%), Burn Shield
  384,831/429,593 (89.58%), Poison Health 1,017,544/1,069,514 (95.14%), and Regen realized Health
  698,112/757,432 (92.17%). The model remains conservative, but the 53-battle near-perfect
  Burn/Poison percentages were not representative.
- V3 measurement/ownership split — latest 100 production bundles / 1,110 battles — **verified**:
  Burn Health 1,302,284/1,310,646 (99.36%), Burn Shield 425,930/429,593 (99.15%), Poison Health
  1,060,375/1,069,514 (99.15%), and Regen realized Health 714,653/758,881 (94.17%). Attributed plus
  Unknown equals the measured amount exactly for all four ledgers; Poison Shield remains zero.
- V3 proof-class non-regression — latest 100 production bundles — **verified**: Exact results rose
  from 1,328 in V2 to 1,365 in V3; Exact Health rose from 895,762 to 940,966 and Exact Shield from
  123,544 to 130,242. Proportional ownership remains explicit in the model and is not promoted to
  Exact merely because the measured pool amount is exact.
- V3 allocation-decision proof coverage — latest 100 production bundles — **verified**: 81,570
  frame × status × source decisions comprise 19,990 Exact, 0 Constrained, and 61,580 Proportional.
  Exact/Proportional Health amounts are Burn 375,365/926,919, Poison 556,825/503,550, and Regen
  58,366/656,287; Burn Shield is 142,714/283,216. Measured Unknown spans 3,188 gap frames: Burn 210,
  Poison 70, and Regen 2,908. Every proof cross-tab reconciles to the attributed total.
- Constrained observability ceiling — model structure plus latest corpus — **verified**: a tick
  exposes only aggregate status and pool movement. Once two resolved owners coexist, transferring
  one unit of realized impact between them yields a second feasible allocation; CardStats validates
  apply totals but adds no tick-source equation. Therefore zero Constrained decisions is the
  evidence-backed maximum on the retained surfaces, not an omitted proof category.
- Static Regen candidate fallback — latest 100 production bundles — **verified**: among status-source
  gaps, 15,694 healing had one or more item/skill entities with an explicit positive
  `RegenApplyAmount`; 40,115 had no event, CardStats identity, or static attribute candidate and
  remains Unknown. Intrinsic/status-absent Regen had zero such candidates in this sample and 56,678
  realized healing is excluded from the item/skill denominator.
- V3 determinism — latest 100 production bundles — **verified across two processes**: normalized
  reports are byte-identical with SHA-256
  `bcf8187338b7f397d685f2dfb9455922275fae5589a9e0db5cef883437dd2653`.
- V3 local replay gate — 59 current V5 replays / 20,818 frames — **verified**: Burn Health
  57,493/57,507 (99.98%), Burn Shield 97,772/98,039 (99.73%), Poison Health 15,725/15,725 (100%),
  and Regen realized Health 55,573/56,288 (98.73%); all conservation gates passed.
- V3 refreshed local replay gate — 62 current V5 replays / 22,016 frames — **verified**: Burn Health
  82,460/82,474 (99.98%), Burn Shield 120,530/120,797 (99.78%), Poison Health 15,851/15,851 (100%),
  and Regen realized Health 103,147/103,862 (99.31%); all conservation and proof-cross-tab gates
  passed.
- V4 terminal Regen correction — latest 100 production bundles / 1,110 battles — **verified**:
  263 death-event frames contain 15,869 capped Regen movement; only 1,387 occurs before the lethal
  threshold and 14,482 occurs after it. V4 excludes the latter while retaining same-frame rescue
  when no death event exists. Regen coverage is 702,230/745,285 (94.22%), with 43,055 remaining
  Unknown; the main provenance-free source-status gap is 38,942.
- V5 terminal periodic measurement — latest 100 production bundles / 1,110 battles — **verified**:
  death-frame Burn Health raw/effective/overkill is 145,563/62,201/83,362 and Poison is
  127,577/68,298/59,279. Burn Shield raw is 12,773; treating each adjustment independently would
  retain only 118, while the evidence-backed atomic Burn group retains 11,928 and excludes 845
  belonging to Burn groups that began after an earlier lethal effect. No battle has a later frame or
  a second death event.
- V5 order sensitivity and atomic-group admission — **verified**: reversing the full adjustment
  list changes death-frame Burn Health from 62,201 to 70,438 and Burn Shield from 118 to 12,632, so
  global reversal is not a valid rule. Across all 62,266 periodic frames the corpus has no frame
  with more than two negative Burn pool adjustments; observed Burn Health/Shield pairs are
  contiguous and represent one tick. The retained harness independently gates raw minus
  terminal-excluded equals the V5 measured total.
- V5 expanded coverage — latest production sample — **verified**: Burn Health
  1,219,514/1,227,284 (99.37%), Burn Shield 425,085/428,748 (99.15%), Poison Health
  1,001,417/1,010,235 (99.13%), and Regen 702,230/745,285 (94.22%). The lower Burn/Poison
  denominators are intentional removal of overkill, not lost attribution; attributed plus Unknown
  still equals every terminal-adjusted ledger exactly.
- V5 refreshed local replay gate — 62 current V5 replays / 22,016 frames — **verified**: terminal
  overkill is Burn 19,711 and Poison 1; atomic post-earlier-lethal Burn Shield exclusion is 1. Final
  coverage is Burn Health 62,749/62,763 (99.98%), Burn Shield 120,529/120,796 (99.78%), Poison
  15,850/15,850 (100%), and Regen 97,579/98,294 (99.27%).
- V5 determinism, repository validation, and local replacement — **verified**: the expanded report
  is byte-identical across processes with SHA-256
  `faf5b7fa464c16062ee973112296dbb4492bf3047af3aa65e9ec624d6e4a62e1`; 149/149 focused tests,
  the complete repository suite, both expanded/local corpus gates, format check, and final Debug
  build passed. All four installed plugin DLLs are byte-identical to the build outputs; the main
  `BazaarPlusPlus.dll` SHA-256 is
  `50f86d9c3f23396b62405c91dd4d79c3c4b4befce52977e4df9a970c137b80b5`.
- V5 reproducibility archive — **verified**: the retained 76 KB evidence snapshot records the exact
  100 bundle names/SHA-256 values, 1,110 sorted battle IDs, schemas, rule/terminal diagnostics,
  attribution/Unknown totals, and the 70 MB full-report hash. The corresponding 39 MB raw sample is
  pinned outside Git under the Analyzer cache. Re-running the harness from that durable copy
  reproduces the evidence snapshot byte-for-byte and the full report hash
  `faf5b7fa464c16062ee973112296dbb4492bf3047af3aa65e9ec624d6e4a62e1`.
- Remaining Regen boundary — 53-payload corpus — **verified**: 2,593 source-status realized healing
  has no CombatSim apply event, periodic CardStats identity, or existing display source row. It stays
  Unknown; attaching it would require synthesizing a speculative source. Separately, 523 intrinsic
  player Regen remains outside the source-status denominator and 7 healing is excluded by an
  unreconciled Health-ledger frame.
- Opening GameSim periodic provenance — 53-payload corpus — **verified absent**: opening GameSim data
  contains zero Burn/Poison/Regen apply actions, so passing it into the runtime attribution model
  would not close the remaining source gap.
- Final stacked same-row presentation — user in The Bazaar Recap — **pending**.
- V3 repository validation and local replacement — **verified**: 137/137 focused tests, the full
  repository suite, both corpus gates, format check, and final Debug build passed with zero build
  warnings/errors. All four installed plugin DLLs are byte-identical to their build outputs; the
  main `BazaarPlusPlus.dll` SHA-256 is
  `6debd87580b0dadccce7e7d5b5bcaec26527233fa401100ca376707bf41234ea`.
- Completion-audit validation and local replacement — **verified**: added explicit reset-epoch and
  unresolved-source fixtures; 139/139 focused tests and the complete repository suite passed.
  The expanded proof report is byte-identical across processes with SHA-256
  `be38dcfc7bba16f822717d523490bd79fa7196c6c07a7d8d37b7ea59b91b0009`. Format check and the final
  Debug build passed with zero warnings/errors; all four installed plugin DLLs match build output,
  with main `BazaarPlusPlus.dll` SHA-256
  `e9254f25beac4c4d1d8ec5451c139ba4ec53e2456f00795955da1236ca72da5b`.

## Implementation boundaries

- The corpus harness may be detailed because it is offline evidence infrastructure. The shipped
  Combat Impact model must remain a small pure bounded-pass rule set over one `CombatSim`; do not
  ship corpus I/O, rule search, a general constraint solver, or replay-report machinery in the
  runtime path. Prefer the simplest rule set that clears the evidence gates; do not optimize
  coverage by making the production model opaque.
- Do not drive the corpus through `CombatImpactProjector`; attribution starts from a pure normalized
  replay model, then Combat Impact consumes its result.
- P0/P1 use `CombatReplayPayloadStore` payload decoding plus direct
  `MessagePackSerializer.Deserialize<NetMessageGameSim/NetMessageCombatSim>` with
  `MessagePackConfig.Options`; do not call `CombatReplayLoader`, whose return type is
  `CombatSequenceMessages` from Assembly-CSharp.
- Keep the attribution engine pure and feature-owned. Reuse a BazaarGameShared-only replay decoder
  seam in the retained acceptance harness; do not add a temporary standalone diagnostic path.
- Add a future replay capture fingerprint for game/schema build. Existing unstamped payloads remain
  exploratory and cannot become cross-version goldens without anonymized, version-pinned fixtures.

## Diffusion sequence

1. **P0 / boundary:** deserialize one real payload's spawn and combat messages in the pure host;
   prove dependency closure and fail loudly on vacuous input.
2. **P1 / observation inventory:** scan the sorted corpus and report raw status adjustments by pool,
   state transitions, apply/remove/modify actions, source resolution, CardStats reconciliation, and
   scenario composition. No attribution rule yet.
3. **M1 / strict proof:** implement epochs and uniqueness proof. Emit only Exact/Constrained or
   Unknown; add golden fixtures and corpus invariants.
4. **M2 / constrained iteration:** add one evidence-backed constraint at a time. Accept it only if
   Exact+Constrained coverage rises and all earlier gates remain green.
5. **M3 / approximation:** if ambiguity remains, add deterministic Proportional allocation and keep
   its proof class in the model; never promote it into correctness coverage.
6. **Integration:** attach separate realized/confidence fields to existing Burn/Poison/Regen groups,
   render on the same row, build and replace the local Debug DLL, then wait for user visual closure.

## Fuses

- Substantial rounds without new goal-level evidence: 0 / 3.
- Repeated unchanged observation from the same probe: 0 / 2. The user supplied a new terminal-order
  hypothesis; V4/V5 produced material corpus evidence and changed the realized-impact definition.
- Model iteration stop: two consecutive accepted-or-rejected constraint rounds without increased
  Exact+Constrained realized-amount coverage.
- Corpus stop: if P1 has fewer than two required scenario classes or fewer than ten relevant
  battles, do not infer model quality from percentages; obtain more corpus or restrict the claim.
- Proof stop: if unexplained state transitions prevent a complete ledger, keep those epochs Unknown
  rather than weakening Exact/Constrained.

## Next decision

`periodic-impact-v6` is implemented and corpus-validated. The measurement ledger books authoritative
typed post-modifier adjustments, then applies a small terminal rule: on the one death-event frame,
ordered Health loss stops at zero, post-terminal periodic groups and Regen are excluded, and a
contiguous Burn Health/Shield pair remains one atomic tick. Raw, effective, overkill, reversed-order,
and atomic-group diagnostics prevent the production rule from validating only against its own copy.
The ownership ledger now accepts Regen fallback candidates only from observed apply executions or
positive `CardStats.RegenAdded`; a static `RegenApplyAmount` proves capability, not contribution.
Stable spawn-positive Regen with no transition or apply evidence remains in total healing
conservation but is excluded from source-attribution coverage and never appears on an item/skill row.

On the pinned 1,110-battle sample, 54,230 reconciled healing is stable spawn baseline; V6 attributes
zero of it. Source-attributable Regen coverage is 686,099 / 691,055 = 99.28%, leaving 4,956 Unknown.
The remaining candidates concern source precision rather than measured totals: initial mixed state,
same-frame decay plus apply, whole-combat weights for ambiguous co-appliers, and absent MaxHealth
evidence. They must remain Proportional/Unknown unless a corpus round supplies an independent
per-source constraint; do not turn them into Exact by formula. The next observer is the user's
in-game numeric/visual smoke test. Do not launch the game automatically, and do not commit, merge,
or update a PR without explicit instruction.

## Iteration log

- 2026-08-04: Goal created. Evidence ordering started at the real saved-replay boundary; no model or
  UI code was added.
- 2026-08-04: Read-only CC red-team completed. Independently verified four design blockers: the live
  projector is not a valid headless normalizer, sole-source-by-absence is unsound, realized pool
  semantics need an explicit versioned policy, and realized values require separate model fields.
  Revised the sequence to P0/P1 before attribution and strengthened Exact to a positive uniqueness
  proof over a reconciled state epoch. No implementation code added.
- 2026-08-04: Paused after three consecutive goal turns without the required post-review user
  confirmation. No implementation code was added. Resume at P0 once the user confirms or amends the
  candidate pool policy and proof contract.
- 2026-08-04: User required accounting-style reconciliation against actual damage and Shield
  consumption. Added coupled status-stock and realized-pool ledgers plus full frame pool balancing.
  Separated current-facing documented rules from live-server facts; P1 must verify the mechanics
  observed by this replay schema before M1 hard-codes them. No implementation code added.
- 2026-08-04: User corrected two stale assumptions: current gameplay has no Joy attribute, and Burn
  no longer follows the old decrement-one rule. Removed Joy from the accounting policy and removed
  all pre-P1 Burn/Poison/Regen mechanics as axioms. Added a hard rule-admission gate: complete corpus
  reconciliation plus counterexample scan. No implementation code added.
- 2026-08-04: User confirmed the revised accounting/proof contract and resumed the goal. Added a
  retained pure corpus harness. P0 loaded all 53 saved payloads without Assembly-CSharp/UnityEngine;
  P1 inventoried 18,414 frames and 2,513 periodic adjustments with zero invalid payloads.
- 2026-08-04: First P1 counterexample scan found compact current-version mechanics: Poison bypasses
  Shield; clean Burn decays by `max(1, floor(3%))`; two Shield points prevent one Burn Health damage;
  and uncluttered Poison/Regen ticks use the opening status value. Burn bridge and clean decay had
  zero counterexamples in 717 and 944 observable ticks respectively. Same-frame ordering and
  multi-source application ambiguity remain active evidence questions.
- 2026-08-04: User clarified that Shield affects Burn but not Poison, and that Shield consumption is
  not numerically interchangeable with Burn damage prevented. Replaced the 1:1 two-ledger model
  with separate status-stock, Health-impact, and Shield ledgers connected by a versioned Burn bridge
  rule. No implementation code added.
- 2026-08-04: Explained that P0/P1 discover version-sensitive combat formulas while the user fixes
  only stable accounting semantics. Paused after three consecutive resumed-goal turns without the
  required final design confirmation. Resume directly at P0 after confirmation.
- 2026-08-04: User confirmed the discovery boundary and required the final Combat Impact integration
  to be a simple effective rule set. Resumed at P0 and added a runtime-complexity boundary: offline
  evidence may be rich, shipped evaluation remains pure and single-pass.
- 2026-08-04: Completed executable P1 gates. Current rules reconciled all 1,111 Burn bridge ticks,
  all 1,019 eligible Burn decays, all 472 Poison ticks, all 820 source-status Regen ticks, all 3,171
  Shield ledgers, and 3,169/3,171 Health ledgers. Two unexplained Health frames are explicitly
  excluded instead of weakening conservation.
- 2026-08-04: Implemented `periodic-impact-v1`: ordered pool accounting plus a small ownership
  ledger. A unique source stays Exact; mixed stacks use deterministic proportional allocation and
  `≈`; unresolved stacks remain Unknown. Attached realized damage/healing and Burn Shield
  consumption to the existing row without a new section. Corpus coverage is 99.92% Burn Health,
  100% Burn Shield, 99.99% Poison Health, and 84.54% Regen Health.
- 2026-08-04: Focused PostCombatImpact tests and the complete repository test suite passed. The
  retained corpus harness also passed its non-vacuity, current-rule, Poison-bypass, conservation,
  assembly-closure, and determinism gates. Final in-game acceptance remains user-owned.
- 2026-08-04: Replaced the clipped inline realized-impact suffix with a dedicated compact metric
  annotation beneath the authoritative total. Ordinary group rows keep their existing layout;
  periodic rows reuse the same 44/48px header and a 0.62-scale, right-aligned secondary label.
  Focused tests passed 131/131, format check and Debug build passed, and the built/local plugin DLL
  SHA-256 matched (`b9d33afb7c8fccf34dca382e3e5e14fe0e039701b9db0d1a07a402d662db35f9`).
- 2026-08-04: Corrected the earlier interpretation of 72 Regen adjustments with zero status Regen.
  Corpus evidence identifies them as player intrinsic/base Regen: player-only, no apply event, and
  recurring every 20 frames. They total 536 attempted / 523 realized and were already excluded from
  the 48,790 source-status denominator, so they do not explain the 7,543 attribution gap. Confirmed
  that the model does consume both `PlayerRegenApply` source events and ordered `EDamageType.Regen`
  adjustments; the unresolved question is opening-state/event provenance, not missing tick input.
- 2026-08-04: Replaced the secondary realized-damage `dmg`/`伤害` text with the game's native
  `DamageAmount` icon while preserving the amount and approximation marker. Focused tests passed
  131/131, format check and Debug build passed, and the built/local plugin DLL SHA-256 matched
  (`aeebe5835d4b2f62ce9a39805a46b44cb456b3481bddab852c3e4b8ebabdee77`).
- 2026-08-04: Classified the full 7,543 source-status Regen gap. Opening GameSim contributes no
  periodic apply events. Added `periodic-impact-v2`: an initial Unknown Regen stack may be allocated
  retrospectively only across item/skill sources independently observed applying Regen in that
  combat, and is always marked `≈`. This recovered 4,950 realized healing and raised coverage from
  84.54% to 94.69%. The residual 2,593 has no event, CardStats identity, or existing source row and
  remains Unknown. The complete suite passed, including 133/133 focused PostCombatImpact tests; the
  retained corpus gates and 94% Regen regression floor passed. Format check and the final Debug
  build passed; built/local plugin DLL SHA-256 matched
  (`f77e82763945a59651d7b747b17098cc44c93ede096084efc98beb5e4519af21`).
- 2026-08-04: User superseded the compact presentation contract: keep Proportional proof in the
  model but omit `≈` punctuation. Removed Ellipsis from all Combat Impact-owned TMP labels. Replaced
  the fixed-width two-object periodic metric stack with one multiline TMP block whose preferred
  width may consume free header space and whose two lines auto-size together inside the row height.
  Burn Shield consumption now uses the native Shield icon. Focused tests passed 133/133, format
  check and Debug build passed, and the built/local plugin DLL SHA-256 matched
  (`cad29ba6cb8ecd368560cba795617fedbc6e51bb13a706111bb05dfa34829054`).
- 2026-08-04: User challenged whether the 53-battle corpus was representative and pointed to the
  Analyzer cache. Verified 120,137 locally cached raw bundles (46 GB) and 1,320,082 projected
  battles, but the local drain was stale. Used the existing read-only Analyzer credentials to fetch
  the latest 100 August 4 run bundles (41 MB) into a temporary directory; the retained harness
  decoded 1,110 battles / 339,649 frames with zero invalid payloads. This expanded sample falsified
  the assumption that only Regen had material gaps: model coverage was 94.90% Burn Health, 89.58%
  Burn Shield, 95.14% Poison Health, and 92.17% Regen Health. The strict corpus gate correctly failed;
  no runtime attribution rule was weakened or changed.
- 2026-08-04: Classified the expanded counterexamples. Most Burn bridge failures are explicitly
  damage-reduced, while the native UI still applies every typed adjustment directly to its named
  Health/Shield pool. Revised the next candidate to separate authoritative pool measurement from
  uncertain source ownership and to retain unexplained residual in an internal suspense account.
  This can make Burn/Poison/Burn-Shield pool totals exact without falsely claiming exact per-source
  attribution. No shipped runtime code or local plugin DLL was changed.
- 2026-08-04: User identified received-damage modifiers as a missing mechanism. Extended the
  retained corpus report with opening/closing flat and percentage damage-reduction state. Explicit
  20%/25% reduction explains 89/89 affected Burn bridge mismatches and 54/54 affected Poison tick
  mismatches after per-pool flooring. Player-side reduction state is often absent despite matching
  output ratios, and Poison does not set `IsDamageReduced`; therefore V3 must consume typed actual
  adjustments and treat modifier reconstruction only as diagnostics. No shipped runtime code or
  local plugin DLL was changed.
- 2026-08-04: Implemented `periodic-impact-v3`. Separated authoritative typed pool measurement from
  source ownership, reanchored frame state from transition evidence, accepted same-frame Regen
  decay/order through the Health ledger, and added a conservative static `RegenApplyAmount`
  candidate fallback. On the latest 1,110-battle sample, coverage reached 99.36% Burn Health,
  99.15% Burn Shield, 99.15% Poison Health, and 94.17% Regen with exact conservation; aggregate
  Exact evidence improved over V2. The remaining 40,115 Regen gap has no retained provenance and
  remains Unknown.
- 2026-08-04: Repeated the expanded corpus in a second process and obtained a byte-identical report.
  The 59-replay local corpus, 137 focused tests, full repository suite, format check, and final Debug
  build all passed. Replaced all four local plugin DLLs and verified byte equality with build
  outputs. Per user instruction, did not launch The Bazaar; final in-game acceptance remains pending.
- 2026-08-04: Completion audit found that final source-row proof totals understated Exact decisions
  and did not explicitly report the empty Constrained class. Added corpus-only allocation diagnostics
  at frame × status × source granularity plus proof-class count/amount conservation gates. The
  latest 1,110-battle sample contains 19,990 Exact, 0 Constrained, and 61,580 Proportional decisions.
  Zero Constrained is the observable ceiling: mixed native ticks carry no source equation, so every
  mixed allocation has a second feasible solution. This changes evidence reporting only, not
  user-visible attribution values.
- 2026-08-04: Added golden reset-epoch and unresolved-source fixtures and refreshed the local corpus
  to 62 battles. Focused tests passed 139/139, the full repository suite passed, the expanded report
  was byte-identical across processes, and the final Debug build/local replacement matched. The
  original epoch-count coverage wording was refined to allocation-decision counts because the
  shipped result intentionally aggregates epochs into one source row. Final in-game acceptance
  remains user-owned and pending.
- 2026-08-04: The same final perceptual probe remained pending for three consecutive goal turns.
  All discrete model, corpus, regression, build, installation, and source-information-loss criteria
  are already verified, while the user explicitly owns real-game acceptance and asked Codex not to
  launch the game. Triggered the repeated-observation fuse and marked the goal blocked pending a
  user screenshot or acceptance result; no further local changes were made.
- 2026-08-04: User hypothesized that Regen adjustments occurring after lethal damage in the same
  frame should not count. Decompiled playback confirms ordered adjustments precede the terminal
  death event, and the expanded corpus found 14,482 capped Regen movement after Health was already
  non-positive. Implemented V4 with death-event-aware ordered Regen and three terminal/rescue
  goldens; source coverage rose slightly because the denominator now excludes ineffective healing.
- 2026-08-04: Ran an independent Claude Opus review of the realized-impact invariant. Its strongest
  finding was the symmetric terminal gap for Burn/Poison and Burn Shield. Corpus instrumentation
  measured 83,362 Burn and 59,279 Poison overkill on 1,110 terminal frames, with no frames after
  death. Implemented V5 Health capping and sticky within-frame terminal state.
- 2026-08-04: Opus then challenged the causal meaning of cross-pool list order. Reversing the list
  materially changed Burn results, while real frames show at most one contiguous Burn Health/Shield
  pair. Revised V5 so that pair is one atomic tick: a lethal Burn retains its paired Shield
  consumption, but a Burn group beginning after an earlier lethal effect is skipped. Added raw,
  effective, overkill, reversed-order, and atomic-group corpus partitions plus ten terminal-order
  goldens. The expanded and local corpus gates pass with exact terminal-adjusted conservation.
- 2026-08-04: User required the analysis to remain reproducible after future algorithm changes.
  Added automatic compact evidence snapshots with source-artifact hashes, battle IDs, complete
  report hash, rule/terminal measurements, proof coverage, and Unknown totals. Pinned the exact 100
  production bundles in the Analyzer cache and proved a clean rerun from that durable copy produces
  byte-identical evidence and the same 70 MB report hash; future iterations no longer start from
  corpus discovery or rule reconstruction.
- 2026-08-04: Fable identified an observation asymmetry: the corpus harness sees spawn
  `HealthRegen`, while the shipped model sees only `CombatSim` transitions. A new partition proved
  all 38,942 measured `StatusStateAbsent` Unknown was opponent spawn-positive, had no Regen
  transition anywhere in combat, and ticked exactly at the opening value. The same stable-baseline
  context exposed 15,288 healing incorrectly assigned through static-only fallback candidates.
- 2026-08-04: Implemented `periodic-impact-v6`. Removed static `RegenApplyAmount` as attribution
  evidence, retained execution/CardStats fallbacks, and added a hard corpus gate that stable spawn
  baseline must attribute zero to item/skill rows. Raw Regen attribution fell by 16,131 and Unknown
  rose by the same amount, preserving exact total conservation. After excluding 54,230 independently
  reconciled baseline healing from the source-attribution denominator, honest coverage is 99.28%
  (686,099 / 691,055). The persisted V6 evidence and full report are byte-identical across two runs.
- 2026-08-04: Final V6 validation passed: 149 focused PostCombatImpact tests, the complete repository
  test suite, format check, the pinned 1,110-battle corpus with zero invalid payloads, and a zero-warning
  Debug build. The checked-in evidence SHA-256 is
  `9ca232d60e5a30ae218400619092af71aeb1e8ab915473e5b5a71fa6e3ee0997`; its complete report hash is
  `eb129d33bdb2a566c9bc87a8f8ce5c77d0bbda7ae5ace585f94808a3bcc3c012`. All four installed plugin
  DLLs match the build outputs; the main DLL SHA-256 is
  `603c92ac6cb1bbf7a70eff3a4df49032ea75e0c5877d090428b86ee0c8d3edda`. The game was not launched.
