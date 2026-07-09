#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal readonly struct BppDockButtonVisualState(
    Selectable.Transition transition,
    ColorBlock colors,
    SpriteState spriteState,
    AnimationTriggers animationTriggers,
    Graphic? targetGraphic,
    Sprite? normalBaseSprite,
    Sprite? selectedBaseSprite
)
{
    internal Selectable.Transition Transition { get; } = transition;

    internal ColorBlock Colors { get; } = colors;

    internal SpriteState SpriteState { get; } = spriteState;

    internal AnimationTriggers AnimationTriggers { get; } = animationTriggers;

    internal Graphic? TargetGraphic { get; } = targetGraphic;

    internal Sprite? NormalBaseSprite { get; } = normalBaseSprite;

    internal Sprite? SelectedBaseSprite { get; } = selectedBaseSprite;

    internal static BppDockButtonVisualState Capture(
        Selectable.Transition transition,
        ColorBlock colors,
        SpriteState spriteState,
        AnimationTriggers animationTriggers
    ) =>
        new(
            transition,
            colors,
            spriteState,
            animationTriggers,
            targetGraphic: null,
            normalBaseSprite: null,
            selectedBaseSprite: null
        );

    internal static BppDockButtonVisualState? Capture(
        Button? button,
        Sprite? normalBaseSprite = null,
        Sprite? selectedBaseSprite = null
    )
    {
        if (button == null)
            return null;

        var targetImage = button.targetGraphic as Image ?? button.image;
        var spriteState = button.spriteState;

        return new(
            button.transition,
            button.colors,
            spriteState,
            button.animationTriggers,
            button.targetGraphic,
            normalBaseSprite ?? targetImage?.sprite,
            selectedBaseSprite ?? spriteState.selectedSprite
        );
    }

    internal ColorBlock ResolveBaselineColors(bool isExpanded)
    {
        if (!isExpanded)
            return Colors;

        var expanded = Colors;
        expanded.normalColor = Colors.selectedColor;
        return expanded;
    }

    internal AnimationTriggers ResolveBaselineAnimationTriggers(bool isExpanded)
    {
        var resolved = new AnimationTriggers
        {
            normalTrigger = AnimationTriggers.normalTrigger,
            highlightedTrigger = AnimationTriggers.highlightedTrigger,
            pressedTrigger = AnimationTriggers.pressedTrigger,
            selectedTrigger = AnimationTriggers.selectedTrigger,
            disabledTrigger = AnimationTriggers.disabledTrigger,
        };
        if (isExpanded && !string.IsNullOrEmpty(AnimationTriggers.selectedTrigger))
            resolved.normalTrigger = AnimationTriggers.selectedTrigger;

        return resolved;
    }

    internal Sprite? ResolveBaselineSprite(bool isExpanded) =>
        isExpanded && SelectedBaseSprite != null ? SelectedBaseSprite : NormalBaseSprite;

    internal void ApplyTo(Button button, Graphic fallbackTargetGraphic)
    {
        button.targetGraphic = TargetGraphic != null ? TargetGraphic : fallbackTargetGraphic;
        button.transition = Transition;
        button.colors = Colors;
        button.spriteState = SpriteState;
        button.animationTriggers = AnimationTriggers;
    }
}
