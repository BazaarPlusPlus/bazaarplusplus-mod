#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

internal static class EnchantPreviewSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Always Show Enchant Preview",
        "\u59cb\u7ec8\u663e\u793a\u9644\u9b54\u9884\u89c8",
        "Verzauberungsvorschau immer anzeigen",
        "Sempre mostrar previa de encantamento",
        "\ub9c8\ubc95\ubd80\uc5ec \ubbf8\ub9ac\ubcf4\uae30 \ud56d\uc0c1 \ud45c\uc2dc",
        "Mostra sempre anteprima incantamento"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode);
    }
}
