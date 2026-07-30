#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

/// <summary>One completed live-combat summary, retained until the next confirmed player action.</summary>
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
