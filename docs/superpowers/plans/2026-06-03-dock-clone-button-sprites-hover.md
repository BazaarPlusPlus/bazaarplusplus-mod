# Dock Clone Button Sprites And Hover Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the center icons of the two native-cloned Bazaar++ dock buttons with explicit custom Sprite resources while preserving the native gold-ring button frame, and give both buttons a native-quality hover/pressed visual state.

**Architecture:** Keep the current clone-based Unity uGUI architecture: both the BPP settings dock and the Collection Panel dock continue to clone the native Setting button and then strip native behavior. The native button structure separates the root `Image` frame from the child `ButtonIcon`; therefore the replacement should target the child icon image first and leave the root frame/default sprite intact. Add a small shared sprite loader and a shared visual configurator in `Game/Settings/` so both controllers keep one button pipeline while receiving explicit icon semantics and hover colors. The fresh-clone path captures and marks the native `ButtonIcon`; the existing-clone fallback only searches the marked icon/native button subtree and must never traverse BPP settings panel children. Do not move feature policy into `GameInterop/`; this is UI feature infrastructure, not a reusable game-runtime adapter.

**Tech Stack:** C# 12, Unity uGUI `Button`/`Image`/`Selectable.ColorBlock`, embedded PNG resources, `Texture2D.LoadImage`, `Sprite.Create`, BepInEx 5.

---

## Current State Evidence

- `Patches/Settings/BppSettingsDockPatch.cs:46-50` attaches both dock buttons beside the native Setting button in main menu / hero select; `Patches/Settings/BppSettingsDockPatch.cs:71-78` does the same in `FightMenuDialog`.
- `Game/Settings/BppNativeSettingsButtonClone.cs:33-40` creates the clone by `Object.Instantiate(anchorButton.gameObject, hostRect, false)`, so the default visible art is whatever native `Image`/children the anchor prefab already has.
- `Game/Settings/BppNativeSettingsButtonClone.cs:74-108` strips `ButtonCustom`, `BazaarButtonController`, copied clone-owner components, and nested `Button`s, then installs a neutral root `Button` with `transition = ColorTint`.
- `Game/Settings/BppSettingsDockController.cs:56-63` wires the shared clone to the BPP settings panel; `Game/CollectionPanel/CollectionPanelDockButtonController.cs:35-42` wires the same clone helper to `CollectionPanel.OpenFromDockButton()`.
- `BazaarPlusPlus.csproj:28-34` currently embeds only JSON data and fonts. `Resources/` currently contains fonts only, so there is no existing dock-button sprite asset path.
- `Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:365-382` and `Game/CardSetPreview/CardSetBuildDataRepository.cs:412-438` are the local embedded-resource lookup pattern to reuse.
- `decompiled/TheBazaarRuntime/BazaarButtonController.cs:10-28` shows the native button type owns root-state sprites (`ClickedImage`, `DefaultImage`) and a separate child `Image ButtonIcon`.
- `decompiled/TheBazaarRuntime/BazaarButtonController.cs:91-94` changes `base.image.sprite` for selected/default state, while `decompiled/TheBazaarRuntime/BazaarButtonController.cs:187-198` changes `ButtonIcon.color` for disabled state. This is the strongest code evidence that the outer frame/background and center icon are separate visuals.
- Because `Game/Settings/BppNativeSettingsButtonClone.cs:74-108` removes `BazaarButtonController`, hover must be configured on the neutral Unity `Button`; however the visual replacement should not replace the root frame unless runtime hierarchy inspection proves no child icon image exists.

---

## File Structure

- Create `Resources/DockButtons/bpp-settings-icon.png`
  - 128x128 PNG, transparent background, ivory tuning-sliders-plus icon only; no gold ring and no black circular backing.
- Create `Resources/DockButtons/collection-panel-icon.png`
  - 128x128 PNG, transparent background, ivory open-compendium/cards icon only; no gold ring and no black circular backing.
- Modify `BazaarPlusPlus.csproj`
  - Embed both PNG files.
  - Add a `UnityEngine.ImageConversionModule` reference because `Texture2D.LoadImage(...)` is used by the sprite loader.
- Modify `Game/Settings/BppSettingsDockPlacement.cs`
  - Add a `ButtonIconKind` property and require placement factories to receive the icon kind explicitly, so button function is not inferred from left/above positioning.
- Modify `Patches/Settings/BppSettingsDockPatch.cs`
  - Pass `SettingsDock` for the BPP settings clone and `CollectionPanel` for the card collection clone at each attach call site.
- Create `Game/Settings/BppDockButtonIconKind.cs`
  - Defines the two sprite slots.
- Create `Game/Settings/BppDockButtonSpriteProvider.cs`
  - Loads embedded PNG bytes once, creates a `Sprite`, and caches it.
- Create `Game/Settings/BppDockButtonVisuals.cs`
  - Applies the icon sprite to the cloned native `ButtonIcon`/child image, preserves the root frame sprite, and configures normal/hover/pressed/disabled colors.
- Modify `Game/Settings/BppNativeSettingsButtonClone.cs`
  - Capture the cloned native `BazaarButtonController.ButtonIcon` before stripping native behavior, then apply the requested icon sprite and hover `ColorBlock`.
  - For existing clones, re-apply only the center icon sprite and button colors; do not mutate unrelated child graphics after panels may have been added.
- Modify tests under `tests/SettingsDockRegistry.Tests/`
  - Assert placement factories choose the correct icon kind.
  - Add hover color model tests without creating Unity `GameObject`/`Image` instances.
  - If tests directly reference `UnityEngine.Color`, copy/reference `UnityEngine.CoreModule.dll` in the test project using the existing `CardSetBuildRecommendationTier.Tests` pattern.

---

## Resource Decision

The generated ImageGen candidate sheet is style reference only; because the native button separates the gold frame from the center icon, production assets should be icon-only:

1. `bpp-settings-icon.png`: use the tuning-sliders-plus icon. Do not use a plain gear; it is too close to the native Setting button and repeats the ambiguity this change is meant to remove. Do not use a text-heavy `B++` monogram as the primary version unless visual review proves it remains readable at the in-game dock size.
2. `collection-panel-icon.png`: use the open compendium/book with small card silhouettes. It reads more like "card encyclopedia" than a simple stacked-card icon, which can be confused with a deck or card set.

Only two icon PNGs are required for this phase. Hover and pressed states should be implemented through Unity `Selectable.ColorBlock` on the neutral cloned `Button` root; this keeps the resource set small and preserves the game's native frame. Generate separate `*-hover.png` / `*-pressed.png` assets only if runtime review shows color tint cannot produce a visible native-like state.

Production export requirements:

```text
canvas: 128x128 square
visible icon diameter: about 96-116 px
transparent padding: about 6-18 px on all sides
background: real alpha transparency, not checkerboard pixels
style: pale ivory icon with subtle mint highlights and thin gold bevel/rim accents
readability: icon must still be identifiable when displayed at 56-72 px
settings detail: plus mark sits in the upper-right quadrant and remains visible at dock size
collection detail: card faces include simple black panels plus gem marks; avoid excessive interior line detail
```

Keep the full gold-ring ImageGen buttons only as visual reference or fallback. If runtime hierarchy inspection shows the native Setting clone has no child icon image in a particular scene, then create a separate fallback plan that replaces the whole root `Image` with a full-button sprite and disables old child graphics. Do not make the full-button path the default.

---

### Task 1: Add Sprite Resources To The Main Assembly

**Files:**
- Create: `Resources/DockButtons/bpp-settings-icon.png`
- Create: `Resources/DockButtons/collection-panel-icon.png`
- Modify: `BazaarPlusPlus.csproj:28-34`

- [ ] **Step 1: Add the final PNG assets**

Place the approved PNG files at these exact paths:

```text
Resources/DockButtons/bpp-settings-icon.png
Resources/DockButtons/collection-panel-icon.png
```

Asset requirements:

```text
format: PNG
size: 128x128 preferred, square required
background: transparent
edge padding: 6-18 px
color: ivory/gold readable on the native black button center
settings icon: tuning sliders with the plus mark in the upper-right quadrant
collection icon: open compendium/cards with black card panels and small gem marks
```

- [ ] **Step 2: Embed the assets**

Add the two resources to the existing embedded-resource item group:

```xml
<EmbeddedResource Include="Resources\DockButtons\bpp-settings-icon.png" />
<EmbeddedResource Include="Resources\DockButtons\collection-panel-icon.png" />
```

Add this reference near the other Unity references. Do this up front; `Texture2D.LoadImage(...)` is provided by `UnityEngine.ImageConversionModule`, so treating it as optional only moves a predictable failure later in the task.

```xml
<Reference Include="UnityEngine.ImageConversionModule">
  <HintPath>$(ManagedPath)\UnityEngine.ImageConversionModule.dll</HintPath>
</Reference>
```

- [ ] **Step 3: Build**

Run:

```bash
dotnet build BazaarPlusPlus.csproj
```

Expected: build succeeds and the two PNGs are part of `BazaarPlusPlus.dll` as embedded manifest resources.

- [ ] **Step 4: Produce a native-size visual comparison**

Create a comparison sheet that renders:

```text
native Icon_RingBtn_Gear_TUI at 56-72 px
bpp-settings-icon.png at the same size
collection-panel-icon.png at the same size
```

Expected: both custom icons are as legible as the native gear at dock-button size, the settings plus mark reads in the upper-right quadrant, and the collection icon still shows black card panels with gem marks. If either icon only reads at full 128x128 size, revise the PNG before continuing.

---

### Task 2: Give Placements Explicit Icon Semantics

**Files:**
- Create: `Game/Settings/BppDockButtonIconKind.cs`
- Modify: `Game/Settings/BppSettingsDockPlacement.cs:18-61`
- Modify: `Patches/Settings/BppSettingsDockPatch.cs:46-78`
- Modify: `tests/SettingsDockRegistry.Tests/BppSettingsDockGeometryTests.cs`

- [ ] **Step 1: Add the icon enum**

Create `Game/Settings/BppDockButtonIconKind.cs`:

```csharp
#nullable enable

namespace BazaarPlusPlus.Game.Settings;

internal enum BppDockButtonIconKind
{
    SettingsDock,
    CollectionPanel,
}
```

- [ ] **Step 2: Extend placement**

Change `BppSettingsDockPlacement` so the constructor stores an icon kind:

```csharp
private BppSettingsDockPlacement(
    string key,
    BppSettingsDockSide side,
    BppSettingsDockPanelDirection panelDirection,
    float siblingGap,
    BppDockButtonIconKind buttonIconKind
)
{
    Key = key;
    Side = side;
    PanelDirection = panelDirection;
    SiblingGap = siblingGap;
    ButtonIconKind = buttonIconKind;
}

internal BppDockButtonIconKind ButtonIconKind { get; }
```

Update the factories so callers pass function semantics explicitly instead of relying on left/above position:

```csharp
internal static BppSettingsDockPlacement LeftOfSettingButton(
    string key,
    BppDockButtonIconKind buttonIconKind
) =>
    new(
        key,
        BppSettingsDockSide.LeftOfAnchor,
        BppSettingsDockPanelDirection.UpLeft,
        DefaultSiblingGap,
        buttonIconKind
    );

internal static BppSettingsDockPlacement AboveSettingButton(
    string key,
    BppDockButtonIconKind buttonIconKind
) =>
    new(
        key,
        BppSettingsDockSide.AboveAnchor,
        BppSettingsDockPanelDirection.UpLeft,
        DefaultSiblingGap,
        buttonIconKind
    );
```

- [ ] **Step 3: Update attach call sites**

Update the main-menu / hero-select attach calls:

```csharp
CollectionPanelDockButtonController.Attach(
    button,
    BppSettingsDockPlacement.AboveSettingButton(
        $"CollectionPanel_{key}",
        BppDockButtonIconKind.CollectionPanel
    )
);
BppSettingsDockController.Attach(
    button,
    BppSettingsDockPlacement.LeftOfSettingButton(key, BppDockButtonIconKind.SettingsDock)
);
```

Update the fight-menu attach calls:

```csharp
CollectionPanelDockButtonController.Attach(
    button,
    BppSettingsDockPlacement.AboveSettingButton(
        "CollectionPanel_FightMenu",
        BppDockButtonIconKind.CollectionPanel
    )
);
BppSettingsDockController.Attach(
    button,
    BppSettingsDockPlacement.LeftOfSettingButton(
        "FightMenu",
        BppDockButtonIconKind.SettingsDock
    )
);
```

- [ ] **Step 4: Update existing placement tests**

Update every existing test placement factory call in `BppSettingsDockGeometryTests.cs` to pass the icon kind that matches the scenario:

```csharp
var placement = BppSettingsDockPlacement.LeftOfSettingButton(
    "MainMenu",
    BppDockButtonIconKind.SettingsDock
);
```

```csharp
var placement = BppSettingsDockPlacement.AboveSettingButton(
    "CollectionPanel",
    BppDockButtonIconKind.CollectionPanel
);
```

- [ ] **Step 5: Add placement tests**

Add assertions to the existing placement tests:

```csharp
Assert.Equal(BppDockButtonIconKind.SettingsDock, mainMenu.ButtonIconKind);
Assert.Equal(BppDockButtonIconKind.CollectionPanel, placement.ButtonIconKind);
```

- [ ] **Step 6: Run targeted tests**

Run:

```bash
dotnet test tests/SettingsDockRegistry.Tests/SettingsDockRegistry.Tests.csproj
```

Expected: placement tests pass and existing geometry behavior is unchanged.

---

### Task 3: Add A Cached Embedded PNG To Sprite Loader

**Files:**
- Create: `Game/Settings/BppDockButtonSpriteProvider.cs`

- [ ] **Step 1: Create the provider**

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.Settings;

internal static class BppDockButtonSpriteProvider
{
    private const string LogCategory = "BppDockButtonSprite";

    private static readonly Dictionary<BppDockButtonIconKind, Sprite?> _cache = new();

    internal static Sprite? Get(BppDockButtonIconKind kind)
    {
        if (_cache.TryGetValue(kind, out var cached))
            return cached;

        var suffix = kind switch
        {
            BppDockButtonIconKind.SettingsDock => "Resources.DockButtons.bpp-settings-icon.png",
            BppDockButtonIconKind.CollectionPanel =>
                "Resources.DockButtons.collection-panel-icon.png",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        var sprite = LoadSprite(suffix, kind.ToString());
        _cache[kind] = sprite;
        return sprite;
    }

    private static Sprite? LoadSprite(string resourceSuffix, string spriteName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName == null)
        {
            BppLog.Warn(LogCategory, $"Embedded sprite resource not found suffix={resourceSuffix}");
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return null;

        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);

        var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false)
        {
            name = $"BPP_{spriteName}_Texture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        if (!texture.LoadImage(bytes.ToArray(), markNonReadable: false))
        {
            UnityEngine.Object.Destroy(texture);
            BppLog.Warn(LogCategory, $"Failed to decode embedded sprite resource {resourceName}");
            return null;
        }

        texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        var sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect
        );
        sprite.name = $"BPP_{spriteName}_Sprite";
        return sprite;
    }
}
```

- [ ] **Step 2: Build**

Run:

```bash
dotnet build BazaarPlusPlus.csproj
```

Expected: provider compiles with the `UnityEngine.ImageConversionModule` reference added in Task 1.

---

### Task 4: Centralize Sprite And Hover Styling

**Files:**
- Create: `Game/Settings/BppDockButtonVisuals.cs`
- Modify: `tests/SettingsDockRegistry.Tests/SettingsDockRegistry.Tests.csproj`
- Test: `tests/SettingsDockRegistry.Tests/BppSettingsDockGeometryTests.cs`

- [ ] **Step 1: Add hover color data plus Unity application**

```csharp
#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal readonly struct BppDockButtonColorSpec(
    Color normal,
    Color highlighted,
    Color pressed,
    Color selected,
    Color disabled,
    float fadeDuration
)
{
    internal Color Normal { get; } = normal;
    internal Color Highlighted { get; } = highlighted;
    internal Color Pressed { get; } = pressed;
    internal Color Selected { get; } = selected;
    internal Color Disabled { get; } = disabled;
    internal float FadeDuration { get; } = fadeDuration;
}

internal static class BppDockButtonVisuals
{
    private const string IconObjectName = "BPP_DockButtonIcon";
    private const string SettingsPanelPrefix = "BPP_SettingsDockPanel_";

    private static readonly Color SettingsHover = new(1f, 0.90f, 0.58f, 1f);
    private static readonly Color CollectionHover = new(0.62f, 0.86f, 1f, 1f);

    internal static BppDockButtonColorSpec ResolveColors(BppDockButtonIconKind kind)
    {
        var hover = kind == BppDockButtonIconKind.CollectionPanel ? CollectionHover : SettingsHover;
        return new BppDockButtonColorSpec(
            normal: Color.white,
            highlighted: hover,
            pressed: Color.Lerp(hover, Color.black, 0.18f),
            selected: hover,
            disabled: new Color(1f, 1f, 1f, 0.34f),
            fadeDuration: 0.08f
        );
    }

    internal static Image? ResolveNativeIconImage(GameObject cloneObject)
    {
        return cloneObject
            .GetComponentInChildren<BazaarButtonController>(includeInactive: true)
            ?.ButtonIcon;
    }

    internal static void Apply(
        GameObject cloneObject,
        BppDockButtonIconKind kind,
        Image? explicitIcon,
        bool freshClone
    )
    {
        if (cloneObject == null)
            return;

        var frame = cloneObject.GetComponent<Image>() ?? cloneObject.AddComponent<Image>();
        var sprite = BppDockButtonSpriteProvider.Get(kind);
        var icon = explicitIcon ?? FindMarkedIconImage(cloneObject) ?? FindIconImage(cloneObject);
        if (sprite != null && icon != null)
            ApplyIcon(icon, sprite);

        frame.raycastTarget = true;

        if (freshClone)
            DisableUnusedChildRaycasts(cloneObject.transform);

        var button = cloneObject.GetComponent<Button>() ?? cloneObject.AddComponent<Button>();
        button.targetGraphic = frame;
        button.transition = Selectable.Transition.ColorTint;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.interactable = true;

        var spec = ResolveColors(kind);
        button.colors = new ColorBlock
        {
            normalColor = spec.Normal,
            highlightedColor = spec.Highlighted,
            pressedColor = spec.Pressed,
            selectedColor = spec.Selected,
            disabledColor = spec.Disabled,
            colorMultiplier = 1f,
            fadeDuration = spec.FadeDuration,
        };
    }

    private static Image? FindMarkedIconImage(GameObject cloneObject)
    {
        var root = cloneObject.transform;
        foreach (var image in cloneObject.GetComponentsInChildren<Image>(includeInactive: true))
        {
            if (!image.gameObject.name.Equals(IconObjectName, StringComparison.Ordinal))
                continue;

            if (IsInsideSettingsPanel(image.transform, root))
                continue;

            return image;
        }

        return null;
    }

    private static Image? FindIconImage(GameObject cloneObject)
    {
        Image? best = null;
        var bestArea = float.MaxValue;
        var root = cloneObject.transform;
        foreach (var image in cloneObject.GetComponentsInChildren<Image>(includeInactive: true))
        {
            if (image.gameObject == cloneObject)
                continue;

            if (IsInsideSettingsPanel(image.transform, root))
                continue;

            var rect = image.transform as RectTransform;
            if (rect == null)
                continue;

            var size = rect.rect.size;
            var area = Mathf.Abs(size.x * size.y);
            if (area <= 0.0001f || area >= bestArea)
                continue;

            best = image;
            bestArea = area;
        }

        return best;
    }

    private static bool IsInsideSettingsPanel(Transform candidate, Transform cloneRoot)
    {
        var current = candidate;
        while (current != null && current != cloneRoot)
        {
            if (current.name.StartsWith(SettingsPanelPrefix, StringComparison.Ordinal))
                return true;

            current = current.parent;
        }

        return false;
    }

    private static void ApplyIcon(Image icon, Sprite sprite)
    {
        icon.gameObject.name = IconObjectName;
        icon.enabled = true;
        icon.sprite = sprite;
        icon.type = Image.Type.Simple;
        icon.preserveAspect = true;
        icon.color = Color.white;
        icon.raycastTarget = false;
    }

    private static void DisableUnusedChildRaycasts(Transform root)
    {
        for (var i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (child.name.StartsWith(SettingsPanelPrefix, StringComparison.Ordinal))
                continue;

            foreach (var graphic in child.GetComponentsInChildren<Graphic>(includeInactive: true))
                graphic.raycastTarget = false;
        }
    }
}
```

The explicit `BazaarButtonController.ButtonIcon` path is the default for fresh clones. `ApplyIcon(...)` renames that image to `BPP_DockButtonIcon`, so later attach passes can resolve the same child deterministically. The `FindIconImage(...)` area heuristic exists only for older existing clones that have already had `BazaarButtonController` stripped; it must skip `BPP_SettingsDockPanel_*` children so it cannot replace a settings-row or panel background image.

- [ ] **Step 2: Ensure Unity CoreModule is available to the test runner**

If `BppSettingsDockGeometryTests.cs` directly imports `UnityEngine`, update `tests/SettingsDockRegistry.Tests/SettingsDockRegistry.Tests.csproj` so `UnityEngine.CoreModule.dll` is copied beside the test assembly:

```xml
<PropertyGroup>
  <ProjectRoot>$(MSBuildThisFileDirectory)../../</ProjectRoot>
  <MacSteamManagedDefault>$(HOME)/Library/Application Support/Steam/steamapps/common/The Bazaar/TheBazaar.app/Contents/Resources/Data/Managed</MacSteamManagedDefault>
</PropertyGroup>

<PropertyGroup Condition="'$(ManagedPath)' == '' and Exists('$(MacSteamManagedDefault)/Assembly-CSharp.dll')">
  <ManagedPath>$(MacSteamManagedDefault)</ManagedPath>
</PropertyGroup>
```

Add this inside the existing `<ItemGroup>` that contains the project reference:

```xml
<Reference Include="UnityEngine.CoreModule">
  <HintPath>$(ManagedPath)/UnityEngine.CoreModule.dll</HintPath>
</Reference>
<None Include="$(ManagedPath)/UnityEngine.CoreModule.dll" Link="UnityEngine.CoreModule.dll">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

This mirrors the existing `tests/CardSetBuildRecommendationTier.Tests/CardSetBuildRecommendationTier.Tests.csproj` pattern and avoids runtime load failures when the test evaluates `UnityEngine.Color`.

- [ ] **Step 3: Add color-spec tests**

Add `using UnityEngine;` at the top of `tests/SettingsDockRegistry.Tests/BppSettingsDockGeometryTests.cs`, then add this test:

```csharp
[Fact]
public void DockButtonHoverColors_are_distinct_per_button_kind()
{
    var settings = BppDockButtonVisuals.ResolveColors(BppDockButtonIconKind.SettingsDock);
    var collection = BppDockButtonVisuals.ResolveColors(BppDockButtonIconKind.CollectionPanel);

    Assert.NotEqual(settings.Highlighted, collection.Highlighted);
    Assert.Equal(Color.white, settings.Normal);
    Assert.Equal(Color.white, collection.Normal);
    Assert.True(settings.FadeDuration > 0f);
    Assert.True(collection.FadeDuration > 0f);
}
```

- [ ] **Step 4: Run targeted tests**

Run:

```bash
dotnet test tests/SettingsDockRegistry.Tests/SettingsDockRegistry.Tests.csproj
```

Expected: tests pass.

---

### Task 5: Apply Visuals In The Clone Helper

**Files:**
- Modify: `Game/Settings/BppNativeSettingsButtonClone.cs:26-108`

- [ ] **Step 1: Re-apply visuals when an existing clone is found**

Change the existing-clone branch:

```csharp
if (existing != null)
{
    ConfigureRect(existing, anchorButton.transform as RectTransform);
    BppDockButtonVisuals.Apply(
        existing.gameObject,
        placement.ButtonIconKind,
        explicitIcon: null,
        freshClone: false
    );
    return existing;
}
```

- [ ] **Step 2: Capture native icon, then strip and apply visuals**

Immediately before `StripNativeButtonBehavior(cloneObject);`, capture the native icon image. Then apply visuals after stripping:

```csharp
var nativeIcon = BppDockButtonVisuals.ResolveNativeIconImage(cloneObject);
StripNativeButtonBehavior(cloneObject);
BppDockButtonVisuals.Apply(
    cloneObject,
    placement.ButtonIconKind,
    nativeIcon,
    freshClone: true
);
```

- [ ] **Step 3: Keep strip behavior focused on behavior, not final colors**

Inside `StripNativeButtonBehavior`, keep the safety steps that remove native behavior and ensure a root `Button`/`Image`, but remove duplicated hover/color setup if it conflicts with `BppDockButtonVisuals.Apply`. The final section should still guarantee:

```csharp
targetGraphic.raycastTarget = true;
button.onClick.RemoveAllListeners();
button.navigation = new Navigation { mode = Navigation.Mode.None };
button.interactable = true;
button.targetGraphic = targetGraphic;
```

- [ ] **Step 4: Build**

Run:

```bash
dotnet build BazaarPlusPlus.csproj
```

Expected: build passes. The clone helper now owns icon replacement and hover setup for both dock buttons while preserving the native root frame.

---

### Task 6: Runtime Validation In The Bazaar Client

**Files:**
- No source changes unless a validation failure requires a fix.

- [ ] **Step 1: Build and launch through Steam**

Run:

```bash
dotnet build BazaarPlusPlus.csproj
open "steam://run/1617400"
```

Expected: Debug build copies to BepInEx plugins if the game path is detected; The Bazaar launches with Steam runtime state.

- [ ] **Step 2: Validate main-menu / hero-select buttons**

Manual checks:

```text
Main menu:
- Native Setting button remains visible and still opens native settings.
- BPP settings clone appears left of native Setting button with the native gold-ring frame and bpp-settings-icon.png in the center.
- Collection Panel clone appears above native Setting button with the native gold-ring frame and collection-panel-icon.png in the center.
- The old gear icon is gone from both clones.
- The settings plus mark is in the upper-right quadrant and remains readable at dock size.
- The collection icon still shows simple black card panels and gem marks; it should not become a dense book illustration.
- Hovering each clone changes tint within 0.08s.
- Pressing each clone shows pressed tint and still triggers the correct action.

Hero select:
- Same visual and click checks.
- Clone buttons are not greyed out by native BazaarButtonController scans.
```

- [ ] **Step 3: Validate fight-menu buttons**

Manual checks:

```text
Fight menu:
- Native Setting button still opens native settings.
- Left clone opens the BPP settings panel.
- Above clone opens 卡牌图鉴.
- Both clones retain the native gold-ring frame and only the center icon changes.
- Hover/pressed tint works while the menu is open.
```

- [ ] **Step 4: Validate screen sizes**

Use windowed resolutions:

```text
1920x1080
2560x1440
3440x1440 or another ultrawide size
1280x720 or the smallest practical game window
```

Expected: cloned icon sprites preserve aspect ratio inside the native frame, do not overlap the native Setting button, panel still opens at the expected scale, and hover/pressed feedback is visible at each size.

- [ ] **Step 5: Read logs**

Check:

```bash
tail -n 200 "$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar/BepInEx/LogOutput.log" | rg "BppNativeSettingsButtonClone|BppDockButtonSprite|CollectionPanelDockButton|BppSettingsDock|Error|Exception"
```

Expected: no sprite resource warnings, no decode warnings, no clone/controller errors.

---

## Browser Note

These two clone buttons are Unity uGUI controls inside The Bazaar, not browser DOM controls. Browser-specific hover validation is not applicable for this repo surface. The equivalent coverage is desktop runtime validation across game scenes, pointer states, and window sizes. If a future web/installer surface reuses these icons, test it separately in Chromium/WebKit/Firefox because that would be a different implementation path.

---

## Attention Points

- Do not edit `decompiled/`; use it only to verify native behavior.
- Do not reintroduce `BazaarButtonController` or `ButtonCustom` on clones. The current safety depends on neutral Unity `Button`s.
- Preserve the root `Image` sprite by default; it is the native frame/default/selected surface.
- Replace only the center icon child image unless a runtime hierarchy inspection proves that no such child exists.
- If custom art is absent, stop at Task 1. Do not ship with silently missing resources unless the fallback to native icon is explicitly accepted.
- Keep commits scoped: one implementation commit for resources/code/tests, with no unrelated CollectionPanel filter changes.

---

## Expected Result

After implementation, the BPP settings dock button and the card collection dock button no longer look like duplicate native gear buttons. Each preserves the native gold-ring frame, uses its own embedded center-icon Sprite resource, keeps native-like click behavior through the shared clone helper, and provides clear hover/pressed feedback via Unity `Selectable.ColorBlock` in main menu, hero select, and fight menu.
