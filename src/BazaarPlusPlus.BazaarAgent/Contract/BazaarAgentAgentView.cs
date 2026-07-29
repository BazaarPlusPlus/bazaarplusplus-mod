#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

/// <summary>Schema for the compact, cache-aware Agent View served from <c>GET /v2/context</c>.</summary>
public static class BazaarAgentAgentViewSchema
{
    public const string Version = "3.2.0";
}

/// <summary>
/// The card data sent on every decision. <see cref="KnowledgeId"/> identifies the immutable
/// semantic detail in <see cref="BazaarAgentAgentView.CardKnowledge"/>; placement and other
/// live decision data deliberately remain here rather than in the cacheable object.
/// </summary>
public sealed class BazaarAgentCardRef
{
    public string InstanceId { get; init; } = "";
    public BazaarAgentCardKind Kind { get; init; }
    public string? TemplateId { get; init; }
    public string? DisplayName { get; init; }
    public string? Size { get; init; }
    public BazaarAgentCardLocation Location { get; init; }
    public string? SocketId { get; init; }
    public int Order { get; init; }
    public string KnowledgeId { get; init; } = "";

    // Live offer / interaction data. These can change without changing card semantics.
    public int? BuyPrice { get; init; }
    public int? SellPrice { get; init; }
    public bool? CanAfford { get; init; }
    public bool? CanFit { get; init; }
    public bool? CanSelect { get; init; }
    public bool? IsFree { get; init; }
    public BazaarAgentTargetSection? TargetSection { get; init; }
    public string? TargetSockets { get; init; }
    public string? UnavailableReason { get; init; }
    public bool? CanSell { get; init; }
}

/// <summary>
/// Immutable semantic card detail, keyed by a content-addressed <see cref="KnowledgeId"/>.
/// A changed effective stat, enchantment, quest state, or ability produces a new ID and a full
/// replacement record; clients never have to apply a partial JSON patch.
/// </summary>
public sealed class BazaarAgentCardKnowledge
{
    public string KnowledgeId { get; init; } = "";
    public BazaarAgentCardKind Kind { get; init; }
    public string? Type { get; init; }
    public string? TemplateId { get; init; }
    public string? DisplayName { get; init; }
    public string? Tier { get; init; }
    public string? Size { get; init; }
    public string? Enchantment { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = System.Array.Empty<string>();
    public IReadOnlyList<string> HiddenTags { get; init; } = System.Array.Empty<string>();
    public IReadOnlyDictionary<string, int> Attributes { get; init; } =
        new Dictionary<string, int>();
    public IReadOnlyList<BazaarAgentCardAbilitySnapshot> ActiveAbilities { get; init; } =
        System.Array.Empty<BazaarAgentCardAbilitySnapshot>();
}

/// <summary>Action transport stripped of the card copy already present in the card-ref lists.</summary>
public sealed class BazaarAgentCompactDecisionOption
{
    public BazaarAgentActionKind ActionKind { get; init; }
    public BazaarAgentActionGroup Group { get; init; }
    public string? CardInstanceId { get; init; }
    public BazaarAgentTargetSection? TargetSection { get; init; }
    public IReadOnlyList<string>? TargetSockets { get; init; }
}

/// <summary>
/// A summary of the most recently completed live combat. It remains attached to actionable
/// post-combat decisions until the first confirmed non-Wait action acknowledges it, and is never
/// emitted for intermediate combat frames.
/// </summary>
public sealed class BazaarAgentBattleSummary
{
    public string SummaryId { get; init; } = "";
    public string BattleType { get; init; } = "unknown";
    public string? Result { get; init; }
    public BazaarAgentBattleCombatant Player { get; init; } = new();
    public BazaarAgentBattleCombatant Opponent { get; init; } = new();
}

public sealed class BazaarAgentBattleCombatant
{
    public IReadOnlyList<BazaarAgentBattleCard> OpeningCards { get; init; } =
        System.Array.Empty<BazaarAgentBattleCard>();
    public BazaarAgentBattleAttributes Attributes { get; init; } = new();
}

public sealed class BazaarAgentBattleCard
{
    public string InstanceId { get; init; } = "";
    public string TemplateId { get; init; } = "";
    public string Type { get; init; } = "";
    public string? Size { get; init; }
    public string? Section { get; init; }
    public string? SocketId { get; init; }
    public IReadOnlyDictionary<string, int> Attributes { get; init; } =
        new Dictionary<string, int>();
}

public sealed class BazaarAgentBattleAttributes
{
    public BazaarAgentBattleValueChange? Health { get; init; }
    public BazaarAgentBattleValueChange? MaxHealth { get; init; }
    public BazaarAgentBattleValueChange? Shield { get; init; }
    public BazaarAgentBattleValueChange? Burn { get; init; }
    public BazaarAgentBattleValueChange? Poison { get; init; }
}

public sealed class BazaarAgentBattleValueChange
{
    public int Start { get; init; }
    public int End { get; init; }
    public int Delta => End - Start;
}

/// <summary>
/// Compact decision view. Card references are always present while <see cref="CardKnowledge"/>
/// contains only knowledge this Agent session has not already received.
/// </summary>
public sealed class BazaarAgentAgentView
{
    public string SchemaVersion { get; init; } = BazaarAgentAgentViewSchema.Version;
    public string AgentSessionId { get; init; } = "";
    public string CacheEpoch { get; init; } = "";
    public ulong TickId { get; init; }
    public string ServerTimeUtc { get; init; } = "";

    public bool IsInRun { get; init; }
    public bool HasActiveRun { get; init; }
    public bool CanStartOrContinueRun { get; init; }
    public bool IsClientBusy { get; init; }
    public string? RunId { get; init; }
    public string? GameModeId { get; init; }
    public BazaarAgentRunStateName StateName { get; init; }
    public string? PlayerHero { get; init; }
    public int? Day { get; init; }
    public int? Hour { get; init; }
    public int? Wins { get; init; }
    public int? Losses { get; init; }
    public int PlayerGold { get; init; }
    public int? PlayerIncome { get; init; }
    public int? PlayerHealth { get; init; }
    public int? PlayerMaxHealth { get; init; }
    public int? PlayerPrestige { get; init; }
    public int? PlayerLevel { get; init; }
    public bool SelectionIsFree { get; init; }
    public bool CanExit { get; init; }
    public bool CanReroll { get; init; }
    public int RerollCost { get; init; }
    public int RerollsRemaining { get; init; }
    public string? CurrentEncounterId { get; init; }
    public string? CurrentEncounterType { get; init; }
    public double ActionCooldownRemainingSeconds { get; init; }
    public BazaarAgentReplayPhase ReplayPhase { get; init; }
    public string? ReplayBattleId { get; init; }
    public IReadOnlyList<string>? InteractableTemplateIds { get; init; }

    public IReadOnlyList<BazaarAgentCardRef> BoardItems { get; init; } =
        System.Array.Empty<BazaarAgentCardRef>();
    public IReadOnlyList<BazaarAgentCardRef> ChestItems { get; init; } =
        System.Array.Empty<BazaarAgentCardRef>();
    public IReadOnlyList<string> LockedBoardSockets { get; init; } = System.Array.Empty<string>();
    public IReadOnlyList<string> LockedChestSockets { get; init; } = System.Array.Empty<string>();
    public IReadOnlyList<BazaarAgentCardRef> PlayerSkills { get; init; } =
        System.Array.Empty<BazaarAgentCardRef>();
    public IReadOnlyList<BazaarAgentCardRef> SelectionOptions { get; init; } =
        System.Array.Empty<BazaarAgentCardRef>();
    public IReadOnlyList<BazaarAgentCompactDecisionOption> AvailableActions { get; init; } =
        System.Array.Empty<BazaarAgentCompactDecisionOption>();
    public IReadOnlyList<BazaarAgentCardKnowledge> CardKnowledge { get; init; } =
        System.Array.Empty<BazaarAgentCardKnowledge>();
    public BazaarAgentBattleSummary? LastBattle { get; init; }
}
