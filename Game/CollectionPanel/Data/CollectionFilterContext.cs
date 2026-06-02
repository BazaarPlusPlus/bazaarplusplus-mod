#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal sealed class CollectionFilterContext
{
    public IReadOnlyCollection<Guid>? OfferedCardIds { get; init; }

    public bool ApplyHeroFilter { get; init; } = true;
}
