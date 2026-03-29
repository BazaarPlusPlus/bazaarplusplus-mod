#nullable enable
using System;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class BppSettingsDockDefinition
{
    internal BppSettingsDockDefinition(
        string key,
        Func<string, string> resolveLabel,
        SettingsMenuToggleBridge bridge
    )
    {
        Key = !string.IsNullOrWhiteSpace(key)
            ? key
            : throw new ArgumentException("Key is required.", nameof(key));
        ResolveLabel = resolveLabel ?? throw new ArgumentNullException(nameof(resolveLabel));
        Bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    internal string Key { get; }

    internal Func<string, string> ResolveLabel { get; }

    internal SettingsMenuToggleBridge Bridge { get; }
}
