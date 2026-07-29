#nullable enable
using BazaarPlusPlus.GameInterop.SteamTimeline;

namespace BazaarPlusPlus.Game.SteamTimeline;

internal sealed class SteamTimelineSessionCore
{
    private static readonly IReadOnlyList<SteamTimelineCommand> NoCommands =
        Array.Empty<SteamTimelineCommand>();

    private readonly HashSet<int> _markedLevels = new();
    private readonly HashSet<string> _preparedBattleIds = new(StringComparer.Ordinal);
    private string? _observedRunId;
    private string? _assignedPhaseId;
    private SteamTimelineBattleContext? _pendingBattle;
    private SteamTimelineBattleContext? _activeBattle;
    private SteamTimelineBattleResult _battleResult;

    internal bool IsPhaseActive { get; private set; }

    internal bool HasActiveBattle => _activeBattle != null;

    internal IReadOnlyList<SteamTimelineCommand> ObserveRunId(string? phaseId)
    {
        _observedRunId = Normalize(phaseId);
        if (
            !IsPhaseActive
            || _observedRunId == null
            || string.Equals(_assignedPhaseId, _observedRunId, StringComparison.Ordinal)
        )
        {
            return NoCommands;
        }

        _assignedPhaseId = _observedRunId;
        return new[] { SteamTimelineCommand.SetPhaseId(_observedRunId) };
    }

    internal IReadOnlyList<SteamTimelineCommand> StartRun()
    {
        if (IsPhaseActive)
            return NoCommands;

        IsPhaseActive = true;
        _assignedPhaseId = null;
        _pendingBattle = null;
        _activeBattle = null;
        _battleResult = SteamTimelineBattleResult.Unknown;
        _markedLevels.Clear();
        _preparedBattleIds.Clear();

        var commands = new List<SteamTimelineCommand> { SteamTimelineCommand.StartPhase() };
        if (_observedRunId != null)
        {
            _assignedPhaseId = _observedRunId;
            commands.Add(SteamTimelineCommand.SetPhaseId(_observedRunId));
        }

        return commands;
    }

    internal IReadOnlyList<SteamTimelineCommand> EndRun(SteamTimelineRunExit exit)
    {
        if (!IsPhaseActive)
        {
            ResetRunState();
            return NoCommands;
        }

        var commands = new List<SteamTimelineCommand>();
        if (_activeBattle != null)
            commands.Add(SteamTimelineCommand.InterruptBattleRange(_activeBattle));

        commands.Add(SteamTimelineCommand.EndPhase(exit));
        ResetRunState();
        return commands;
    }

    internal IReadOnlyList<SteamTimelineCommand> PrepareBattle(SteamTimelineBattleContext battle)
    {
        if (battle == null)
            throw new ArgumentNullException(nameof(battle));
        if (!IsPhaseActive || !_preparedBattleIds.Add(battle.CorrelationId))
            return NoCommands;

        var commands = new List<SteamTimelineCommand>();
        if (_activeBattle != null)
        {
            commands.Add(SteamTimelineCommand.InterruptBattleRange(_activeBattle));
            _activeBattle = null;
        }

        _pendingBattle = battle;
        _battleResult = SteamTimelineBattleResult.Unknown;
        return commands;
    }

    internal void ObserveBattleResult(SteamTimelineBattleResult result)
    {
        if (!IsPhaseActive || result == SteamTimelineBattleResult.Unknown)
            return;
        if (_pendingBattle == null && _activeBattle == null)
            return;

        _battleResult = result;
    }

    internal IReadOnlyList<SteamTimelineCommand> StartBattlePlayback()
    {
        if (!IsPhaseActive || _pendingBattle == null || _activeBattle != null)
            return NoCommands;

        _activeBattle = _pendingBattle;
        _pendingBattle = null;
        return new[] { SteamTimelineCommand.StartBattleRange(_activeBattle) };
    }

    internal IReadOnlyList<SteamTimelineCommand> EndBattlePlayback()
    {
        if (!IsPhaseActive || _activeBattle == null)
            return NoCommands;

        var battle = _activeBattle;
        _activeBattle = null;
        var result = _battleResult;
        _battleResult = SteamTimelineBattleResult.Unknown;

        return result == SteamTimelineBattleResult.Unknown
            ? new[] { SteamTimelineCommand.InterruptBattleRange(battle) }
            : new[] { SteamTimelineCommand.CompleteBattleRange(battle, result) };
    }

    internal IReadOnlyList<SteamTimelineCommand> ObserveLevelIncrease(
        int previousLevel,
        int currentLevel,
        int? day,
        string? hero
    )
    {
        if (
            !IsPhaseActive
            || currentLevel <= previousLevel
            || currentLevel <= 0
            || !_markedLevels.Add(currentLevel)
        )
        {
            return NoCommands;
        }

        return new[] { SteamTimelineCommand.AddLevelMarker(currentLevel, day, hero) };
    }

    internal void Reset()
    {
        _observedRunId = null;
        ResetRunState();
    }

    private void ResetRunState()
    {
        IsPhaseActive = false;
        _observedRunId = null;
        _assignedPhaseId = null;
        _pendingBattle = null;
        _activeBattle = null;
        _battleResult = SteamTimelineBattleResult.Unknown;
        _markedLevels.Clear();
        _preparedBattleIds.Clear();
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
