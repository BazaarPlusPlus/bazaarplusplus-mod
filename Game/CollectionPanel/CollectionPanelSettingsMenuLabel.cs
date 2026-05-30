#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.CollectionPanel;

// Localized strings for the settings-dock row. Kept separate from CollectionPanelText so the
// settings dock can call it without dragging in the rest of the panel's vocabulary.
internal static class CollectionPanelSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Card Collection",
        "卡牌图鉴",
        "Kartensammlung",
        "Colecao de cartas",
        "카드 도감",
        "Collezione di carte"
    );

    private static readonly LocalizedTextSet OpenStatuses = new(
        "OPEN",
        "已打开",
        "OFFEN",
        "ABERTO",
        "열림",
        "APERTO"
    );

    private static readonly LocalizedTextSet ViewStatuses = new(
        "VIEW",
        "查看",
        "ANZEIGEN",
        "VER",
        "보기",
        "VEDI"
    );

    private static readonly LocalizedTextSet InRunStatuses = new(
        "UNAVAILABLE",
        "战斗中不可用",
        "VORUBERGEHEND NICHT VERFUGBAR",
        "INDISPONIVEL",
        "대국 중 일시 사용 불가",
        "TEMPORANEAMENTE NON DISPONIBILE"
    );

    internal static string Resolve(string languageCode) => Labels.Resolve(languageCode);

    internal static string ResolveOpenStatus(string languageCode) =>
        OpenStatuses.Resolve(languageCode);

    internal static string ResolveViewStatus(string languageCode) =>
        ViewStatuses.Resolve(languageCode);

    internal static string ResolveInRunStatus(string languageCode) =>
        InRunStatuses.Resolve(languageCode);
}
