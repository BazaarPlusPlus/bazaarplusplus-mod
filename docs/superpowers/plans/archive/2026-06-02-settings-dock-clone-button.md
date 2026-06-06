# Settings Dock Clone Button Implementation Plan

> **Status: IMPLEMENTED — 历史归档（spent plan）。** 本计划描述的工作已全部落地（`BppSettingsDockPlacement.cs` / `BppNativeSettingsButtonClone.cs`）；复选框未回填不代表有未完成项。保留为历史记录，勿据此重做。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Revision note (2026-06-03):** This plan was rewritten after a multi-agent design review. Two scope decisions from the user: (1) **drop the BPP sprite swap entirely** — the clone keeps the native gear sprite for now; (2) **two visually identical gear buttons side by side are acceptable** for this version (left = BPP panel, right = native settings). The review's blocker and medium findings are folded in below — most importantly the **controller-clone lifecycle fix** (mirrors the established `BppKeybindSettingsPatch` clone pattern) and **stripping `BazaarButtonController`** off the clone.

**Goal:** Replace the current handwritten BazaarPlusPlus settings-dock activator with a clone of the native Setting button placed immediately to the left of the original Setting button, and connect the existing BPP settings panel to that cloned button. No sprite replacement in this version — the clone deliberately keeps the native Setting appearance.

**Architecture:** Keep the existing settings catalog, row definitions, and panel behavior in `BppSettingsDockController`. Change the dock activator creation path so that, instead of building a rectangular `Image/Button/Outline` from scratch, we **clone the native Setting button GameObject**, neutralize the native click/interactivity components, position it to the left of the anchor, and wire its click to the existing BPP panel.

The critical structural change vs the previous draft is **clone ordering**. The current code adds `BppSettingsDockController` to the anchor button and *then* `Instantiate`s that same GameObject — so the clone carries a copy of the controller whose `OnEnable` runs synchronously during `Instantiate` and throws. The fix mirrors the established prior art in `Patches/Settings/BppSettingsDockPatch.cs` → `BppKeybindSettingsPatch.cs:87-102`, which clones a **controller-free native object** and only afterwards `AddComponent`s the BPP controller. We adapt it:

- Create the stripped clone in `Attach` **before** adding the controller, so the clone is never made from a controller-bearing GameObject.
- Keep `BppSettingsDockController` **on the anchor** (not the clone), so the existing `OnRectTransformDimensionsChange` resize-tracking seam is preserved unchanged.
- Pass the already-created clone into `Initialize`.

As a result, `RemoveCopiedBppControllers` / `RemoveNestedDockArtifacts` are **not needed** and are not introduced.

**Tech Stack:** Unity uGUI, BepInEx 5, Harmony, C# 12, The Bazaar publicized game assemblies, existing `BppSettingsDockCatalog` / `ISettingsDockEntry` system.

---

## Risks & Assumptions

The review's highest-value findings were Unity-runtime hazards the previous draft never named. These are the load-bearing assumptions; each task that depends on one carries a runtime to-verify step (per `bazaarplusplus-mod/CLAUDE.md`: add a temporary probe on the main path, build + reload to verify, then remove or drop to Debug).

1. **Controller must never be cloned.** Cloning a GameObject that hosts `BppSettingsDockController` copies the controller; its `OnEnable → RefreshView` runs *synchronously* inside `Instantiate` and a cloned MonoBehaviour does **not** run C# field initializers, so `_rows` is `null` → `NullReferenceException`, swallowed by the patch `try/catch` → the clone silently fails to appear. **Mitigation:** clone before `AddComponent` (see Architecture). To-verify: probe confirms the clone has **no** `BppSettingsDockController`.
2. **The native Setting button is a `BazaarButtonController` (a `Button` subclass), not a plain `Button`.** `SettingDialogsView.cs:61` resolves it via `GetComponent<BazaarButtonController>()`; `BazaarButtonController.cs:10`. The game scans `GetComponentsInChildren<BazaarButtonController>(includeInactive:true)` and calls `SetUnInteractable` on the results (`HeroSelectView.cs:111-123`), which would **grey out our clone during an active run**. **Mitigation:** strip `BazaarButtonController` (and `ButtonCustom`) off the clone and replace with a neutral `UnityEngine.UI.Button`. To-verify: clone is not greyed on the Hero Select screen during an active run.
3. **Native click wiring is runtime, not persistent.** `ShowDialogs` is added at runtime in `SettingDialogsView.Start()` via `onClick.AddListener` (`SettingDialogsView.cs:66-73`); `Instantiate` does not copy runtime `UnityEvent` listeners, and the patch clones in `Awake` postfix *before* `Start` runs. The FightMenu anchor uses a `ButtonCustom` whose click is a C# `MouseClickEvent` Action (`FightMenuDialog.cs:59,146`), which `Instantiate` also does not copy. **Conclusion:** the clone will not re-open native settings; stripping is for component-scan safety (assumption 2), not click leakage. To-verify: clicking the clone never opens the native settings dialog.
4. **The clone's `localScale` is unknown** (the native button's scale is not visible in decompiled source). The BPP panel is a child of the clone, so panel on-screen size depends on it. **Mitigation:** compute panel local scale from the clone's actual `localScale` at runtime (see Task 3). To-verify: probe logs the clone's `lossyScale`; panel renders at the intended size across resolutions.

Accepted (not risks to mitigate): two visually identical gear buttons (no sprite, native label kept); BPP entry remains mouse-only.

---

## File Structure

- Modify `Patches/Settings/BppSettingsDockPatch.cs`
  - Keep locating `SettingDialogsView.MainMenuSettingOptionButton`, `SettingDialogsView.HeroSelectSettingOptionButton`, and `FightMenuDialog.SettingButton`.
  - Pass a placement key into `BppSettingsDockController.Attach`.

- Create `Game/Settings/BppSettingsDockPlacement.cs`
  - Owns clone object names, side, gap, and panel opening direction.
  - Per-anchor naming keeps multiple docks in the same parent from colliding.

- Modify `Game/Settings/BppSettingsDockController.cs`
  - `Attach` find-or-creates the **stripped native clone before adding the controller**, keeps the controller on the anchor, and passes the clone into `Initialize`.
  - Strip `BazaarButtonController` / `ButtonCustom` off the clone; add a neutral `Button`.
  - Wire the clone's click to the existing panel toggle.
  - **Delete** `UpdateDockButtonAccent` and its calls (see Task 3).

- Modify `Game/Settings/BppSettingsDockController.Presentation.cs`
  - Replace fixed-offset placement with "left of anchor" placement based on the anchor's runtime world bounds.
  - Compute panel local scale from the clone's actual `localScale`.
  - Add panel direction support (right-side anchor → open up-left; left-side anchor → up-right).
  - Remove the dead handwritten-activator helpers.

- **No** `BppSettingsDockSpriteProvider.cs` — sprite swap is out of scope for this version.

- Test/verify with:
  - `dotnet build BazaarPlusPlus.csproj`
    - **If building from a `.claude/worktrees/...` worktree**, append `-p:BPPInstallerSourcePath=<abs path to installer resources>` (see root `CLAUDE.md`); the relative installer-source path does not resolve at worktree depth and the build fails with MSB3030. Do **not** edit `BazaarPlusPlus.csproj` to work around this.
  - `open "steam://run/1617400"` (launch through Steam so runtime state is present)
  - Runtime screenshots at 16:9, ultrawide/windowed, and a low-height window.

---

### Task 1: Add Placement Model

**Files:**
- Create: `Game/Settings/BppSettingsDockPlacement.cs`
- Modify: `Patches/Settings/BppSettingsDockPatch.cs`

- [ ] **Step 1: Create the placement value object**

Add `Game/Settings/BppSettingsDockPlacement.cs`:

```csharp
#nullable enable

namespace BazaarPlusPlus.Game.Settings;

internal enum BppSettingsDockSide
{
    LeftOfAnchor,
    RightOfAnchor,
}

internal enum BppSettingsDockPanelDirection
{
    UpLeft,
    UpRight,
}

internal readonly struct BppSettingsDockPlacement
{
    private const float DefaultSiblingGap = 18f;

    private BppSettingsDockPlacement(
        string key,
        BppSettingsDockSide side,
        BppSettingsDockPanelDirection panelDirection,
        float siblingGap
    )
    {
        Key = key;
        Side = side;
        PanelDirection = panelDirection;
        SiblingGap = siblingGap;
    }

    internal string Key { get; }

    internal BppSettingsDockSide Side { get; }

    internal BppSettingsDockPanelDirection PanelDirection { get; }

    internal float SiblingGap { get; }

    internal string DockButtonObjectName => $"BPP_SettingsDockButton_{Key}";

    internal string PanelObjectName => $"BPP_SettingsDockPanel_{Key}";

    internal static BppSettingsDockPlacement LeftOfSettingButton(string key) =>
        new(key, BppSettingsDockSide.LeftOfAnchor, BppSettingsDockPanelDirection.UpLeft, DefaultSiblingGap);
}
```

> Note: this struct is pure string/enum derivation. Per the repo's no-coverage-theater rule, do **not** add a unit test that asserts `DockButtonObjectName == $"BPP_..._{key}"` — that is matching exact source text. If a non-trivial branch is added later, add a behavior test then.

- [ ] **Step 2: Update the patch to pass a placement key**

In `Patches/Settings/BppSettingsDockPatch.cs`, change `AttachDock` to accept a key:

```csharp
private static void AttachDock(Button? button, string key)
{
    if (button == null)
        return;

    BppSettingsDockController.Attach(button, BppSettingsDockPlacement.LeftOfSettingButton(key));
}
```

Call it:

```csharp
AttachDock(MainMenuSettingOptionButtonField?.GetValue(__instance) as Button, "MainMenu");
AttachDock(HeroSelectSettingOptionButtonField?.GetValue(__instance) as Button, "HeroSelect");
```

For `FightMenuDialog`:

```csharp
if (button != null)
    BppSettingsDockController.Attach(button, BppSettingsDockPlacement.LeftOfSettingButton("FightMenu"));
```

- [ ] **Step 3: Build**

```bash
dotnet build BazaarPlusPlus.csproj
```

Expected: build passes. The attach path now carries placement metadata; runtime behavior is otherwise unchanged at this point (the controller still creates its old handwritten button — replaced in Task 2).

---

### Task 2: Clone The Native Setting Button To The Left (Visual-Only)

**Files:**
- Modify: `Game/Settings/BppSettingsDockController.cs`
- Modify: `Game/Settings/BppSettingsDockController.Presentation.cs`

This task makes the clone appear and be click-inert so it can be validated before the panel is wired. The controller stays on the anchor; the clone is created before the controller is added.

- [ ] **Step 1: Rewrite `Attach` to clone-before-AddComponent**

Replace the current `Attach(Button anchorButton)` with the placement-aware, clone-first version:

```csharp
internal static void Attach(Button anchorButton, BppSettingsDockPlacement placement)
{
    if (anchorButton == null)
        return;

    var hostRect = anchorButton.transform.parent as RectTransform;
    if (hostRect == null)
        return;

    // Find-or-create the stripped clone BEFORE adding the controller, so the
    // clone (Instantiate of the native anchor) never carries a copy of
    // BppSettingsDockController. Mirrors BppKeybindSettingsPatch.cs:87-102.
    var dockButton =
        hostRect.Find(placement.DockButtonObjectName) as RectTransform
        ?? CreateStrippedDockButton(anchorButton, hostRect, placement);
    if (dockButton == null)
        return;

    var controller =
        anchorButton.GetComponent<BppSettingsDockController>()
        ?? anchorButton.gameObject.AddComponent<BppSettingsDockController>();
    controller.Initialize(anchorButton, placement, dockButton);
}
```

There are no non-patch callers today; do not keep a compatibility overload. If a compile error finds another caller, update it to pass a placement key.

- [ ] **Step 2: Add the static clone+strip helper**

```csharp
private static RectTransform? CreateStrippedDockButton(
    Button anchorButton,
    RectTransform hostRect,
    BppSettingsDockPlacement placement
)
{
    var cloneObject = UnityEngine.Object.Instantiate(
        anchorButton.gameObject,
        hostRect,
        worldPositionStays: false
    );
    cloneObject.name = placement.DockButtonObjectName;

    StripNativeButtonBehavior(cloneObject);

    var rect = cloneObject.GetComponent<RectTransform>();
    ConfigureDockButtonRect(rect);

    // Probe (Debug-only): confirm the clone is controller-free, the native
    // Selectable subclass is gone, and record its scale (see Risks 1/2/4).
    if (BppLog.IsDebug)
    {
        BppLog.Debug(
            LogCategory,
            $"Clone '{placement.Key}': hasDockController={cloneObject.GetComponent<BppSettingsDockController>() != null}, "
                + $"hasBazaarButtonController={cloneObject.GetComponent<BazaarButtonController>() != null}, "
                + $"hasButtonCustom={cloneObject.GetComponent<ButtonCustom>() != null}, "
                + $"localScale={rect.localScale}, lossyScale={rect.lossyScale}"
        );
    }

    return rect;
}

private static void StripNativeButtonBehavior(GameObject cloneObject)
{
    // ButtonCustom (FightMenu anchor) re-adds an onClick listener in Start();
    // destroy it before that deferred Start can run.
    foreach (var custom in cloneObject.GetComponents<ButtonCustom>())
        UnityEngine.Object.DestroyImmediate(custom);

    // The native Setting button is a BazaarButtonController : Button. Remove it
    // so the game's GetComponentsInChildren<BazaarButtonController> scans
    // (e.g. HeroSelectView.Show -> SetUnInteractable) can't grey out our clone.
    var native = cloneObject.GetComponent<BazaarButtonController>();
    if (native != null)
        UnityEngine.Object.DestroyImmediate(native);

    var button = cloneObject.GetComponent<Button>() ?? cloneObject.AddComponent<Button>();
    button.onClick.RemoveAllListeners();
    button.transition = Selectable.Transition.ColorTint;
    button.navigation = new Navigation { mode = Navigation.Mode.None };
    button.interactable = true;
    button.targetGraphic = cloneObject.GetComponent<Image>();
}
```

> `DestroyImmediate` is used (not `Destroy`) so two `Button`-derived components never coexist on the clone for a frame and the deferred `ButtonCustom.Start` never runs. This is one-time setup inside a Harmony postfix, not a gameplay hot path.

- [ ] **Step 3: Update controller fields and `Initialize` to take the clone**

```csharp
private BppSettingsDockPlacement _placement;
private bool _panelClickEnabled;
```

```csharp
private void Initialize(Button anchorButton, BppSettingsDockPlacement placement, RectTransform dockButton)
{
    _anchorButton = anchorButton;
    _placement = placement;
    _panelClickEnabled = false; // visual-only this task; flipped + removed in Task 3
    _dockButtonRect = dockButton;
    _dockButton = dockButton.GetComponent<Button>();
    if (_dockButton == null)
        return;

    ResolveTextStyle();

    _dockButton.onClick.RemoveAllListeners();
    _dockButton.onClick.AddListener(OnDockButtonClicked);

    SyncDockButtonPlacement();
    ApplyScreenshotSuppressionVisibility();
    // Do NOT call TryEnsurePanel() in this task — keeps the clone visual-only.
}
```

Delete the old `TryEnsureDockButton()` (its create/find responsibilities now live in `Attach`/`CreateStrippedDockButton`).

- [ ] **Step 4: Make the click visual-only for this task**

```csharp
private void OnDockButtonClicked()
{
    if (!_panelClickEnabled)
    {
        BppLog.Info(LogCategory, $"Visual-only cloned dock button clicked for placement '{_placement.Key}'.");
        return;
    }

    SetExpanded(!_isExpanded);
}
```

- [ ] **Step 5: Position the clone to the left of the anchor**

In `BppSettingsDockController.Presentation.cs`, keep `ConfigureDockButtonRect` minimal and **preserve the clone's native scale** (do not set `localScale` or `sizeDelta`):

```csharp
private static void ConfigureDockButtonRect(RectTransform rectTransform)
{
    rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
    rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
    rectTransform.pivot = new Vector2(0.5f, 0.5f);
    rectTransform.localRotation = Quaternion.identity;
}
```

Replace the body of `SyncDockButtonPlacement()`:

```csharp
private void SyncDockButtonPlacement()
{
    if (_anchorButton == null || _dockButtonRect == null)
        return;

    var parentRect = _dockButtonRect.parent as RectTransform;
    var anchorRect = _anchorButton.transform as RectTransform;
    if (parentRect == null || anchorRect == null)
        return;

    var corners = new Vector3[4];
    anchorRect.GetWorldCorners(corners);

    var centerWorld = (corners[0] + corners[2]) * 0.5f;
    var leftWorld = (corners[0] + corners[1]) * 0.5f;
    var rightWorld = (corners[2] + corners[3]) * 0.5f;

    var centerLocal = parentRect.InverseTransformPoint(centerWorld);
    var leftLocal = parentRect.InverseTransformPoint(leftWorld);
    var rightLocal = parentRect.InverseTransformPoint(rightWorld);
    var widthLocal = Mathf.Abs(rightLocal.x - leftLocal.x);

    var direction = _placement.Side == BppSettingsDockSide.LeftOfAnchor ? -1f : 1f;
    _dockButtonRect.localPosition = new Vector3(
        centerLocal.x + direction * (widthLocal + _placement.SiblingGap),
        centerLocal.y,
        _dockButtonRect.localPosition.z
    );
    _dockButtonRect.localRotation = Quaternion.identity;
}
```

Resize tracking is unchanged: the controller still lives on the anchor, so the existing `OnRectTransformDimensionsChange()` → `SyncDockButtonPlacement()` seam fires on anchor-rect changes.

- [ ] **Step 6: Build and runtime-verify visual-only placement**

```bash
dotnet build BazaarPlusPlus.csproj
open "steam://run/1617400"
```

Expected:

- A second native Setting-looking button appears immediately to the left of the original (identical appearance — this is intended; no sprite swap).
- Clicking the clone does **not** open the native settings dialog (logs the visual-only line instead).
- The original Setting button still opens the native dialog.
- The clone moves with the Hero Select view and stays aligned after window resize.
- **Risk-2 check:** start/continue a run, return to Hero Select — the clone is **not** greyed out.
- **Risk-1 check:** `BepInEx/LogOutput.log` has no `[BPP][BppSettingsDock]` NRE; the Debug probe shows `hasDockController=false`, `hasBazaarButtonController=false`.

Stop here for visual confirmation before wiring the panel.

---

### Task 3: Connect The Existing BPP Panel To The Clone

**Files:**
- Modify: `Game/Settings/BppSettingsDockController.cs`
- Modify: `Game/Settings/BppSettingsDockController.Presentation.cs`

- [ ] **Step 1: Enable the panel and remove the visual-only gate**

The visual-only checkpoint has served its purpose; remove `_panelClickEnabled` and the early-return branch so no transitional dead state ships. `OnDockButtonClicked` returns to:

```csharp
private void OnDockButtonClicked()
{
    SetExpanded(!_isExpanded);
}
```

In `Initialize`, drop the `_panelClickEnabled` line and create the panel after the clone is wired:

```csharp
private void Initialize(Button anchorButton, BppSettingsDockPlacement placement, RectTransform dockButton)
{
    _anchorButton = anchorButton;
    _placement = placement;
    _dockButtonRect = dockButton;
    _dockButton = dockButton.GetComponent<Button>();
    if (_dockButton == null)
        return;

    ResolveTextStyle();

    _dockButton.onClick.RemoveAllListeners();
    _dockButton.onClick.AddListener(OnDockButtonClicked);

    SyncDockButtonPlacement();
    ApplyScreenshotSuppressionVisibility();

    if (!TryEnsurePanel())
        return;

    RefreshView();
    SetExpanded(false);
}
```

Delete the `private bool _panelClickEnabled;` field.

- [ ] **Step 2: Use placement-specific panel object names**

In `TryEnsurePanel`, replace fixed `PanelObjectName` with `_placement.PanelObjectName`:

```csharp
var existingPanel = _dockButtonRect.Find(_placement.PanelObjectName) as RectTransform;
```

and:

```csharp
var panelObject = new GameObject(
    _placement.PanelObjectName,
    typeof(RectTransform),
    typeof(Image),
    typeof(Outline)
);
```

- [ ] **Step 3: Make the panel open away from the edge and fix its scale**

The panel is a child of the clone, and the clone keeps the native button's `localScale` (Risk 4). Compute the panel's local scale by dividing the desired on-screen scale by the clone's actual scale, instead of the old hardcoded `/ DockButtonScale`:

```csharp
private void ConfigurePanelRect(RectTransform rectTransform, BppSettingsDockPlacement placement)
{
    rectTransform.anchorMin = new Vector2(0f, 1f);
    rectTransform.anchorMax = new Vector2(0f, 1f);
    rectTransform.pivot =
        placement.PanelDirection == BppSettingsDockPanelDirection.UpRight
            ? new Vector2(0f, 0f)
            : new Vector2(1f, 0f);

    // Panel is a child of the clone; divide out the clone's local scale so the
    // panel's on-screen scale stays at PanelExpandedScale regardless of the
    // native button's scale.
    var cloneScale = _dockButtonRect != null ? _dockButtonRect.localScale.x : 1f;
    var panelScale = cloneScale > 0.0001f ? PanelExpandedScale / cloneScale : PanelExpandedScale;
    rectTransform.localScale = new Vector3(panelScale, panelScale, 1f);
    rectTransform.localRotation = Quaternion.identity;
    rectTransform.anchoredPosition =
        placement.PanelDirection == BppSettingsDockPanelDirection.UpRight
            ? new Vector2(8f, 0f)
            : new Vector2(-8f, 0f);
    rectTransform.sizeDelta = new Vector2(
        PanelWidth,
        CalculatePanelHeight(BppSettingsDockCatalog.Definitions.Count)
    );
}
```

`ConfigurePanelRect` becomes an instance method (it reads `_dockButtonRect`); update its existing call sites to `ConfigurePanelRect(panelRect, _placement);` and `ConfigurePanelRect(existingPanel, _placement);`. `PanelExpandedScale` is retained (still the on-screen target); `DockButtonScale` is not.

- [ ] **Step 4: Delete the custom dock-button accent (it can't work on the clone)**

`UpdateDockButtonAccent` requires both an `Image` and an `Outline` on the dock-button root and returns early otherwise (`BppSettingsDockController.cs:400-403`). The clone root has no `Outline`, so it would silently no-op. Rather than ship dead visual logic, **delete `UpdateDockButtonAccent` and its call sites** in `SetExpanded` and `RefreshView`.

Consequence (accepted for this version): the dock button has no custom "expanded"/"has-enabled-settings" tint; the panel appearing on click is the feedback, and the neutral `Button`'s `ColorTint` still gives hover/press response. If an expanded-state highlight is wanted later, tint the neutral `Button`'s `targetGraphic` — do not resurrect the Image+Outline assumption.

- [ ] **Step 5: Keep panel interactions identical to current behavior**

Do not change `EnsureRows()`, `CreateRow(...)`, `ActivateDefinition(...)`, `RefreshView()` (other than removing the `UpdateDockButtonAccent` call), or `ApplyRowState(...)`. These already wire rows to `BppSettingsDockDefinition.Activate()` and refresh status.

- [ ] **Step 6: Build and runtime-verify panel behavior**

```bash
dotnet build BazaarPlusPlus.csproj
open "steam://run/1617400"
```

Expected:

- Clicking the clone opens the BPP panel; clicking the original opens the native dialog.
- BPP panel rows toggle/open the same entries as before.
- Panel appears above the clone, does not cover the original Setting button, and **renders at the intended size** (Risk-4 check — confirm against the probed `lossyScale`).
- Screenshot / run-capture flows still hide the dock via `BeginScreenshotSuppression()`.

---

### Task 4: Layout Conflict Checks

**Files:**
- Modify only if runtime validation finds a problem:
  - `Game/Settings/BppSettingsDockController.Presentation.cs`

- [ ] **Step 1: Sibling order**

If the clone renders behind the original Setting button or other right-rail art, add `_dockButtonRect.SetAsLastSibling();` at the end of `SyncDockButtonPlacement()`. (Safe to apply pre-emptively.)

- [ ] **Step 2: Layout-group parent check**

The clone is positioned via `localPosition`. If the anchor's parent runs an automatic layout group, it will override manual positioning. If the clone drifts or snaps, add a `LayoutElement` with `ignoreLayout = true` to the clone. (The current handwritten dock uses the same parent without issue, so this is most likely only relevant to the FightMenu anchor; verify there specifically.)

- [ ] **Step 3: Clamp the panel inside the screen if it clips**

If the panel clips on narrow/low-height windows, clamp it against the **top-level Canvas / screen rect** (not the immediate parent, which may be a narrow container), and guard against a panel larger than the reference:

```csharp
private static void ClampPanelToRect(RectTransform panelRect, RectTransform referenceRect)
{
    var panelCorners = new Vector3[4];
    var refCorners = new Vector3[4];
    panelRect.GetWorldCorners(panelCorners);
    referenceRect.GetWorldCorners(refCorners);

    var panelWidth = panelCorners[2].x - panelCorners[0].x;
    var refWidth = refCorners[2].x - refCorners[0].x;
    var panelHeight = panelCorners[2].y - panelCorners[0].y;
    var refHeight = refCorners[2].y - refCorners[0].y;

    var delta = Vector3.zero;
    if (panelWidth <= refWidth)
    {
        if (panelCorners[0].x < refCorners[0].x)
            delta.x += refCorners[0].x - panelCorners[0].x;
        else if (panelCorners[2].x > refCorners[2].x)
            delta.x -= panelCorners[2].x - refCorners[2].x;
    }
    if (panelHeight <= refHeight)
    {
        if (panelCorners[2].y > refCorners[2].y)
            delta.y -= panelCorners[2].y - refCorners[2].y;
        else if (panelCorners[0].y < refCorners[0].y)
            delta.y += refCorners[0].y - panelCorners[0].y;
    }

    if (delta != Vector3.zero)
        panelRect.position += delta;
}
```

Call it after panel activation, passing the top-level Canvas rect as the reference.

- [ ] **Step 4: Event isolation**

- Original Setting button: opens native settings.
- Cloned BPP button: opens only the BPP panel.
- Left Back button: still returns to main menu.

---

### Task 5: Dead-Code Removal, Final Verification, Branch Wrap-Up

**Files:**
- All files changed in prior tasks.

- [ ] **Step 1: Remove the now-dead handwritten-activator code**

Unused `private const` fields do **not** raise a compiler error or a reliable warning, so a build cannot confirm they are gone — verify by `grep`. Delete the following (and confirm zero references first):

Methods:
- `SyncFloatingButton` (`Presentation.cs`)
- `ConfigureDockButtonVisual` (`Presentation.cs`)
- `CreateDockButtonLabel` (`Presentation.cs`)
- `UpdateDockButtonAccent` (`BppSettingsDockController.cs` — already removed in Task 3 Step 4)

Constants:
- `DockButtonObjectName`, `DockButtonLabelObjectName`, `PanelObjectName` (replaced by `BppSettingsDockPlacement`)
- `DockButtonWidth`, `DockButtonHeight` (clone keeps native size)
- `DockButtonOffsetX`, `DockButtonOffsetY` (replaced by left-of placement)
- `DockButtonScale` (clone keeps native scale)

Keep (still used): `HeaderObjectName`, `PanelWidth`, `PanelExpandedScale`, and all panel/row/header layout constants.

- [ ] **Step 2: Build**

```bash
dotnet build BazaarPlusPlus.csproj
```

Expected: build passes (remember the worktree `-p:BPPInstallerSourcePath` note if applicable).

- [ ] **Step 3: Runtime verification through Steam**

```bash
open "steam://run/1617400"
```

Check:

- Hero Select / active-run screen: clone sits left of native Setting and is **not greyed during an active run**.
- Main Menu: clone sits left of native Setting.
- Fight menu: clone appears left of native Setting **when the fight menu is open**, and is hidden when the fight menu is closed (it is a child of the fight-menu dialog subtree). In the end-of-run state where the native fight-menu Setting button does not open the dialog, the BPP dock being unavailable is acceptable.
- Window resize (16:9, ultrawide/windowed, low-height): clone stays aligned; panel does not clip.
- Panel opens from the clone, rows work, panel size looks right.
- Original Setting still opens native settings; Back button unchanged.
- `LogOutput.log` clean of BPP NRE/errors.

- [ ] **Step 4: Demote or remove the temporary probe**

Drop the Debug clone-component/scale probe from `CreateStrippedDockButton`, or leave it at `Debug` level if it stays useful (it is gated by `BppLog.IsDebug`, so it never emits in Release). Do not leave `Info`-level probes on the main path.

- [ ] **Step 5: Review diff**

```bash
git diff -- Patches/Settings/BppSettingsDockPatch.cs Game/Settings/BppSettingsDockController.cs Game/Settings/BppSettingsDockController.Presentation.cs Game/Settings/BppSettingsDockPlacement.cs
```

Expected:

- No edits under `decompiled/`.
- No unrelated formatting churn (if csharpier reformats files outside this change, commit those separately).
- No legacy fallback path that creates the old rectangular text button.
- No `RemoveCopiedBppControllers` / `RemoveNestedDockArtifacts` (unnecessary by construction).
- None of the dead symbols listed in Step 1 remain (`grep`-verified).

- [ ] **Step 6: Wrap up via the finishing sub-skill**

After user-approved runtime results, hand off to **`superpowers:finishing-a-development-branch`** for the commit / merge-to-master / push / branch-cleanup decision. Do **not** hand-write `git add`/`git commit` here. When the commit message is authored, use an imperative, no-prefix title (e.g. `Use cloned native button for BPP settings dock`) — no conventional-commit prefix — and include a `Release Notes:` section if a PR is opened.

---

## Acceptance Criteria

- A copied native Setting button is placed to the left of the original Setting button, on the same parent Canvas/layout context, and follows window resizing.
- The clone is created without ever cloning `BppSettingsDockController`; no NRE appears in `LogOutput.log`.
- The clone is a neutral `Button` (no `BazaarButtonController` / `ButtonCustom`) and is not greyed out by the game's component scans during an active run.
- Clicking the clone opens the existing BPP settings panel and preserves all current dock rows; the panel renders at the intended on-screen size.
- The original Setting button and the left Back button keep their original behavior.
- The clone keeps the native Setting appearance (no sprite swap, native label retained); two identical gear buttons side by side is the accepted state for this version.
- The dead handwritten-activator methods and constants are removed (grep-verified); no sprite provider is introduced.
- No files under `decompiled/` are modified.
