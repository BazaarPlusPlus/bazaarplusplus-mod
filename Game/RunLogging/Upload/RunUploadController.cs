#nullable enable
using System;
using System.Threading;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Online;
using BazaarPlusPlus.Game.Upload;
using UnityEngine;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunUploadController : MonoBehaviour
{
    private RunBundleUploadService? _uploadService;
    private CancellationTokenSource? _shutdown;
    private StartupUploadAttemptGate? _startupGate;
    private IDisposable? _runLifecycleSubscription;
    private IDisposable? _replayPersistenceDrainedSubscription;
    private readonly StartupUploadAttemptRunner _startupRunner = new(
        "RunUploadController",
        "Skipping startup run upload because a live run is active.",
        "Starting startup run upload attempt.",
        "Startup upload failed"
    );

    private void Awake()
    {
        try
        {
            var databasePath = BppRuntimeHost.Paths.RunLogDatabasePath;
            var replayRootPath = BppRuntimeHost.Paths.CombatReplayDirectoryPath;

            var startupDelaySeconds = Math.Max(5, V3UploadDefaults.StartupDelaySeconds);
            var retryIntervalSeconds = Math.Max(1, V3UploadDefaults.IntervalSeconds);
            var requestTimeoutSeconds = Math.Max(10, V3UploadDefaults.RequestTimeoutSeconds);
            if (
                string.IsNullOrWhiteSpace(databasePath)
                || string.IsNullOrWhiteSpace(replayRootPath)
            )
            {
                BppLog.Warn(
                    "RunUploadController",
                    "Run bundle upload is enabled but local replay or database paths are invalid."
                );
                return;
            }

            var routes = V3Routes.TryCreate(V3UploadDefaults.ApiBaseUrl);
            if (routes == null)
                return;

            var uploadStore = new RunBundleUploadStore(databasePath, replayRootPath);
            _uploadService = new RunBundleUploadService(
                uploadStore,
                routes,
                timeout: TimeSpan.FromSeconds(requestTimeoutSeconds)
            );
            _shutdown = new CancellationTokenSource();
            _startupGate = new StartupUploadAttemptGate(
                Time.unscaledTime + startupDelaySeconds,
                retryIntervalSeconds
            );
            _runLifecycleSubscription = BppRuntimeHost.EventBus.Subscribe<RunLifecycleChanged>(
                OnRunLifecycleChanged
            );
            _replayPersistenceDrainedSubscription =
                BppRuntimeHost.EventBus.Subscribe<CombatReplayPersistenceDrained>(
                    OnCombatReplayPersistenceDrained
                );
            BppLog.Info(
                "RunUploadController",
                $"Startup run-bundle upload armed. timeout={requestTimeoutSeconds}s, startup_delay={startupDelaySeconds}s, retry_interval={retryIntervalSeconds}s."
            );
        }
        catch (Exception ex)
        {
            BppLog.Error("RunUploadController", $"Failed to initialize upload service: {ex}");
        }
    }

    private void Update()
    {
        if (_uploadService == null || _shutdown == null || _startupGate == null)
            return;

        _startupRunner.Tick(
            _startupGate,
            Time.unscaledTime,
            BppRuntimeHost.RunContext.IsInGameRun,
            _uploadService.UploadPendingRunBundlesAsync,
            _shutdown.Token
        );
    }

    private void OnDestroy()
    {
        _replayPersistenceDrainedSubscription?.Dispose();
        _replayPersistenceDrainedSubscription = null;
        _runLifecycleSubscription?.Dispose();
        _runLifecycleSubscription = null;

        if (_shutdown != null)
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
            _shutdown = null;
        }

        _uploadService?.Dispose();
        _uploadService = null;
    }

    private void OnRunLifecycleChanged(RunLifecycleChanged change)
    {
        if (change.IsInGameRun)
            return;

        _startupGate?.ArmImmediateAttempt(Time.unscaledTime);
    }

    private void OnCombatReplayPersistenceDrained(CombatReplayPersistenceDrained _)
    {
        if (BppRuntimeHost.RunContext.IsInGameRun)
            return;

        _startupGate?.ArmImmediateAttempt(Time.unscaledTime);
    }
}
