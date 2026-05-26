#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.LegendaryPosition;
using BazaarPlusPlus.Game.Screenshots.Upload;
using BazaarPlusPlus.Game.UpgradePreview;
using HistoryPanelFeature = BazaarPlusPlus.Game.HistoryPanel.HistoryPanel;
using HistoryPanelLabel = BazaarPlusPlus.Game.HistoryPanel.HistoryPanelSettingsMenuLabel;

namespace BazaarPlusPlus.Game.Settings;

internal static class BppSettingsDockCatalog
{
    private static IBppConfig? _config;

    private static readonly List<BppSettingsDockDefinition> _definitions =
    [
        new(
            "GameHistory",
            HistoryPanelLabel.Resolve,
            ResolveHistoryPanelStatus,
            IsHistoryPanelActionable,
            HistoryPanelFeature.OpenFromDockEntry,
            collapseAfterActivate: true
        ),
        new(
            "LegendaryPositionDisplay",
            LegendaryPositionSettingsMenuLabel.Resolve,
            languageCode =>
                ResolveLegendaryPositionDisplayStatus(
                    ReadLegendaryPositionDisplayMode(),
                    languageCode
                ),
            IsLegendaryPositionDisplayOverrideActive,
            CycleLegendaryPositionDisplayMode,
            collapseAfterActivate: false
        ),
        new(
            "EnchantPreview",
            EnchantPreviewSettingsMenuLabel.Resolve,
            languageCode =>
                ResolvePreviewVisibilityModeStatus(ReadEnchantPreviewMode(), languageCode),
            IsEnchantPreviewOverrideActive,
            CycleEnchantPreviewMode,
            collapseAfterActivate: false
        ),
        new(
            "UpgradePreview",
            UpgradePreviewSettingsMenuLabel.Resolve,
            languageCode =>
                ResolvePreviewVisibilityModeStatus(ReadUpgradePreviewMode(), languageCode),
            IsUpgradePreviewOverrideActive,
            CycleUpgradePreviewMode,
            collapseAfterActivate: false
        ),
        new(
            "BazaarDbUpload",
            BazaarDbScreenshotUploadSettingsMenuLabel.Resolve,
            new SettingsMenuToggleBridge(
                ReadBazaarDbUploadEnabled,
                WriteBazaarDbUploadEnabled,
                BazaarDbScreenshotUploadController.OnEnabledChanged
            )
        ),
        new(
            "ChineseLocaleMode",
            ResolveChineseLocaleModeLabel,
            _ => BppChineseLocalization.ResolveModeStatus(ReadChineseLocaleMode()),
            IsChineseLocaleOverrideActive,
            CycleChineseLocaleMode,
            collapseAfterActivate: false
        ),
    ];

    // Order slots reserved for the migrated registry entries. The hardcoded list
    // below uses the complement of this set (0, 1, 2, 3, 4, 6, 7) so registered
    // entries can slot back into their legacy index. As more features migrate to
    // the registry in Task 3.3, the hardcoded list shrinks and the Orders below
    // collapse to the still-hardcoded entries only.
    // Final expected indices 0..7:
    //   GameHistory, NameOverride, LegendaryPositionDisplay, EnchantPreview,
    //   UpgradePreview, CombatStatusBar, BazaarDbUpload, ChineseLocaleMode.
    private static readonly int[] _hardcodedOrders = [0, 2, 3, 4, 6, 7];

    public static void Install(IBppConfig config, SettingsDockEntryRegistry registry)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));

        var merged = new List<(int Order, BppSettingsDockDefinition Def)>(
            _definitions.Count + 8
        );
        for (var i = 0; i < _definitions.Count; i++)
            merged.Add((_hardcodedOrders[i], _definitions[i]));
        foreach (var pair in registry.MaterializeWithOrder(config))
            merged.Add(pair);
        merged.Sort((a, b) => a.Order.CompareTo(b.Order));

        _definitions.Clear();
        foreach (var pair in merged)
            _definitions.Add(pair.Def);
    }

    private static IBppConfig Config =>
        _config
        ?? throw new InvalidOperationException(
            "BppSettingsDockCatalog.Install must be called at startup."
        );

    internal static IReadOnlyList<BppSettingsDockDefinition> Definitions => _definitions;

    private static string ResolveHistoryPanelStatus(string languageCode)
    {
        if (TheBazaar.Data.IsInCombat)
            return HistoryPanelLabel.ResolveInRunStatus(languageCode);

        return HistoryPanelFeature.IsVisible
            ? HistoryPanelLabel.ResolveOpenStatus(languageCode)
            : HistoryPanelLabel.ResolveViewStatus(languageCode);
    }

    private static bool IsHistoryPanelActionable()
    {
        return !TheBazaar.Data.IsInCombat;
    }

    private static PreviewVisibilityMode ReadEnchantPreviewMode()
    {
        return Config.EnchantPreviewModeConfig?.Value ?? PreviewVisibilityMode.AutoOnPedestalChoice;
    }

    private static void CycleEnchantPreviewMode()
    {
        var config = Config.EnchantPreviewModeConfig;
        if (config != null)
            config.Value = NextPreviewVisibilityMode(config.Value);
    }

    private static bool IsEnchantPreviewOverrideActive()
    {
        return ReadEnchantPreviewMode() != PreviewVisibilityMode.Off;
    }

    private static PreviewVisibilityMode ReadUpgradePreviewMode()
    {
        return Config.UpgradePreviewModeConfig?.Value ?? PreviewVisibilityMode.AutoOnPedestalChoice;
    }

    private static void CycleUpgradePreviewMode()
    {
        var config = Config.UpgradePreviewModeConfig;
        if (config != null)
            config.Value = NextPreviewVisibilityMode(config.Value);
    }

    private static bool IsUpgradePreviewOverrideActive()
    {
        return ReadUpgradePreviewMode() != PreviewVisibilityMode.Off;
    }

    private static PreviewVisibilityMode NextPreviewVisibilityMode(PreviewVisibilityMode mode) =>
        mode switch
        {
            PreviewVisibilityMode.Off => PreviewVisibilityMode.AutoOnPedestalChoice,
            PreviewVisibilityMode.AutoOnPedestalChoice => PreviewVisibilityMode.Always,
            PreviewVisibilityMode.Always => PreviewVisibilityMode.Off,
            _ => PreviewVisibilityMode.AutoOnPedestalChoice,
        };

    private static string ResolvePreviewVisibilityModeStatus(
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

    private static string ResolveChineseLocaleModeLabel(string languageCode)
    {
        return new LocalizedTextSet("Chinese Locale", "中文模式").Resolve(languageCode);
    }

    private static BppChineseLocaleMode ReadChineseLocaleMode()
    {
        return Config.ChineseLocaleModeConfig?.Value ?? BppChineseLocaleMode.Mainland;
    }

    private static void CycleChineseLocaleMode()
    {
        var config = Config.ChineseLocaleModeConfig;
        if (config != null)
            config.Value = BppChineseLocalization.GetNextMode(config.Value);

        HistoryPanelFeature.RefreshLocalization();
    }

    private static bool IsChineseLocaleOverrideActive()
    {
        return ReadChineseLocaleMode() != BppChineseLocaleMode.Mainland;
    }

    private static LegendaryPositionDisplayMode ReadLegendaryPositionDisplayMode()
    {
        return Config.LegendaryPositionDisplayModeConfig?.Value
            ?? LegendaryPositionDisplayMode.Default;
    }

    private static void CycleLegendaryPositionDisplayMode()
    {
        var config = Config.LegendaryPositionDisplayModeConfig;
        if (config == null)
            return;

        config.Value = config.Value switch
        {
            LegendaryPositionDisplayMode.Default => LegendaryPositionDisplayMode.Blank,
            LegendaryPositionDisplayMode.Blank => LegendaryPositionDisplayMode.Fixed999999,
            LegendaryPositionDisplayMode.Fixed999999 =>
                LegendaryPositionDisplayMode.PositionWithRating,
            _ => LegendaryPositionDisplayMode.Default,
        };

        LegendaryPositionUiRefresh.TryRefreshVisibleDisplays();
    }

    private static bool IsLegendaryPositionDisplayOverrideActive()
    {
        return ReadLegendaryPositionDisplayMode() != LegendaryPositionDisplayMode.Default;
    }

    private static string ResolveLegendaryPositionDisplayStatus(
        LegendaryPositionDisplayMode mode,
        string languageCode
    )
    {
        if (LanguageCodeMatcher.IsChinese(languageCode))
        {
            return mode switch
            {
                LegendaryPositionDisplayMode.Default => "默认",
                LegendaryPositionDisplayMode.Blank => "无人知晓",
                LegendaryPositionDisplayMode.Fixed999999 => "战力爆表",
                LegendaryPositionDisplayMode.PositionWithRating => "双显模式",
                _ => "默认",
            };
        }

        return mode switch
        {
            LegendaryPositionDisplayMode.Default => "DEF",
            LegendaryPositionDisplayMode.Blank => "BLANK",
            LegendaryPositionDisplayMode.Fixed999999 => "999999",
            LegendaryPositionDisplayMode.PositionWithRating => "P|R",
            _ => "DEF",
        };
    }

    private static bool ReadBazaarDbUploadEnabled()
    {
        return Config.BazaarDbUploadEnabled?.Value ?? false;
    }

    private static void WriteBazaarDbUploadEnabled(bool enabled)
    {
        var config = Config.BazaarDbUploadEnabled;
        if (config != null)
            config.Value = enabled;
    }
}
