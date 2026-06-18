#nullable enable

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal sealed class DealerPlayerState
{
    public int Day { get; init; }
    public IReadOnlyCollection<Guid> PlayerSkillCardIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyCollection<Guid> RerollExclusionIds { get; init; } = Array.Empty<Guid>();
}
