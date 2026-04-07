# Chest Scene Freeze Analysis

The mod does not directly patch any `TheBazaar.Feature.Chest` classes, but several Harmony patches target generic game classes (`CardController`, `CardTooltipController`, `NetMessageProcessor`) that are also used by the chest scene. This document lists the suspected interference points.

## Chest Scene Overview

The chest scene is driven by `ChestSceneController` (a state machine) with states defined under `TheBazaar.Feature.Chest.Scene.States`:

- **StateSelect** — chest wheel selection, calls `playerChestInventory.OpenChests()` over the network
- **StateOpen** — single-chest open animation, loads reward via `LoadSelectionChestRewardAsync()`
- **StateMultiSelect** — multi-chest spawn and lever UI
- **StateMultiOpen** — multi-chest open animation sequence

Chest prizes are displayed as card GameObjects, likely backed by `CardController`.

## Suspected Causes (by likelihood)

### 1. PreviewBoardSurface raycast blocking card interaction

**File:** `Patches/Showcase/PreviewBoardSurfacePatches.cs:9`
**Patch target:** `CardController.IsPointerOverThis` (Postfix)

This patch runs `Physics.RaycastAll` on every `IsPointerOverThis` call. If the closest hit has a `PreviewBoardSurfaceMarker` component in its parent hierarchy, the patch sets `__result = false`, making the card invisible to pointer checks.

**How it could cause a freeze:**
If a `PreviewBoardSurfaceMarker` GameObject survives scene transition (e.g. it lives on a DontDestroyOnLoad object or is parented to the mod's root), it would block all `CardController` interaction in the chest scene. The user would see the UI but be unable to click anything.

**Verification:**
Check whether any `PreviewBoardSurfaceMarker` objects exist in the chest scene. Log or breakpoint in the Postfix when the chest scene is active.

### 2. ShowcaseCardMarker on pooled/reused card objects

**File:** `Patches/Showcase/ShowcaseCardInteractionPatches.cs:7`
**Patch target:** `CardController.ProceedClick` (Prefix, returns false)

When a `CardController` has a `ShowcaseCardMarker` component, the patch returns `false`, completely swallowing the click. The marker is added by `MonsterPreviewItemCardFactory` and `MonsterPreviewSkillCardFactory`.

**How it could cause a freeze:**
If the game reuses or pools card GameObjects, a `ShowcaseCardMarker` from a previous preview session could remain attached to a card object that later appears as a chest reward. All clicks on that card would be silently ignored.

**Verification:**
In the chest scene, enumerate all `CardController` objects and check for unexpected `ShowcaseCardMarker` components.

### 3. MonsterLockShowcaseRuntime.Update() intercepting mouse clicks

**File:** `Game/MonsterPreview/MonsterLockShowcaseRuntime.cs:59`

The `Update()` method runs every frame. When `IsPreviewActive` is true (`_lockedCard != null`) and `_closeOnNextClickArmed` is true, it consumes the next left or right mouse click to close the preview overlay.

**How it could cause a freeze:**
If the user enters the chest scene while a monster preview is still "active" (e.g. `_lockedCard` references a destroyed-but-not-null Card object), the runtime would intercept the first click to "close" the preview. If `HideOverlay` fails silently (e.g. `_overlayController` is null after scene change), `_lockedCard` may not get cleared, causing every subsequent click to be consumed.

**Verification:**
Log `IsPreviewActive` and `_closeOnNextClickArmed` values when entering the chest scene. Check whether `HideOverlay` successfully sets `_lockedCard = null`.

### 4. Tooltip lock toggle interception

**File:** `Patches/Showcase/ShowcaseTooltipPatches.cs:88`
**Patch target:** `CardTooltipController.LockTooltipToggle` (Prefix)

When `MonsterPreviewFeature.UseCustomLivePreview` is true, right-click tooltip lock attempts pass through `MonsterLockShowcaseRuntime.TryConsumeNextClickToClosePreview` and `ShouldInterceptLockToggle` before reaching the original method.

**How it could cause a freeze:**
If these methods access destroyed Unity objects (e.g. `Data.CardAndSkillLookup?.GetCardController(card)` on a card from a previous scene), they could throw a `MissingReferenceException`. Since this is a Harmony Prefix, an unhandled exception would prevent the original `LockTooltipToggle` from executing, leaving the tooltip controller in a locked state.

**Verification:**
Wrap the prefix body in a try-catch and log exceptions. Check whether `currentCard` is a valid Unity object reference when in the chest scene.

### 5. Network message processing patches

**Files:**
- `Patches/RunLogging/RunInitializedPatch.cs:10` (Prefix on `NetMessageProcessor.ReceiveOrQueue`)
- `Patches/Combat/CombatReplayCapturePatch.cs:10` (Postfix on `NetMessageProcessor.ReceiveOrQueue`)

Both patches run on every network message. They filter by message type (`NetMessageRunInitialized`, `NetMessageGameSim`, `NetMessageCombatSim`) and should skip chest-related messages.

**How it could cause a freeze:**
If `BppRuntimeHost.EventBus.Publish()` throws for a chest-related message type that unexpectedly matches one of the type checks, it could block the message queue. The chest scene relies on async network calls (`OpenChests`) that wait for server responses; a blocked message queue would cause the state machine to hang indefinitely at the `await` in `StateSelect.OnOpen` or `StateOpen.Enter`.

**Verification:**
Check BepInEx logs for exceptions during chest opening. Add defensive try-catch around `EventBus.Publish` calls if not already present.

## Debugging Checklist

1. Reproduce the freeze and immediately check `BepInEx/LogOutput.log` for exceptions or error-level messages.
2. Determine the freeze type:
   - **Hard freeze** (UI completely unresponsive, Unity stops rendering) — likely an infinite loop or main-thread deadlock.
   - **Soft freeze** (UI renders but clicks do nothing) — likely one of the interaction-blocking patches above.
   - **Async hang** (UI responsive but chest animation never completes) — likely a network message or async task issue.
3. Temporarily disable individual patches to isolate the cause:
   - Comment out `ShowcaseCardClickPatch` and `PreviewBoardSurfaceBlocksUnderlyingCardsPatch` first (highest suspicion).
   - Then try disabling `MonsterLockShowcaseRuntime` by skipping `AddComponent<MonsterLockShowcaseRuntime>()` in `Plugin.cs`.
4. If the issue is intermittent, check whether it correlates with having viewed a monster preview before opening chests.
