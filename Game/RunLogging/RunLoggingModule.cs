#nullable enable
using System;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.RunLogging.Models;

namespace BazaarPlusPlus.Game.RunLogging;

internal sealed class RunLoggingModule
{
    private readonly IBppEventBus _eventBus;
    private readonly RunLogSessionManager _sessionManager;
    private readonly RunLoggingControllerCore _core;
    private readonly Func<RunLogSessionState?> _ensureActiveRunFromGame;
    private IDisposable? _selectionSubscription;
    private IDisposable? _syncSubscription;
    private IDisposable? _pvpBattleSubscription;
    private bool _wasInRunLastTick;

    public RunLoggingModule(
        IBppEventBus eventBus,
        RunLogSessionManager sessionManager,
        RunLoggingControllerCore core,
        Func<RunLogSessionState?> ensureActiveRunFromGame
    )
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _core = core ?? throw new ArgumentNullException(nameof(core));
        _ensureActiveRunFromGame =
            ensureActiveRunFromGame ?? throw new ArgumentNullException(nameof(ensureActiveRunFromGame));
    }

    public void Start()
    {
        _selectionSubscription = _eventBus.Subscribe<SelectionObserved>(OnSelectionObserved);
        _syncSubscription = _eventBus.Subscribe<RunLoggingSyncRequested>(
            OnRunLoggingSyncRequested
        );
        _pvpBattleSubscription = _eventBus.Subscribe<PvpBattleRecorded>(OnPvpBattleRecorded);
    }

    public void Stop()
    {
        _pvpBattleSubscription?.Dispose();
        _pvpBattleSubscription = null;
        _syncSubscription?.Dispose();
        _syncSubscription = null;
        _selectionSubscription?.Dispose();
        _selectionSubscription = null;
    }

    private void OnRunLoggingSyncRequested(RunLoggingSyncRequested _)
    {
        var inRun = BppRuntimeHost.RunContext.IsInGameRun;
        var completionAttempted = false;
        var completionSucceeded = false;
        try
        {
            if (inRun)
            {
                var session = _ensureActiveRunFromGame();
                if (session != null)
                {
                    if (
                        RunLoggingGameDataReader.TryBuildRunLogRunProgressInput(
                            out var progressInput
                        )
                    )
                    {
                        _core.AcceptRunProgress(progressInput);
                    }

                    if (RunLoggingGameDataReader.TryBuildRunLogStateSnapshot(out var stateInput))
                    {
                        _core.AcceptStateSnapshot(stateInput);
                    }
                }
            }
            else if (_wasInRunLastTick && _sessionManager.HasActiveSession)
            {
                completionAttempted = true;
                _core.CompleteRun(
                    RunLoggingGameDataReader.BuildRunLogCompletion("run_state_exit")
                );
                completionSucceeded = true;
            }
        }
        catch (Exception ex)
        {
            BppLog.Error("RunLoggingModule", $"Sync failed: {ex}");
        }
        finally
        {
            if (inRun)
            {
                _wasInRunLastTick = true;
            }
            else if (!completionAttempted || completionSucceeded || !_sessionManager.HasActiveSession)
            {
                _wasInRunLastTick = false;
            }
        }
    }

    private void OnSelectionObserved(SelectionObserved _)
    {
        try
        {
            if (!BppRuntimeHost.RunContext.IsInGameRun)
                return;

            if (_ensureActiveRunFromGame() == null)
                return;

            if (
                RunLoggingGameDataReader.TryBuildRunLogSelectionSnapshot(out var selectionInput)
            )
            {
                _core.AcceptSelectionSnapshot(selectionInput);
            }
        }
        catch (Exception ex)
        {
            BppLog.Error("RunLoggingModule", $"Selection capture failed: {ex}");
        }
    }

    private void OnPvpBattleRecorded(PvpBattleRecorded recorded)
    {
        try
        {
            var manifest = recorded.Manifest;
            if (
                manifest == null
                || !string.Equals(manifest.CombatKind, "PVPCombat", StringComparison.Ordinal)
                || !BppRuntimeHost.RunContext.IsInGameRun
            )
            {
                return;
            }

            if (_ensureActiveRunFromGame() == null)
                return;

            _core.AcceptCombatReplay(
                new RunLogPvpBattleInput
                {
                    Day = manifest.Day,
                    Hour = manifest.Hour,
                    EncounterId = manifest.EncounterId,
                    CombatKind = manifest.CombatKind,
                    BattleId = manifest.BattleId,
                    OpponentName = manifest.Participants.OpponentName,
                }
            );
        }
        catch (Exception ex)
        {
            BppLog.Error("RunLoggingModule", $"PVP battle capture failed: {ex}");
        }
    }
}
