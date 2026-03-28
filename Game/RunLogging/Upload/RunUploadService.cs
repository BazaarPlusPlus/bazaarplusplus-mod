#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunUploadService : IDisposable
{
    private readonly RunUploadSqliteStore _store;
    private readonly RunUploadIdentityStore _identityStore;
    private readonly RunUploadClientStateStore _clientStateStore;
    private readonly RunUploadKeyStore _keyStore;
    private readonly RunUploadEndpointSet _endpoint;
    private readonly int _batchSize;
    private readonly HttpClient _httpClient;

    public RunUploadService(
        RunUploadSqliteStore store,
        RunUploadIdentityStore identityStore,
        RunUploadClientStateStore clientStateStore,
        RunUploadKeyStore keyStore,
        RunUploadEndpointSet endpoint,
        int batchSize,
        TimeSpan timeout
    )
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _identityStore = identityStore ?? throw new ArgumentNullException(nameof(identityStore));
        _clientStateStore =
            clientStateStore ?? throw new ArgumentNullException(nameof(clientStateStore));
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));

        _batchSize = Math.Max(1, batchSize);
        _httpClient = new HttpClient { Timeout = timeout };
    }

    public async Task<RunUploadCycleResult> UploadPendingRunsAsync(
        CancellationToken cancellationToken
    )
    {
        var pendingRunIds = _store.GetPendingCompletedRunIds(_batchSize);
        if (pendingRunIds.Count == 0)
            return new RunUploadCycleResult(uploadedCount: 0, hasMorePending: false);

        var installId = _identityStore.GetOrCreateInstallId();
        var uploadedCount = 0;
        var clientId = await EnsureClientRegistrationAsync(installId, cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return new RunUploadCycleResult(uploadedCount: 0, hasMorePending: true);

        var apiClient = CreateApiClient();
        foreach (var runId in pendingRunIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recoveredRegistration = false;
            while (true)
            {
                var attemptedAtUtc = DateTimeOffset.UtcNow;
                var snapshot = _store.TryBuildSnapshot(runId, installId, clientId);
                if (snapshot == null)
                {
                    _store.MarkRunUploadFailed(runId, attemptedAtUtc, "run_snapshot_not_found");
                    break;
                }

                try
                {
                    var json = JsonConvert.SerializeObject(
                        snapshot.Payload,
                        RunUploadSerialization.SerializerSettings
                    );
                    var uploadResult = await apiClient.UploadRunAsync(
                        json,
                        clientId!,
                        installId,
                        runId,
                        cancellationToken
                    );
                    if (!uploadResult.Succeeded)
                    {
                        if (uploadResult.ShouldReRegister && !recoveredRegistration)
                        {
                            recoveredRegistration = true;
                            BppLog.Warn(
                                "RunUploadService",
                                $"Upload rejected for run {runId}; clearing cached client registration and retrying once."
                            );
                            _clientStateStore.ClearScopedClientId(RunUploadScopes.Runs);
                            clientId = await EnsureClientRegistrationAsync(
                                installId,
                                cancellationToken
                            );
                            if (string.IsNullOrWhiteSpace(clientId))
                            {
                                _store.MarkRunUploadFailed(
                                    runId,
                                    attemptedAtUtc,
                                    "registration_unavailable"
                                );
                                break;
                            }

                            continue;
                        }

                        _store.MarkRunUploadFailed(
                            runId,
                            attemptedAtUtc,
                            uploadResult.Error ?? "upload_failed"
                        );
                        BppLog.Warn(
                            "RunUploadService",
                            $"Upload failed for run {runId}: {uploadResult.Error ?? "unknown_error"}."
                        );
                        break;
                    }

                    _store.MarkRunUploaded(
                        runId,
                        snapshot.LastSeq,
                        snapshot.UploadedStatus,
                        DateTimeOffset.UtcNow
                    );
                    uploadedCount++;
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _store.MarkRunUploadFailed(
                        runId,
                        attemptedAtUtc,
                        RunUploadErrorFormatter.Truncate(ex.Message)
                    );
                    BppLog.Warn(
                        "RunUploadService",
                        $"Upload failed for run {runId}: {ex.GetType().Name} - {ex.Message}"
                    );
                    break;
                }
            }
        }

        var hasMorePending = _store.HasMorePendingCompletedRuns();
        return new RunUploadCycleResult(uploadedCount, hasMorePending);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private RunUploadApiClient CreateApiClient()
    {
        return new RunUploadApiClient(
            _httpClient,
            new RunUploadRequestSigner(_keyStore),
            _endpoint.UploadEndpoint
        );
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
