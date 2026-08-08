# Combat Impact Accounting Ledger Design

Status: discussion draft; no implementation approval.

## Background

The immediate report was a Charge group whose displayed total was 25 while the visible trigger
sources summed to 17. PR #230 keeps mixed self-trigger occurrences and fixes that exact shape.

A broader audit of 197 local replays showed that this is not only a self-trigger bug. The current
UI places values with different dimensions beside each other as if they were one additive
breakdown:

- group `Count` is an effect execution/application count;
- trigger-source `Count` is a distinct-frame activation-batch count;
- target values can be exact, lower bounds, or partial observations;
- native `CardStats` values are authoritative totals for only a subset of metrics;
- periodic realized impact can be exact or proportionally attributed;
- unresolved targets and unattributed periodic pool movement are tracked in different places, and
  some residuals never reach the presentation model.

The user-visible requirement is therefore an accounting contract, not another isolated source
filter.

## Design Principle

Every displayed number must carry four facts:

1. **Dimension**: application, activation batch, amount, duration, percentage points, or target.
2. **Basis**: native authoritative total, explicit execution, exact adjustment, reconstructed
   transition, or estimate.
3. **Coverage**: exact, lower bound, partial, estimated, or unavailable.
4. **Provenance**: source, target, trigger source, and any unattributed residual.

Two values may be presented as an additive breakdown only when they have the same dimension and
basis. If a breakdown is incomplete, the residual must be represented explicitly. Different
dimensions must use different labels and must not share an unexplained `xN` presentation.

## Ledger Books

### 1. Activity Ledger

Records native item use counts and skill activation observations.

- Item `UseCount` comes from native `CardStats`.
- Skill activation batches are observations, not native totals.
- A distinct frame is currently the most stable replay boundary, but it is only an observed batch.
  It must not be named an exact trigger count without stronger producer evidence.

No equation is expected between native `UseCount`, activation batches, and effect applications.
Native metrics whose declared basis is `ApplicationCount` belong to the application-count control
ledger below and may be compared only when their metric semantics match the projected group.

### 2. Effect Application Ledger

Every projected `CombatImpactEvent` is one journal entry from which the product builds two filtered
views:

- a causing-source view for item and skill sources;
- a received-effect view for item and skill targets.

The views must be aggregated from the same entries rather than rebuilt independently, but they are
not a balanced debit/credit pair. Effects received by heroes or players are intentionally absent
from the received view, while authoritative source metrics can create a source group with no event.
No cross-view total equation is valid.

Required equations:

```text
source.EffectCount = sum(source.Groups.ApplicationCount)
group.ApplicationCount = sum(group.Targets.ApplicationCount) + group.UnresolvedTargetCount
received.EffectCount = sum(received.Groups.ApplicationCount)
incomingGroup.ApplicationCount
  = sum(incomingGroup.Sources.ApplicationCount)
  + incomingGroup.UnresolvedSourceCount
```

The two source-view equations and the received-group total already hold. The incoming-source
equation does not: sources missing from the entity map are currently filtered out and the model has
no `UnresolvedSourceCount`. The migration must add that residual before locking the equation in a
test.

### 3. Trigger Provenance Ledger

Trigger attribution needs application classifications plus a separate activation observation. Each
explicit execution-backed `CombatImpactEvent` must retain trigger provenance before aggregation:

```text
CombatImpactEvent
  RawDirectSourceId?
  TriggerSourceId?
  ActivitySourceResolution
    Direct | TriggerFallback
  TriggerScope
    AttributedExternal | AttributedSelf | AttributedViaTriggerFallback
    | NoTriggerEvidence | Unattributed | NotApplicable
  TriggerFrameIndex?
```

`NotApplicable` covers reconstructed or synthetic journal entries that did not come from an effect
execution. `NoTriggerEvidence` means the replay did not present a trigger source; it must not be
silently reclassified as either direct or missing until producer semantics prove that distinction.
`Unattributed` is reserved for a trigger reference that exists but cannot be resolved to a supported
activity entity. `AttributedViaTriggerFallback` records the existing `ResolveActivitySource` case in
which the direct source is not an activity entity and the trigger source becomes the journal entry's
effective source. It must not be mislabeled as self-trigger merely because the resolved `SourceId`
equals `TriggerSourceId`.

The aggregate needs two counters, not one:

```text
TriggerSourceImpact
  Entity
  ApplicationCount
  ObservedActivationBatchCount
```

- `ApplicationCount` counts effect journal entries attributed to that trigger source. This is the
  number shown in an additive `Triggered by` breakdown.
- `ObservedActivationBatchCount` counts distinct observed activation batches and is used only in a
  separately labeled activity summary.
- Self-trigger entries remain in the ledger even if a pure-self breakdown is hidden by presentation
  policy.

The group also stores:

```text
UnattributedTriggerApplicationCount
TriggerFallbackApplicationCount
NoTriggerEvidenceApplicationCount
NotApplicableTriggerApplicationCount
TriggerApplicationCoverage
ObservedUnattributedActivationBatchCount
TriggerActivationCoverage
```

Required equation:

```text
group.ApplicationCount
  = sum(group.TriggerSources.ApplicationCount)
  + group.UnattributedTriggerApplicationCount
  + group.TriggerFallbackApplicationCount
  + group.NoTriggerEvidenceApplicationCount
  + group.NotApplicableTriggerApplicationCount
```

This is a classification equation, not a claim that every effect application was triggered. A
`Triggered by` breakdown is rendered when at least one external trigger source is attributed or when
a self-attributed group has any non-zero remainder. In a mixed external/self group, both external
and self rows are visible. A partially attributed breakdown shows `attributed N / total M` and
discloses every non-zero remainder bucket, so the presentation-side identity is also reviewable:

```text
group.ApplicationCount
  = sum(visible external and self rows)
  + disclosed unattributed applications
  + disclosed trigger-fallback applications
  + disclosed no-evidence applications
  + disclosed not-applicable applications
```

Hide the breakdown only when the group is 100% self-attributed with no remainder. A self-attributed
group with any remainder renders its self row, `attributed N / total M`, and the remainder. Groups
with zero attributed trigger sources omit breakdown rows instead of showing a 100% unknown row.
Explicit unresolved or trigger-fallback evidence renders `breakdown unavailable`. A group made only
of `NoTriggerEvidence` or `NotApplicable` entries renders no trigger section: absence of a trigger
reference does not prove that the ordinary application belongs to a trigger-scoped population,
though the distinct ledger buckets remain available for diagnostics.

These are distinct presentation states:

- `HiddenSelfOnly`: attribution is complete but intentionally suppressed as redundant;
- `BreakdownUnavailable`: no source row could be attributed;
- `PartialBreakdown`: at least one source row and at least one remainder are visible.

Application attribution and activation-batch evidence must come from the same journal entries.
`TriggerFrameIndex` is distinct-counted per attributed trigger source to produce observed batches,
which guarantees that batch evidence is a subset of attributed applications. The parallel
`HashSet<TriggerOccurrence>` is removed. Pure-self suppression moves from projection to
presentation.

Bucket assignment happens at the event producer, not in aggregation. The source-resolution matrix
for every emitted execution-backed event is:

- direct activity + trigger activity: `AttributedSelf` when raw IDs match, otherwise
  `AttributedExternal`;
- direct non-activity/missing + trigger activity: `AttributedViaTriggerFallback`;
- direct activity + non-activity trigger reference: `Unattributed`;
- direct activity + no trigger reference: `NoTriggerEvidence`;
- no resolvable activity on either side: no journal entry, counted by a projection diagnostic rather
  than a group ledger;
- card-action-cost, aura-transition, F-minor, and other reconstructed/synthetic producers:
  `NotApplicable`.

The source-level skill summary remains an activation observation, not an application total:

```text
source.ObservedActivationBatchCount
  = distinct (TriggerSourceId, TriggerFrameIndex)
    across the source's attributed execution-backed journal entries
```

It replaces ambiguous source-level `TriggerCount`. Trigger-fallback entries participate because the
raw trigger source and frame are observed; in the group application book they remain in the distinct
fallback bucket. Group source rows remain application counts, and no additive equation is expected
between the source activation summary and group application counts.

There is no exact activation-count equation while frame is the only stable boundary. If the game
producer later exposes a stable activation identity, the activation ledger can be upgraded without
changing application accounting.

### 4. Application-Count Control Ledger

Application counts from journal entries and native authoritative application metrics are a separate
book from effect amounts:

```text
CombatImpactApplicationLedger
  ProjectedApplicationCount
  AuthoritativeApplicationCount?
  ComparableToAuthoritativeCount
  ApplicationResidual?
  ControlStatus
    Exact | PositiveResidual | OverObserved | NotComparable
```

When the native metric contract, group key, surface, and application semantics are comparable:

```text
AuthoritativeApplicationCount
  = ProjectedApplicationCount + ApplicationResidual
```

This is a reconciliation identity, not independent proof that the native and projected metrics have
matching semantics. Its falsifiable controls are: residual is non-negative when comparable,
residual is null when not comparable, `OverObserved` is set for a negative difference, and corpus
reports show the distribution of every control state. `UseCount` never enters this equation.

The currently available `HastedCardsCount`, `SlowedCardsCount`, and `FrozenCardsCount` producer
semantics are not present in the retained client source; their names describe affected cards, not
proven application entries. They remain visible as affected-card totals but default to
`NotComparable` until producer evidence establishes application semantics. Synthetic tests may opt
into comparability only when that premise is explicit.

### 5. Effect Amount Ledger

Do not collapse authoritative totals and observed details into one nullable integer.

```text
CombatImpactAmountLedger
  AuthoritativeTotal?
  ObservedAmount?
  ObservedCoverage
  ValuedApplicationCount
  TotalApplicationCount
  Unit
  WeakestValueBasis
  ComparableToAuthoritativeTotal
  ResidualAmount?
  ResidualCoverage
    Exact | UpperBound | Unknown
  ControlStatus
    Exact | PositiveResidual | OverObserved | NotComparable
```

An unknown event value cannot be converted into an unattributed amount. A numeric residual exists
only when an authoritative total and observed amount have the same metric semantics, unit, and
direction:

```text
ResidualAmount = AuthoritativeTotal - ObservedAmount
AuthoritativeTotal = ObservedAmount + ResidualAmount
```

`ComparableToAuthoritativeTotal` is false when units, native metric semantics, surface, or direction
differ, or when the group contains mixed positive and negative values. Residual coverage depends on
the observation:

- `Exact` observation: exact residual;
- `Partial` whose every known event has an exact value basis, whose known values are disjoint, and
  whose sole gap is missing values: exact aggregate residual, but no invented per-event allocation;
- `LowerBound`: at most `AuthoritativeTotal - ObservedAmount`, never a bare exact residual;
- any partial set containing `None`, `NetFrameDelta`, mixed units, or another non-exact basis: unknown
  residual regardless of the `Partial` label.

A negative difference is `OverObserved`, not a presentable residual. These are reconciliation
controls, not independent proof that the native total describes the same population.

Display rules:

- authoritative total: bare total;
- exact observation: bare observed value;
- configured/nominal observation: show the bare value; retain `Estimated` internally so it cannot
  create an exact residual;
- lower bound: `at least N` / `至少 N`;
- partial observation: `N recorded (m/n)` / `已记录 N（m/n）`;
- unavailable amount: show the application count only;
- exact comparable residual: render an explicit `unattributed N` disclosure;
- upper-bound residual: render `at most N unattributed` / `至多 N 未归因`;
- over-observed control: render an accounting warning in diagnostics and do not present a misleading
  residual.

Target details inherit their own coverage. Group-level divergence alone is insufficient to decide
whether a target value is qualified.

Across trigger, target/source, amount, and periodic ledgers, a breakdown with zero attributed parts
renders the authoritative/group total plus `breakdown unavailable`; it does not render a standalone
`unattributed total` row. A non-zero attributed subset renders its residuals so the displayed
breakdown remains reviewable.

### 6. Periodic Realization Ledger

Stack applications and realized pool movement are different ledgers. `BurnAdded` is not Burn damage,
and neither may be relabeled as the other.

`PeriodicEffectAttribution.Project` should return a report rather than only source allocations:

```text
PeriodicAttributionReport
  MeasuredTotalsByCombatantKindAndPool
  SourceAllocations
    Combatant
    Kind
    SourceId
    HealthAmount
    ShieldAmount
    Proof
  Residuals
    Combatant
    Kind
    Pool
    Amount
    Reasons
```

Required equations for each combatant, periodic kind, and pool:

```text
MeasuredPoolMovement
  = sum(SourceAllocations for the same combatant, kind, and pool across all proof classes)
  + Residual.Unattributed
```

Presentation rules:

- exact allocation: bare number;
- proportional allocation: show the bare value; retain `Proportional` internally for controls and
  diagnostics;
- every retained proof class has an explicit presentation and accounting policy;
- residuals are never assigned to a source without evidence;
- a source tooltip may disclose that the combat contains unattributed impact, but must not imply the
  residual belongs to the hovered source.

The current producer emits only `Exact` and `Proportional`; all `Constrained` emission searches and
both v5/v6 committed corpus snapshots show zero constrained results. The implementation removes the
dead `Constrained` enum member, bumps the periodic model version, and regenerates evidence rather
than carrying an undefined proof bucket forward. This explicitly supersedes the unimplemented M1
`Exact/Constrained/Unknown` taxonomy, whose surviving rationale now lives in the model-invariants
header of `src/BazaarPlusPlus/Game/PostCombatImpact/Data/PeriodicEffectAttribution.cs`;
consolidation must not leave both decisions active.

Residuals are combat-level facts keyed by combatant, periodic kind, and pool. They belong on
`CombatImpactReport`, not on any source group. Source-group periodic totals are derived rollups over
the report's combatant-keyed allocations; the detailed allocation book retains combatant identity so
its equations remain verifiable even when one source affects both sides.

### 7. Critical Subledger

Critical metadata stays attached to the originating application entry.

Required equations and guards:

```text
0 <= CriticalApplicationCount <= ApplicationCount
CriticalObservedAmount <= ObservedAmount
```

The amount inequality applies only to non-negative metrics with comparable units and semantics.
Recovered critical counts or amounts must carry reconstructed coverage rather than appearing exact.

## Candidate Approaches

### A. Relabel Existing UI Only

Rename trigger rows to `trigger batches` and add generic partial markers.

Rejected as the final solution because the model still cannot prove application-level conservation
or carry residuals. Useful only as an emergency mitigation.

### B. Add Ledger Fields to the Existing Projection Pipeline

Keep `CombatImpactEvent` as the journal entry and extend the aggregate models with separate
application, activation-batch, observed amount, authoritative amount, and residual fields.

Recommended. The current single event list is a shared journal for the source and received views,
but each view has its own filters and residuals. Extending that journal addresses the per-view
accounting contracts without inventing an invalid cross-view balance or replacing unrelated
projection behavior.

### C. Replace Projection with a New Event-Sourced Subsystem

Introduce stable journal-entry identities and rebuild every metric from a new event store.

Deferred. It may eventually improve diagnostics, but the replay producer does not currently expose a
stable activation identity, and a full replacement would add risk without making that missing fact
available.

## Migration Checklist

### Phase 1: Contract Tests

- [x] Add invariant helpers for all equations above.
- [x] Add synthetic cases for mixed self/external triggers, pure self, missing trigger source,
      non-activity trigger source, direct-source fallback to trigger, multi-target fan-out, and two
      same-target effects in one frame.
- [x] Cover the full direct-source-resolvability by trigger-source-resolvability matrix and assert
      that every emitted execution event has exactly one trigger scope.
- [x] Cover `HiddenSelfOnly`, `BreakdownUnavailable`, and `PartialBreakdown` independently.
- [x] Add amount cases for exact, lower-bound, partial, authoritative residual, and unknown amount.
- [x] Add periodic cases for exact, proportional, and residual allocation.
- [x] Compute raw residual differences in invariant helpers and fail on negative values; do not rely
      on the production `Math.Max(0, ...)` target clamp.
- [x] Assert only per-view equations; explicitly forbid a caused-vs-received grand-total invariant.
- [x] Make every new regression explicitly discoverable by the test runner.

### Phase 2: Data Model

- [x] Use `ApplicationCount` for new trigger rows while retaining legacy group/source positional
      `Count` fields to avoid unrelated call-site churn.
- [x] Add `UnresolvedSourceCount` to incoming groups.
- [x] Add trigger application rows and classified residual/application counts.
- [x] Keep observed activation batches in a separate field and label them as observations.
- [x] Replace source-level `TriggerCount` with `ObservedActivationBatchCount` derived across all group
      journal entries without double-counting the same trigger source/frame pair.
- [x] Include trigger-fallback entries in the activation-batch rollup while keeping their group
      applications in the fallback bucket.
- [x] Add new counters as defaulted properties rather than new positional-record parameters where
      that avoids unnecessary call-site churn.
- [x] Split authoritative/observed application controls from the amount ledger.
- [x] Add combat-level periodic residuals to `CombatImpactReport`.
- [x] Change periodic `Project` to return the richer report and migrate production, focused tests,
      and the corpus together; do not merge a dual-API fallback state.
- [x] Remove the dead `Constrained` proof member in the same model-version change.
- [x] Keep the change within existing shared source files, so linked test-project source lists do not
      need new entries.

### Phase 3: Projection and Aggregation

- [x] Emit exactly one journal entry per displayed effect application.
- [x] Populate source-caused and target-received views from the same entries.
- [x] Attach trigger provenance and scope to execution-backed journal entries before aggregation.
- [x] Assign trigger scope in every event producer, including reconstructed/synthetic producers.
- [x] Derive trigger application counts from journal entries without frame deduplication.
- [x] Derive observed activation batches from the same attributed entries by distinct frame and
      remove the parallel occurrence set.
- [x] Move all-self trigger suppression from projection into presentation policy.
- [x] Preserve every rejected trigger, target, source, or value as a typed residual instead of
      dropping it.
- [x] Preserve periodic proof class through aggregation.
- [x] Preserve `Combatant` on periodic allocations and derive source-group totals as rollups.
- [x] Keep group keys derived only from journal entries or authoritative controls so
      `source.EffectCount` cannot diverge through an empty synthetic key.

### Phase 4: Presentation

- [x] Make `Triggered by` application counts additive to the group application count.
- [x] Show self rows in mixed self/external breakdowns and show `attributed N / total M` plus every
      non-zero remainder category.
- [x] Show pure-self rows when any remainder exists; suppress only the 100%-self/no-remainder case.
- [x] Label activation batches separately in summaries.
- [x] Render lower-bound, partial, and unattributed states distinctly; retain estimated/proportional
      proof internally without adding user-facing qualifiers.
- [x] For every ledger, omit breakdown rows when zero parts are attributed; show the total with
      `breakdown unavailable` instead.
- [x] Keep authoritative totals visually primary when available.
- [x] Hide pure-self trigger rows only as presentation policy; never delete their ledger entries.
- [x] Keep `attributed/total` visible before remainder details; render remainder categories in a
      compact priority order and allow wrapping without widening the native tooltip.

### Phase 5: Verification

- [x] Extend the existing corpus acceptance path rather than creating standalone diagnostic
      scaffolding.
- [x] Require zero failures for every exact conservation equation over the local replay corpus.
- [x] Report counts by coverage/proof class so a passing run cannot be vacuous.
- [x] Bump `PeriodicEffectAttribution.ModelVersion` and regenerate committed corpus evidence when the
      periodic accounting model changes.
- [ ] Golden-check at least one replay for every non-exact disclosure state.
- [ ] Golden-check a wide mixed-source/remainder case in both locales at the minimum readable width.
- [ ] Perform final Recap hover acceptance for caused and received perspectives in both locales.

## Claude Code Review and Codex Reconciliation

Agreement:

- the current trigger breakdown violates additive accounting by mixing application and activation
  dimensions;
- mixed-self retention is valid but insufficient;
- trigger and periodic residuals need first-class model fields;
- `LowerBound` and `Partial` survive into presentation; `Estimated` and `Proportional` remain in
  the internal proof model without adding user-facing qualifiers;
- current formatter tests intentionally lock in proof/coverage hiding and must change;
- received-source and periodic residuals need report-level owners rather than disappearing during
  projection.

Codex corrections to the peer review:

- missing event values do not imply a numeric unattributed amount; use unknown application count
  unless an authoritative comparable total makes the residual computable;
- distinct frames are evidence for activation batches, not proof of exact activations;
- a pure-self skill currently omits the trigger summary rather than literally displaying zero, but
  the underlying problem remains: summary activation data is derived from presentation-filtered
  trigger rows;
- the received view is intentionally filtered and therefore cannot balance the caused view; only
  per-view conservation equations are valid;
- `Constrained` exists in the periodic proof enum but is not emitted by the current implementation,
  and both v5/v6 corpus snapshots record zero constrained results;
- an absent trigger source is not automatically an unattributed trigger: no-evidence,
  not-applicable, and unresolved cases need separate classifications.

### Fable Review

Accepted findings:

- trigger scope must retain raw direct/trigger identities because activity-source fallback can make
  a proxy-triggered event look self-triggered after resolution;
- periodic allocations need a combatant dimension for the proposed per-combatant equations;
- pure-self groups with a remainder must render an attributed/total control rather than disappear;
- source-level trigger summary semantics and native-tooltip layout budget need explicit migration
  rules;
- removing `Constrained` must explicitly supersede the older unimplemented milestone taxonomy.

Qualified findings:

- `Partial` observed coverage does not always forbid a numeric amount residual: an authoritative
  total minus exact, disjoint known journal values can yield an exact aggregate residual. Other
  partial bases remain unknown, and `LowerBound` yields only an upper-bound residual;
- reconciliation equations are useful control identities but do not independently prove semantic
  comparability. Status/nullability invariants and non-vacuous corpus distributions carry that
  burden.

Follow-up resolution:

- exact residual eligibility is gated by every known event's value basis, not by the `Partial` label
  alone;
- self-only policy suppression, genuinely unavailable provenance, and partial breakdown are distinct
  presentation states;
- trigger-fallback entries count toward observed activation batches because their raw trigger/frame
  evidence exists, while their applications remain in the fallback bucket.

## Decisions Required Before Implementation

1. Use trigger-source `xN` for additive application counts. Recommended: yes.
2. Keep a separate `trigger batches` summary where only frame-level evidence exists. Recommended:
   yes, with conservative wording.
3. Show `at least`, `at most`, `recorded`, and `about` qualifiers directly in metrics. Recommended:
   yes.
4. Hide pure-self trigger rows only when the group is 100% self-attributed with no remainder.
   Recommended: yes; retain the data internally. If a remainder exists, render the self row and
   coverage control.
5. Keep PR #230 narrow while this design is reviewed, then either build on it or supersede its model
   changes in the ledger implementation. Recommended: do not merge it as the complete accounting
   solution.
6. In mixed self/external groups, show the self row and an attributed/total control; when no parts are
   attributable, show only the total plus `breakdown unavailable`. Recommended: yes.
7. Replace the periodic dictionary result in one coordinated change, remove dead `Constrained`, bump
   the model version, and migrate all linked tests/corpus callers without a merged fallback API.
   Recommended: yes.
8. Treat direct-source-to-trigger fallback as its own accounting bucket, never as self-trigger, and
   preserve raw source identities on the journal entry. Recommended: yes.
