#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;

namespace BazaarPlusPlus.Game.HistoryPanel;

// Decides what cards the preview renderer should show, given the current selection.
// The coordinator owns selection state; this class is a pure projection on top of records
// plus an optional disk lookup for ghost payloads that the repository does not load eagerly.
internal sealed class HistoryPanelPreviewSource
{
    private const string GhostPayloadDirectoryName = "GhostBattlePayloads";

    private readonly IHistoryPanelRuntime _runtime;

    public HistoryPanelPreviewSource(IHistoryPanelRuntime runtime)
    {
        _runtime = runtime;
    }

    public PreviewRequest Build(
        PreviewSelectionMode previewSelectionMode,
        HistorySectionMode sectionMode,
        HistoryBattleRecord? activeSelectedBattle,
        HistoryRunRecord? selectedRun,
        IReadOnlyList<HistoryBattleRecord> runBattles
    )
    {
        if (previewSelectionMode == PreviewSelectionMode.Battle && activeSelectedBattle != null)
        {
            var previewData =
                sectionMode == HistorySectionMode.Ghost
                    ? ResolveGhostPreviewData(activeSelectedBattle)
                    : HistoryBattlePreviewProjection.Build(activeSelectedBattle.Snapshots).OpponentHandOnly();
            return new PreviewRequest($"battle:{activeSelectedBattle.BattleId}", previewData);
        }

        var runPreviewBattle = PickRunPreviewBattle(runBattles);
        if (runPreviewBattle != null)
        {
            return new PreviewRequest(
                $"run:{selectedRun?.RunId}:{runPreviewBattle.BattleId}",
                HistoryBattlePreviewProjection.Build(runPreviewBattle.Snapshots).PlayerHandOnly()
            );
        }

        return new PreviewRequest(null, null);
    }

    // Ghost replay payload snapshots stay in the uploader's original perspective.
    // For the local "against me" view, our board is stored on the opponent side.
    private HistoryBattlePreviewData ResolveGhostPreviewData(HistoryBattleRecord battle)
    {
        if (battle.Source != HistoryBattleSource.Ghost)
            return HistoryBattlePreviewProjection.Build(battle.Snapshots);

        var replayDirectoryPath = _runtime.CombatReplayDirectoryPath;
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return HistoryBattlePreviewProjection.BuildEmpty();

        var ghostPayloadStore = new GhostBattlePayloadStore(
            BuildGhostPayloadDirectoryPath(replayDirectoryPath)
        );
        var ghostPayload = ghostPayloadStore.Load(battle.BattleId);
        var snapshots = ghostPayload?.BattleManifest?.Snapshots;
        if (snapshots == null)
            return HistoryBattlePreviewProjection.BuildEmpty();

        return HistoryBattlePreviewProjection.Build(snapshots).OpponentHandOnly();
    }

    private static HistoryBattleRecord? PickRunPreviewBattle(
        IReadOnlyList<HistoryBattleRecord> runBattles
    )
    {
        if (runBattles.Count == 0)
            return null;

        return runBattles
            .OrderByDescending(battle => battle.Day ?? int.MinValue)
            .ThenByDescending(battle => battle.Hour ?? int.MinValue)
            .ThenByDescending(battle => battle.RecordedAtUtc)
            .FirstOrDefault();
    }

    private static string BuildGhostPayloadDirectoryPath(string replayDirectoryPath)
    {
        var parentDirectory = Path.GetDirectoryName(replayDirectoryPath);
        return string.IsNullOrWhiteSpace(parentDirectory)
            ? Path.Combine(replayDirectoryPath, GhostPayloadDirectoryName)
            : Path.Combine(parentDirectory, GhostPayloadDirectoryName);
    }

    public readonly struct PreviewRequest
    {
        public PreviewRequest(string? renderId, HistoryBattlePreviewData? previewData)
        {
            RenderId = renderId;
            PreviewData = previewData;
        }

        public string? RenderId { get; }

        public HistoryBattlePreviewData? PreviewData { get; }
    }
}
