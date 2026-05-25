#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbScreenshotUploadService
{
    private const int BatchSize = 3;
    private const string AnonymousPlayerAccountId = "anonymous-player";

    private readonly BazaarDbScreenshotUploadStore _store;
    private readonly ModApiRoutes _routes;
    private readonly HttpClient _httpClient;
    private readonly Func<string?> _playerAccountIdResolver;

    public BazaarDbScreenshotUploadService(
        BazaarDbScreenshotUploadStore store,
        ModApiRoutes routes,
        HttpClient httpClient,
        Func<string?> playerAccountIdResolver
    )
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _playerAccountIdResolver =
            playerAccountIdResolver ?? throw new ArgumentNullException(nameof(playerAccountIdResolver));
    }

    public async Task UploadPendingAsync(CancellationToken cancellationToken)
    {
        _store.EnsureBackfilled();

        var pending = _store.GetPendingScreenshotIds(BatchSize);
        if (pending.Count == 0)
        {
            BppLog.Info(
                "BazaarDbScreenshotUploadService",
                "No screenshots are waiting for BazaarDB upload."
            );
            return;
        }

        var playerAccountId =
            (_playerAccountIdResolver()?.Trim() is { Length: > 0 } resolved)
                ? resolved
                : AnonymousPlayerAccountId;

        var client = new BazaarDbScreenshotClient(_httpClient, _routes);
        foreach (var screenshotId in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attemptedAtUtc = DateTime.UtcNow;
            try
            {
                var snapshot = _store.TryBuildSnapshot(screenshotId, playerAccountId);
                if (snapshot == null)
                {
                    _store.MarkPermanentFailure(
                        screenshotId,
                        attemptedAtUtc,
                        "build_snapshot_failed"
                    );
                    continue;
                }

                var result = await client.UploadScreenshotAsync(snapshot.Payload, cancellationToken);
                if (result.Succeeded)
                {
                    _store.MarkUploaded(screenshotId, DateTime.UtcNow);
                    continue;
                }

                var error = result.Error ?? "bazaardb_upload_failed";
                if (result.Permanent)
                    _store.MarkPermanentFailure(screenshotId, attemptedAtUtc, error);
                else
                    _store.MarkTransientFailure(screenshotId, attemptedAtUtc, error);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _store.MarkTransientFailure(screenshotId, attemptedAtUtc, ex.Message);
            }
        }
    }
}
