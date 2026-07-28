#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunBundleUploadFeed : IUploadFeed
{
    private readonly IPvpBattleCatalog _battleCatalog;

    public RunBundleUploadFeed(IPvpBattleCatalog battleCatalog)
    {
        _battleCatalog = battleCatalog ?? throw new ArgumentNullException(nameof(battleCatalog));
    }

    public UploadFeedKind Kind => UploadFeedKind.RunBundle;

    public IUploadFeedSession? Activate(
        IBppServices services,
        UploadFeedLogState logState,
        UploadPumpCadence cadence
    )
    {
        try
        {
            var databasePath = services.Paths.RunLogDatabasePath;
            var replayRootPath = services.Paths.CombatReplayDirectoryPath;

            var requestTimeoutSeconds = Math.Max(10, ModApiUploadDefaults.RequestTimeoutSeconds);
            if (
                string.IsNullOrWhiteSpace(databasePath) || string.IsNullOrWhiteSpace(replayRootPath)
            )
            {
                logState.ReportDegraded(null, UploadLogReasonCode.InvalidLocalPaths, null);
                return null;
            }

            var routes = ModApiRoutes.TryCreate(ModApiUploadDefaults.ApiBaseUrl);
            if (routes == null)
                return null;

            var uploadStore = new RunBundleUploadStore(
                databasePath,
                replayRootPath,
                _battleCatalog
            );
            var uploadService = new RunBundleUploadService(
                uploadStore,
                routes,
                timeout: TimeSpan.FromSeconds(requestTimeoutSeconds)
            );
            BppLog.DebugEvent(
                UploadLogEvents.FeedArmed,
                () =>
                    [
                        UploadLogEvents.FeedArmedFeed.Bind(Kind),
                        UploadLogEvents.FeedArmedRequestTimeoutMs.Bind(
                            requestTimeoutSeconds * 1000L
                        ),
                        UploadLogEvents.FeedArmedStartupDelayMs.Bind(
                            cadence.StartupDelaySeconds * 1000L
                        ),
                        UploadLogEvents.FeedArmedRetryIntervalMs.Bind(
                            cadence.RetryIntervalSeconds * 1000L
                        ),
                    ]
            );

            return new Session(services, uploadService);
        }
        catch (Exception ex)
        {
            logState.ReportDegraded(null, UploadLogReasonCode.InitializationException, ex);
            return null;
        }
    }

    private sealed class Session : IUploadFeedSession
    {
        private readonly IBppServices _services;
        private readonly RunBundleUploadService _uploadService;
        private IDisposable? _armSubscription;
        private int _disposed;

        public Session(IBppServices services, RunBundleUploadService uploadService)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _uploadService =
                uploadService ?? throw new ArgumentNullException(nameof(uploadService));
        }

        public bool IsEnabled => true;

        public Task<UploadAttemptResult> RunAttemptAsync(CancellationToken cancellationToken) =>
            _uploadService.UploadPendingRunBundlesInBackgroundAsync(cancellationToken);

        public IDisposable? SubscribeArmSignals(Action arm)
        {
            if (arm == null)
                throw new ArgumentNullException(nameof(arm));

            // Pump holds the returned handle and disposes it first on shutdown. Keep a reference
            // so Dispose can idempotently fall back if the pump path is skipped.
            var subscription = _services.EventBus.Subscribe<CombatReplayPersistenceDrained>(_ =>
            {
                if (_services.RunContext.IsInGameRun)
                    return;

                arm();
            });
            _armSubscription = subscription;
            return subscription;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            // Idempotent fallback: pump normally disposed this already before cancel/drain.
            var armSubscription = Interlocked.Exchange(ref _armSubscription, null);
            armSubscription?.Dispose();
            _uploadService.Dispose();
        }
    }
}
