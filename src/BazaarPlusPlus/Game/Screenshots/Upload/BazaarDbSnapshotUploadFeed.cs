#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.HistoryPanel.AccountLink;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbSnapshotUploadFeed : IUploadFeed
{
    private readonly BazaarDbAccountLinkStore _accountLinkStore = new();

    public UploadFeedKind Kind => UploadFeedKind.BazaarDbSnapshot;

    public IUploadFeedSession? Activate(
        IBppServices services,
        UploadFeedLogState logState,
        UploadPumpCadence cadence
    )
    {
        // Cadence is pump-owned; BazaarDb does not re-log arm timing from it.
        _ = logState;
        _ = cadence;
        var screenshotLogState = new ScreenshotUploadLogState();
        try
        {
            var databasePath = services.Paths.RunLogDatabasePath;
            var screenshotsDirectoryPath = services.Paths.ScreenshotsDirectoryPath;

            if (
                string.IsNullOrWhiteSpace(databasePath)
                || string.IsNullOrWhiteSpace(screenshotsDirectoryPath)
            )
            {
                screenshotLogState.ReportInitializationDegraded(
                    ScreenshotUploadLogValues.InvalidLocalPaths
                );
                return null;
            }

            var routes = ModApiRoutes.TryCreate(ModApiUploadDefaults.ApiBaseUrl);
            if (routes == null)
            {
                screenshotLogState.ReportInitializationDegraded(
                    ScreenshotUploadLogValues.RouteUnavailable
                );
                return null;
            }

            var requestTimeoutSeconds = Math.Max(10, ModApiUploadDefaults.RequestTimeoutSeconds);
            var store = new BazaarDbSnapshotUploadStore(databasePath, screenshotsDirectoryPath);
            var httpClient = BppHttpClientFactory.Create(
                productVersion: BppPluginVersion.Current,
                userAgentSuffix: "BazaarDbSnapshotUpload",
                timeout: TimeSpan.FromSeconds(requestTimeoutSeconds)
            );
            var uploadService = new BazaarDbSnapshotUploadService(
                store,
                routes,
                httpClient,
                BppClientCacheBridge.TryGetProfileAccountId,
                screenshotLogState
            );

            return new Session(
                isEnabled: () =>
                    EvaluateEnabled(
                        services.Config.BazaarDbUploadEnabled?.Value ?? false,
                        BppClientCacheBridge.TryGetProfileAccountId,
                        _accountLinkStore.IsLinked
                    ),
                runAttemptAsync: cancellationToken =>
                    RunAttemptAsync(
                        uploadService.UploadPendingInBackgroundAsync,
                        screenshotLogState,
                        cancellationToken
                    ),
                resources: httpClient
            );
        }
        catch (Exception ex)
        {
            screenshotLogState.ReportInitializationDegraded(
                ScreenshotUploadLogValues.InitializationException,
                ex
            );
            return null;
        }
    }

    /// <summary>
    /// Builds a session that only probes the three-condition enablement matrix. Used by tests so
    /// eligibility assertions drive <see cref="IUploadFeedSession.IsEnabled"/> rather than a
    /// static method signature pin.
    /// </summary>
    internal static IUploadFeedSession CreateEnabledProbe(
        bool uploadEnabled,
        Func<string?> playerAccountIdResolver,
        Func<string, bool> isAccountLinked
    )
    {
        return new Session(
            isEnabled: () =>
                EvaluateEnabled(uploadEnabled, playerAccountIdResolver, isAccountLinked),
            runAttemptAsync: _ => Task.FromResult(UploadAttemptResult.NoHealthSignal()),
            resources: null
        );
    }

    internal static bool EvaluateEnabled(
        bool uploadEnabled,
        Func<string?> playerAccountIdResolver,
        Func<string, bool> isAccountLinked
    )
    {
        if (!uploadEnabled)
            return false;

        var playerAccountId = playerAccountIdResolver()?.Trim();
        return !string.IsNullOrWhiteSpace(playerAccountId) && isAccountLinked(playerAccountId);
    }

    internal static async Task<UploadAttemptResult> RunAttemptAsync(
        Func<CancellationToken, Task> uploadAsync,
        ScreenshotUploadLogState logState,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await uploadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            logState.ReportHealthDegraded(
                ScreenshotUploadLogValues.ServiceException,
                roundTripMilliseconds: null
            );
        }
        return UploadAttemptResult.NoHealthSignal();
    }

    private sealed class Session : IUploadFeedSession
    {
        private readonly Func<bool> _isEnabled;
        private readonly Func<CancellationToken, Task<UploadAttemptResult>> _runAttemptAsync;
        private readonly IDisposable? _resources;
        private int _disposed;

        public Session(
            Func<bool> isEnabled,
            Func<CancellationToken, Task<UploadAttemptResult>> runAttemptAsync,
            IDisposable? resources
        )
        {
            _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
            _runAttemptAsync =
                runAttemptAsync ?? throw new ArgumentNullException(nameof(runAttemptAsync));
            _resources = resources;
        }

        public bool IsEnabled => _isEnabled();

        public Task<UploadAttemptResult> RunAttemptAsync(CancellationToken cancellationToken) =>
            _runAttemptAsync(cancellationToken);

        public IDisposable? SubscribeArmSignals(Action arm) => null;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _resources?.Dispose();
        }
    }
}
