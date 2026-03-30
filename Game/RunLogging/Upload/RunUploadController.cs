#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunUploadController : MonoBehaviour
{
    private const float BacklogDrainDelaySeconds = 5f;

    private RunUploadService? _uploadService;
    private CancellationTokenSource? _shutdown;
    private Task<RunUploadCycleResult>? _uploadTask;
    private float _nextAttemptAt;
    private float _intervalSeconds;
    private bool _waitingForRunExitLogged;
    private bool _waitingForScheduleLogged;

    private void Awake()
    {
        try
        {
            if (BppRuntimeHost.Config.EnableRunUploadConfig?.Value != true)
                return;

            var uploadEndpoint = RunUploadDefaults.UploadEndpoint;
            var registrationEndpoint = RunUploadDefaults.RegistrationEndpoint;
            var databasePath = BppRuntimeHost.Paths.RunLogDatabasePath;
            var identityPath = BppRuntimeHost.Paths.RunUploadInstallIdentityPath;
            var clientStatePath = BppRuntimeHost.Paths.RunUploadClientStatePath;
            var privateKeyPath = BppRuntimeHost.Paths.RunUploadPrivateKeyPath;

            if (
                string.IsNullOrWhiteSpace(databasePath)
                || string.IsNullOrWhiteSpace(identityPath)
                || string.IsNullOrWhiteSpace(clientStatePath)
                || string.IsNullOrWhiteSpace(privateKeyPath)
            )
            {
                BppLog.Warn(
                    "RunUploadController",
                    "Run upload is enabled but local auth/state paths are missing."
                );
                return;
            }

            var startupDelaySeconds = Math.Max(5, RunUploadDefaults.StartupDelaySeconds);
            _intervalSeconds = Math.Max(15, RunUploadDefaults.IntervalSeconds);
            var batchSize = Math.Max(1, RunUploadDefaults.BatchSize);
            var requestTimeoutSeconds = Math.Max(10, RunUploadDefaults.RequestTimeoutSeconds);
            var endpoint = TryBuildEndpointSet(registrationEndpoint, uploadEndpoint);
            if (endpoint == null)
            {
                BppLog.Warn(
                    "RunUploadController",
                    "Run upload is enabled but the registration/upload endpoint pair is not configured."
                );
                return;
            }

            var uploadStore = new RunUploadSqliteStore(databasePath);
            var identityStore = new RunUploadIdentityStore(identityPath);
            var clientStateStore = new RunUploadClientStateStore(clientStatePath);
            var keyStore = new RunUploadKeyStore(privateKeyPath);
            _uploadService = new RunUploadService(
                uploadStore,
                identityStore,
                clientStateStore,
                keyStore,
                endpoint,
                batchSize,
                timeout: TimeSpan.FromSeconds(requestTimeoutSeconds)
            );
            _shutdown = new CancellationTokenSource();
            _nextAttemptAt = Time.unscaledTime + startupDelaySeconds;
            BppLog.Info(
                "RunUploadController",
                $"Background run upload armed. timeout={requestTimeoutSeconds}s, batch_size={batchSize}, startup_delay={startupDelaySeconds}s, interval={_intervalSeconds}s."
            );
        }
        catch (Exception ex)
        {
            BppLog.Error("RunUploadController", $"Failed to initialize upload service: {ex}");
        }
    }

    private void Update()
    {
        if (_uploadService == null || _shutdown == null)
            return;

        if (_uploadTask != null)
        {
            if (!_uploadTask.IsCompleted)
                return;

            var delaySeconds = _intervalSeconds;
            try
            {
                var result = _uploadTask.GetAwaiter().GetResult();
                if (result.HasMorePending)
                    delaySeconds = BacklogDrainDelaySeconds;
            }
            catch (OperationCanceledException)
            {
                delaySeconds = _intervalSeconds;
            }
            catch (Exception ex)
            {
                BppLog.Error("RunUploadController", $"Background upload failed: {ex}");
            }
            finally
            {
                _uploadTask = null;
                _nextAttemptAt = Time.unscaledTime + delaySeconds;
                _waitingForScheduleLogged = false;
            }
        }

        if (BppRuntimeHost.RunContext.IsInGameRun)
        {
            _waitingForScheduleLogged = false;
            if (!_waitingForRunExitLogged)
            {
                BppLog.Info(
                    "RunUploadController",
                    "Skipping background run upload because a live run is active."
                );
                _waitingForRunExitLogged = true;
            }
            return;
        }

        _waitingForRunExitLogged = false;

        if (Time.unscaledTime < _nextAttemptAt)
        {
            if (!_waitingForScheduleLogged)
            {
                var waitSeconds = Math.Max(0f, _nextAttemptAt - Time.unscaledTime);
                BppLog.Info(
                    "RunUploadController",
                    $"Waiting {waitSeconds:F1}s before the next run upload attempt."
                );
                _waitingForScheduleLogged = true;
            }
            return;
        }

        _waitingForScheduleLogged = false;
        BppLog.Info("RunUploadController", "Starting background run upload attempt.");
        _uploadTask = _uploadService.UploadPendingRunsAsync(_shutdown.Token);
    }

    private void OnDestroy()
    {
        if (_shutdown != null)
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
            _shutdown = null;
        }

        _uploadService?.Dispose();
        _uploadService = null;
    }

    private static RunUploadEndpointSet? TryBuildEndpointSet(
        string? registrationEndpoint,
        string? uploadEndpoint
    )
    {
        if (
            string.IsNullOrWhiteSpace(registrationEndpoint)
            || string.IsNullOrWhiteSpace(uploadEndpoint)
        )
        {
            return null;
        }

        if (
            !Uri.TryCreate(registrationEndpoint, UriKind.Absolute, out var registrationUri)
            || !Uri.TryCreate(uploadEndpoint, UriKind.Absolute, out var uploadUri)
            || !IsSupportedScheme(registrationUri)
            || !IsSupportedScheme(uploadUri)
        )
        {
            return null;
        }

        return new RunUploadEndpointSet
        {
            RegistrationEndpoint = registrationUri.ToString(),
            UploadEndpoint = uploadUri.ToString(),
        };
    }

    private static bool IsSupportedScheme(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
    }
}
