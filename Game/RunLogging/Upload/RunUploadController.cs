#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunUploadController : MonoBehaviour
{
    private RunUploadService? _uploadService;
    private CancellationTokenSource? _shutdown;
    private Task<RunUploadCycleResult>? _uploadTask;
    private StartupUploadAttemptGate? _startupGate;
    private bool _waitingForRunExitLogged;

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
            _startupGate = new StartupUploadAttemptGate(Time.unscaledTime + startupDelaySeconds);
            BppLog.Info(
                "RunUploadController",
                $"Startup run upload armed. timeout={requestTimeoutSeconds}s, batch_size={batchSize}, startup_delay={startupDelaySeconds}s."
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

        if (_uploadTask != null)
        {
            if (!_uploadTask.IsCompleted)
                return;

            try
            {
                _uploadTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                BppLog.Error("RunUploadController", $"Startup upload failed: {ex}");
            }
            finally
            {
                _uploadTask = null;
            }

            return;
        }

        switch (_startupGate.Poll(Time.unscaledTime, BppRuntimeHost.RunContext.IsInGameRun))
        {
            case StartupUploadAttemptDecision.Wait:
                return;
            case StartupUploadAttemptDecision.SkipLiveRun:
                if (!_waitingForRunExitLogged)
                {
                    BppLog.Info(
                        "RunUploadController",
                        "Skipping startup run upload because a live run is active."
                    );
                    _waitingForRunExitLogged = true;
                }
                return;
            case StartupUploadAttemptDecision.Done:
                return;
            case StartupUploadAttemptDecision.Start:
                break;
        }

        _waitingForRunExitLogged = false;
        BppLog.Info("RunUploadController", "Starting startup run upload attempt.");
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
