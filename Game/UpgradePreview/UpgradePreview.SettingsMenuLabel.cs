#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.UpgradePreview;

internal static class UpgradePreviewSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Upgrade Preview",
        "升级预览",
        "Upgrade-Vorschau",
        "Previa de Aprimoramento",
        "업그레이드 미리보기",
        "Anteprima Potenziamento"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode);
    }
}
