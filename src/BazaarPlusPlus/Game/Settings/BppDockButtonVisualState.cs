#nullable enable
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal readonly struct BppDockButtonVisualState(
    Selectable.Transition transition,
    ColorBlock colors,
    SpriteState spriteState,
    AnimationTriggers animationTriggers,
    Graphic? targetGraphic
)
{
    internal Selectable.Transition Transition { get; } = transition;

    internal ColorBlock Colors { get; } = colors;

    internal SpriteState SpriteState { get; } = spriteState;

    internal AnimationTriggers AnimationTriggers { get; } = animationTriggers;

    internal Graphic? TargetGraphic { get; } = targetGraphic;

    internal static BppDockButtonVisualState Capture(
        Selectable.Transition transition,
        ColorBlock colors,
        SpriteState spriteState,
        AnimationTriggers animationTriggers
    ) => new(transition, colors, spriteState, animationTriggers, targetGraphic: null);

    internal static BppDockButtonVisualState? Capture(Button? button)
    {
        if (button == null)
            return null;

        return new(
            button.transition,
            button.colors,
            button.spriteState,
            button.animationTriggers,
            button.targetGraphic
        );
    }

    internal void ApplyTo(Button button, Graphic fallbackTargetGraphic)
    {
        button.targetGraphic = TargetGraphic != null ? TargetGraphic : fallbackTargetGraphic;
        button.transition = Transition;
        button.colors = Colors;
        button.spriteState = SpriteState;
        button.animationTriggers = AnimationTriggers;
    }
}
