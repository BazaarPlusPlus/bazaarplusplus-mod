#nullable enable
namespace BazaarPlusPlus.GameInterop.SteamTimeline;

internal enum SteamTimelineClipPriority
{
    None,
    Standard,
    Featured,
}

internal enum SteamTimelineBattleResult
{
    Unknown,
    Victory,
    Defeat,
}

internal sealed class SteamTimelineBattleContext
{
    internal SteamTimelineBattleContext(
        string correlationId,
        int? day,
        string? playerHero,
        string? opponentHero
    )
    {
        CorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? throw new ArgumentException("Correlation ID is required.", nameof(correlationId))
            : correlationId;
        Day = day;
        PlayerHero = playerHero;
        OpponentHero = opponentHero;
    }

    internal string CorrelationId { get; }

    internal int? Day { get; }

    internal string? PlayerHero { get; }

    internal string? OpponentHero { get; }
}

internal readonly struct SteamTimelineRunSummary
{
    internal SteamTimelineRunSummary(int? day, int? wins, string? hero)
    {
        Day = day;
        Wins = wins;
        Hero = hero;
    }

    internal int? Day { get; }

    internal int? Wins { get; }

    internal string? Hero { get; }
}

internal readonly struct SteamTimelineRangeHandle : IEquatable<SteamTimelineRangeHandle>
{
    internal SteamTimelineRangeHandle(ulong value)
    {
        Value = value;
    }

    internal ulong Value { get; }

    internal bool IsValid => Value != 0;

    public bool Equals(SteamTimelineRangeHandle other) => Value == other.Value;

    public override bool Equals(object? obj) =>
        obj is SteamTimelineRangeHandle other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();
}

internal enum SteamTimelineAdapterOperation
{
    Probe,
    StartGamePhase,
    SetGamePhaseId,
    AddGamePhaseTag,
    SetGamePhaseAttribute,
    StartRangeEvent,
    UpdateRangeEvent,
    EndRangeEvent,
    AddInstantaneousEvent,
    EndGamePhase,
}

internal sealed class SteamTimelineAdapterFailure
{
    internal SteamTimelineAdapterFailure(
        SteamTimelineAdapterOperation operation,
        Exception exception
    )
    {
        Operation = operation;
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
    }

    internal SteamTimelineAdapterOperation Operation { get; }

    internal Exception Exception { get; }
}
