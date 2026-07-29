#nullable enable
namespace BazaarPlusPlus.GameInterop;

/// <summary>
/// One-shot handoff for a completed live combat. The BazaarAgent host consumes the summary only
/// after the game has returned to an actionable non-combat state.
/// </summary>
public interface IBazaarAgentBattleSummarySource
{
    BazaarAgentBattleSummarySnapshot? TakeCompletedSummary();
}

public sealed class BazaarAgentBattleSummarySnapshot
{
    public string BattleType { get; init; } = "unknown";
    public string? Result { get; init; }
    public BazaarAgentBattleCombatantSnapshot Player { get; init; } = new();
    public BazaarAgentBattleCombatantSnapshot Opponent { get; init; } = new();
}

public sealed class BazaarAgentBattleCombatantSnapshot
{
    public IReadOnlyList<BazaarAgentBattleCardSnapshot> OpeningCards { get; init; } =
        Array.Empty<BazaarAgentBattleCardSnapshot>();
    public BazaarAgentBattleAttributesSnapshot Attributes { get; init; } = new();
}

public sealed class BazaarAgentBattleCardSnapshot
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

/// <summary>Combat-only player attributes. Each pair is the value at combat start and end.</summary>
public sealed class BazaarAgentBattleAttributesSnapshot
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
