#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayCardSnapshot
{
    public string InstanceId { get; set; } = string.Empty;

    public string TemplateId { get; set; } = string.Empty;

    public ECardType Type { get; set; }

    public ECardSize Size { get; set; }

    public EInventorySection? Section { get; set; }

    public EContainerSocketId? Socket { get; set; }
}
