#nullable enable
using System;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbSnapshotUploadFeed : IUploadFeed
{
    public const string BazaarDbSnapshotScope = "BazaarDbSnapshotUploadController";

    public UploadFeedDescriptor Descriptor { get; } =
        new(
            BazaarDbSnapshotScope,
            "Skipping BazaarDB screenshot upload because a live run is active.",
            "Starting BazaarDB screenshot upload attempt.",
            "BazaarDB screenshot upload failed"
        );

    public UploadFeedActivation? Activate(IBppServices services)
    {
        try
        {
            var databasePath = services.Paths.RunLogDatabasePath;
            var screenshotsDirectoryPath = services.Paths.ScreenshotsDirectoryPath;

            if (
                string.IsNullOrWhiteSpace(databasePath)
                || string.IsNullOrWhiteSpace(screenshotsDirectoryPath)
            )
            {
                BppLog.Warn(
                    BazaarDbSnapshotScope,
                    "BazaarDB screenshot upload is enabled but database or screenshots paths are invalid."
                );
                return null;
            }

            var routes = ModApiRoutes.TryCreate(ModApiUploadDefaults.ApiBaseUrl);
            if (routes == null)
                return null;

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
                BppClientCacheBridge.TryGetProfileAccountId
            );

            var startupDelaySeconds = Math.Max(5, ModApiUploadDefaults.StartupDelaySeconds);
            var retryIntervalSeconds = Math.Max(1, ModApiUploadDefaults.IntervalSeconds);
            BppLog.Info(
                BazaarDbSnapshotScope,
                $"BazaarDB screenshot uploader armed. enabled={IsEnabled(services)}, startup_delay={startupDelaySeconds}s, retry_interval={retryIntervalSeconds}s."
            );

            return new UploadFeedActivation
            {
                UploadInBackgroundAsync = uploadService.UploadPendingInBackgroundAsync,
                IsEnabled = () => IsEnabled(services),
                Disposable = httpClient,
            };
        }
        catch (Exception ex)
        {
            BppLog.Error(
                BazaarDbSnapshotScope,
                $"Failed to initialize BazaarDB screenshot upload service: {ex}"
            );
            return null;
        }
    }

    private static bool IsEnabled(IBppServices services) =>
        services.Config.BazaarDbUploadEnabled?.Value ?? false;
}
