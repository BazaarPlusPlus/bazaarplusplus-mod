# Recap right-click is not received

## Background

Issue #187 adds per-item and per-skill combat impact details beside the native post-combat Recap Tooltip. The first implementation attached an `IPointerClickHandler` to each `RecapItemVisualController`; the first runtime correction changed that handler to `IPointerDownHandler`, added a `CardTooltipData` fallback, and made missing impact data render an explicit empty state.

## Current problem

After installing commit `7fe96c50`, right-clicking a Recap item still produces no visible response.

Verified on the 2026-07-30 23:14 run:

- The installed DLL timestamp is 23:02 and the plugin reports successful initialization from that DLL.
- No aggregate Harmony degradation is logged.
- No `post_combat_impact.interaction.observed` event appears:
  - no `recap_card_bound`;
  - no `recap_pointer_down_received`;
  - no shown/empty/failure outcome.
- The game process had exited by the time the log was inspected.
- Debug events may be filtered by the current BepInEx log-level configuration, but the absence of every Info outcome still proves that `ShowDetails` was not reached.

The visible symptom therefore occurs before source lookup and before native Tooltip construction.

## Candidate mechanisms

1. **Dynamic EventSystem handler is not on the effective pointer-down hierarchy.**
   `RecapItemVisualController` receives hover callbacks, but Unity may resolve pointer-down on a child handler and stop walking before the dynamically attached parent component.
2. **The Initialize prefix never leaves a usable click target.**
   The async Recap initialization or pooling path may replace/disable the object after the prefix binds it.
3. **Direct mouse state is required for this native proxy.**
   The reliable existing signal is native hover enter/exit. Track the currently hovered Recap proxy from those native methods, then read the right-button edge once per frame from the mounted feature controller.

## Selected next approach

Stop depending on a dynamically attached `IPointerDownHandler`.

- Patch the existing native `RecapItemVisualController.OnPointerEnter` and `OnPointerExit` lifecycle.
- Resolve and store the hovered card, Tooltip data, offset, and proxy from the already initialized native fields.
- In `PostCombatImpactController.Update`, consume a direct right-button-down edge only while Recap is open and a Recap proxy is hovered.
- Track skills through their native `SkillProxyRenderer.OnPointerEnter` and `OnPointerExit` lifecycle and use the same polled right-button edge as items.
- Log hover binding, right-button edge receipt, and final Tooltip outcome.

## Requirement correction

The impact data must not be appended to the original card Tooltip. Reusing native Tooltip UI means reusing a native container and visual language, not modifying the container that owns the card description.

Selected rendering path:

- Keep the original native card Tooltip unchanged and locked while the impact details are visible.
- Open the game's independent `AuxiliaryTooltipController` for the combat-impact content.
- Position that auxiliary Tooltip beside the primary card Tooltip with `UIPositioner.PositionRectRelativeToAnother`, preferring right then left and clamping vertically to the screen.
- If another native feature takes over the singleton auxiliary Tooltip, remove the custom content and unlock/clean the primary Tooltip before the native caller renders.
- Treat the native auxiliary show as an asynchronous request: readiness is proven only after the patched `AuxiliaryTooltipController.ShowAuxiliaryTooltipController` observes the expected anchor/header and its native positioning coroutine completes. `IsAuxiliaryTooltipDisplayed` alone can describe a stale or fading node.
- Defer the native auxiliary request until the frame after `StartCoroutine`: Unity advances a new coroutine to its first `yield` before returning its handle, so synchronous reuse of an already-active auxiliary host can otherwise invoke the Harmony callback before `_pendingShow` is assigned.
- Patch the auxiliary fade-out lifecycle as well as show takeover; native code can hide the auxiliary Tooltip without showing another one, and that hide must release the primary card Tooltip lock.
- `AuxiliaryTooltipController.PositioningCanvas` is null in the live Recap prefab. Match the game's secondary-tooltip strategy and pass `CardTooltipController.RootCanvasComponent` to `UIPositioner.PositionRectRelativeToAnother`.
- The live auxiliary prefab keeps both its positioning rect and `auxParent` at a narrow native default. Capture and restore their native width/height, explicitly size the custom content, and copy the rebuilt `auxParent` height back to the positioning rect before asking `UIPositioner` to place it.
- Put the opaque fill on the custom content root itself. A separate backdrop child cannot assume a safe draw order inside the serialized auxiliary hierarchy and can cover later custom content.
- Do not place a flexible TMP object directly under the auxiliary prefab's `HorizontalLayoutGroup`: its rect can be correct while its glyphs render behind the native surface. Give the horizontal row a flexible nested text column and place the TMP object inside that column, matching the source-summary layout.
- Omit unresolved `Custom_*` attribute changes. They are internal card-state fields without player-facing semantics and otherwise inflate the effect count with labels such as `Custom_0`.
- Right-clicking another Recap item or skill replaces both the primary source and the separate impact Tooltip; blank click or leaving Recap cleans both.
- Do not retain the embedded `BppTooltipSections` implementation as a fallback.

## Verification

Automated:

- focused PostCombatImpact tests;
- architecture guard that the retired dynamic pointer handler is absent;
- full repository tests;
- format and diff checks;
- installed DLL hash equals the built Debug DLL.

Runtime:

1. Restart the game.
2. Replay or complete one battle and open Recap.
3. Hover an item and right-click once.
4. Expect the ordinary native card Tooltip plus a separate adjacent “Combat impact” Tooltip, or the explicit “no attributable impact” empty state in that second Tooltip.
5. If not visible, inspect `post_combat_impact.interaction.observed`:
   - hover signal absent → native hover patch/field resolution failed;
   - hover present, right-button edge absent → mouse input adapter failed;
   - edge present → use the final outcome reason to isolate Tooltip creation.

Verified against the installed Debug DLL on 2026-07-31:

- Day 9 vs Roodles: Food Truck rendered an adjacent impact Tooltip with source art,
  multiple effect groups, target rows, and readable metrics.
- Day 6 vs Crossa: Cruise Ship exercised the dense multi-group layout; Venom
  exercised the compact single-group layout; changing sources replaced both
  Tooltips, and a blank click dismissed them.
- Day 6 vs Crossa: Quick Freeze rendered `1 use · 1 effect`,
  `Freeze → Haladie ×1 · 2s`; the internal `Custom_0` state was omitted.
- The auxiliary Tooltip positioned on either side of the native card Tooltip as
  screen space allowed.
- Runtime logs recorded hover, right-button, shown, replacement, and dismissed
  outcomes with no Tooltip rendering exception or degraded outcome.
