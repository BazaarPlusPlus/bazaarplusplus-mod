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
        new(
            key,
            BppSettingsDockSide.LeftOfAnchor,
            BppSettingsDockPanelDirection.UpLeft,
            DefaultSiblingGap
        );
}
