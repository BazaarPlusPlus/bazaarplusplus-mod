#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.RunLogging.Upload;

namespace BazaarPlusPlus.Game.CombatReplay.Upload;

internal sealed class CombatReplayUploadService : IDisposable
{
    internal const string ReplayClientStateScope = "ReplayCloudflare";

    private readonly CombatReplayUploadSqliteStore _store;
    private readonly RunUploadIdentityStore _identityStore;
    private readonly RunUploadClientStateStore _clientStateStore;
    private readonly RunUploadKeyStore _keyStore;
    private readonly string _registrationEndpoint;
    private readonly string _uploadEndpoint;
    private readonly int _batchSize;
    private readonly HttpClient _httpClient;

    public CombatReplayUploadService(
        CombatReplayUploadSqliteStore store,
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
        _clientStateStore = clientStateStore ?? throw new ArgumentNullException(nameof(clientStateStore));
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        if (string.IsNullOrWhiteSpace(registrationEndpoint))
            throw new ArgumentException("Registration endpoint is required.", nameof(registrationEndpoint));
        if (string.IsNullOrWhiteSpace(uploadEndpoint))
            throw new ArgumentException("Upload endpoint is required.", nameof(uploadEndpoint));

        _registrationEndpoint = registrationEndpoint;
        _uploadEndpoint = uploadEndpoint;
        _batchSize = Math.Max(1, batchSize);
        _httpClient = new HttpClient
        {
            Timeout = timeout,
        };
    }

    public async Task<CombatReplayUploadCycleResult> UploadPendingReplaysAsync(
        CancellationToken cancellationToken
    )
    {
        var pendingBattleIds = _store.GetPendingBattleIds(_batchSize);
        if (pendingBattleIds.Count == 0)
            return new CombatReplayUploadCycleResult(uploadedCount: 0, hasMorePending: false);

        var installId = _identityStore.GetOrCreateInstallId();
        var uploadedCount = 0;
        string? clientId = null;

        var apiClient = new CombatReplayUploadApiClient(
            _httpClient,
            new CombatReplayUploadRequestSigner(_keyStore),
            _uploadEndpoint
        );

        foreach (var battleId in pendingBattleIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recoveredRegistration = false;
            while (true)
            {
                var attemptedAtUtc = DateTimeOffset.UtcNow;
                var snapshot = _store.TryBuildSnapshot(battleId, installId, clientId);
                if (snapshot == null)
                {
                    _store.MarkReplayUploadTerminalFailure(
                        battleId,
                        attemptedAtUtc,
                        "replay_snapshot_not_found"
                    );
                    break;
                }

                try
                {
                    if (string.IsNullOrWhiteSpace(clientId))
                    {
                        clientId = await EnsureClientRegistrationAsync(installId, cancellationToken);
                        if (string.IsNullOrWhiteSpace(clientId))
                        {
                            _store.MarkReplayUploadFailed(
                                battleId,
                                attemptedAtUtc,
                                "registration_unavailable"
                            );
                            break;
                        }

                        snapshot = _store.TryBuildSnapshot(battleId, installId, clientId);
                        if (snapshot == null)
                        {
                            _store.MarkReplayUploadTerminalFailure(
                                battleId,
                                attemptedAtUtc,
                                "replay_snapshot_not_found"
                            );
                            break;
                        }
                    }

                    var resolvedClientId = clientId!;
                    var uploadResult = await apiClient.UploadReplayAsync(
                        snapshot.Json,
                        resolvedClientId,
                        installId,
                        battleId,
                        snapshot.Payload.RunId,
                        cancellationToken
                    );
                    if (!uploadResult.Succeeded)
                    {
                        if (uploadResult.ShouldReRegister && !recoveredRegistration)
                        {
                            recoveredRegistration = true;
                            _clientStateStore.ClearScopedClientId(ReplayClientStateScope);
                            clientId = await EnsureClientRegistrationAsync(
                                installId,
                                cancellationToken
                            );
                            if (string.IsNullOrWhiteSpace(clientId))
                            {
                                _store.MarkReplayUploadFailed(
                                    battleId,
                                    attemptedAtUtc,
                                    "registration_unavailable"
                                );
                                break;
                            }

                            continue;
                        }

                        _store.MarkReplayUploadFailed(
                            battleId,
                            attemptedAtUtc,
                            uploadResult.Error ?? "upload_failed"
                        );
                        break;
                    }

                    _store.MarkReplayUploaded(
                        battleId,
                        snapshot.PayloadSha256,
                        uploadResult.ObjectKey,
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
                    _store.MarkReplayUploadFailed(
                        battleId,
                        attemptedAtUtc,
                        RunUploadErrorFormatter.Truncate(ex.Message)
                    );
                    break;
                }
            }
        }

        return new CombatReplayUploadCycleResult(
            uploadedCount,
            _store.HasMorePendingReplays()
        );
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
            ReplayClientStateScope,
            "replays",
            _registrationEndpoint
        );
        return registrationClient.EnsureClientRegistrationAsync(installId, cancellationToken);
    }

    internal static string? TryDeriveReplayUploadEndpoint(string? runUploadEndpoint)
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

        var replayPath = absolutePath[..^"/runs/upload".Length] + "/replays/upload";
        var builder = new UriBuilder(uploadUri)
        {
            Path = replayPath,
            Query = string.Empty,
        };
        return builder.Uri.ToString();
    }
}
