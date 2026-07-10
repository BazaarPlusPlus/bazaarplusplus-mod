#nullable enable

namespace BazaarPlusPlus.Game.Settings;

internal enum BppSettingsDockPanelDirection
{
    UpLeft,
    UpRight,
}

internal readonly struct BppSettingsDockPlacement
{
    private const float DefaultSiblingGap = 18f;

    private BppSettingsDockPlacement(string key, BppDockButtonIconKind buttonIconKind)
    {
        Key = key;
        ButtonIconKind = buttonIconKind;
    }

    internal string Key { get; }

    internal BppSettingsDockPanelDirection PanelDirection => BppSettingsDockPanelDirection.UpLeft;

    internal float SiblingGap => DefaultSiblingGap;

    internal BppDockButtonIconKind ButtonIconKind { get; }

    internal string DockButtonObjectName => $"BPP_SettingsDockButton_{Key}";

    internal string PanelObjectName => $"BPP_SettingsDockPanel_{Key}";

    internal static BppSettingsDockPlacement ForButton(
        string key,
        BppDockButtonIconKind buttonIconKind
    ) => new(key, buttonIconKind);
}
