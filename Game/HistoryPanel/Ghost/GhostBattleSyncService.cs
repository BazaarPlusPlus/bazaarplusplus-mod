#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.Identity;
using BazaarPlusPlus.Game.Online;
using TheBazaar;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

internal sealed class GhostBattleSyncService : IDisposable
{
    private const int MaxSyncBattleLimit = 200;

    private readonly HistoryPanelRepository _repository;
    private readonly ModOnlineClient _onlineClient;
    private readonly AuthStore _authStore;

    public GhostBattleSyncService(
        HistoryPanelRepository repository,
        ModOnlineClient onlineClient,
        AuthStore authStore
    )
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _onlineClient = onlineClient ?? throw new ArgumentNullException(nameof(onlineClient));
        _authStore = authStore ?? throw new ArgumentNullException(nameof(authStore));
    }

    public async Task<GhostBattleSyncResult> SyncRecentBattlesAsync(
        CancellationToken cancellationToken
    )
    {
        var bearer = _onlineClient.Bearer;
        if (!bearer.IsAvailable || string.IsNullOrWhiteSpace(bearer.PlayerAccountId))
            return GhostBattleSyncResult.Failure("not_logged_in");

        var apiClient = new GhostBattleApiClient(_onlineClient.HttpClient, _onlineClient.Routes);
        var syncStartedAtUtc = DateTimeOffset.UtcNow;
        var queryResult = await apiClient.QueryAgainstMeAsync(
            bearer.PlayerAccountId!,
            bearer.Token!,
            MaxSyncBattleLimit,
            cancellationToken
        );
        if (!queryResult.Succeeded)
        {
            if (queryResult.ShouldReRegister)
                _onlineClient.HandleUnauthorized(_authStore);
            return GhostBattleSyncResult.Failure(queryResult.Error ?? "ghost_sync_failed");
        }

        _repository.UpsertGhostBattles(bearer.PlayerAccountId!, queryResult.Battles);
        _repository.MarkOldUndownloadedGhostBattlesDeleted(bearer.PlayerAccountId!, syncStartedAtUtc);
        if (ShouldAdvanceCheckpoint(queryResult.Battles.Count, MaxSyncBattleLimit))
            _repository.SaveGhostSyncCheckpointUtc(bearer.PlayerAccountId!, syncStartedAtUtc);
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

        var bearer = _onlineClient.Bearer;
        if (!bearer.IsAvailable || string.IsNullOrWhiteSpace(bearer.PlayerAccountId))
            return GhostBattleReplayDownloadResult.Failure("not_logged_in");

        var apiClient = new GhostBattleApiClient(_onlineClient.HttpClient, _onlineClient.Routes);
        var linkResult = await apiClient.RequestReplayDownloadLinkAsync(
            battleId,
            bearer.Token!,
            cancellationToken
        );
        if (!linkResult.Succeeded)
        {
            if (linkResult.ShouldReRegister)
                _onlineClient.HandleUnauthorized(_authStore);
            return GhostBattleReplayDownloadResult.Failure(
                linkResult.Error ?? "ghost_replay_link_failed"
            );
        }

        var payloadResult = await apiClient.DownloadReplayPayloadAsync(
            battleId,
            linkResult.DownloadUrl!,
            bearer.Token!,
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
        _repository.MarkGhostReplayDownloaded(bearer.PlayerAccountId!, battleId);
        return GhostBattleReplayDownloadResult.Success();
    }

    public void Dispose()
    {
    }

    private static bool ShouldAdvanceCheckpoint(int importedCount, int limit)
    {
        return importedCount < limit;
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
