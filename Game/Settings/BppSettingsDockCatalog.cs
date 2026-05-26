#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.Settings;

internal static class BppSettingsDockCatalog
{
    private static readonly List<BppSettingsDockDefinition> _definitions = new();

    public static void Install(IBppConfig config, SettingsDockEntryRegistry registry)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));

        var ordered = new List<(int Order, BppSettingsDockDefinition Def)>(
            registry.MaterializeWithOrder(config)
        );
        ordered.Sort((a, b) => a.Order.CompareTo(b.Order));

        _definitions.Clear();
        foreach (var pair in ordered)
            _definitions.Add(pair.Def);
    }

    internal static IReadOnlyList<BppSettingsDockDefinition> Definitions => _definitions;

    internal static PreviewVisibilityMode NextPreviewVisibilityMode(PreviewVisibilityMode mode) =>
        mode switch
        {
            PreviewVisibilityMode.Off => PreviewVisibilityMode.AutoOnPedestalChoice,
            PreviewVisibilityMode.AutoOnPedestalChoice => PreviewVisibilityMode.Always,
            PreviewVisibilityMode.Always => PreviewVisibilityMode.Off,
            _ => PreviewVisibilityMode.AutoOnPedestalChoice,
        };

    internal static string ResolvePreviewVisibilityModeStatus(
        PreviewVisibilityMode mode,
        string languageCode
    )
    {
        if (LanguageCodeMatcher.IsChinese(languageCode))
        {
            return mode switch
            {
                PreviewVisibilityMode.Off => "关闭",
                PreviewVisibilityMode.AutoOnPedestalChoice => "智能",
                PreviewVisibilityMode.Always => "总是",
                _ => "智能",
            };
        }

        return mode switch
        {
            PreviewVisibilityMode.Off => "OFF",
            PreviewVisibilityMode.AutoOnPedestalChoice => "AUTO",
            PreviewVisibilityMode.Always => "ON",
            _ => "AUTO",
        };
    }
}
