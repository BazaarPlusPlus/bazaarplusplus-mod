#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal static class BazaarDbSnapshotUploadSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Upload screenshots to BazaarDB",
        "BazaarDB 数据共建",
        "Screenshots zu BazaarDB hochladen",
        "Subir capturas a BazaarDB",
        "스크린샷을 BazaarDB에 업로드",
        "Carica gli screenshot su BazaarDB"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }
}
