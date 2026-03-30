#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.RunLogging.Upload;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Upload;

internal sealed class BattleUploadController : MonoBehaviour
{
    private const float BacklogDrainDelaySeconds = 5f;

    private BattleUploadService? _uploadService;
    private CancellationTokenSource? _shutdown;
    private Task<BattleUploadCycleResult>? _uploadTask;
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

            var registrationEndpoint = RunUploadDefaults.RegistrationEndpoint;
            var runUploadEndpoint = RunUploadDefaults.UploadEndpoint;
            var uploadEndpoint = BattleUploadService.TryDeriveBattleUploadEndpoint(
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
                    "BattleUploadController",
                    "Battle upload is enabled but requires the shared run upload endpoint."
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
                BppLog.Warn(
                    "BattleUploadController",
                    "Battle upload is enabled but local auth/state or replay paths are missing."
                );
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
                    "BattleUploadController",
                    "Battle upload is enabled but the shared registration/upload endpoints are invalid."
                );
                return;
            }

            var startupDelaySeconds = Math.Max(
                5,
                RunUploadDefaults.StartupDelaySeconds
            );
            _intervalSeconds = Math.Max(
                15,
                RunUploadDefaults.IntervalSeconds
            );
            var batchSize = Math.Max(1, RunUploadDefaults.BatchSize);
            var requestTimeoutSeconds = Math.Max(
                10,
                RunUploadDefaults.RequestTimeoutSeconds
            );

            var uploadStore = new BattleUploadSqliteStore(databasePath, replayRootPath);
            var identityStore = new RunUploadIdentityStore(identityPath);
            var clientStateStore = new RunUploadClientStateStore(clientStatePath);
            var keyStore = new RunUploadKeyStore(privateKeyPath);
            _uploadService = new BattleUploadService(
                uploadStore,
                identityStore,
                clientStateStore,
                keyStore,
                registrationUri.ToString(),
                uploadUri.ToString(),
                batchSize,
                timeout: TimeSpan.FromSeconds(requestTimeoutSeconds)
            );
            _shutdown = new CancellationTokenSource();
            _nextAttemptAt = Time.unscaledTime + startupDelaySeconds;
            BppLog.Info(
                "BattleUploadController",
                $"Background battle upload armed. timeout={requestTimeoutSeconds}s, batch_size={batchSize}, startup_delay={startupDelaySeconds}s, interval={_intervalSeconds}s."
            );
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "BattleUploadController",
                $"Failed to initialize battle upload service: {ex}"
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
                    "BattleUploadController",
                    $"Background battle upload failed: {ex}"
                );
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
                    "BattleUploadController",
                    "Skipping background battle upload because a live run is active."
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
                    "BattleUploadController",
                    $"Waiting {waitSeconds:F1}s before the next battle upload attempt."
                );
                _waitingForScheduleLogged = true;
            }
            return;
        }

        _waitingForScheduleLogged = false;
        BppLog.Info("BattleUploadController", "Starting background battle upload attempt.");
        _uploadTask = _uploadService.UploadPendingBattlesAsync(_shutdown.Token);
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
