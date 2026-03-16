#nullable enable
using System;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class SettingsMenuToggleDefinition
{
    internal SettingsMenuToggleDefinition(
        string toggleObjectName,
        string logCategory,
        Func<string, string> resolveLabel,
        SettingsMenuToggleBridge bridge
    )
    {
        ToggleObjectName = !string.IsNullOrWhiteSpace(toggleObjectName)
            ? toggleObjectName
            : throw new ArgumentException("Toggle object name is required.", nameof(toggleObjectName));
        LogCategory = !string.IsNullOrWhiteSpace(logCategory)
            ? logCategory
            : throw new ArgumentException("Log category is required.", nameof(logCategory));
        ResolveLabel = resolveLabel ?? throw new ArgumentNullException(nameof(resolveLabel));
        Bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    internal string ToggleObjectName { get; }

    internal string LogCategory { get; }

    internal Func<string, string> ResolveLabel { get; }

    internal SettingsMenuToggleBridge Bridge { get; }
}
