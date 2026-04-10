#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.Identity;
using BazaarPlusPlus.Game.Online;
using TheBazaar;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

internal sealed class GhostBattleSyncService : IDisposable
{
    private static readonly TimeSpan CheckpointLookbackPadding = TimeSpan.FromHours(24);
    private const int InitialSyncLookbackDays = 3;
    private const int MaxSyncLookbackDays = 14;
    private const int MaxSyncBattleLimit = 200;

    private readonly HistoryPanelRepository _repository;
    private readonly InstallationRecordStore _installationStore;
    private readonly V3Routes _routes;
    private readonly HttpClient _httpClient;

    public GhostBattleSyncService(
        HistoryPanelRepository repository,
        InstallationRecordStore installationStore,
        V3Routes routes,
        TimeSpan timeout
    )
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _installationStore =
            installationStore ?? throw new ArgumentNullException(nameof(installationStore));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
        _httpClient = new HttpClient { Timeout = timeout };
    }

    public async Task<GhostBattleSyncResult> SyncRecentBattlesAsync(
        CancellationToken cancellationToken
    )
    {
        var localPlayerAccountId = TryGetCurrentPlayerAccountId();
        if (string.IsNullOrWhiteSpace(localPlayerAccountId))
            return GhostBattleSyncResult.Failure("player_account_id_unavailable");
        if (!_installationStore.TryLoad(out var installation) || installation == null)
            return GhostBattleSyncResult.Failure("installation_unavailable");

        var apiClient = new GhostBattleApiClient(
            _httpClient,
            new InstallationRequestSigner(_installationStore),
            _routes
        );
        var syncStartedAtUtc = DateTimeOffset.UtcNow;
        var checkpointUtc = _repository.TryGetGhostSyncCheckpointUtc(localPlayerAccountId);
        var lookbackDays = CalculateLookbackDays(checkpointUtc, syncStartedAtUtc);
        var queryResult = await apiClient.QueryAgainstMeAsync(
            installation,
            lookbackDays,
            MaxSyncBattleLimit,
            cancellationToken
        );
        if (!queryResult.Succeeded)
        {
            return GhostBattleSyncResult.Failure(queryResult.Error ?? "ghost_sync_failed");
        }

        _repository.UpsertGhostBattles(localPlayerAccountId, queryResult.Battles);
        _repository.MarkOldUndownloadedGhostBattlesDeleted(localPlayerAccountId, syncStartedAtUtc);
        if (ShouldAdvanceCheckpoint(queryResult.Battles.Count, MaxSyncBattleLimit, lookbackDays))
            _repository.SaveGhostSyncCheckpointUtc(localPlayerAccountId, syncStartedAtUtc);
        return GhostBattleSyncResult.Success(queryResult.Battles.Count);
    }

    public async Task<GhostBattleReplayDownloadResult> DownloadReplayAsync(
        string battleId,
        string replayDirectoryPath,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return GhostBattleReplayDownloadResult.Failure("battle_id_required");
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return GhostBattleReplayDownloadResult.Failure("replay_directory_required");
        var localPlayerAccountId = TryGetCurrentPlayerAccountId();
        if (string.IsNullOrWhiteSpace(localPlayerAccountId))
            return GhostBattleReplayDownloadResult.Failure("player_account_id_unavailable");
        if (!_installationStore.TryLoad(out var installation) || installation == null)
            return GhostBattleReplayDownloadResult.Failure("installation_unavailable");

        var apiClient = new GhostBattleApiClient(
            _httpClient,
            new InstallationRequestSigner(_installationStore),
            _routes
        );
        var linkResult = await apiClient.RequestReplayDownloadLinkAsync(
            battleId,
            installation,
            cancellationToken
        );
        if (!linkResult.Succeeded)
        {
            return GhostBattleReplayDownloadResult.Failure(
                linkResult.Error ?? "ghost_replay_link_failed"
            );
        }

        var payloadResult = await apiClient.DownloadReplayPayloadAsync(
            battleId,
            linkResult.DownloadUrl!,
            cancellationToken
        );
        if (!payloadResult.Succeeded || payloadResult.Payload?.ReplayPayload == null)
        {
            return GhostBattleReplayDownloadResult.Failure(
                payloadResult.Error ?? "ghost_replay_payload_failed"
            );
        }
        if (
            !string.Equals(
                payloadResult.Payload.ReplayPayload.BattleId,
                battleId,
                StringComparison.Ordinal
            )
        )
        {
            return GhostBattleReplayDownloadResult.Failure("ghost_replay_battle_id_mismatch");
        }

        var payloadStore = new GhostBattlePayloadStore(
            BuildGhostBattlePayloadDirectoryPath(replayDirectoryPath)
        );
        payloadStore.Save(payloadResult.Payload);
        _repository.MarkGhostReplayDownloaded(localPlayerAccountId, battleId);
        return GhostBattleReplayDownloadResult.Success();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static int CalculateLookbackDays(DateTimeOffset? checkpointUtc, DateTimeOffset nowUtc)
    {
        if (checkpointUtc == null)
            return InitialSyncLookbackDays;

        var fromUtc = checkpointUtc.Value - CheckpointLookbackPadding;
        var totalDays = Math.Ceiling((nowUtc - fromUtc).TotalDays);
        if (double.IsNaN(totalDays) || double.IsInfinity(totalDays))
            return MaxSyncLookbackDays;

        return Math.Clamp((int)Math.Max(1, totalDays), 1, MaxSyncLookbackDays);
    }

    private static bool ShouldAdvanceCheckpoint(int importedCount, int limit, int lookbackDays)
    {
        return importedCount < limit;
    }

    private static string? TryGetCurrentPlayerAccountId()
    {
        try
        {
            return BppClientCacheBridge.TryGetProfileAccountId();
        }
        catch
        {
            return null;
        }
    }

    private static string BuildGhostBattlePayloadDirectoryPath(string replayDirectoryPath)
    {
        var parentDirectory = System.IO.Path.GetDirectoryName(replayDirectoryPath);
        return string.IsNullOrWhiteSpace(parentDirectory)
            ? System.IO.Path.Combine(replayDirectoryPath, "GhostBattlePayloads")
            : System.IO.Path.Combine(parentDirectory, "GhostBattlePayloads");
    }
}

internal readonly struct GhostBattleSyncResult
{
    private GhostBattleSyncResult(bool succeeded, int importedCount, string? error)
    {
        Succeeded = succeeded;
        ImportedCount = importedCount;
        Error = error;
    }

    public bool Succeeded { get; }

    public int ImportedCount { get; }

    public string? Error { get; }

    public static GhostBattleSyncResult Success(int importedCount) =>
        new(true, importedCount, null);

    public static GhostBattleSyncResult Failure(string error) => new(false, 0, error);
}

internal readonly struct GhostBattleReplayDownloadResult
{
    private GhostBattleReplayDownloadResult(bool succeeded, string? error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    public static GhostBattleReplayDownloadResult Success() => new(true, null);

    public static GhostBattleReplayDownloadResult Failure(string error) => new(false, error);
}
