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
        var apiClient = new GhostBattleApiClient(
            _httpClient,
            new RunUploadRequestSigner(_keyStore),
            _endpoint.UploadEndpoint
        );
        var routeClient = CreateAuthenticatedRouteClient();
        var requestResult = await routeClient.SendAsync(
            installId,
            (clientId, token) => apiClient.QueryAgainstMeAsync(clientId, installId, token),
            cancellationToken
        );
        if (!requestResult.RegistrationAvailable)
        {
            return GhostBattleSyncResult.Failure("registration_failed");
        }

        var queryResult = requestResult.Response;
        if (!queryResult.Succeeded)
        {
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
        var apiClient = new GhostBattleApiClient(
            _httpClient,
            new RunUploadRequestSigner(_keyStore),
            _endpoint.UploadEndpoint
        );
        var routeClient = CreateAuthenticatedRouteClient();
        var requestResult = await routeClient.SendAsync(
            installId,
            (clientId, token) =>
                apiClient.RequestReplayDownloadLinkAsync(battleId, clientId, installId, token),
            cancellationToken
        );
        if (!requestResult.RegistrationAvailable)
        {
            return GhostBattleReplayDownloadResult.Failure("registration_failed");
        }

        var linkResult = requestResult.Response;
        if (!linkResult.Succeeded)
        {
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

    private BppAuthenticatedRouteClient CreateAuthenticatedRouteClient()
    {
        var registrationClient = new RunUploadRegistrationClient(
            _httpClient,
            _clientStateStore,
            _keyStore,
            RunUploadScopes.Runs,
            "runs",
            _endpoint.RegistrationEndpoint
        );
        return new BppAuthenticatedRouteClient(
            registrationClient,
            _clientStateStore,
            RunUploadScopes.Runs
        );
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
