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

internal readonly struct BppDockButtonNativeVisuals(
    ColorBlock colors,
    SpriteState spriteState,
    AnimationTriggers animationTriggers,
    Image? targetImage,
    Sprite? normalSprite
)
{
    internal ColorBlock Colors { get; } = colors;
    internal SpriteState SpriteState { get; } = spriteState;
    internal AnimationTriggers AnimationTriggers { get; } = animationTriggers;
    internal Image? TargetImage { get; } = targetImage;
    internal Sprite? NormalSprite { get; } = normalSprite;

    internal bool HasSpriteSwapSprites =>
        SpriteState.highlightedSprite != null
        || SpriteState.pressedSprite != null
        || SpriteState.selectedSprite != null
        || SpriteState.disabledSprite != null;
}

internal static class BppDockButtonVisuals
{
    private const string IconObjectName = "BPP_DockButtonIcon";
    private const string SettingsPanelPrefix = "BPP_SettingsDockPanel_";

    private static readonly Color SettingsHover = new(1f, 0.9f, 0.58f, 1f);
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

    internal static BppDockButtonNativeVisuals? CaptureNativeButtonVisuals(GameObject cloneObject)
    {
        var button = cloneObject.GetComponent<Button>();
        if (button == null)
            return null;

        var nativeButton = cloneObject.GetComponent<BazaarButtonController>();
        var targetImage =
            button.targetGraphic as Image
            ?? button.image
            ?? nativeButton?.GetComponent<Image>()
            ?? cloneObject.GetComponent<Image>();
        var normalSprite = nativeButton?.DefaultImage ?? targetImage?.sprite;
        var spriteState = button.spriteState;
        if (spriteState.pressedSprite == null && nativeButton?.ClickedImage != null)
            spriteState.pressedSprite = nativeButton.ClickedImage;

        if (spriteState.selectedSprite == null && nativeButton?.ClickedImage != null)
            spriteState.selectedSprite = nativeButton.ClickedImage;

        return new BppDockButtonNativeVisuals(
            button.colors,
            spriteState,
            button.animationTriggers,
            targetImage,
            normalSprite
        );
    }

    internal static void Apply(
        GameObject cloneObject,
        BppDockButtonIconKind kind,
        Image? explicitIcon,
        bool freshClone,
        BppDockButtonNativeVisuals? nativeButtonVisuals
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
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.interactable = true;

        if (TryApplyNativeButtonVisuals(button, nativeButtonVisuals, frame))
            return;

        button.transition = Selectable.Transition.ColorTint;
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

    private static bool TryApplyNativeButtonVisuals(
        Button button,
        BppDockButtonNativeVisuals? nativeButtonVisuals,
        Image fallbackTargetImage
    )
    {
        if (nativeButtonVisuals is not { } native || !native.HasSpriteSwapSprites)
            return false;

        var targetImage = ResolveTargetImage(button, native.TargetImage) ?? fallbackTargetImage;
        if (native.NormalSprite != null)
            targetImage.sprite = native.NormalSprite;

        targetImage.enabled = true;
        fallbackTargetImage.raycastTarget = true;
        if (targetImage != fallbackTargetImage)
            targetImage.raycastTarget = false;

        button.transition = Selectable.Transition.None;
        button.colors = native.Colors;
        button.spriteState = native.SpriteState;
        button.animationTriggers = native.AnimationTriggers;
        button.targetGraphic = targetImage;

        var stateVisual =
            button.GetComponent<BppDockButtonNativeStateVisual>()
            ?? button.gameObject.AddComponent<BppDockButtonNativeStateVisual>();
        stateVisual.Initialize(
            button,
            targetImage,
            native.NormalSprite ?? targetImage.sprite,
            native.SpriteState.highlightedSprite,
            native.SpriteState.pressedSprite,
            native.SpriteState.selectedSprite,
            native.SpriteState.disabledSprite
        );
        return true;
    }

    private static Image? ResolveTargetImage(Button button, Image? targetImage)
    {
        if (targetImage == null)
            return null;

        if (
            targetImage.transform == button.transform
            || targetImage.transform.IsChildOf(button.transform)
        )
            return targetImage;

        return null;
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

            if (image.transform is not RectTransform rect)
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
        for (var index = 0; index < root.childCount; index++)
        {
            var child = root.GetChild(index);
            if (child.name.StartsWith(SettingsPanelPrefix, StringComparison.Ordinal))
                continue;

            foreach (var graphic in child.GetComponentsInChildren<Graphic>(includeInactive: true))
                graphic.raycastTarget = false;
        }
    }
}
