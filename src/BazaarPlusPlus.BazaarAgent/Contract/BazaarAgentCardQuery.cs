#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

/// <summary>Request body for <c>POST /v2/cards/query</c>.</summary>
public sealed class BazaarAgentCardQueryRequest
{
    public IReadOnlyList<string> InstanceIds { get; set; } = System.Array.Empty<string>();
}

/// <summary>One independently resolved card requested by an Agent.</summary>
public sealed class BazaarAgentCardQueryResult
{
    public string InstanceId { get; init; } = "";
    public bool Found { get; init; }
    public string? Error { get; init; }
    public BazaarAgentCardRef? Card { get; init; }
}

/// <summary>
/// Batch card lookup response. Explicit lookup always includes complete semantic knowledge for
/// every found card, even when that knowledge was already sent through the compact context feed.
/// </summary>
public sealed class BazaarAgentCardQueryResponse
{
    public string SchemaVersion { get; init; } = BazaarAgentAgentViewSchema.Version;
    public string AgentSessionId { get; init; } = "";
    public string CacheEpoch { get; init; } = "";
    public ulong TickId { get; init; }
    public IReadOnlyList<BazaarAgentCardQueryResult> Results { get; init; } =
        System.Array.Empty<BazaarAgentCardQueryResult>();
    public IReadOnlyList<BazaarAgentCardKnowledge> CardKnowledge { get; init; } =
        System.Array.Empty<BazaarAgentCardKnowledge>();
}
