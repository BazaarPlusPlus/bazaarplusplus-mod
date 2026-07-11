#nullable enable

namespace BazaarPlusPlus.Game.Settings;

internal readonly struct BppSettingsDockPlacement
{
    internal const float DefaultSiblingGap = 18f;

    private BppSettingsDockPlacement(string key, BppDockButtonIconKind buttonIconKind)
    {
        Key = key;
        ButtonIconKind = buttonIconKind;
    }

    internal string Key { get; }

    internal BppDockButtonIconKind ButtonIconKind { get; }

    internal string DockButtonObjectName => $"BPP_CollectionDockButton_{Key}";

    internal static BppSettingsDockPlacement ForButton(
        string key,
        BppDockButtonIconKind buttonIconKind
    ) => new(key, buttonIconKind);
}
