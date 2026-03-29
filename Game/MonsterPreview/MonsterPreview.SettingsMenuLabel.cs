#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal static class MonsterPreviewSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Use Native Monster Preview",
        "\u4f7f\u7528\u539f\u751f\u91ce\u602a\u9884\u89c8",
        "Native Monstervorschau verwenden",
        "Usar previa nativa de monstro",
        "\uae30\ubcf8 \ubaac\uc2a4\ud130 \ubbf8\ub9ac\ubcf4\uae30 \uc0ac\uc6a9",
        "Usa anteprima mostro nativa"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode);
    }
}
