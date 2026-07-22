#nullable enable
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
        string? combatReplayVideoDirectoryPath,
        string? pluginsDirectoryPath,
        Func<CombatReplayRuntime?> combatReplayRuntimeAccessor
    )
    {
        _runContext = runContext ?? throw new ArgumentNullException(nameof(runContext));
        RunLogDatabasePath = runLogDatabasePath ?? string.Empty;
        CombatReplayDirectoryPath = combatReplayDirectoryPath ?? string.Empty;
        CombatReplayVideoDirectoryPath = combatReplayVideoDirectoryPath ?? string.Empty;
        CombatReportDirectoryPath = ResolveCombatReportDirectoryPath(
            CombatReplayVideoDirectoryPath
        );
        PluginsDirectoryPath = pluginsDirectoryPath ?? string.Empty;
        CombatReplayRuntimeAccessor =
            combatReplayRuntimeAccessor
            ?? throw new ArgumentNullException(nameof(combatReplayRuntimeAccessor));
    }

    public bool IsInGameRun => _runContext.IsInGameRun;

    public string? CurrentServerRunId => _runContext.CurrentServerRunId;

    public string RunLogDatabasePath { get; }

    public string CombatReplayDirectoryPath { get; }

    public string CombatReplayVideoDirectoryPath { get; }

    public string CombatReportDirectoryPath { get; }

    public string PluginsDirectoryPath { get; }

    public Func<CombatReplayRuntime?> CombatReplayRuntimeAccessor { get; }

    private static string ResolveCombatReportDirectoryPath(string videoDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(videoDirectoryPath))
            return string.Empty;

        try
        {
            var dataRoot = Path.GetDirectoryName(Path.GetFullPath(videoDirectoryPath));
            return string.IsNullOrWhiteSpace(dataRoot)
                ? string.Empty
                : Path.Combine(dataRoot, "reports");
        }
        catch
        {
            return string.Empty;
        }
    }
}
