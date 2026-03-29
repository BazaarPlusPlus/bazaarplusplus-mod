#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Game History",
        "\u5bf9\u5c40\u5386\u53f2",
        "Spielverlauf",
        "Historico de partidas",
        "\uac8c\uc784 \uc804\uc801",
        "Cronologia partite"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode);
    }
}
