# ADR-0010: One Destroy chip covers the full destroy-mechanic cluster

Status: Accepted; supersedes the narrow Destroy definition shipped by #149 / PR #154

## Decision

The Collection Panel exposes exactly **one** `CollectionMechanic.Destroy` flag and one primary-section chip. Selecting Destroy returns the whole destroy-mechanic cluster, not only cards whose active base ability executes `TActionCardDestroy`.

A card matches Destroy when any of the following holds on its **base** template (active tier ability IDs / base attributes / native hidden tags only):

| Surface | Match |
| --- | --- |
| Action | `TActionCardDestroy`, `TActionCardRepair`, or `TActionCardTransformDestroyed` (root or nested under `TActionAnd`) |
| Trigger | `TTriggerOnBeforeCardDestroyed`, `TTriggerOnCardDestroyed`, or `TTriggerOnCardPerformedDestruction` (root or nested under `TTriggerOr`), independent of the ability's action |
| Hidden tag | `EHiddenTag.AbsorbDestroy` (mapped through `TryFromHiddenTag` into Destroy; never its own keyword chip) |
| Attribute | positive base `ECardAttributeType.DestroyImmunity` at any supported tier |

Deliberate exclusions:

- `TTriggerOnCardRepaired` ("was repaired") is **not** Destroy. Repair *actions* match; the repaired trigger does not.
- Abilities nested inside a `TActionCardTransformDestroyed.Abilities` spawn template belong to the replacement card and are never walked (sub-rule retained from #149; no longer separately observable as a second flag).
- Enchantment-provided abilities, auras, attributes (including Radiant destroy immunity), and hidden tags are structural exclusions — the projector only reads `template.Abilities` / tier `AbilityIds` / base attributes / `template.HiddenTags`.
- Destroy-related tooltip, name, or description text never contributes.
- Orphaned / inactive abilities (not referenced by any tier's `AbilityIds`) never contribute.
- Multicast classification and availability are unchanged.

Product presentation stays on the game's native Destroy label: `NativeTagTypography.Resolve("Destroy")`. No new localized string, and the chip does **not** move into the keyword panel's Related subsection. The broader meaning under the same native label is intentional.

## Why

#149 shipped a narrow Destroy that matched only `TActionCardDestroy`. #150 proposed a second "Destroy Related" chip covering reactions, repair, replace-destroyed, absorb, and immunity, and was closed as wontfix. Players still need to find the rest of the cluster; adding a second chip split a single mental category and forced Any/All gymnastics.

Merging into one chip also settles a direct contradiction between #149 and #150: #149 excluded replace-destroyed (`TActionCardTransformDestroyed`) from Destroy, while #150 listed it as an included related mechanism. Under the merged chip, replace-destroyed **is** Destroy. The only surviving sub-rule is that spawn-template abilities inside the replace action are still never walked.

## Guardrails

- Projection lives only at the catalog VM boundary ([`CollectionCardVm.From`](../../src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardVm.From.cs), [`CollectionMechanicFacts.Project`](../../src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionMechanicFacts.cs)). Filtering and view refreshes read the cached flag only; architecture tests forbid effect-graph types on those paths ([`CollectionMechanicArchitectureTests`](../../tests/Architecture.Tests/CollectionMechanicArchitectureTests.cs)).
- One pass per card over active abilities (action + trigger), with tier attribute checks in the same tier loop. Do not re-walk tiers or abilities after the merge.
- `AbsorbDestroy` stays off `CollectionKeywordWhitelist.Ordered`; mapping it in `TryFromHiddenTag` only feeds the Destroy mechanic.
- Do not reintroduce a second Destroy-related chip, sub-flag, or Related-section placement without a new product decision that supersedes this ADR.
- Executable coverage lives in [`tests/CollectionFilterEngine.Tests/Program.cs`](../../tests/CollectionFilterEngine.Tests/Program.cs).

## History

- #149 / PR #154: narrow Destroy (`TActionCardDestroy` only) — **superseded by this ADR**.
- #150: separate Destroy Related chip — closed wontfix; final disposition points here.
- #156: destruction-reaction triggers.
- #157: repair, replace-destroyed, absorb, immunity.
- #158: facet availability / Any/All verification and this record.

Reopen only if product decides to split the cluster again or to reclassify `TTriggerOnCardRepaired`.
