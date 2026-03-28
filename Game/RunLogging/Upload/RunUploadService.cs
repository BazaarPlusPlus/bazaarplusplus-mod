#nullable enable
using System;
using System.Collections.Generic;
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
    private readonly RunUploadRouteSelector _routeSelector;
    private readonly RunUploadEndpointSet? _globalEndpoint;
    private readonly RunUploadEndpointSet? _cnEndpoint;
    private readonly int _batchSize;
    private readonly HttpClient _httpClient;

    public RunUploadService(
        RunUploadSqliteStore store,
        RunUploadIdentityStore identityStore,
        RunUploadClientStateStore clientStateStore,
        RunUploadKeyStore keyStore,
        RunUploadRouteSelector routeSelector,
        RunUploadEndpointSet? globalEndpoint,
        RunUploadEndpointSet? cnEndpoint,
        int batchSize,
        TimeSpan timeout
    )
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _identityStore = identityStore ?? throw new ArgumentNullException(nameof(identityStore));
        _clientStateStore = clientStateStore ?? throw new ArgumentNullException(nameof(clientStateStore));
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        _routeSelector = routeSelector ?? throw new ArgumentNullException(nameof(routeSelector));
        _globalEndpoint = globalEndpoint;
        _cnEndpoint = cnEndpoint;

        _batchSize = Math.Max(1, batchSize);
        _httpClient = new HttpClient
        {
            Timeout = timeout,
        };
    }

    public async Task<RunUploadCycleResult> UploadPendingRunsAsync(CancellationToken cancellationToken)
    {
        var pendingRunIds = _store.GetPendingCompletedRunIds(_batchSize);
        if (pendingRunIds.Count == 0)
            return new RunUploadCycleResult(uploadedCount: 0, hasMorePending: false);

        var installId = _identityStore.GetOrCreateInstallId();
        var remainingRunIds = new List<string>(pendingRunIds);
        var uploadedCount = 0;
        foreach (var endpoint in _routeSelector.GetRouteOrder(_globalEndpoint, _cnEndpoint))
        {
            if (remainingRunIds.Count == 0)
                break;

            cancellationToken.ThrowIfCancellationRequested();
            var clientId = await EnsureClientRegistrationAsync(endpoint, installId, cancellationToken);
            if (string.IsNullOrWhiteSpace(clientId))
            {
                _routeSelector.RecordRouteFailure(endpoint.RouteKind);
                continue;
            }

            var apiClient = CreateApiClient(endpoint);
            var routeUploadedAny = false;
            var shouldTryFallback = false;

            foreach (var runId in remainingRunIds.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var recoveredRegistration = false;
                while (true)
                {
                    var attemptedAtUtc = DateTimeOffset.UtcNow;
                    var snapshot = _store.TryBuildSnapshot(runId, installId, clientId);
                    if (snapshot == null)
                    {
                        _store.MarkRunUploadFailed(
                            runId,
                            attemptedAtUtc,
                            "run_snapshot_not_found"
                        );
                        break;
                    }

                    try
                    {
                        if (string.IsNullOrWhiteSpace(clientId))
                        {
                            shouldTryFallback = true;
                            BppLog.Warn(
                                "RunUploadService",
                                $"Client registration was unavailable for route {endpoint.RouteKind}; trying fallback."
                            );
                            break;
                        }

                        var json = JsonConvert.SerializeObject(
                            snapshot.Payload,
                            RunUploadSerialization.SerializerSettings
                        );
                        var uploadResult = await apiClient.UploadRunAsync(
                            json,
                            clientId,
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
                                    $"Upload rejected for run {runId} on route {endpoint.RouteKind}; clearing cached client registration and retrying once."
                                );
                                _clientStateStore.ClearClientId(endpoint.RouteKind);
                                clientId = await EnsureClientRegistrationAsync(
                                    endpoint,
                                    installId,
                                    cancellationToken
                                );
                                if (string.IsNullOrWhiteSpace(clientId))
                                {
                                    shouldTryFallback = true;
                                    BppLog.Warn(
                                        "RunUploadService",
                                        $"Re-registration failed for route {endpoint.RouteKind}; trying fallback."
                                    );
                                    break;
                                }

                                continue;
                            }

                            if (uploadResult.ShouldFallback)
                            {
                                shouldTryFallback = true;
                                BppLog.Warn(
                                    "RunUploadService",
                                    $"Route {endpoint.RouteKind} failed for run {runId}; trying fallback."
                                );
                                break;
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
                        remainingRunIds.Remove(runId);
                        uploadedCount++;
                        routeUploadedAny = true;
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        shouldTryFallback = true;
                        BppLog.Warn(
                            "RunUploadService",
                            $"Upload failed for run {runId}: {ex.GetType().Name} - {ex.Message}"
                        );
                        break;
                    }
                }

                if (shouldTryFallback)
                    break;
            }

            if (routeUploadedAny)
                _routeSelector.RecordRouteSuccess(endpoint.RouteKind);
            else if (shouldTryFallback)
                _routeSelector.RecordRouteFailure(endpoint.RouteKind);

            if (!shouldTryFallback)
                break;
        }

        var hasMorePending = remainingRunIds.Count > 0 || _store.HasMorePendingCompletedRuns();
        return new RunUploadCycleResult(uploadedCount, hasMorePending);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private RunUploadApiClient CreateApiClient(RunUploadEndpointSet endpoint)
    {
        return new RunUploadApiClient(
            _httpClient,
            new RunUploadRequestSigner(_keyStore),
            endpoint.UploadEndpoint
        );
    }

    private Task<string?> EnsureClientRegistrationAsync(
        RunUploadEndpointSet endpoint,
        string installId,
        CancellationToken cancellationToken
    )
    {
        var registrationClient = new RunUploadRegistrationClient(
            _httpClient,
            _clientStateStore,
            _keyStore,
            endpoint.RouteKind,
            endpoint.RegistrationEndpoint
        );
        return registrationClient.EnsureClientRegistrationAsync(installId, cancellationToken);
    }
}
