#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.Screenshots;

internal static class BattleStartScreenshotSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Battle Start Screenshot",
        "战斗开始截图",
        "Kampfbeginn-Screenshot",
        "Captura no inicio da batalha",
        "전투 시작 스크린샷",
        "Screenshot a inizio combattimento"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode);
    }
}
