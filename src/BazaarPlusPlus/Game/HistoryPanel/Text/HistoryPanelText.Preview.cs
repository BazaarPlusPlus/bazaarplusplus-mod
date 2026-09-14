#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static partial class HistoryPanelText
{
    internal static string PartialPreview(int count) =>
        FormatSimple(
            $"{count} cards are unavailable in this game version.",
            $"{count} 张卡牌在当前版本不可用。"
        );

    internal static string GhostPerspective() =>
        Resolve(new LocalizedTextSet("Ghost · Uploader build", "幽灵 · 上传者阵容"));
}
