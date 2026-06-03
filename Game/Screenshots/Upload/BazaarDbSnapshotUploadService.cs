#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbSnapshotUploadService
{
    private const int BatchSize = 3;

    private readonly BazaarDbSnapshotUploadStore _store;
    private readonly ModApiRoutes _routes;
    private readonly HttpClient _httpClient;
    private readonly Func<string?> _playerAccountIdResolver;

    public BazaarDbSnapshotUploadService(
        BazaarDbSnapshotUploadStore store,
        ModApiRoutes routes,
        HttpClient httpClient,
        Func<string?> playerAccountIdResolver
    )
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _playerAccountIdResolver =
            playerAccountIdResolver
            ?? throw new ArgumentNullException(nameof(playerAccountIdResolver));
    }

    public async Task UploadPendingAsync(CancellationToken cancellationToken)
    {
        _store.EnsureBackfilled();

        var pending = _store.GetPendingSnapshotIds(BatchSize);
        if (pending.Count == 0)
        {
            BppLog.Info(
                "BazaarDbSnapshotUploadService",
                "No screenshots are waiting for BazaarDB upload."
            );
            return;
        }

        if (_playerAccountIdResolver()?.Trim() is not { Length: > 0 } playerAccountId)
        {
            BppLog.Info(
                "BazaarDbSnapshotUploadService",
                $"Skipping {pending.Count} pending screenshot(s): player account id not yet available."
            );
            return;
        }

        var client = new BazaarDbSnapshotClient(_httpClient, _routes);
        ModApiHealthProbeResult? healthProbe = null;
        foreach (var snapshotId in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attemptedAtUtc = DateTime.UtcNow;
            try
            {
                var snapshot = _store.TryBuildSnapshot(snapshotId, playerAccountId);
                if (snapshot == null)
                {
                    _store.MarkPermanentFailure(
                        snapshotId,
                        attemptedAtUtc,
                        "build_snapshot_failed"
                    );
                    continue;
                }

                if (!healthProbe.HasValue)
                {
                    healthProbe = await new ModApiHealthClient(_httpClient, _routes)
                        .ProbeAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (!healthProbe.Value.Succeeded)
                    {
                        BppLog.Warn(
                            "BazaarDbSnapshotUploadService",
                            $"Bazaar++ service health probe failed error={healthProbe.Value.Error ?? "unknown"} rtt_ms={healthProbe.Value.RoundTripMilliseconds} probed_at_utc={healthProbe.Value.ProbedAtUtc:O}; retrying later."
                        );
                        return;
                    }

                    var serverTimeUtc = healthProbe.Value.ServerTimeUtc?.ToString("O") ?? "unknown";
                    BppLog.Debug(
                        "BazaarDbSnapshotUploadService",
                        $"Bazaar++ service health ok rtt_ms={healthProbe.Value.RoundTripMilliseconds} server_time_utc={serverTimeUtc} probed_at_utc={healthProbe.Value.ProbedAtUtc:O}."
                    );
                }

                var result = await client.UploadSnapshotAsync(snapshot.Payload, cancellationToken);
                if (result.Succeeded)
                {
                    _store.MarkUploaded(snapshotId, DateTime.UtcNow);
                    continue;
                }

                var error = result.Error ?? "bazaardb_upload_failed";
                if (result.Permanent)
                    _store.MarkPermanentFailure(snapshotId, attemptedAtUtc, error);
                else
                    _store.MarkTransientFailure(snapshotId, attemptedAtUtc, error);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _store.MarkTransientFailure(snapshotId, attemptedAtUtc, ex.Message);
            }
        }
    }
}
