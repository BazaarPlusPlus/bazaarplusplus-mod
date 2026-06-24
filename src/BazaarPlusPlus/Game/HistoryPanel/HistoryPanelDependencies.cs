#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.ModApi.Clients;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelDependencies
{
    public HistoryPanelDependencies(
        IHistoryPanelRuntime runtime,
        HistoryPanelDataService dataService,
        HistoryPanelReplayService replayService,
        GhostBattleSyncService? ghostSyncService
    )
        : this(runtime, dataService, replayService, ghostSyncService, null) { }

    public HistoryPanelDependencies(
        IHistoryPanelRuntime runtime,
        HistoryPanelDataService dataService,
        HistoryPanelReplayService replayService,
        GhostBattleSyncService? ghostSyncService,
        IHistoryPanelServerHealthProbe? serverHealthProbe
    )
        : this(runtime, dataService, replayService, ghostSyncService, serverHealthProbe, null) { }

    public HistoryPanelDependencies(
        IHistoryPanelRuntime runtime,
        HistoryPanelDataService dataService,
        HistoryPanelReplayService replayService,
        GhostBattleSyncService? ghostSyncService,
        IHistoryPanelServerHealthProbe? serverHealthProbe,
        BazaarDbLinkClient? accountLinkClient
    )
    {
        Runtime = runtime;
        DataService = dataService;
        ReplayService = replayService;
        GhostSyncService = ghostSyncService;
        ServerHealthProbe = serverHealthProbe;
        AccountLinkClient = accountLinkClient;
    }

    public IHistoryPanelRuntime Runtime { get; }

    public HistoryPanelDataService DataService { get; }

    public HistoryPanelReplayService ReplayService { get; }

    public GhostBattleSyncService? GhostSyncService { get; }

    public IHistoryPanelServerHealthProbe? ServerHealthProbe { get; }

    public BazaarDbLinkClient? AccountLinkClient { get; }
}
