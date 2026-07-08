#nullable enable

namespace BazaarPlusPlus.Game.Settings;

internal enum BppSettingsDockSide
{
    LeftOfAnchor,
    RightOfAnchor,
    AboveAnchor,
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
        float siblingGap,
        BppDockButtonIconKind buttonIconKind,
        int siblingStepCount
    )
    {
        Key = key;
        Side = side;
        PanelDirection = panelDirection;
        SiblingGap = siblingGap;
        ButtonIconKind = buttonIconKind;
        SiblingStepCount = siblingStepCount > 0 ? siblingStepCount : 1;
    }

    internal string Key { get; }

    internal BppSettingsDockSide Side { get; }

    internal BppSettingsDockPanelDirection PanelDirection { get; }

    internal float SiblingGap { get; }

    internal BppDockButtonIconKind ButtonIconKind { get; }

    internal int SiblingStepCount { get; }

    internal string DockButtonObjectName => $"BPP_SettingsDockButton_{Key}";

    internal string PanelObjectName => $"BPP_SettingsDockPanel_{Key}";

    internal static BppSettingsDockPlacement LeftOfSettingButton(
        string key,
        BppDockButtonIconKind buttonIconKind
    ) =>
        new(
            key,
            BppSettingsDockSide.LeftOfAnchor,
            BppSettingsDockPanelDirection.UpLeft,
            DefaultSiblingGap,
            buttonIconKind,
            siblingStepCount: 1
        );

    internal static BppSettingsDockPlacement AboveSettingButton(
        string key,
        BppDockButtonIconKind buttonIconKind,
        int siblingStepCount = 1
    ) =>
        new(
            key,
            BppSettingsDockSide.AboveAnchor,
            BppSettingsDockPanelDirection.UpLeft,
            DefaultSiblingGap,
            buttonIconKind,
            siblingStepCount
        );
}
