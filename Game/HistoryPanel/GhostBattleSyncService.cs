#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.RunLogging.Upload;
using TheBazaar;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class GhostBattleSyncService : IDisposable
{
    private static readonly TimeSpan CheckpointLookbackPadding = TimeSpan.FromHours(24);
    private const int InitialSyncLookbackDays = 3;
    private const int MaxSyncLookbackDays = 14;
    private const int MaxSyncBattleLimit = 200;

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
        var localPlayerAccountId = TryGetCurrentPlayerAccountId();
        if (string.IsNullOrWhiteSpace(localPlayerAccountId))
            return GhostBattleSyncResult.Failure("player_account_id_unavailable");

        var installId = _identityStore.GetOrCreateInstallId();
        var apiClient = new GhostBattleApiClient(
            _httpClient,
            new RunUploadRequestSigner(_keyStore),
            _endpoint.UploadEndpoint
        );
        var routeClient = CreateAuthenticatedRouteClient();
        var syncStartedAtUtc = DateTimeOffset.UtcNow;
        var checkpointUtc = _repository.TryGetGhostSyncCheckpointUtc(localPlayerAccountId);
        var lookbackDays = CalculateLookbackDays(checkpointUtc, syncStartedAtUtc);
        var requestResult = await routeClient.SendAsync(
            installId,
            async (clientId, token) =>
            {
                var bindingResult = await EnsurePlayerBindingAsync(
                    clientId,
                    installId,
                    localPlayerAccountId,
                    token
                );
                if (!bindingResult.Succeeded)
                {
                    BppLog.Warn(
                        "GhostBattleSync",
                        $"Binding failed for ghost sync on client {clientId}: {bindingResult.Error ?? "binding_failed"}. Continuing with query attempt."
                    );
                }

                var queryResult = await apiClient.QueryAgainstMeAsync(
                    clientId,
                    installId,
                    lookbackDays,
                    MaxSyncBattleLimit,
                    token
                );
                if (
                    !bindingResult.Succeeded
                    && !queryResult.Succeeded
                    && ShouldTreatGhostErrorAsBindingFailure(queryResult.Error)
                )
                {
                    return GhostBattleApiResult.Failure(
                        bindingResult.Error ?? "binding_failed",
                        bindingResult.ShouldFallback || queryResult.ShouldFallback,
                        bindingResult.ShouldReRegister || queryResult.ShouldReRegister
                    );
                }

                return queryResult;
            },
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

        var installId = _identityStore.GetOrCreateInstallId();
        var apiClient = new GhostBattleApiClient(
            _httpClient,
            new RunUploadRequestSigner(_keyStore),
            _endpoint.UploadEndpoint
        );
        var routeClient = CreateAuthenticatedRouteClient();
        var requestResult = await routeClient.SendAsync(
            installId,
            async (clientId, token) =>
            {
                var bindingResult = await EnsurePlayerBindingAsync(
                    clientId,
                    installId,
                    localPlayerAccountId,
                    token
                );
                if (!bindingResult.Succeeded)
                {
                    BppLog.Warn(
                        "GhostBattleSync",
                        $"Binding failed for ghost replay download on client {clientId}: {bindingResult.Error ?? "binding_failed"}. Continuing with replay link request."
                    );
                }

                var linkResult = await apiClient.RequestReplayDownloadLinkAsync(
                    battleId,
                    clientId,
                    installId,
                    token
                );
                if (
                    !bindingResult.Succeeded
                    && !linkResult.Succeeded
                    && ShouldTreatGhostErrorAsBindingFailure(linkResult.Error)
                )
                {
                    return GhostBattleReplayDownloadLinkResult.Failure(
                        bindingResult.Error ?? "binding_failed",
                        bindingResult.ShouldFallback || linkResult.ShouldFallback,
                        bindingResult.ShouldReRegister || linkResult.ShouldReRegister
                    );
                }

                return linkResult;
            },
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

    private async Task<RunUploadBindingResult> EnsurePlayerBindingAsync(
        string clientId,
        string installId,
        string playerAccountId,
        CancellationToken cancellationToken
    )
    {
        var bindEndpoint = TryDeriveBindEndpoint(_endpoint.RegistrationEndpoint);
        if (string.IsNullOrWhiteSpace(bindEndpoint))
        {
            return RunUploadBindingResult.Failure(
                "bind_endpoint_unavailable",
                shouldFallback: false,
                shouldReRegister: false
            );
        }
        var bindingClient = new RunUploadBindingClient(
            _httpClient,
            new RunUploadRequestSigner(_keyStore),
            bindEndpoint
        );
        var result = await bindingClient.BindPlayerAccountAsync(
            clientId,
            installId,
            playerAccountId,
            cancellationToken
        );
        if (result.Succeeded)
        {
            _clientStateStore.SaveScopedBoundPlayerAccountId(
                RunUploadScopes.Runs,
                clientId,
                playerAccountId
            );
        }

        return result;
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

    private static int CalculateLookbackDays(
        DateTimeOffset? checkpointUtc,
        DateTimeOffset nowUtc
    )
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

    private static bool ShouldTreatGhostErrorAsBindingFailure(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return false;

        return error.Contains("battle_forbidden", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetCurrentPlayerAccountId()
    {
        try
        {
            return ClientCache.Profile.Value?.AccountId.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryDeriveBindEndpoint(string registrationEndpoint)
    {
        if (
            string.IsNullOrWhiteSpace(registrationEndpoint)
            || !Uri.TryCreate(registrationEndpoint, UriKind.Absolute, out var registrationUri)
        )
        {
            return null;
        }

        var absolutePath = registrationUri.AbsolutePath;
        const string suffix = "/clients/register";
        if (!absolutePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return null;

        var bindPath = absolutePath[..^suffix.Length] + "/clients/bind";
        var builder = new UriBuilder(registrationUri) { Path = bindPath, Query = string.Empty };
        return builder.Uri.ToString();
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
