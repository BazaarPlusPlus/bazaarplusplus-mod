#nullable enable
using System;
using System.Collections;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.Screenshots.Persistence;
using BazaarPlusPlus.Game.Settings;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.EndOfRun;
using UnityEngine;
using CombatStatusBarFeature = BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBar;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunScreenshotController : MonoBehaviour
{
    private const string ManualScreenshotBindingPath = "<Keyboard>/f9";
    private static readonly System.Reflection.MethodInfo ContinueClickMethod = AccessTools.Method(
        typeof(EndOfRunScreenController),
        "OnContinueClick"
    )!;
    private static EndOfRunScreenshotController? _current;
    private readonly EndOfRunScreenshotGate _gate = new();
    private bool _battleCaptureInFlight;
    private bool _isCombatActive;
    private ScreenshotService? _screenshotService;
    private RunScreenshotSqliteStore? _screenshotStore;
    private IDisposable? _runInitializedSubscription;
    private IDisposable? _battleScreenshotContextSubscription;
    private string? _bufferedRunId;
    private string? _bufferedHeroName;
    private PvpBattlePendingScreenshotContext? _pendingBattleScreenshot;

    private void Awake()
    {
        _current = this;
        RefreshBufferedRunContext();

        var screenshotsDirectoryPath = BppRuntimeHost.Paths.ScreenshotsDirectoryPath;
        var runLogDatabasePath = BppRuntimeHost.Paths.RunLogDatabasePath;
        if (string.IsNullOrWhiteSpace(screenshotsDirectoryPath))
        {
            BppLog.Warn(
                "EndOfRunScreenshot",
                "Screenshot controller initialized without a screenshots directory."
            );
            return;
        }

        _screenshotService = new ScreenshotService(screenshotsDirectoryPath);
        if (!string.IsNullOrWhiteSpace(runLogDatabasePath))
            _screenshotStore = new RunScreenshotSqliteStore(runLogDatabasePath);
    }

    private void OnEnable()
    {
        Events.RunStarted.AddListener(OnRunStarted, this);
        Events.RunEnded.AddListener(OnRunEnded, this);
        Events.RunInterrupted.AddListener(OnRunInterrupted, this);
        Events.CombatStarted.AddListener(OnCombatStarted, this);
        Events.CombatEnded.AddListener(OnCombatEnded, this);
        _runInitializedSubscription = BppRuntimeHost.EventBus.Subscribe<RunInitializedObserved>(
            OnRunInitializedObserved
        );
        _battleScreenshotContextSubscription =
            BppRuntimeHost.EventBus.Subscribe<PvpBattleScreenshotContextAvailable>(
                OnPvpBattleScreenshotContextAvailable
            );
    }

    private void OnDisable()
    {
        Events.RunStarted.RemoveListener(OnRunStarted);
        Events.RunEnded.RemoveListener(OnRunEnded);
        Events.RunInterrupted.RemoveListener(OnRunInterrupted);
        Events.CombatStarted.RemoveListener(OnCombatStarted);
        Events.CombatEnded.RemoveListener(OnCombatEnded);
        _runInitializedSubscription?.Dispose();
        _runInitializedSubscription = null;
        _battleScreenshotContextSubscription?.Dispose();
        _battleScreenshotContextSubscription = null;
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_current, this))
            _current = null;
    }

    private void Update()
    {
        if (_screenshotService == null)
            return;

        if (!BppHotkeyService.WasPressedThisFrame(ManualScreenshotBindingPath))
            return;

        StartManualScreenshotCapture(RunScreenshotCaptureSource.ManualF9);
    }

    private void OnRunStarted()
    {
        _gate.ResetForNewRun();
        ResetBufferedRunContext();
        RefreshBufferedRunContext();
        ClearPendingBattleScreenshot();
    }

    private void OnRunEnded()
    {
        ClearPendingBattleScreenshot();
    }

    private void OnRunInterrupted()
    {
        ClearPendingBattleScreenshot();
    }

    private void OnCombatStarted()
    {
        _isCombatActive = true;
        TryStartBattleScreenshotCapture();
    }

    private void OnCombatEnded()
    {
        _isCombatActive = false;
    }

    private void OnRunInitializedObserved(RunInitializedObserved observed)
    {
        if (!string.IsNullOrWhiteSpace(observed.RunId))
            _bufferedRunId = observed.RunId;

        RefreshBufferedRunContext();
    }

    private void OnPvpBattleScreenshotContextAvailable(
        PvpBattleScreenshotContextAvailable available
    )
    {
        if (string.IsNullOrWhiteSpace(available.BattleId))
            return;

        _pendingBattleScreenshot = new PvpBattlePendingScreenshotContext
        {
            BattleId = available.BattleId,
            RunId = string.IsNullOrWhiteSpace(available.RunId) ? null : available.RunId,
        };
        TryStartBattleScreenshotCapture();
    }

    public static bool TryConsumeContinuePassthrough()
    {
        return _current?.ConsumeContinuePassthrough() == true;
    }

    public static bool ShouldSuppressContinueWhileCaptureInFlight()
    {
        return _current?.GetShouldSuppressContinueWhileCaptureInFlight() == true;
    }

    public static void RequestManualScreenshot(
        RunScreenshotCaptureSource source = RunScreenshotCaptureSource.ManualF9
    )
    {
        _current?.StartManualScreenshotCapture(source);
    }

    private bool ConsumeContinuePassthrough()
    {
        return _gate.ConsumeContinuePassthrough();
    }

    private void StartManualScreenshotCapture(RunScreenshotCaptureSource source)
    {
        if (_screenshotService == null)
            return;

        StartCoroutine(
            CaptureManualScreenshot(
                new ScreenshotCaptureRequest
                {
                    RunId = ResolveLiveRunId(),
                    HeroName = ResolveLiveHeroName(),
                    CaptureSource = source,
                }
            )
        );
    }

    private bool GetShouldSuppressContinueWhileCaptureInFlight()
    {
        return _gate.IsAttemptInFlight();
    }

    public static bool TryCaptureFirstContinue(
        EndOfRunScreenController controller,
        bool isInteractionBlocked
    )
    {
        return _current?.CaptureFirstContinue(controller, isInteractionBlocked) == true;
    }

    private bool CaptureFirstContinue(
        EndOfRunScreenController controller,
        bool isInteractionBlocked
    )
    {
        if (_screenshotService == null || !_gate.ShouldCaptureOnContinue(isInteractionBlocked))
            return false;

        StartCoroutine(CaptureAndContinue(controller));
        return true;
    }

    private IEnumerator CaptureAndContinue(EndOfRunScreenController controller)
    {
        ScreenshotCaptureResult? capture = null;
        using (BeginUiSuppression())
        {
            yield return new WaitForEndOfFrame();
            capture = _screenshotService?.CaptureCurrentFrame(
                new ScreenshotCaptureRequest
                {
                    RunId = ResolveRunId(),
                    HeroName = ResolveHeroName(),
                    CaptureSource = RunScreenshotCaptureSource.EndOfRunAuto,
                }
            );
        }
        var screenshotQueued = capture != null;
        if (screenshotQueued)
        {
            PersistCapture(capture, isPrimary: true);
            _gate.MarkAttemptCompleted();
            BppSettingsDockController.NotifyScreenshotCaptured();
        }
        else
        {
            _gate.MarkAttemptAborted();
            BppLog.Warn(
                "EndOfRunScreenshot",
                "Screenshot attempt aborted before it could be queued."
            );
        }

        yield return null;

        _gate.AllowNextContinuePassthrough();
        ContinueClickMethod.Invoke(controller, []);
    }

    private bool TryStartBattleScreenshotCapture()
    {
        if (_screenshotService == null)
            return false;
        var pendingBattleScreenshot = _pendingBattleScreenshot;
        if (!IsBattleStartScreenshotEnabled())
            return false;
        if (!_isCombatActive || _battleCaptureInFlight)
            return false;
        if (pendingBattleScreenshot == null || pendingBattleScreenshot.Captured)
            return false;

        _battleCaptureInFlight = true;
        StartCoroutine(CaptureBattleStartScreenshot(pendingBattleScreenshot!));
        return true;
    }

    private IEnumerator CaptureBattleStartScreenshot(PvpBattlePendingScreenshotContext context)
    {
        ScreenshotCaptureResult? capture = null;
        using (BeginUiSuppression())
        {
            capture = _screenshotService?.CaptureCurrentFrame(
                new ScreenshotCaptureRequest
                {
                    RunId = !string.IsNullOrWhiteSpace(context.RunId) ? context.RunId : ResolveLiveRunId(),
                    HeroName = ResolveLiveHeroName(),
                    BattleId = context.BattleId,
                    CaptureSource = RunScreenshotCaptureSource.PvpBattleStart,
                }
            );
        }
        if (capture != null)
        {
            PersistCapture(capture);
            context.Captured = true;
            BppSettingsDockController.NotifyScreenshotCaptured();
        }
        else
        {
            BppLog.Warn(
                "BattleScreenshot",
                $"Failed to queue combat-start screenshot for battle {context.BattleId}."
            );
        }

        _battleCaptureInFlight = false;
        yield break;
    }

    private void PersistCapture(ScreenshotCaptureResult? capture, bool isPrimary = false)
    {
        if (capture == null || _screenshotStore == null)
            return;

        try
        {
            _screenshotStore.Save(RunScreenshotMetadataReader.CreateRecord(capture, isPrimary));
        }
        catch (Exception ex)
        {
            BppLog.Error("ScreenshotService", "Failed to persist screenshot metadata.", ex);
        }
    }

    private void ClearPendingBattleScreenshot()
    {
        _pendingBattleScreenshot = null;
        _battleCaptureInFlight = false;
        _isCombatActive = false;
    }

    private string? ResolveRunId()
    {
        RefreshBufferedRunContext();
        return _bufferedRunId;
    }

    private static string? ResolveLiveRunId()
    {
        return BppRuntimeHost.RunContext.CurrentServerRunId;
    }

    private string? ResolveHeroName()
    {
        RefreshBufferedRunContext();
        return _bufferedHeroName;
    }

    private static string? ResolveLiveHeroName()
    {
        return Data.Run?.Player?.Hero.ToString();
    }

    private void ResetBufferedRunContext()
    {
        _bufferedRunId = null;
        _bufferedHeroName = null;
    }

    private void RefreshBufferedRunContext()
    {
        var liveRunId = BppRuntimeHost.RunContext.CurrentServerRunId;
        if (!string.IsNullOrWhiteSpace(liveRunId))
            _bufferedRunId = liveRunId;

        var liveHeroName = Data.Run?.Player?.Hero.ToString();
        if (!string.IsNullOrWhiteSpace(liveHeroName))
            _bufferedHeroName = liveHeroName;
    }

    private static bool IsBattleStartScreenshotEnabled()
    {
        return false;
    }

    private IEnumerator CaptureManualScreenshot(ScreenshotCaptureRequest request)
    {
        ScreenshotCaptureResult? capture = null;
        using (BeginUiSuppression())
        {
            yield return new WaitForEndOfFrame();
            capture = _screenshotService?.CaptureCurrentFrame(request);
        }

        PersistCapture(capture);
        if (capture != null)
            BppSettingsDockController.NotifyScreenshotCaptured();
    }

    private static IDisposable? BeginUiSuppression()
    {
        return ScreenshotUiSuppressionScope.Begin(
            BppSettingsDockController.BeginScreenshotSuppression,
            CombatStatusBarFeature.BeginScreenshotSuppression
        );
    }
}
