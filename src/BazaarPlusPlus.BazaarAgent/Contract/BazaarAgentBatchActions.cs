#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

public sealed class BazaarAgentSellItemsRequest
{
    public IReadOnlyList<string> CardInstanceIds { get; set; } = System.Array.Empty<string>();
    public ulong? ForTickId { get; set; }
    public string? Reason { get; set; }
}

public sealed class BazaarAgentLayoutPlacement
{
    public string CardInstanceId { get; set; } = "";
    public BazaarAgentTargetSection TargetSection { get; set; }
    public IReadOnlyList<string> TargetSockets { get; set; } = System.Array.Empty<string>();
}

public sealed class BazaarAgentSetLayoutRequest
{
    public IReadOnlyList<BazaarAgentLayoutPlacement> Placements { get; set; } =
        System.Array.Empty<BazaarAgentLayoutPlacement>();
    public ulong? ForTickId { get; set; }
    public string? Reason { get; set; }
}

public sealed class BazaarAgentBatchActionStepResult
{
    public BazaarAgentActionKind ActionKind { get; init; }
    public string? CardInstanceId { get; init; }
    public string Status { get; init; } = "";
    public string? Error { get; init; }
}

public sealed class BazaarAgentBatchActionResponse
{
    public string Status { get; init; } = "";
    public IReadOnlyList<BazaarAgentBatchActionStepResult> Steps { get; init; } =
        System.Array.Empty<BazaarAgentBatchActionStepResult>();
}
