# Prerequisite Skill Combat Impact Attribution

Status: implemented and validated on PR #230's working branch.

## Problem

PR #230 reconciles the application, trigger, amount, and residual ledgers after a journal entry
exists. It does not yet recover a class of journal entries whose native implementation source is a
socket effect while the player-visible source is a prerequisite skill.

The current `AddFMinorTempoEvents` workaround identifies F Minor and F Note by template GUID,
reconstructs one entry per qualifying item use, and assigns the entry to F Minor. This produces the
expected number for one card, but it encodes card identity instead of the execution graph. A Minor
has the same graph shape and remains uncounted; the current production data contains the same shape
for B, C, D, E, and G Minor as well.

The fix belongs at the journal producer boundary. The existing accounting ledgers can aggregate the
result once the producer records all three roles:

```text
raw implementation source: Note socket effect
player-visible source:      prerequisite Minor skill
trigger source:             occupying item that was used
```

## Verified Runtime Contract

The decompiled runtime establishes the following behavior:

- `TTriggerOnItemUsed.Subject` supplies the triggering card target.
- `TTargetCardOccupying` enumerates items occupying any socket covered by its socket-effect owner,
  then applies its card condition.
- `TCardConditionalTag` reads public `Tags`; `TCardConditionalHiddenTag` reads `HiddenTags`.
- `TPrerequisiteCardCount` evaluates a target selector and count comparison.
- `TTargetCardSection(AbsolutePlayerSkills)` selects the player's skills, and the observed
  prerequisite condition combines a positive template ID condition with a positive tier condition.
- `TActionPlayerModifyAttribute(Tempo, Add)` targets the socket effect's owning player and uses a
  fixed value in the observed graph.
- `DTOUtils.CreateCard` populates the template but does not copy runtime tags. Replay fallback must
  restore public tags from the battle snapshot or recover them from the current template.
- native Recap renders only the `CardStats` sources it receives. The validated F Minor replay has
  no F Minor `CardStats`, so BPP must create the player-visible journal source for that replay.
  `CardStats` absence is not assumed for every replay or future producer version.

The 5.0 battle snapshot already retains item template ID, type, size, socket, tier, enchantment,
public tags, and attributes. It also retains skill template ID/tier, while the combat payload retains
item `UseCount`. The current template is reattached by template ID during replay rehydration. These
facts are sufficient for deterministic reconstruction of the observed graph shape; no historical
effect graph is embedded in the replay, so compatibility is bounded by the current static-data
generation.

## Production Data Audit

The current production `GameData.db` contains 20 matching active abilities across seven Music Note
socket effects:

| Note | Item condition | Skill tiers represented | Fixed Tempo |
| --- | --- | --- | --- |
| A | public tag `Weapon` | Silver, Gold, Diamond | 1, 2, 3 |
| B | hidden tag `Burn` | Silver, Gold, Diamond | 1, 2, 3 |
| C | hidden tag `Slow` | Silver, Gold, Diamond | 1, 2, 3 |
| D | hidden tag `Shield` | Silver, Gold | 1, 2 |
| E | hidden tags `Heal` or `Regen` | Silver, Gold, Diamond | 1, 2, 3 |
| F | hidden tag `Haste` | Silver, Gold, Diamond | 1, 2, 3 |
| G | hidden tags `Tempo` or `TempoReference` | Silver, Gold, Diamond | 1, 2, 3 |

D Note's graph has no Diamond rule even though D Minor has a Diamond tier. The implementation must
therefore project only rules present in the ability graph and must not infer a missing rule from the
skill name, tier, tooltip text, or `Custom_0`.

The broader prerequisite audit covered all 3,277 card templates, not only Music Notes. Of those,
593 contain a card-count prerequisite and 17 target a skill section. Besides the 20 Minor abilities,
the current data contains seven Note auras whose implementation source grants Multicast while a
Major skill is the unique prerequisite. It also contains structurally different prerequisite-gated
cross-card auras and spawn/transform actions. Those shapes do not share the Minor `UseCount`
equation. The model therefore separates generic observed-source mapping from mechanism-specific
reconstruction: it may map an observed implementation event to a unique prerequisite skill, but it
reconstructs missing events only for the strict OnItemUsed fixed-Tempo rule proven below.

## Model Amendment

### Normalized producer rule

Add a feature-local immutable rule to the socket-effect entity snapshot:

```text
CombatImpactUseAttributionRule
  EffectId
  Action: PlayerTempoAdd
  FixedAmount
  ItemCondition
    Domain: PublicTag | HiddenTag
    Operator: Any | All | None
    Tags
  PrerequisiteSkillTemplateId
  PrerequisiteSkillTiers
```

Also add merged public `Tags` to `CombatImpactEntity`, parallel to the existing merged
`HiddenTags`. Merge runtime, template, and active-enchantment tags. Add the card's inventory
`Section`, and resolve `DisplaySpan` from `card.Size` for socket effects as well as items. These are
the producer-boundary compatibility facts required to avoid matching a stash card that reuses a
hand socket number and to reproduce a multi-socket `TTargetCardOccupying` range. The parser may
reject non-hand rules/entities it cannot reproduce exactly.

Do not attach the raw template or the full ability object graph to `CombatImpactEntity`. Parse once
while the native card/template is available, retain only the facts required by projection, and keep
the parser in the PostCombatImpact feature because this is attribution policy rather than a shared
runtime adapter.

### Strict parser boundary

Produce a rule only when an ability matches every condition below:

1. trigger is `TTriggerOnItemUsed`;
2. trigger subject is `TTargetCardOccupying`, and its single `Conditions` object is exactly one
   supported `TCardConditionalTag` or `TCardConditionalHiddenTag`; a multi-tag `Any` condition such
   as E/G remains one supported condition object;
3. action is `TActionPlayerModifyAttribute` with `Tempo`, `Add`, a positive integral
   `TFixedValue`, no duration or cost, and `TTargetPlayerRelative(Self)` without conditions;
4. `ActiveIn` is `HandOnly`, `WorksIn` is `Anywhere`, and the prerequisites list contains exactly
   one `TPrerequisiteCardCount`; extra run/card prerequisites are rejected rather than ignored;
5. card count is exactly `GreaterThanOrEqual`, amount `1`, and its subject is
   `AbsolutePlayerSkills` without `ExcludeSelf`;
6. the prerequisite card condition is an `And` containing exactly one positive, non-empty template
   ID condition and one positive, non-empty tier condition;
7. effect ID is non-empty and the normalized rule is unambiguous.

Unknown compound actions, nested/negated conditions, dynamic values, opponent targets, and
unsupported comparisons produce no reconstruction rule. Runtime diagnostics cover recognized
rules whose skill/use evidence is missing, ambiguous, or contradictory; the production-data audit
covers unsupported graph shapes without turning every unrelated prerequisite into a runtime
warning.

### Source resolution

Extend `CombatImpactActivitySourceResolution` with `PrerequisiteSkill`.

For a matching native execution, intercept inside `CombatImpactProjector.Project` before the
ordinary attribute displayability gate. Match against the raw `CombatSimEventEffectExecuted`, not a
surviving display event, because ordinary `PlayerModifyAttribute` resolution may fail or produce a
different group. Map the implementation source to the unique prerequisite skill instance,
normalize the event to the rule's group/value, and preserve the original roles:

```text
SourceId                 = prerequisite skill instance
RawDirectSourceId        = socket-effect instance
TriggerSourceId          = used item instance
ActivitySourceResolution = PrerequisiteSkill
TriggerScope             = AttributedExternal
OccurrenceBasis          = ExplicitExecution
Kind                     = AttributeChange
NativeAttributeKey       = TempoApplyAmount
Surface                  = AppliedEffect
Value                    = rule FixedAmount
ValueBasis               = ConfiguredActionAmount
```

For a reconstructed entry, use the same fields, set `OccurrenceBasis` to
`ReconstructedTransition`, and leave `TriggerFrameIndex` empty. It still belongs to the trigger
application ledger because the item/use relationship is authoritative; it contributes zero
observed activation batches because no frame identity was observed.

The configured value comes from the ability action, not the skill's `Custom_0`. Keep
`ConfiguredActionAmount` as the internal value basis. Presentation remains a bare value without an
approximation marker; basis and coverage are accounting metadata and diagnostics, not extra user
copy.

`ActivitySourceResolution` is provenance metadata; the current aggregator does not branch on it.
No aggregator arithmetic change is required. The producer intentionally broadens
`TriggerScope.AttributedExternal` from direct-activity executions to graph-derived activity
sources. A reconstructed entry has an authoritative item/application relationship but no frame, so
it contributes to trigger-source application count and contributes zero observed activation
batches. A mixed explicit/reconstructed group remains group-level `ReconstructedTransition` under
the existing weakest-occurrence rule.

### Deterministic matching

For each normalized socket-effect rule:

1. find hand items on the same combatant whose socket spans overlap the hand socket-effect span;
2. evaluate the normalized condition against merged public/hidden tags;
3. find exactly one same-combatant skill instance matching template ID and tier;
4. read the item's authoritative native `UseCount`;
5. match raw native explicit executions by raw socket-effect ID, effect ID, trigger item ID, action,
   player target, and resolved prerequisite skill;
6. emit the native mapped entries and reconstruct only the positive remainder:

```text
reconstructed applications = max(0, item UseCount - matching explicit applications)
```

If no unique skill exists, the item use count is missing, or explicit applications exceed the use
count, do not invent balancing entries. Record a typed diagnostic with the rule/effect and entity
IDs. Multiple socket effects are evaluated independently because the runtime executes each effect;
multiple matching skill instances are ambiguous and must never multiply the same prerequisite.

The raw action matcher accepts both `PlayerModifyAttribute` and the optional `PlayerTempoApply`
command for this rule shape. A socket-effect execution whose effect ID belongs to a parsed rule but
fails the remaining raw-execution match emits a typed diagnostic and is not silently treated as an
ordinary item-attributed event plus a full reconstruction. This closes the version-dependent
double-representation path while retaining a visible signal for producer drift.

This explicit/reconstructed reconciliation prevents the current folded-event replay from losing
the contribution and prevents a future replay schema with standalone socket-effect executions from
double counting it.

The prerequisite selector says `AbsolutePlayerSkills`, while the replay projection has explicit
player/opponent combatants. The reconstruction translates it to the socket effect's owning
combatant, preserving the current F behavior. This ownership translation is an explicit replay
model decision and requires an opponent-side regression where both combatants own the same Minor
template. It must never resolve a skill across combatants.

### Native authoritative metrics

Keep native `CardStats` metrics attached to the instance ID that produced them; do not transfer or
suppress an item's native Tempo metric merely because the item also triggered a prerequisite skill.
There is no proven cross-source additive equation, and the item may have its own Tempo action.

- If the prerequisite skill carries a native `TempoAdded` metric, its normalized entries reconcile
  against that metric in the existing amount ledger.
- If the skill metric is absent, as in the validated F replay, its amount ledger remains
  `NotComparable`; deterministic graph/use-count reconstruction is still displayed.
- If an item carries `TempoAdded`, keep its authoritative item group and do not compare it to the
  skill's reconstructed amount.
- If a socket effect carries the metric, it remains a non-displayable native implementation source
  and is recorded by a diagnostic; remapping that metric needs separate producer evidence.

Before implementation closes, inventory the 1110-replay corpus by Minor/Note presence and report
where `TempoAdded` appears (skill, triggering item, socket effect, other, or absent). The existing
PR #230 F unit test that injects `TempoAdded` on F Minor is a synthetic amount-ledger control, not
evidence from the real F replay, whose PR record explicitly says F Minor had no `CardStats`. Split
those two contracts in the revised tests.

### Existing Music Note parser

`MusicNoteEffectClassifier` is badge policy, not attribution policy. It scans a wider set of
ability/aura conditions and intentionally removes `*Reference` pseudo-tags from visual badges. The
new attribution parser must evaluate the exact runtime subject condition, including G Note's
`TempoReference`, and must not share that filtering rule.

## Implementation Checklist

- [x] Add `CombatImpactUseAttributionRule`, normalized tag-condition types, public `Tags`, inventory
      `Section`, and `PrerequisiteSkill` source resolution to the PostCombatImpact data model.
- [x] Add a strict `CombatImpactUseAttributionRuleReader` over active socket-effect template
      abilities and return only normalized rules whose equations can be reproduced exactly.
- [x] Merge runtime/template/enchantment public tags in `CombatImpactEntitySnapshotReader`, retain
      socket-effect size/section, and parse attribution rules while native templates are available.
- [x] Retain effect ID and raw execution identity in `ProjectedExecution`; before the ordinary
      displayability gate, resolve exact explicit socket-effect executions to their prerequisite
      skill, accept both generic and optional Tempo apply action commands, and normalize
      key/surface/value to the rule.
- [x] Replace `AddFMinorTempoEvents` with a graph-driven producer that reconciles each rule/item pair
      against explicit execution count and `UseCount`.
- [x] Delete the F Minor/F Note GUID constants from production source.
- [x] Keep existing aggregator equations unchanged; the new source-resolution enum is metadata and
      `AttributedExternal` is the existing trigger-ledger classification used by the new producer.
- [x] Add diagnostics for ambiguous skill source, missing use count, and
      explicit-over-use reconciliation, plus rule-bearing socket executions that fail the exact
      raw-execution match.
- [x] Keep native authoritative metrics on their recorded instance. Tests prove that an item's
      Tempo metric stays with the item and a socket-effect metric is diagnosed without being moved
      to the prerequisite skill; no corpus-derived cross-source equation is assumed.

## Validation Checklist

- [x] Parser positive tests for A's public `Weapon` condition and F's hidden `Haste` condition.
- [x] Parser rejection tests cover a non-additive operation, non-integral value, and extra
      prerequisite; the parser implementation also rejects duration/cost, non-self targets, wrong
      active/work scope, unsupported comparisons/conditions, and empty effect IDs by construction.
- [x] Producer-boundary test: runtime public tags empty, replay/template public tags include
      `Weapon`, and A Minor attribution is recovered.
- [x] Producer-boundary test: a rehydrated `ItemCard` has empty runtime public/hidden tags while its
      template restores `Weapon` and `Haste` through the same adapter used by the snapshot reader.
- [x] A Minor golden: Gold A Minor + A Note + overlapping Weapon item with `UseCount=N` yields N
      applications and `2N` Tempo assigned to A Minor, with the item as trigger source.
- [x] F Minor golden preserves the already validated Gold F behavior without either F GUID in
      production source.
- [x] Explicit-event golden: one matching native execution plus `UseCount=N` yields one explicit and
      `N-1` reconstructed entry in one normalized `TempoApplyAmount/AppliedEffect` group, never
      `N+1`; the mixed group occurrence basis is `ReconstructedTransition`.
- [x] Explicit-event action cases cover both `PlayerModifyAttribute` and optional
      `PlayerTempoApply`; a rule-bearing mismatch produces a diagnostic instead of ordinary item
      attribution plus full skill reconstruction.
- [x] Ambiguity and reconciliation tests cover duplicate same-owner prerequisite skills,
      same-template opponent skills, overlapping multi-slot item/socket spans, same-number stash
      cards, explicit count greater than use count, and duplicate rule data that disables
      reconstruction without crashing.
- [x] Opponent-side golden with the same Minor template on both combatants proves ownership cannot
      cross combatants.
- [x] Native metric controls cover skill metric present, skill metric absent, item metric present,
      and socket-effect metric diagnostic without moving metrics between sources.
- [x] Static-data audit enumerates all matching production Music Note abilities and confirms every
      supported graph parses; it reports D Note's absent Diamond rule rather than synthesizing it.
- [x] Existing PostCombatImpact focused suite (217 tests), full `./run.sh test`, Debug build, format
      check, and the local 197-replay/70,048-frame corpus remain green. The configured 1110-replay
      corpus was not available in this worktree run.
- [x] Real Recap acceptance through Steam on an old 5.0 F replay shows two observed trigger batches,
      `2/2 attributed`, and four Tempo on F Minor with `recap_hover_observed=shown` and no projection
      degradation. The local history contains no battle with both A Minor and an A Note; its reported
      A run contains B/C/D/E/G Notes only, so A Minor correctly remains zero there. A's positive path
      is covered by the production graph audit and Gold A golden test; a separate negative golden
      locks the no-A-Note result. It is not claimed as local positive visual acceptance.

## Failure Postmortem

- **What was wrong:** the F-only reconstruction encoded two template GUIDs and `Custom_0` instead
  of the native ability graph's subject, action, and prerequisite provenance.
- **Impact:** A Minor remained uncounted, and the same omission was possible for the other five
  structurally equivalent Minor skills even though PR #230's downstream ledgers balanced their
  incomplete input.
- **Root cause:** the journal producer snapshot had hidden tags but no public tags or normalized
  prerequisite-skill mapping. The projector repaired one observed replay after provenance had
  already been discarded.
- **Why tests missed it:** the regression fixture represented only F's hidden-tag case and asserted
  the final total. It did not inventory the production graph family, exercise a public-tag replay
  fallback, or assert that source resolution came from graph semantics.
- **Prevention:** A/F golden cases, production graph inventory, runtime-empty tag recovery tests,
  explicit/reconstructed reconciliation, and a source scan that forbids known Minor/Note GUID
  constants in production PostCombatImpact code.
