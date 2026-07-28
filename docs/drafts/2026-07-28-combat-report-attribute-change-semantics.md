# Combat report attribute-change semantics

Status: implemented and locally verified on 2026-07-28; exact modifier-source
resolution remains follow-up work.

## Implemented outcome

- All current player and card enum members have an explicit report classification.
- Cooldown ticks are omitted. Haste, Slow, and Freeze consume the complete raw stream
  and serialize one compact interval per active span.
- Player chart state stays in metric samples instead of becoming duplicate generic
  markers; Health remains a fallback settlement only when the frame has no explicit
  health adjustment.
- Named modifier/economy diffs render `+/- value` and old → new. Opaque diagnostic
  slots fail closed until there is a dedicated diagnostic disclosure.
- Same-frame source guessing was removed. A modifier source remains unknown unless the
  raw event establishes it exactly.
- Raw `aura` execution records remain in the report as provenance data but are hidden
  from Timeline, Combat Log, and Inspector markers. They do not carry an attribute
  subtype or a value, so showing them as `Status change` only obscures the concrete
  before/after attribute records from the same frame.

## New-report regression: Welding Torch frame 270

The 2026-07-28 report `75b5aab6ad424d0289042e4834d02d0a` contains four records
for Welding Torch at frame 270:

- two `aura/Aura` records with effect IDs `e2` and `1`, no value, and the same card as
  source and target;
- `BurnApplyAmount`, `860 → 814` (`−46`);
- `ShieldApplyAmount`, `4,540 → 4,310` (`−230`).

The old Viewer clustered the first pair as `Status change ×2`, making it look as though
the concrete attribute work had not shipped. The presentation rule is now deterministic:
generic aura bookkeeping is not a marker, while the two typed card-attribute transitions
remain visible as `Burn applied −46` and `Shield applied −230`, with their proven native
Burn and Shield icon semantics.

## Background and current failure

The report currently projects every non-zero player/card attribute delta into a generic
`player-attribute` or `card-attribute` event. The protocol gives those state updates an
attribute type and before/after values, but no source:

- card projection: `CombatReportProjector.cs:75-115`
- player projection: `CombatReportProjector.cs:390-469`
- protocol update shapes: `CombatSimPlayerAttributeUpdate.cs:8-24` and
  `CombatSimCardAttributeUpdate.cs:8-24`

Separately, `CombatSimEventEffectExecuted` carries source, target, `EffectId`, and only
the coarse `EActionCommandType`; `PlayerModifyAttribute` / `CardModifyAttribute` does
not carry the concrete attribute subtype (`CombatSimEventEffectExecuted.cs:8-30`).

The first repair only gave `HealthMax` a Viewer semantic and paired it with a same-frame
`PlayerModifyAttribute` when that application was unique. That fixes the observed Pasta
case, but it is not a complete attribute model. Generalizing that same-frame join to all
attributes would be wrong: a modifier can execute in a frame where unrelated health,
Burn, Poison, Shield, Rage, timer, or status settlement also occurs.

## Evidence

### Game-defined inventories

The full source-of-truth enums are:

- player: `decompiled/.../EPlayerAttributeType.cs:3-44`
- card: `decompiled/.../ECardAttributeType.cs:3-93`

The game's own tooltip adapter maps only a subset of player attributes to native card
attribute icon semantics (`TooltipComponentExtensions.cs:151-188`). Unknown values are
intentionally unmapped.

### Real-report census

A read-only scan of the 50 canonical 32-hex report files currently installed under
`BazaarPlusPlusV4/reports/` found the following non-zero updates.

Player attributes:

| Attribute | Events | Observed delta |
| --- | ---: | ---: |
| Burn | 1,803 | −185 … +744 |
| Custom_3 | 4 | +1 |
| Enraged | 64 | −1 … +1 |
| EnragedDuration | 3,249 | −50 … +5,000 |
| Gold | 4 | +1 |
| Health | 1,764 | −20,738 … +7,545 |
| HealthMax | 147 | +10 … +5,584 |
| HealthRegen | 579 | −25 … +1,188 |
| Poison | 248 | −47 … +408 |
| Rage | 1,342 | −100 … +34 |
| Shield | 1,958 | −6,030 … +6,660 |

Card attributes:

| Attribute group | Observed attributes (event count) |
| --- | --- |
| Live clocks / statuses | Cooldown (126,825), Haste (45,114), Slow (13,944), Freeze (3,178), Chilled (118), Heated (108), Flying (444) |
| Capacity / reductions | CooldownMax (652), FlatCooldownReduction (88), PercentCooldownReduction (694), PercentSlowReduction (447), PercentFreezeReduction (448) |
| Core values | Ammo (32), CritChance (4,569), DamageAmount (2,450), Multicast (34), Lifesteal (2) |
| Effect amounts | BurnApplyAmount (1,565), PoisonApplyAmount (227), RegenApplyAmount (361), HealAmount (202), ShieldApplyAmount (1,216), ChargeAmount (5), FreezeAmount (2), SlowAmount (1) |
| Target counts | FreezeTargets (2), SlowTargets (2) |
| Economy | BuyPrice (2), SellPrice (3) |
| Quest counters | QuestCompletedCount (1), Quest_1 (118), Quest_2 (13), Quest_3 (15) |
| Opaque slots | Custom_0 (1,398), Custom_1 (70), Custom_3 (315), Custom_4 (26), Custom_6 (77), Custom_8 (14) |

The census also demonstrates why a target+frame join is not a general source resolver:

- `HealthMax`: 147 updates; 141 had one same-frame `PlayerModifyAttribute`, 6 were
  ambiguous, and none had zero candidates.
- `Health`: 1,764 updates; only 140 had one candidate, 1,618 had none, 6 were ambiguous.
- `Burn`: 1,803 updates; only 8 had one candidate, 1,793 had none. Those 8 are not proof
  that Pasta modified Burn; they can simply share the frame with Pasta's HealthMax action.
- `Cooldown`: 126,825 updates; the overwhelming majority are normal 50 ms clock
  progression, not user-facing stat mutations.

## Complete inventory and proposed classification

The classification is about **how the report should render a transition**, not whether
the game internally stores it as an attribute.

### Player attributes

| Class | Full enum inventory | Default report treatment |
| --- | --- | --- |
| Current combat state | `Burn`, `Joy`, `Health`, `HealthRegen`, `Poison`, `Shield`, `Rage`, `Enraged`, `EnragedDuration`, `Tempo` | State chart/track. Do not render every delta as “Status change”. Explicit application/settlement events own markers and combat-log rows. |
| Capacity / persistent combat modifier | `HealthMax`, `RageMax`, `EnragedDurationMax`, `TempoGainCooldownMax` | Inspector diff (`+N`, old → new), timeline marker when changed, statistics net/count. |
| Offensive / defensive modifier | `CritChance`, `DamageCrit`, `HealAmount`, `HealCrit`, `JoyCrit`, `ShieldCrit`, `FlatDamageReduction`, `PercentDamageReduction`, `FlatTempoGainCooldownReduction`, `PercentTempoGainCooldownReduction` | Inspector diff and statistics. Use percent only where the game enum/tooltip contract establishes percent semantics. |
| Run economy / progression | `Experience`, `Gold`, `Income`, `Prestige`, `Level`, `RerollCostModifier` | Usually outside battle analytics. If emitted during combat, show a named diff in Inspector; keep out of damage/status statistics unless product requirements say otherwise. |
| Opaque game slots | `Custom_0` … `Custom_9` | Preserve exact raw name/value in diagnostic detail only until a concrete game/template meaning is resolved. Never relabel from one observed item. |

Notes:

- `HealthMax` is a capacity change, not healing. It can use the native heal artwork
  because the game maps it to `HealAmount`, but its label and amount remain “Max health
  +N”, not “Healing N”.
- `Health`, `Shield`, `Burn`, `Poison`, `HealthRegen`, and `Rage` are already represented
  by charts and/or explicit application/settlement events. Rendering their raw state deltas
  again creates duplicate and misleading rows.
- `Tempo` and the Enraged family require a real-report/decompiled behavior check before
  final sign/unit policy; the enum name alone is insufficient.

### Card/item/skill attributes

| Class | Full enum inventory | Default report treatment |
| --- | --- | --- |
| Live clock / transient state | `Cooldown`, `Haste`, `Slow`, `Freeze`, `Heated`, `Chilled`, `Flying`, `CooldownDisabled` | State bar/track only. Explicit `CardCharge`, `CardHaste`, `CardSlow`, `CardFreeze` events own markers/log entries. Never aggregate normal per-frame decay as a stat mutation. |
| Resource / capacity | `Ammo`, `AmmoMax`, `CooldownMax` | Named diff. Ammo positive and negative changes must remain distinguishable; do not collapse reload/gain and spend into one unsigned total. |
| Action amounts | `ReloadAmount`, `ChargeAmount`, `HasteAmount`, `SlowAmount`, `FreezeAmount`, `BurnApplyAmount`, `BurnRemoveAmount`, `PoisonApplyAmount`, `PoisonRemoveAmount`, `DamageAmount`, `HealAmount`, `JoyApplyAmount`, `JoyRemoveAmount`, `ShieldApplyAmount`, `ShieldRemoveAmount`, `RegenApplyAmount`, `RegenRemoveAmount`, `RageApplyAmount`, `RageRemoveAmount` | Named diff and statistics net/count. Milliseconds for charge/haste/slow/freeze amounts; points otherwise. |
| Chance / multiplier / scalar | `Multicast`, `Lifesteal`, `CritChance`, `DamageCrit`, `HealCrit`, `JoyCrit`, `ShieldCrit`, `BurnCrit`, `PoisonCrit`, `RegenCrit` | Named diff and statistics. Confirm percent vs count per native tooltip semantics. |
| Reduction / cost modifier | `FlatCooldownReduction`, `PercentCooldownReduction`, `PercentChargeReduction`, `PercentHasteReduction`, `PercentSlowReduction`, `PercentFreezeReduction`, `TempoCost`, `FlatTempoCostReduction`, `PercentTempoCostReduction` | Named diff. `Flat*Cooldown` uses ms, `Percent*` uses %, `TempoCost` uses points. |
| Target count | `ReloadTargets`, `ChargeTargets`, `HasteTargets`, `SlowTargets`, `FreezeTargets`, `ForceUseTargets`, `EnchantTargets`, `UpgradeTargets`, `DisableTargets`, `RepairTargets`, `DestroyTargets`, `TransformTargets`, `EnchantRemoveTargets`, `FlyingTargets` | Named integer diff. These are capabilities/counts, not lifecycle events. In particular, `DestroyTargets +1` is not “Destroyed”. |
| Economy | `BuyPrice`, `SellPrice` | Named currency diff; normally Inspector-only in a battle report. |
| Counters / quest | `Counter`, `QuestCompletedCount`, `Quest_1` … `Quest_12` | Prefer the explicit quest event when available. Raw counters are diagnostic unless a stable display name can be resolved. |
| Immunity / boolean-like | `DestroyImmunity` | Named enabled/disabled transition, not numeric damage/status. |
| Opaque game slots | `Custom_0` … `Custom_8` | Preserve raw name/value only until resolved from the concrete effect/template. |

Actual item destruction, repair, transform, enchant, and quest completion are lifecycle
events (`effect-executed`, `card-transformed`, `card-enchanted`,
`card-quest-completed`), not generic attribute diffs.

## Rendering model

Every projected transition should land in one of four lanes:

| Lane | Meaning | Timeline / Inspector | Statistics |
| --- | --- | --- | --- |
| State | Current value evolving over time | chart/area/status band; no per-tick “Status change” row | optional duration/peak, never sum of ticks |
| Application | An action applied damage/heal/Burn/Poison/Regen/Shield/Charge/Haste/Slow/Freeze | native semantic marker + source/target/amount | uses/count/amount by source and target |
| Modifier diff | A stat/capability changed | `+/- value`, old → new, source when exact | net change + count, split gain/loss when useful |
| Lifecycle | Destroyed/repaired/transformed/enchanted/quest completed | explicit named event | count by source/target |

Unknown/opaque updates are a fifth diagnostic fallback and must not borrow a known semantic.

## Red-team revision: do not compress state clocks by delta sign

An independent read-only review of the first implementation attempt found that filtering
status updates with `current > previous || current <= 0` is not correct enough to ship.

Concrete failures:

1. A positive-to-positive decrease is not necessarily an ambient 50 ms tick. A partial
   cleanse/reduction can shorten Haste, Slow, or Freeze while the remaining value stays
   positive. Dropping it makes the rendered range too long.
2. The legacy Viewer estimates an active range's end from its last raw sample
   (`clusters.ts:305-314`). Removing intermediate samples makes that fallback use an old
   duration and breaks any range changed after application.
3. A card entering the report with an already-active status needs a frame-zero baseline.
   If only the terminal edge survives, the Viewer cannot construct any range.
4. Player live states currently bypass the generator classifier
   (`CombatReportProjector.cs:435-476`), so `EnragedDuration` and `Tempo` can still generate
   per-frame events even though the Viewer hides their markers.
5. The legacy same-frame HealthMax source join remains semantically unsafe: the sole
   `PlayerModifyAttribute` in that frame can modify a different player attribute. A concrete
   displayed source contradicts the event's `target-exact-source-unknown` confidence.
6. Frontend `inspectorVisible` / `statisticsVisible` policy fields are not useful unless
   their consumers actually enforce them.

### Revised state representation

Do not serialize Haste/Slow/Freeze countdown samples as general report events, and do not
guess which raw samples are safe to discard. Instead:

1. During projection, consume **all** raw Haste/Slow/Freeze updates into a dedicated status
   interval builder keyed by `(card instance, attribute)`.
2. Capture frame-zero active state, exact start, every extension/reduction, terminal zero,
   and the last observed remaining duration.
3. Serialize one compact interval per active span with `startMs`, `endMs`, target, semantic,
   and optional related application event IDs. The interval is derived from the complete raw
   stream before any samples are discarded.
4. Stop emitting those raw clock samples as `card-attribute` events in new reports.
5. Legacy reports keep the current raw-sample range builder, but their samples remain hidden
   from marker/Inspector/statistics surfaces.
6. `Cooldown` remains omitted from the event stream: it is a live clock with no current
   report interval/marker consumer.

### Revised player-state representation

- Continue writing the six chart metrics (`Health`, `Rage`, `HealthRegen`, `Shield`, `Burn`,
  `Poison`) to metric samples, but do not also emit generic attribute markers for them.
- Preserve the Health fallback settlement only when that frame has no explicit health
  adjustment for the same target.
- `Enraged`, `EnragedDuration`, `Tempo`, and `Joy` need an explicit state surface before
  projection; until then they fail closed instead of becoming generic Status change events.
- Player modifier/economy attributes remain discrete diffs under the classification
  policy. Diagnostic attributes fail closed until the Viewer has a dedicated disclosure.

### Revised source policy

- Remove the frontend same-frame HealthMax source inference.
- New reports may display a source only after the generator resolves the exact `EffectId`
  to a concrete `PlayerModifyAttribute` / `CardModifyAttribute` subtype.
- Legacy reports keep source unknown; correctness is preferred over a plausible Pasta label.

## Source attribution design

### Current limitation

The frontend helper joins one same-frame `PlayerModifyAttribute` to one player attribute
only by target (`frame-event-groups.ts:16-40`). This is acceptable only as a narrow legacy
fallback for an allowlisted attribute such as `HealthMax`; it must not become the general
algorithm.

### Preferred new-report path

1. At report generation, resolve the effect definition using the exact source instance,
   template/enchantment, and `EffectId`.
2. Read the concrete action's `AttributeType` and operation.
3. Project that subtype on the application event (for example
   `attributeAction: "HealthMax"`), without changing the raw protocol DTO.
4. Join application to state transition on `(frame, target, attributeAction)`.
5. Attribute only a unique one-to-one match; otherwise preserve source unknown.

This mirrors the game's own replay lookup: it resolves `EffectId` against the source
template ability, including enchantment abilities (`CombatSimHandler.cs:517-525` and
`575-595`).

### Legacy reports

- Keep exact explicit effect sources already serialized.
- Do not infer modifier sources from target+frame alone, including HealthMax.
- Never use source display names, item art, or observed co-occurrence as identity.

## Native icon policy

Use native icons only where the game's tooltip mapping establishes the semantic:

- player: Burn, CritChance, DamageCrit, Joy/JoyCrit, Health/HealthMax/HealAmount/HealCrit,
  Poison, Shield/ShieldCrit, Gold/Income, Tempo, Experience, HealthRegen, Rage/RageMax.
- card: use the matching native `ECardAttributeType` icon when available.
- unknown/custom/quest slots: text/raw fallback; no invented Lucide/game icon.

The label remains specific even when multiple attributes share artwork. Example:
`HealthMax` may reuse heal artwork but reads “Max health +25”.

## Executable checklist

### Data contract

- [ ] Add a pure resolver for effect definition → concrete attribute type/operation.
- [ ] Cover base abilities, enchantment abilities, auras, and unresolved/transformed sources.
- [ ] Add optional subtype/operation fields to report events without breaking schema-v1 readers.
- [ ] Add projection tests for one-to-one, ambiguous, and unresolved attribution.
- [x] Add a compact card-status interval contract and keep legacy readers valid.
- [x] Build intervals from complete raw updates, including frame-zero, extension, partial
  reduction, terminal, and battle-end cases.

### Viewer semantics

- [ ] Replace the flat semantic maps with one descriptor table carrying class, unit,
  sign policy, native semantic key, timeline policy, Inspector policy, and statistics policy.
- [x] Route live state attributes to charts/tracks/intervals and suppress their raw per-tick rows.
- [x] Consume new compact status intervals while retaining a legacy raw-sample adapter.
- [x] Render modifier diffs as `+/- value` plus old → new.
- [x] Render lifecycle events separately from target-count attributes.
- [ ] Keep custom/unknown attributes raw and visibly unclassified.
- [x] Remove legacy same-frame HealthMax source inference.

### Statistics

- [ ] Aggregate modifier net/count by entity and target.
- [ ] Split Ammo gain/reload from Ammo spend when both appear.
- [x] Never sum clock decay/status settlement as “attribute activity”.
- [x] Keep explicit application totals separate from modifier totals.

### Verification

- [x] Unit-test every classified enum member and assert no enum value is silently omitted.
- [ ] Fixture-test HealthMax (Pasta/Boar Roast), Multicast loss after Ice Swan destruction,
  damage/Crit/Cooldown modifiers, Ammo gain/loss, and actual destroy vs DestroyTargets.
- [x] Fixture-test the Welding Torch frame-270 shape: duplicate raw aura bookkeeping does
  not render, while Burn/Shield amount diffs remain explicit.
- [x] Fixture-test status start, extension, partial reduction, terminal zero, active-at-frame-zero,
  and active-at-battle-end.
- [ ] Run a census assertion over representative real reports: no per-frame Cooldown/Haste/
  Slow/Freeze rows appear as modifier events.
- [ ] Playwright: inspect each lane, source/target, diff, native icon, and statistics tooltip.
- [ ] Re-record with the updated DLL; legacy-report behavior remains conservative.

## Decisions still requiring confirmation

1. Whether economy/progression changes belong in combat report statistics or Inspector only.
2. Whether unknown `Custom_*` values should be visible by default or behind a diagnostic disclosure.
3. Whether Ammo statistics should show two columns (gained/spent) or one signed net cell with
   a gain/spend tooltip breakdown.
