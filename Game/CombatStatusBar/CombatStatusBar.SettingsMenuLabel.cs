#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal static class CombatStatusBarSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Combat Status Bar",
        "\u6218\u6597\u72b6\u6001\u680f",
        "Kampfstatusleiste",
        "Barra de status do combate",
        "\uc804\ud22c \uc0c1\ud0dc \ubc14",
        "Barra stato combattimento"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode);
    }
}
