#nullable enable
namespace BazaarPlusPlus.Core.Events;

internal sealed class PvpBattleScreenshotContextAvailable
{
    public string BattleId { get; set; } = string.Empty;

    public string? RunId { get; set; }
}
