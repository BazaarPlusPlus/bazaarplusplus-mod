#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.RunLogging.Upload;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Upload;

internal sealed class CombatReplayUploadController : MonoBehaviour
{
    private const float BacklogDrainDelaySeconds = 5f;

    private CombatReplayUploadService? _uploadService;
    private CancellationTokenSource? _shutdown;
    private Task<CombatReplayUploadCycleResult>? _uploadTask;
    private float _nextAttemptAt;
    private float _intervalSeconds;

    private void Awake()
    {
        try
        {
            if (BppRuntimeHost.Config.EnableRunUploadConfig?.Value != true)
                return;

            var registrationEndpoint =
                BppRuntimeHost.Config.RunUploadRegistrationEndpointGlobalConfig?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(registrationEndpoint))
            {
                registrationEndpoint =
                    BppRuntimeHost.Config.RunUploadRegistrationEndpointConfig?.Value?.Trim();
            }

            var runUploadEndpoint =
                BppRuntimeHost.Config.RunUploadEndpointGlobalConfig?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(runUploadEndpoint))
            {
                runUploadEndpoint = BppRuntimeHost.Config.RunUploadEndpointConfig?.Value?.Trim();
            }

            var uploadEndpoint = CombatReplayUploadService.TryDeriveReplayUploadEndpoint(
                runUploadEndpoint
            );
            var databasePath = BppRuntimeHost.Paths.RunLogDatabasePath;
            var replayRootPath = BppRuntimeHost.Paths.CombatReplayDirectoryPath;
            var identityPath = BppRuntimeHost.Paths.RunUploadInstallIdentityPath;
            var clientStatePath = BppRuntimeHost.Paths.RunUploadClientStatePath;
            var privateKeyPath = BppRuntimeHost.Paths.RunUploadPrivateKeyPath;

            if (
                string.IsNullOrWhiteSpace(registrationEndpoint)
                || string.IsNullOrWhiteSpace(uploadEndpoint)
            )
            {
                BppLog.Warn(
                    "CombatReplayUploadController",
                    "Replay upload is enabled but requires the shared Cloudflare/global run upload endpoint."
                );
                return;
            }

            if (
                string.IsNullOrWhiteSpace(databasePath)
                || string.IsNullOrWhiteSpace(replayRootPath)
                || string.IsNullOrWhiteSpace(identityPath)
                || string.IsNullOrWhiteSpace(clientStatePath)
                || string.IsNullOrWhiteSpace(privateKeyPath)
            )
            {
                return;
            }

            if (
                !Uri.TryCreate(registrationEndpoint, UriKind.Absolute, out var registrationUri)
                || !Uri.TryCreate(uploadEndpoint, UriKind.Absolute, out var uploadUri)
                || !IsSupportedScheme(registrationUri)
                || !IsSupportedScheme(uploadUri)
            )
            {
                BppLog.Warn(
                    "CombatReplayUploadController",
                    "Replay upload is enabled but the shared Cloudflare registration/upload endpoints are invalid."
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

            var uploadStore = new CombatReplayUploadSqliteStore(databasePath, replayRootPath);
            var identityStore = new RunUploadIdentityStore(identityPath);
            var clientStateStore = new RunUploadClientStateStore(clientStatePath);
            var keyStore = new RunUploadKeyStore(privateKeyPath);
            _uploadService = new CombatReplayUploadService(
                uploadStore,
                identityStore,
                clientStateStore,
                keyStore,
                registrationUri.ToString(),
                uploadUri.ToString(),
                batchSize,
                timeout: TimeSpan.FromSeconds(10)
            );
            _shutdown = new CancellationTokenSource();
            _nextAttemptAt = Time.unscaledTime + startupDelaySeconds;
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "CombatReplayUploadController",
                $"Failed to initialize replay upload service: {ex}"
            );
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
                BppLog.Error(
                    "CombatReplayUploadController",
                    $"Background replay upload failed: {ex}"
                );
            }
            finally
            {
                _uploadTask = null;
                _nextAttemptAt = Time.unscaledTime + delaySeconds;
            }
        }

        if (BppRuntimeHost.RunContext.IsInGameRun || Time.unscaledTime < _nextAttemptAt)
            return;

        _uploadTask = _uploadService.UploadPendingReplaysAsync(_shutdown.Token);
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

    private static bool IsSupportedScheme(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
    }
}
