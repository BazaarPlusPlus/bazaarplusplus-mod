# Sell Hotkey Regression Debug Plan

## Status

Draft, investigation plan. No code changes have landed from this note yet.

## Problem

After BazaarPlusPlus is installed, the official sell-item hotkey can intermittently stop working.

The mod does not implement its own sell hotkey, so the failure should be treated as an interaction regression against the game's native input and card-selection pipeline, not as a missing BazaarPlusPlus command.

## Native Sell Path

Current game behavior, verified against the local installed `TheBazaarRuntime.dll` on 2026-05-31:

1. `BoardManager` subscribes to the official `Gameplay/SellItem` action.
2. When the action performs, `BoardManager.OnSellItemHotkeyClicked()` runs.
3. The handler checks that the game is not in combat or replay, and that board interaction is allowed.
4. It calls `RaycastForComponent<ItemController>()` using the current pointer screen position.
5. The sell only proceeds if the raycast hit resolves to a player-owned `ItemController`, stash rules allow it, and no card tooltip controller is locked.
6. The final sell call is `AppState.CurrentState.SellCardCommand(itemController.CardData as ItemCard)`.

Important detail: this is a physics raycast from the mouse position, not a UI button click. UGUI `GraphicRaycaster` blockers can break UI interaction, but they do not directly block this physics raycast unless a BazaarPlusPlus object also contributes a collider or changes the pointer/state assumptions.

## Observed Signals

- BazaarPlusPlus uses `Ctrl` and `Shift` as default tooltip preview modifier hotkeys.
- `BppHotkeyService` creates separate `InputAction` instances for BazaarPlusPlus hotkeys and only checks conflicts among BazaarPlusPlus actions.
- Tooltip preview mode changes can hide and re-show the current tooltip in the same frame path.
- The official sell handler explicitly rejects the sell if `Data.TooltipParentComponent.HasAnyLockedTooltipControllers()` is true.
- The local BepInEx log currently warns that `TheBazaar.UI.KeyBindController._keybindAction` no longer exists. The installed game has moved keybind rows to `InputActionReference _action`, so patches that reflect `_keybindAction` are stale against the live game.
- The older collection panel input blocker issue is a useful prior failure pattern, but the current collection overlay default is polled hover without an overlay `GraphicRaycaster`, so that specific blocker is not the primary sell-hotkey suspect.

## Root-Cause Hypotheses

### H1: Tooltip lock or preview refresh blocks sell

The official sell handler silently refuses to sell when any card tooltip controller is locked. BazaarPlusPlus tooltip preview changes can refresh tooltip controllers while the pointer is over a card. If preview refresh and native lock state overlap with the sell action frame, the official handler can early-return even though the key was received.

This is the highest-priority hypothesis because it directly matches a native guard in the sell handler.

### H2: Hotkey binding conflict is not detected

The game now stores official input bindings in `InputSystemActions` action maps, including `Gameplay/SellItem` and `Gameplay/LockTooltip`. BazaarPlusPlus stores its own bindings separately in `BazaarPlusPlus.cfg`.

Today BazaarPlusPlus rejects conflicts only between BazaarPlusPlus actions. It does not compare against native `Gameplay/SellItem`, `Gameplay/LockTooltip`, `Gameplay/ToggleStash`, or menu shortcuts. If a user binds BazaarPlusPlus preview to the same control as sell, or sell to `Ctrl`/`Shift`, both systems can react to the same input.

### H3: Stale keybind reflection leaves settings rows partially broken

`NativeKeybindLabelPatch` still reflects `_keybindAction`, but the live game no longer has that field. The log warning does not prove the sell action is disabled, but it proves our keybind integration is using an obsolete shape. Any fix in the keybind area should update this compatibility layer before relying on settings-page behavior.

### H4: A BazaarPlusPlus visual object changes the physics raycast target

The native sell handler uses `Physics.Raycast(...)` and then `hitInfo.collider.GetComponentInParent<ItemController>()`. If a BazaarPlusPlus preview object creates or preserves a collider in front of a real card, the raycast can hit the wrong object and return no `ItemController`.

This is lower confidence for the current collection panel path because its overlay is UGUI/UITK and defaults away from raycast blockers, but it remains a required diagnostic for HistoryPanel, CardSetPreview, and native cloned `CardPreviewBase` objects.

### H5: Native input context is not Gameplay

In the current game, official actions are gated by `InputContextStack`; `Gameplay` actions are enabled only when the active context is `Gameplay`. BazaarPlusPlus mostly polls `Keyboard.current` or creates standalone `InputAction`s, so it can still react while native `Gameplay/SellItem` is disabled by a modal, loading, rebind, or debug-console context.

This explains cases where BazaarPlusPlus appears alive but the official sell action does not fire.

## Debug Instrumentation Plan

Add a temporary diagnostic feature guarded by a config flag, for example:

```ini
[Diagnostics]
SellHotkeyDebug = false
```

When enabled, instrument the official sell flow with Harmony patches and log one compact line per attempted sell:

- whether `Gameplay/SellItem` performed
- active input context if accessible
- `Gameplay/SellItem.enabled` and effective binding
- current state type and `AllowInteraction`
- pointer screen position
- physics raycast hit object, layer, collider type, and parent `ItemController` presence
- item instance id, owner, section, and hidden unsellable status when an item is found
- `HasAnyLockedTooltipControllers()` and primary / secondary lock status
- BazaarPlusPlus tooltip preview mode and configured hotkeys
- visibility of HistoryPanel, CollectionPanel, LiveBuildPanel (CapsLock toggle), CombatStatusBar, and end-of-run blocker

Do not log every frame. Only log on `SellItem` perform, and optionally on BazaarPlusPlus tooltip refresh while the sell key is pressed.

## Fix Plan

1. Update keybind compatibility first.
   - Replace `_keybindAction` reflection with the current live game shape based on `InputActionReference _action`.
   - Keep a fallback for older game versions if the old field exists.
   - Stop emitting repeated Harmony field warnings in normal gameplay.

2. Add native conflict detection.
   - At startup, read official effective bindings for `Gameplay/SellItem`, `Gameplay/LockTooltip`, `Gameplay/ToggleStash`, `MenuShortcuts/OpenSettings`, and core navigation if practical.
   - Compare them with BazaarPlusPlus `EnchantPreview`, `UpgradePreview`, and `CollectionPanel`.
   - Warn in logs and in the BazaarPlusPlus keybind row when a conflict exists.
   - Reject new BazaarPlusPlus bindings that collide with native gameplay bindings unless there is an explicit future override policy.

3. Make tooltip refresh sell-safe.
   - If the official sell binding is pressed this frame, defer BazaarPlusPlus tooltip refresh by one frame.
   - Do not unlock user-locked native tooltips just to force sell through.
   - If diagnostics show BazaarPlusPlus-created preview tooltip is the blocker, track ownership of those preview tooltips and only clear/defer our own preview state.

4. Audit preview objects for physics colliders.
   - For all BazaarPlusPlus-created preview roots and cloned native preview cards, ensure visual-only objects are on an ignore-raycast layer or have colliders disabled.
   - Do not change colliders on real board cards.
   - Validate LiveBuildPanel, HistoryPanel preview, and CollectionPanel separately because they create preview card objects in different hosts.

5. Align BazaarPlusPlus hotkeys with native input context where needed.
   - Global toggles such as HistoryPanel and CollectionPanel can keep standalone polling if intentional.
   - Tooltip preview modifiers should avoid changing tooltip state while native `Gameplay` actions are not active, unless the behavior is explicitly desired.

## Verification Matrix

Verify with diagnostics on first, then off:

- normal board item, tooltip unlocked
- normal board item, tooltip manually locked
- stash item with stash closed
- stash item with stash open
- sell binding set to a normal key
- sell binding set to `Ctrl` or `Shift`
- BazaarPlusPlus enchant / upgrade preview held while selling
- after opening and closing HistoryPanel
- after opening and closing CollectionPanel
- after toggling LiveBuildPanel (CapsLock)
- after combat ends and the next non-combat state becomes interactive
- while a modal or settings screen is open, confirming native no-op is expected

Expected end state:

- If the official key fires and the pointer is over a valid sellable player item, BazaarPlusPlus should not create extra locked-tooltip or raycast conditions that make the native handler return early.
- If the native game intentionally refuses the sell, diagnostics should name the exact native guard rather than leaving the symptom as "hotkey failed."
