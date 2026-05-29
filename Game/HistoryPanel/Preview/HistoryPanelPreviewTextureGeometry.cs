#nullable enable

namespace BazaarPlusPlus.Game.HistoryPanel.Preview;

internal static class HistoryPanelPreviewTextureGeometry
{
    // 10 slots × 260px = the card root width incl. its frame + corner stat-gems (measured
    // in-game), so adjacent cards tile without their frames overlapping. Consumed by the
    // overlay preview path (BattleBoardPreview clip/board sizing + HistoryPanel auto-fit scale).
    public const int NativeBoardWidth = 2600;
    public const int NativeBoardHeight = 600;
}
