#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.LegendaryPosition;
using BazaarPlusPlus.Game.NameOverride;
using CombatStatusBarFeature = BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBar;
using HistoryPanelFeature = BazaarPlusPlus.Game.HistoryPanel.HistoryPanel;
using HistoryPanelLabel = BazaarPlusPlus.Game.HistoryPanel.HistoryPanelSettingsMenuLabel;

namespace BazaarPlusPlus.Game.Settings;

internal static class BppSettingsDockCatalog
{
    internal static IReadOnlyList<BppSettingsDockDefinition> Definitions { get; } =
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
            "NameOverride",
            NameOverrideSettingsMenuLabel.Resolve,
            new NameOverrideSettingsMenuBridge(
                ReadNameOverrideEnabled,
                WriteNameOverrideEnabled,
                NameOverrideUiRefresh.TryRefreshVisibleHeroBanners
            )
        ),
        new(
            "EnchantPreview",
            EnchantPreviewSettingsMenuLabel.Resolve,
            new SettingsMenuToggleBridge(ReadEnchantPreviewEnabled, WriteEnchantPreviewEnabled)
        ),
        new(
            "CombatStatusBar",
            CombatStatusBarSettingsMenuLabel.Resolve,
            new CombatStatusBarSettingsMenuBridge(
                CombatStatusBarFeature.GetEnabledSettingValue,
                CombatStatusBarFeature.SetEnabledSettingValue
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
        new(
            "LegendaryPositionDisplay",
            LegendaryPositionSettingsMenuLabel.Resolve,
            _ => ResolveLegendaryPositionDisplayStatus(ReadLegendaryPositionDisplayMode()),
            IsLegendaryPositionDisplayOverrideActive,
            CycleLegendaryPositionDisplayMode,
            collapseAfterActivate: false
        ),
    ];

    private static bool ReadNameOverrideEnabled()
    {
        return BppRuntimeHost.Config.EnableNameOverrideConfig?.Value ?? false;
    }

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

    private static void WriteNameOverrideEnabled(bool enabled)
    {
        var config = BppRuntimeHost.Config.EnableNameOverrideConfig;
        if (config != null)
            config.Value = enabled;
    }

    private static bool ReadEnchantPreviewEnabled()
    {
        return BppRuntimeHost.Config.EnchantPreviewAlwaysShowConfig?.Value ?? false;
    }

    private static void WriteEnchantPreviewEnabled(bool enabled)
    {
        var config = BppRuntimeHost.Config.EnchantPreviewAlwaysShowConfig;
        if (config != null)
            config.Value = enabled;
    }

    private static string ResolveChineseLocaleModeLabel(string languageCode)
    {
        return new LocalizedTextSet("Chinese Locale", "中文模式").Resolve(languageCode);
    }

    private static BppChineseLocaleMode ReadChineseLocaleMode()
    {
        return BppRuntimeHost.Config.ChineseLocaleModeConfig?.Value
            ?? BppChineseLocaleMode.Mainland;
    }

    private static void CycleChineseLocaleMode()
    {
        var config = BppRuntimeHost.Config.ChineseLocaleModeConfig;
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
        return BppRuntimeHost.Config.LegendaryPositionDisplayModeConfig?.Value
            ?? LegendaryPositionDisplayMode.Default;
    }

    private static void CycleLegendaryPositionDisplayMode()
    {
        var config = BppRuntimeHost.Config.LegendaryPositionDisplayModeConfig;
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
    }

    private static bool IsLegendaryPositionDisplayOverrideActive()
    {
        return ReadLegendaryPositionDisplayMode() != LegendaryPositionDisplayMode.Default;
    }

    private static string ResolveLegendaryPositionDisplayStatus(LegendaryPositionDisplayMode mode)
    {
        return mode switch
        {
            LegendaryPositionDisplayMode.Default => "DEF",
            LegendaryPositionDisplayMode.Blank => "BLANK",
            LegendaryPositionDisplayMode.Fixed999999 => "999999",
            LegendaryPositionDisplayMode.PositionWithRating => "P|R",
            _ => "DEF",
        };
    }
}
