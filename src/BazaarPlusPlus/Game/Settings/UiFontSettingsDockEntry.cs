#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Settings;

internal static class UiFontSettingsDockEntry
{
    private static readonly LocalizedTextSet Label = new("UI Font", "界面字体");
    private static readonly LocalizedTextSet LxgwWenKaiStatus = new("KAI", "楷体");
    private static readonly LocalizedTextSet SansSerifStatus = new("SANS", "黑体");

    internal static CyclingSettingsDockEntry<BppUiFontKind> Create() =>
        new CyclingSettingsDockEntry<BppUiFontKind>(
            BppSettingsDockOrder.UiFont,
            "UiFont",
            ResolveLabel,
            new[] { BppUiFontKind.LxgwWenKai, BppUiFontKind.SansSerif },
            ReadKind,
            (config, kind) =>
            {
                var entry = config.UiFontKindConfig;
                if (entry != null)
                    entry.Value = kind;
            },
            kind => kind != BppConfig.DefaultUiFontKind,
            ResolveStatus
        );

    private static string ResolveLabel(string languageCode) =>
        Label.Resolve(languageCode, L.CurrentMode);

    private static BppUiFontKind ReadKind(IBppConfig config) =>
        config.UiFontKindConfig?.Value == BppUiFontKind.SansSerif
            ? BppUiFontKind.SansSerif
            : BppConfig.DefaultUiFontKind;

    private static string ResolveStatus(BppUiFontKind kind, string languageCode)
    {
        return (kind == BppUiFontKind.SansSerif ? SansSerifStatus : LxgwWenKaiStatus).Resolve(
            languageCode,
            L.CurrentMode
        );
    }
}
