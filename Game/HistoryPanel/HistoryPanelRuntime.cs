#nullable enable
using System;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.GameInterop;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelRuntime : IHistoryPanelRuntime
{
    private readonly IRunContext _runContext;

    public HistoryPanelRuntime(
        IRunContext runContext,
        string? runLogDatabasePath,
        string? combatReplayDirectoryPath,
        Func<CombatReplayRuntime?> combatReplayRuntimeAccessor
    )
    {
        _runContext = runContext ?? throw new ArgumentNullException(nameof(runContext));
        RunLogDatabasePath = runLogDatabasePath ?? string.Empty;
        CombatReplayDirectoryPath = combatReplayDirectoryPath ?? string.Empty;
        CombatReplayRuntimeAccessor =
            combatReplayRuntimeAccessor
            ?? throw new ArgumentNullException(nameof(combatReplayRuntimeAccessor));
    }

    public bool IsInGameRun => _runContext.IsInGameRun;

    public string? CurrentServerRunId => _runContext.CurrentServerRunId;

    public string RunLogDatabasePath { get; }

    public string CombatReplayDirectoryPath { get; }

    public Func<CombatReplayRuntime?> CombatReplayRuntimeAccessor { get; }
}
