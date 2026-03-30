#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.RunLogging.Upload;

namespace BazaarPlusPlus.Game.CombatReplay.Upload;

internal sealed class BattleUploadService : IDisposable
{
    private readonly BattleUploadSqliteStore _store;
    private readonly RunUploadIdentityStore _identityStore;
    private readonly RunUploadClientStateStore _clientStateStore;
    private readonly RunUploadKeyStore _keyStore;
    private readonly string _registrationEndpoint;
    private readonly string _uploadEndpoint;
    private readonly int _batchSize;
    private readonly HttpClient _httpClient;

    public BattleUploadService(
        BattleUploadSqliteStore store,
        RunUploadIdentityStore identityStore,
        RunUploadClientStateStore clientStateStore,
        RunUploadKeyStore keyStore,
        string registrationEndpoint,
        string uploadEndpoint,
        int batchSize,
        TimeSpan timeout
    )
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _identityStore = identityStore ?? throw new ArgumentNullException(nameof(identityStore));
        _clientStateStore =
            clientStateStore ?? throw new ArgumentNullException(nameof(clientStateStore));
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        if (string.IsNullOrWhiteSpace(registrationEndpoint))
            throw new ArgumentException(
                "Registration endpoint is required.",
                nameof(registrationEndpoint)
            );
        if (string.IsNullOrWhiteSpace(uploadEndpoint))
            throw new ArgumentException("Upload endpoint is required.", nameof(uploadEndpoint));

        _registrationEndpoint = registrationEndpoint;
        _uploadEndpoint = uploadEndpoint;
        _batchSize = Math.Max(1, batchSize);
        _httpClient = new HttpClient { Timeout = timeout };
    }

    public async Task<BattleUploadCycleResult> UploadPendingBattlesAsync(
        CancellationToken cancellationToken
    )
    {
        var pendingBattleIds = _store.GetPendingBattleIds(_batchSize);
        if (pendingBattleIds.Count == 0)
        {
            BppLog.Info("BattleUploadService", "No battle artifacts are waiting for upload.");
            return new BattleUploadCycleResult(uploadedCount: 0, hasMorePending: false);
        }

        BppLog.Info(
            "BattleUploadService",
            $"Starting upload cycle for {pendingBattleIds.Count} pending battle artifact(s)."
        );

        var installId = _identityStore.GetOrCreateInstallId();
        var uploadedCount = 0;
        var apiClient = new BattleUploadApiClient(
            _httpClient,
            new BattleUploadRequestSigner(_keyStore),
            _uploadEndpoint
        );
        var routeClient = CreateAuthenticatedRouteClient();

        foreach (var battleId in pendingBattleIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attemptedAtUtc = DateTimeOffset.UtcNow;
            BppLog.Info("BattleUploadService", $"Preparing upload for battle {battleId}.");
            var preflightSnapshot = _store.TryBuildSnapshot(battleId, installId, clientId: null);
            if (preflightSnapshot == null)
            {
                _store.MarkReplayUploadTerminalFailure(
                    battleId,
                    attemptedAtUtc,
                    "replay_snapshot_not_found"
                );
                BppLog.Warn(
                    "BattleUploadService",
                    $"Marking battle {battleId} as terminal failure because the local snapshot is unavailable."
                );
                continue;
            }

            BattleUploadSnapshot? snapshot = null;
            try
            {
                var requestResult = await routeClient.SendAsync(
                    installId,
                    async (clientId, token) =>
                    {
                        snapshot = _store.TryBuildSnapshot(battleId, installId, clientId);
                        if (snapshot == null)
                        {
                            return BattleUploadApiResult.Failure(
                                "replay_snapshot_not_found",
                                shouldFallback: false,
                                shouldReRegister: false
                            );
                        }

                        BppLog.Info(
                            "BattleUploadService",
                            $"Uploading battle {battleId} with client_id={clientId}, run_id={snapshot.Payload.RunId ?? "none"}."
                        );
                        return await apiClient.UploadBattleAsync(
                            snapshot.Json,
                            clientId,
                            installId,
                            battleId,
                            snapshot.Payload.RunId,
                            token
                        );
                    },
                    cancellationToken
                );

                if (!requestResult.RegistrationAvailable)
                {
                    _store.MarkReplayUploadFailed(
                        battleId,
                        attemptedAtUtc,
                        "registration_unavailable"
                    );
                    BppLog.Warn(
                        "BattleUploadService",
                        $"Skipping battle {battleId} because client registration is unavailable."
                    );
                    continue;
                }

                var uploadResult = requestResult.Response;
                if (!uploadResult.Succeeded)
                {
                    if (
                        string.Equals(
                            uploadResult.Error,
                            "replay_snapshot_not_found",
                            StringComparison.Ordinal
                        )
                    )
                    {
                        _store.MarkReplayUploadTerminalFailure(
                            battleId,
                            attemptedAtUtc,
                            "replay_snapshot_not_found"
                        );
                        BppLog.Warn(
                            "BattleUploadService",
                            $"Marking battle {battleId} as terminal failure because the local snapshot disappeared before upload."
                        );
                        continue;
                    }

                    _store.MarkReplayUploadFailed(
                        battleId,
                        attemptedAtUtc,
                        uploadResult.Error ?? "upload_failed"
                    );
                    BppLog.Warn(
                        "BattleUploadService",
                        $"Upload failed for battle {battleId}: {uploadResult.Error ?? "unknown_error"}."
                    );
                    continue;
                }

                if (snapshot == null)
                {
                    _store.MarkReplayUploadTerminalFailure(
                        battleId,
                        attemptedAtUtc,
                        "replay_snapshot_not_found"
                    );
                    BppLog.Warn(
                        "BattleUploadService",
                        $"Marking battle {battleId} as terminal failure because the local snapshot was lost before completion."
                    );
                    continue;
                }

                _store.MarkReplayUploaded(battleId, DateTimeOffset.UtcNow);
                BppLog.Info(
                    "BattleUploadService",
                    $"Uploaded battle {battleId} with object_key={uploadResult.ObjectKey ?? "none"}."
                );
                uploadedCount++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _store.MarkReplayUploadFailed(
                    battleId,
                    attemptedAtUtc,
                    RunUploadErrorFormatter.Truncate(ex.Message)
                );
                BppLog.Warn(
                    "BattleUploadService",
                    $"Upload failed for battle {battleId}: {ex.GetType().Name} - {ex.Message}"
                );
            }
        }

        var hasMorePending = _store.HasMorePendingReplays();
        BppLog.Info(
            "BattleUploadService",
            $"Battle upload cycle finished: uploaded={uploadedCount}, remaining={(hasMorePending ? "yes" : "no")}."
        );
        return new BattleUploadCycleResult(uploadedCount, hasMorePending);
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
            RunUploadScopes.Replays,
            "replays",
            _registrationEndpoint
        );
        return new BppAuthenticatedRouteClient(
            registrationClient,
            _clientStateStore,
            RunUploadScopes.Replays
        );
    }

    internal static string? TryDeriveBattleUploadEndpoint(string? runUploadEndpoint)
    {
        if (
            string.IsNullOrWhiteSpace(runUploadEndpoint)
            || !Uri.TryCreate(runUploadEndpoint, UriKind.Absolute, out var uploadUri)
        )
        {
            return null;
        }

        var absolutePath = uploadUri.AbsolutePath;
        if (!absolutePath.EndsWith("/runs/upload", StringComparison.OrdinalIgnoreCase))
            return null;

        var replayPath = absolutePath[..^"/runs/upload".Length] + "/battles/upload";
        var builder = new UriBuilder(uploadUri) { Path = replayPath, Query = string.Empty };
        return builder.Uri.ToString();
    }
}
