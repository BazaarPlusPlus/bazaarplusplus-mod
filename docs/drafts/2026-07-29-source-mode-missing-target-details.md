# Source mode missing target and attribute details

## Background

Timeline supports two event-placement modes:

- Target mode places an event on the affected entity lane and treats that lane as the implicit target.
- Source mode places the same event on its source lane and should therefore expose the affected targets in the Inspector.

Attribute markers are intentionally generic on the lane, but the Inspector must retain every concrete underlying attribute transition.

## Current problem

The real report `411e68a020d341559a724882dc413b02.html` exposes two missing-detail cases:

1. At frame 284 (`14.2s`), Primal Core shows `Charge` in source mode but the Inspector shows no target.
2. At frame 221 (`11.1s`), Miss Isles shows a generic `Status change` marker but the Inspector shows no concrete attribute name, delta, or `before → after` transition.

These may share a presentation symptom while having different data-path causes. The report payload is authoritative for determining whether target IDs and attribute transition fields were captured.

## Verified root causes

The supplied immutable report retains all of the data needed for both examples.

### Primal Core charge

Frame 284 contains `effect-executed/CardCharge` event `e722`:

- source: Primal Core;
- trigger source: SMG;
- target: Primal Core.

`FrameInspector.relationTreeNodes()` currently rejects every relation whose entity ID equals
the inspected lane entity. That suppression is correct in target mode, where the lane already
is the implicit target, but wrong in source mode: a self-target is still an explicit outcome
and must remain visible.

### Miss Isles attribute changes

Frame 221 contains:

- `e526`: `CardModifyAttribute`, source/target Miss Isles, trigger Primal Core;
- `e529`: `CardReload`, source/target Miss Isles, trigger Primal Core;
- `e531`: `Ammo`, `0 → 1`;
- `e532`: `DamageAmount`, `170 → 175`.

Target mode can render the two typed card-attribute records directly. Source mode cannot place
those records because the protocol intentionally records their target but not their source.
It instead places the two exact action executions on the Miss Isles source lane; the Viewer
hides `CardModifyAttribute` and renders `CardReload` as a generic status event.

The game contracts establish the safe part of the association:

- `TActionCardReload` is a valued card action;
- the native tooltip adapter maps it to `ReloadAmount`;
- the same raw frame records the positive Ammo transition on its exact target.

For legacy reports, the Viewer can therefore resolve action details conservatively:

1. match a unique `CardReload` to a unique positive Ammo transition on the same exact target;
2. reserve that transition;
3. match a unique remaining `CardModifyAttribute` to a unique remaining visible modifier
   transition on the same target;
4. fail closed whenever either side is ambiguous.

This does not assign an unknown source to a raw attribute record. It enriches an execution whose
source and target are already explicit, and only when the action/transition pairing is unique.

## Candidate causes and approaches

1. **The report contains the required fields, but Inspector grouping drops them.**
   - Fix the Viewer projection/relationship rendering.
   - Preserve target-mode implicit-target suppression while rendering targets in source mode.

2. **The report event omits target or transition fields even though adjacent raw events contain them.**
   - Fix the report projector at the exact action/attribute mapping boundary.
   - Do not infer a target merely from same-frame proximity unless the game action establishes that relationship.

3. **The game action is self-targeting and the current source-mode tree suppresses source-equals-target.**
   - Render an explicit self-target in source mode because the selected lane represents the source, not the affected endpoint.

4. **The Miss Isles marker is a diagnostic/fallback event rather than a supported attribute change.**
   - Identify the exact native action and attribute enum from the report and decompiled game source.
   - Either classify it into a concrete supported attribute semantic or filter it if it has no product meaning; do not leave a detail-less `Status change`.

## Settled implementation

- Preserve target mode as the exact target-side projection.
- In source mode, replace only uniquely resolved `CardReload` / `CardModifyAttribute`
  execution markers with attribute-detail projections that retain the execution's event ID,
  source, trigger source, and targets.
- Group those resolved executions into one generic lane `Attribute change` marker, while the
  hover/click Inspector expands the concrete native icon, label, signed delta, and transition.
- Keep unresolved executions hidden rather than reintroducing generic status noise.
- Render a compact visible relationship role above each related entity name:
  target mode shows `Source` / `Trigger source`; source mode shows `Target` /
  `Removed target`.
- Allow the inspected entity to appear as a target only in source mode, covering self-targeting
  effects without duplicating the implicit target in target mode.
- Teach new-report projection to quantify a unique `CardReload` from the exact Ammo transition,
  including its before/after values, while retaining the conservative legacy resolver for
  existing immutable reports.

## Verification

- Parse the exact frame-221 and frame-284 events from the supplied immutable report.
- Trace their normalized Viewer records and Inspector groups without relying on screenshot text.
- For game action semantics, cite the matching decompiled action/property behavior before changing projection logic.
- Add Chromium and WebKit behavior fixtures for:
  - source-mode Charge with an explicit target, including self-target;
  - a generic lane marker whose Inspector expands to concrete signed attribute details.
- Open the supplied report through localhost and verify the exact two markers in source mode.
- Run Viewer typecheck, pure tests, focused Playwright tests, full frontend tests, and the mod build.
