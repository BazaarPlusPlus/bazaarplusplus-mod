#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class UiFontSettingsDockEntry : ISettingsDockEntry
{
    private static readonly LocalizedTextSet Label = new("UI Font", "界面字体");
    private static readonly LocalizedTextSet LxgwWenKaiStatus = new("KAI", "楷体");
    private static readonly LocalizedTextSet SansSerifStatus = new("SANS", "黑体");

    public int Order => BppSettingsDockOrder.UiFont;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "UiFont",
            ResolveLabel,
            languageCode => ResolveStatus(ReadKind(config), languageCode),
            () => IsOverrideActive(config),
            () => CycleKind(config),
            collapseAfterActivate: false
        );

    private static string ResolveLabel(string languageCode) =>
        Label.Resolve(languageCode, L.CurrentMode);

    private static BppUiFontKind ReadKind(IBppConfig config) =>
        config.UiFontKindConfig?.Value == BppUiFontKind.SansSerif
            ? BppUiFontKind.SansSerif
            : BppConfig.DefaultUiFontKind;

    private static bool IsOverrideActive(IBppConfig config) =>
        ReadKind(config) != BppConfig.DefaultUiFontKind;

    private static void CycleKind(IBppConfig config)
    {
        var entry = config.UiFontKindConfig;
        if (entry == null)
            return;

        entry.Value = ReadKind(config) switch
        {
            BppUiFontKind.SansSerif => BppUiFontKind.LxgwWenKai,
            _ => BppUiFontKind.SansSerif,
        };
    }

    private static string ResolveStatus(BppUiFontKind kind, string languageCode)
    {
        return (kind == BppUiFontKind.SansSerif ? SansSerifStatus : LxgwWenKaiStatus).Resolve(
            languageCode,
            L.CurrentMode
        );
    }
}
