#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.RunLogging.Upload;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class GhostBattleSyncService : IDisposable
{
    private readonly HistoryPanelRepository _repository;
    private readonly RunUploadIdentityStore _identityStore;
    private readonly RunUploadClientStateStore _clientStateStore;
    private readonly RunUploadKeyStore _keyStore;
    private readonly RunUploadEndpointSet _endpoint;
    private readonly HttpClient _httpClient;

    public GhostBattleSyncService(
        HistoryPanelRepository repository,
        RunUploadIdentityStore identityStore,
        RunUploadClientStateStore clientStateStore,
        RunUploadKeyStore keyStore,
        RunUploadEndpointSet endpoint,
        TimeSpan timeout
    )
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _identityStore = identityStore ?? throw new ArgumentNullException(nameof(identityStore));
        _clientStateStore =
            clientStateStore ?? throw new ArgumentNullException(nameof(clientStateStore));
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _httpClient = new HttpClient { Timeout = timeout };
    }

    public async Task<GhostBattleSyncResult> SyncRecentBattlesAsync(
        CancellationToken cancellationToken
    )
    {
        var installId = _identityStore.GetOrCreateInstallId();
        var clientId = await EnsureClientRegistrationAsync(installId, cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return GhostBattleSyncResult.Failure("registration_failed");
        }

        var apiClient = new GhostBattleApiClient(
            _httpClient,
            new RunUploadRequestSigner(_keyStore),
            _endpoint.UploadEndpoint
        );
        var queryResult = await apiClient.QueryAgainstMeAsync(
            clientId,
            installId,
            cancellationToken
        );
        if (!queryResult.Succeeded)
        {
            if (queryResult.ShouldReRegister)
                _clientStateStore.ClearScopedClientId(RunUploadScopes.Runs);

            return GhostBattleSyncResult.Failure(queryResult.Error ?? "ghost_sync_failed");
        }

        _repository.ReplaceGhostBattles(queryResult.Battles);
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

        var installId = _identityStore.GetOrCreateInstallId();
        var clientId = await EnsureClientRegistrationAsync(installId, cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return GhostBattleReplayDownloadResult.Failure("registration_failed");
        }

        var apiClient = new GhostBattleApiClient(
            _httpClient,
            new RunUploadRequestSigner(_keyStore),
            _endpoint.UploadEndpoint
        );
        var linkResult = await apiClient.RequestReplayDownloadLinkAsync(
            battleId,
            clientId,
            installId,
            cancellationToken
        );
        if (!linkResult.Succeeded)
        {
            if (linkResult.ShouldReRegister)
                _clientStateStore.ClearScopedClientId(RunUploadScopes.Runs);

            return GhostBattleReplayDownloadResult.Failure(
                linkResult.Error ?? "ghost_replay_link_failed"
            );
        }

        var payloadResult = await apiClient.DownloadReplayPayloadAsync(
            linkResult.DownloadUrl!,
            cancellationToken
        );
        if (!payloadResult.Succeeded || payloadResult.Payload?.ReplayPayload == null)
        {
            return GhostBattleReplayDownloadResult.Failure(
                payloadResult.Error ?? "ghost_replay_payload_failed"
            );
        }

        var payloadStore = new CombatReplayPayloadStore(replayDirectoryPath);
        payloadStore.Save(payloadResult.Payload.ReplayPayload);
        _repository.MarkGhostReplayDownloaded(battleId);
        return GhostBattleReplayDownloadResult.Success();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private Task<string?> EnsureClientRegistrationAsync(
        string installId,
        CancellationToken cancellationToken
    )
    {
        var registrationClient = new RunUploadRegistrationClient(
            _httpClient,
            _clientStateStore,
            _keyStore,
            RunUploadScopes.Runs,
            "runs",
            _endpoint.RegistrationEndpoint
        );
        return registrationClient.EnsureClientRegistrationAsync(installId, cancellationToken);
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
