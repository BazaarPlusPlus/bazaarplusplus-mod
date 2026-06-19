#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Achievements;

internal sealed class AchievementCardDefinition
{
    public string AchievementId { get; init; } = string.Empty;
    public Guid TemplateId { get; init; }
    public string InternalName { get; init; } = string.Empty;
    public LocalizedTextSet Title { get; init; } = new("", "", "");
    public LocalizedTextSet Description { get; init; } = new("", "", "");
    public string Category { get; init; } = string.Empty;
    public string RuleKind { get; init; } = string.Empty;
    public int Target { get; init; }
    public ETier DisplayTier { get; init; }
    public ECardSize DisplaySize { get; init; }
    public int SortKey { get; init; }
    public bool HiddenUntilUnlocked { get; init; }
}
