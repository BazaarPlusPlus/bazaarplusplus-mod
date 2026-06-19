#nullable enable
using System;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunBundleUploadFeed : IUploadFeed
{
    private const string LogScope = "RunUploadController";
    private readonly IPvpBattleCatalog _battleCatalog;

    public RunBundleUploadFeed(IPvpBattleCatalog battleCatalog)
    {
        _battleCatalog = battleCatalog ?? throw new ArgumentNullException(nameof(battleCatalog));
    }

    public UploadFeedDescriptor Descriptor { get; } =
        new(
            LogScope,
            "Skipping startup run upload because a live run is active.",
            "Starting startup run upload attempt.",
            "Startup upload failed"
        );

    public UploadFeedActivation? Activate(IBppServices services)
    {
        try
        {
            var databasePath = services.Paths.RunLogDatabasePath;
            var replayRootPath = services.Paths.CombatReplayDirectoryPath;

            var startupDelaySeconds = Math.Max(5, ModApiUploadDefaults.StartupDelaySeconds);
            var retryIntervalSeconds = Math.Max(1, ModApiUploadDefaults.IntervalSeconds);
            var requestTimeoutSeconds = Math.Max(10, ModApiUploadDefaults.RequestTimeoutSeconds);
            if (
                string.IsNullOrWhiteSpace(databasePath) || string.IsNullOrWhiteSpace(replayRootPath)
            )
            {
                BppLog.Warn(
                    LogScope,
                    "Run bundle upload is enabled but local replay or database paths are invalid."
                );
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
            BppLog.Info(
                LogScope,
                $"Startup run-bundle upload armed. timeout={requestTimeoutSeconds}s, startup_delay={startupDelaySeconds}s, retry_interval={retryIntervalSeconds}s."
            );

            return new UploadFeedActivation
            {
                UploadInBackgroundAsync = uploadService.UploadPendingRunBundlesInBackgroundAsync,
                Disposable = uploadService,
                ExtraArmHook = new UploadArmHook(
                    (hookServices, arm) =>
                        hookServices.EventBus.Subscribe<CombatReplayPersistenceDrained>(_ =>
                        {
                            if (hookServices.RunContext.IsInGameRun)
                                return;

                            arm();
                        })
                ),
            };
        }
        catch (Exception ex)
        {
            BppLog.Error(LogScope, $"Failed to initialize upload service: {ex}");
            return null;
        }
    }
}
