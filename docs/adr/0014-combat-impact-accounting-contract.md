# ADR-0014: Combat Impact numbers are ledger entries, reconciled per view

Status: Accepted

## Context

Combat Impact used to place effect applications, activation observations, amounts, and durations beside native totals as if they formed one additive breakdown. Values had different dimensions and evidence bases, while failed attribution disappeared during projection.

## Decision

Every displayed number carries dimension, basis, coverage, and provenance in the `CombatImpactEvent` ledger.

- Additive breakdowns require the same dimension and basis. An incomplete breakdown renders `attributed N / total M` with every non-zero remainder bucket; zero attribution renders the total plus `breakdown unavailable`.
- Conservation is per view. The received view intentionally omits hero/player targets, so no caused-versus-received grand total exists ([tests](../../tests/PostCombatImpact.Tests/CombatImpactAggregatorTests.cs)).
- `ObservedActivationBatchCount` is a distinct trigger-source/frame observation, never an exact trigger count or an application count ([models](../../src/BazaarPlusPlus/Game/PostCombatImpact/Data/CombatImpactModels.cs)).
- Producer-assigned `CombatImpactTriggerScope` distinguishes external, self, trigger-fallback, no-evidence, unattributed, and not-applicable provenance. Rejected trigger, target, source, and value evidence survives as typed residuals.
- `CombatImpactAmountLedger` keeps authoritative totals and observed amounts separate. A numeric residual exists only for comparable semantics, unit, and direction; a negative difference is an `OverObserved` diagnostic.
- Periodic attribution returns allocations and residuals with an `Exact` or `Proportional` proof. Presentation may hide a complete pure-self breakdown but never deletes its ledger entries.

## Producer boundary

Attribution follows the native ability graph, never card identity. [`CombatImpactAttributionRuleReader`](../../src/BazaarPlusPlus/Game/PostCombatImpact/Data/CombatImpactAttributionRuleReader.cs) reads structural rules, [`CombatImpactProjector`](../../src/BazaarPlusPlus/Game/PostCombatImpact/Data/CombatImpactProjector.cs) reconciles explicit executions with native metrics, and [`CombatImpactEntityTags`](../../src/BazaarPlusPlus/Game/PostCombatImpact/Data/CombatImpactEntityTags.cs) merges runtime, template, and enchantment tags needed after replay rehydration. Metrics stay attached to the instance that produced them.

Critical attribution is evidence-bounded. A critical outcome requires a native critical health adjustment, an exact unambiguous doubled amount, or an executed `TTriggerOnCardCritted` ability whose source identifies the originating card. The listener path attaches one occurrence to a representative explicit effect and deduplicates stronger evidence. Without one of those proofs, the outcome remains unknown.

## Guardrails

- Keep per-view conservation; never add a caused-versus-received equation.
- Keep authoritative and observed amounts separate, and preserve every typed residual.
- Present frame-derived activation batches as observations until the producer exposes stable activation identity.
- Extend attribution through ability-graph evidence, never template GUID constants or cross-source metric transfer.
