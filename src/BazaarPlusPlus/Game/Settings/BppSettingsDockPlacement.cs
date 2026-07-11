#nullable enable

namespace BazaarPlusPlus.Game.Settings;

internal readonly struct BppSettingsDockPlacement
{
    internal const float DefaultSiblingGap = 18f;

    private BppSettingsDockPlacement(string key)
    {
        Key = key;
    }

    internal string Key { get; }

    internal string DockButtonObjectName => $"BPP_CollectionDockButton_{Key}";

    internal static BppSettingsDockPlacement ForButton(string key) => new(key);
}
