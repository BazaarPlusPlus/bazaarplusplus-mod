#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.NameOverride;

internal static class NameOverrideSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Anonymous Mode",
        "\u533f\u540d\u6a21\u5f0f",
        "Anonymer Modus",
        "Modo anonimo",
        "\uc775\uba85 \ubaa8\ub4dc",
        "Modalita anonima"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode);
    }
}
