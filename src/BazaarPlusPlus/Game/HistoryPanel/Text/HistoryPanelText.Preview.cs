#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static partial class HistoryPanelText
{
    internal static string GhostPerspective() =>
        Resolve(new LocalizedTextSet("Ghost · Uploader build", "幽灵 · 上传者阵容"));
}
