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

        BppLog.Info(
            "RunUploadService",
            $"Starting upload cycle for {pendingRunIds.Count} pending run(s)."
        );

        var installId = _identityStore.GetOrCreateInstallId();
        var uploadedCount = 0;
        var apiClient = CreateApiClient();
        var routeClient = CreateAuthenticatedRouteClient();
        foreach (var runId in pendingRunIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attemptedAtUtc = DateTimeOffset.UtcNow;
            RunUploadSnapshot? snapshot = null;
            BppLog.Info("RunUploadService", $"Preparing upload for run {runId}.");
            try
            {
                var requestResult = await routeClient.SendAsync(
                    installId,
                    async (clientId, token) =>
                    {
                        snapshot = _store.TryBuildSnapshot(runId, installId, clientId);
                        if (snapshot == null)
                        {
                            return RunUploadApiResult.Failure(
                                "run_snapshot_not_found",
                                shouldFallback: false,
                                shouldReRegister: false
                            );
                        }

                        BppLog.Info(
                            "RunUploadService",
                            $"Uploading run {runId} with client_id={clientId}, events={snapshot.Payload.Events.Count}, battles={snapshot.Payload.PvpBattles.Count}."
                        );
                        var json = JsonConvert.SerializeObject(
                            snapshot.Payload,
                            RunUploadSerialization.SerializerSettings
                        );
                        return await apiClient.UploadRunAsync(
                            json,
                            clientId,
                            installId,
                            runId,
                            token
                        );
                    },
                    cancellationToken
                );

                if (!requestResult.RegistrationAvailable)
                {
                    _store.MarkRunUploadFailed(runId, attemptedAtUtc, "registration_unavailable");
                    BppLog.Warn(
                        "RunUploadService",
                        $"Skipping run {runId} because client registration is unavailable."
                    );
                    continue;
                }

                var uploadResult = requestResult.Response;
                if (!uploadResult.Succeeded)
                {
                    _store.MarkRunUploadFailed(
                        runId,
                        attemptedAtUtc,
                        uploadResult.Error ?? "upload_failed"
                    );
                    BppLog.Warn(
                        "RunUploadService",
                        $"Upload failed for run {runId}: {uploadResult.Error ?? "unknown_error"}."
                    );
                    continue;
                }

                if (snapshot == null)
                {
                    _store.MarkRunUploadFailed(runId, attemptedAtUtc, "run_snapshot_not_found");
                    continue;
                }

                _store.MarkRunUploaded(
                    runId,
                    snapshot.LastSeq,
                    snapshot.UploadedStatus,
                    DateTimeOffset.UtcNow
                );
                BppLog.Info(
                    "RunUploadService",
                    $"Uploaded run {runId} with last_seq={snapshot.LastSeq}, status={snapshot.UploadedStatus ?? "unknown"}."
                );
                uploadedCount++;
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
            }
        }

        var hasMorePending = _store.HasMorePendingCompletedRuns();
        BppLog.Info(
            "RunUploadService",
            $"Run upload cycle finished: uploaded={uploadedCount}, remaining={(hasMorePending ? "yes" : "no")}."
        );
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
