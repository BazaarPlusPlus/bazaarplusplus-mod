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

    private void Awake()
    {
        try
        {
            if (BppRuntimeHost.Config.EnableRunUploadConfig?.Value != true)
                return;

            var legacyUploadEndpoint = BppRuntimeHost.Config.RunUploadEndpointConfig?.Value?.Trim();
            var legacyRegistrationEndpoint =
                BppRuntimeHost.Config.RunUploadRegistrationEndpointConfig?.Value?.Trim();
            var globalUploadEndpoint =
                BppRuntimeHost.Config.RunUploadEndpointGlobalConfig?.Value?.Trim();
            var globalRegistrationEndpoint =
                BppRuntimeHost.Config.RunUploadRegistrationEndpointGlobalConfig?.Value?.Trim();
            var cnUploadEndpoint = BppRuntimeHost.Config.RunUploadEndpointCnConfig?.Value?.Trim();
            var cnRegistrationEndpoint =
                BppRuntimeHost.Config.RunUploadRegistrationEndpointCnConfig?.Value?.Trim();
            var databasePath = BppRuntimeHost.Paths.RunLogDatabasePath;
            var identityPath = BppRuntimeHost.Paths.RunUploadInstallIdentityPath;
            var clientStatePath = BppRuntimeHost.Paths.RunUploadClientStatePath;
            var privateKeyPath = BppRuntimeHost.Paths.RunUploadPrivateKeyPath;
            var routeStatePath = BppRuntimeHost.Paths.RunUploadRouteStatePath;

            globalUploadEndpoint = string.IsNullOrWhiteSpace(globalUploadEndpoint)
                ? legacyUploadEndpoint
                : globalUploadEndpoint;
            globalRegistrationEndpoint = string.IsNullOrWhiteSpace(globalRegistrationEndpoint)
                ? legacyRegistrationEndpoint
                : globalRegistrationEndpoint;

            if (
                string.IsNullOrWhiteSpace(databasePath)
                || string.IsNullOrWhiteSpace(identityPath)
                || string.IsNullOrWhiteSpace(clientStatePath)
                || string.IsNullOrWhiteSpace(privateKeyPath)
                || string.IsNullOrWhiteSpace(routeStatePath)
            )
            {
                BppLog.Warn(
                    "RunUploadController",
                    "Run upload is enabled but local auth/state paths are missing."
                );
                return;
            }

            var startupDelaySeconds = Math.Max(
                5,
                BppRuntimeHost.Config.RunUploadStartupDelaySecondsConfig?.Value ?? 20
            );
            _intervalSeconds = Math.Max(
                15,
                BppRuntimeHost.Config.RunUploadIntervalSecondsConfig?.Value ?? 180
            );
            var batchSize = Math.Max(1, BppRuntimeHost.Config.RunUploadBatchSizeConfig?.Value ?? 3);
            var failureThreshold = Math.Max(
                1,
                BppRuntimeHost.Config.RunUploadGlobalFailureThresholdConfig?.Value ?? 2
            );
            var preferredRouteCacheMinutes = Math.Max(
                5,
                BppRuntimeHost.Config.RunUploadPreferredRouteCacheMinutesConfig?.Value ?? 1440
            );
            var mode = RunUploadRouteSelector.ParseMode(
                BppRuntimeHost.Config.RunUploadModeConfig?.Value
            );

            var globalEndpoint = TryBuildEndpointSet(
                RunUploadRouteKind.Global,
                globalRegistrationEndpoint,
                globalUploadEndpoint
            );
            var cnEndpoint = TryBuildEndpointSet(
                RunUploadRouteKind.CN,
                cnRegistrationEndpoint,
                cnUploadEndpoint
            );
            if (
                mode != RunUploadMode.Off
                && globalEndpoint == null
                && cnEndpoint == null
            )
            {
                BppLog.Warn(
                    "RunUploadController",
                    "Run upload is enabled but neither Global nor CN endpoint pair is configured."
                );
                return;
            }

            var uploadStore = new RunUploadSqliteStore(databasePath);
            var identityStore = new RunUploadIdentityStore(identityPath);
            var clientStateStore = new RunUploadClientStateStore(clientStatePath);
            var keyStore = new RunUploadKeyStore(privateKeyPath);
            var routeStateStore = new RunUploadRouteStateStore(routeStatePath);
            var routeSelector = new RunUploadRouteSelector(
                mode,
                routeStateStore,
                failureThreshold,
                TimeSpan.FromMinutes(preferredRouteCacheMinutes)
            );
            _uploadService = new RunUploadService(
                uploadStore,
                identityStore,
                clientStateStore,
                keyStore,
                routeSelector,
                globalEndpoint,
                cnEndpoint,
                batchSize,
                timeout: TimeSpan.FromSeconds(10)
            );
            _shutdown = new CancellationTokenSource();
            _nextAttemptAt = Time.unscaledTime + startupDelaySeconds;
            BppLog.Info(
                "RunUploadController",
                $"Background run upload armed in {mode} mode."
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
            }
        }

        if (BppRuntimeHost.RunContext.IsInGameRun || Time.unscaledTime < _nextAttemptAt)
            return;

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
        RunUploadRouteKind routeKind,
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
            RouteKind = routeKind,
            RegistrationEndpoint = registrationUri.ToString(),
            UploadEndpoint = uploadUri.ToString(),
        };
    }

    private static bool IsSupportedScheme(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
    }
}
