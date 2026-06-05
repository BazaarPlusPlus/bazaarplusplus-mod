#nullable enable
using BazaarPlusPlus.GameInterop.ItemBoardPreview;

namespace BazaarPlusPlus.Game.BuildRecommendations;

internal sealed class BuildRecommendation
{
    public string ModeLabel { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string SetSignature { get; set; } = string.Empty;

    public double GoldScore { get; set; }

    public int ResultIndex { get; set; }

    public int ResultCount { get; set; }

    public BppItemBoard Board { get; set; } = BppItemBoard.Empty;
}
