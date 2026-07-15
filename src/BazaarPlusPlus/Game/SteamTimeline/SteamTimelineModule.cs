#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.GameInterop.Events;
using BazaarPlusPlus.GameInterop.SteamTimeline;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;

namespace BazaarPlusPlus.Game.SteamTimeline;

internal sealed class SteamTimelineModule : IBppFeature
{
    private readonly IBppServices _services;
    private readonly SteamTimelineGameProbe _gameProbe;
    private readonly SteamTimelineSessionCore _core = new();
    private ISteamTimelineAdapter? _adapter;
    private SteamTimelineLocale _locale = SteamTimelineLocale.English;
    private SteamTimelineRangeHandle _activeRangeHandle;
    private IDisposable? _runLifecycleSubscription;
    private IDisposable? _runInitializedSubscription;
    private IDisposable? _messageSubscription;
    private IDisposable? _combatSimSubscription;
    private IDisposable? _playbackStartedSubscription;
    private IDisposable? _playbackEndedSubscription;
    private bool _runtimeDisabled;
    private bool _heroTagAdded;

    internal SteamTimelineModule(IBppServices services)
        : this(services, new SteamTimelineGameProbe()) { }

    internal SteamTimelineModule(IBppServices services, SteamTimelineGameProbe gameProbe)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _gameProbe = gameProbe ?? throw new ArgumentNullException(nameof(gameProbe));
    }

    public void Start()
    {
        _runLifecycleSubscription = _services.EventBus.Subscribe<RunLifecycleChanged>(
            OnRunLifecycleChanged
        );
        _runInitializedSubscription = _services.EventBus.Subscribe<RunInitializedObserved>(
            OnRunInitializedObserved
        );
        _messageSubscription = _services.EventBus.Subscribe<NetMessageObserved>(
            OnNetMessageObserved
        );
        _combatSimSubscription = _services.EventBus.Subscribe<CombatSimObserved>(
            OnCombatSimObserved
        );
        _playbackStartedSubscription = _services.EventBus.Subscribe<LivePvpPlaybackStartedObserved>(
            _ => Execute(_core.StartBattlePlayback())
        );
        _playbackEndedSubscription = _services.EventBus.Subscribe<LivePvpPlaybackEndedObserved>(_ =>
            Execute(_core.EndBattlePlayback())
        );
        Events.PlayerAttributeChanged.AddListener(OnPlayerAttributeChanged, null);

        ReconcileEnabledState();
    }

    public void Stop()
    {
        Events.PlayerAttributeChanged.RemoveListener(OnPlayerAttributeChanged);
        _playbackEndedSubscription?.Dispose();
        _playbackStartedSubscription?.Dispose();
        _combatSimSubscription?.Dispose();
        _messageSubscription?.Dispose();
        _runInitializedSubscription?.Dispose();
        _runLifecycleSubscription?.Dispose();
        _playbackEndedSubscription = null;
        _playbackStartedSubscription = null;
        _combatSimSubscription = null;
        _messageSubscription = null;
        _runInitializedSubscription = null;
        _runLifecycleSubscription = null;

        if (!_runtimeDisabled)
            Execute(_core.EndRun(SteamTimelineRunExit.PluginStopped));
        _core.Reset();
        _activeRangeHandle = default;
        _adapter = null;
    }

    private void OnRunLifecycleChanged(RunLifecycleChanged change)
    {
        if (_runtimeDisabled)
            return;

        if (change.IsInGameRun)
        {
            if (!IsConfiguredEnabled())
                return;
            if (!TryEnsureAdapter())
                return;
            Execute(_core.StartRun());
            return;
        }

        Execute(
            _core.EndRun(
                change.LastRunExitKind == RunExitKind.Interrupted
                    ? SteamTimelineRunExit.Interrupted
                    : SteamTimelineRunExit.Completed
            )
        );
    }

    private void OnRunInitializedObserved(RunInitializedObserved observed)
    {
        if (!IsConfiguredEnabled() || _runtimeDisabled)
            return;

        Execute(_core.ObserveRunId(SteamTimelinePhaseId.FromRunId(observed.RunId)));
        TryAddHeroTag();
    }

    private void OnNetMessageObserved(NetMessageObserved observed)
    {
        if (!CanEmit() || observed.Message is not NetMessageGameSim message)
            return;

        var battle = _gameProbe.TryCreateBattleContext(message);
        if (battle != null)
            Execute(_core.PrepareBattle(battle));
    }

    private void OnCombatSimObserved(CombatSimObserved observed)
    {
        if (!CanEmit())
            return;

        _core.ObserveBattleResult(_gameProbe.ResolveBattleResult(observed.Message));
    }

    private void OnPlayerAttributeChanged(PlayerAttributeChangedEvent change)
    {
        if (
            !CanEmit()
            || _gameProbe.IsReplayPlayback
            || change.CombatantId != ECombatantId.Player
            || change.Update.AttributeType != EPlayerAttributeType.Level
        )
        {
            return;
        }

        var summary = _gameProbe.ReadRunSummary();
        Execute(
            _core.ObserveLevelIncrease(
                change.Update.PreviousValue,
                change.Update.CurrentValue,
                summary.Day,
                summary.Hero
            )
        );
    }

    internal void OnEnabledChanged(bool _) => ReconcileEnabledState();

    private void ReconcileEnabledState()
    {
        if (!IsConfiguredEnabled())
        {
            if (!_runtimeDisabled)
                Execute(_core.EndRun(SteamTimelineRunExit.Disabled));
            return;
        }
        if (_runtimeDisabled || !_services.RunContext.IsInGameRun || !TryEnsureAdapter())
            return;

        Execute(
            _core.ObserveRunId(
                SteamTimelinePhaseId.FromRunId(_services.RunContext.CurrentServerRunId)
            )
        );
        Execute(_core.StartRun());
    }

    private bool TryEnsureAdapter()
    {
        if (_runtimeDisabled)
            return false;
        if (_adapter?.IsOperational == true)
            return true;

        _adapter = new SteamworksTimelineAdapter(OnAdapterFailure);
        if (!_adapter.TryInitialize())
            return false;

        _locale = SteamTimelineTextFormatter.ResolveLocale(_adapter.SteamUiLanguage);
        return true;
    }

    private bool CanEmit() =>
        IsConfiguredEnabled()
        && !_runtimeDisabled
        && _adapter?.IsOperational == true
        && _core.IsPhaseActive;

    private bool IsConfiguredEnabled() => _services.Config.EnableSteamTimelineConfig?.Value ?? true;

    private void Execute(System.Collections.Generic.IReadOnlyList<SteamTimelineCommand> commands)
    {
        if (commands.Count == 0)
            return;
        if (!TryEnsureAdapter())
            return;

        foreach (var command in commands)
        {
            if (_runtimeDisabled || _adapter?.IsOperational != true)
                return;

            Execute(command, _adapter);
        }
    }

    private void Execute(SteamTimelineCommand command, ISteamTimelineAdapter adapter)
    {
        switch (command.Kind)
        {
            case SteamTimelineCommandKind.StartPhase:
                _heroTagAdded = false;
                if (adapter.TryStartGamePhase())
                {
                    LogLifecycle(SteamTimelineLogEventKind.Run, SteamTimelineLogOutcome.Started);
                    TryAddHeroTag();
                }
                break;
            case SteamTimelineCommandKind.SetPhaseId:
                if (command.PhaseId != null)
                    adapter.TrySetGamePhaseId(command.PhaseId);
                break;
            case SteamTimelineCommandKind.EndPhase:
                ApplyRunSummary(command.RunExit, adapter);
                if (adapter.TryEndGamePhase())
                {
                    LogLifecycle(
                        SteamTimelineLogEventKind.Run,
                        command.RunExit == SteamTimelineRunExit.Interrupted
                            ? SteamTimelineLogOutcome.Interrupted
                            : SteamTimelineLogOutcome.Completed
                    );
                }
                _heroTagAdded = false;
                break;
            case SteamTimelineCommandKind.StartBattleRange:
                StartBattleRange(command.Battle!, adapter);
                break;
            case SteamTimelineCommandKind.CompleteBattleRange:
                CompleteBattleRange(command.Battle!, command.BattleResult, adapter);
                break;
            case SteamTimelineCommandKind.InterruptBattleRange:
                InterruptBattleRange(command.Battle!, adapter);
                break;
            case SteamTimelineCommandKind.AddLevelMarker:
                AddLevelMarker(command, adapter);
                break;
        }
    }

    private void StartBattleRange(SteamTimelineBattleContext battle, ISteamTimelineAdapter adapter)
    {
        if (_activeRangeHandle.IsValid)
            return;

        TryAddHeroTag();
        var text = SteamTimelineTextFormatter.BattleStarted(battle, _locale);
        if (
            adapter.TryStartRangeEvent(
                text.Title,
                text.Description,
                text.Icon,
                text.Priority,
                text.ClipPriority,
                out var handle
            )
        )
        {
            _activeRangeHandle = handle;
            LogLifecycle(SteamTimelineLogEventKind.Battle, SteamTimelineLogOutcome.Started);
        }
    }

    private void CompleteBattleRange(
        SteamTimelineBattleContext battle,
        SteamTimelineBattleResult result,
        ISteamTimelineAdapter adapter
    )
    {
        if (!_activeRangeHandle.IsValid)
            return;

        var rangeText = SteamTimelineTextFormatter.BattleRangeCompleted(battle, result, _locale);
        var markerText = SteamTimelineTextFormatter.BattleCompleted(battle, result, _locale);
        if (
            !adapter.TryUpdateRangeEvent(
                _activeRangeHandle,
                rangeText.Title,
                rangeText.Description,
                rangeText.Icon,
                rangeText.Priority,
                rangeText.ClipPriority
            )
        )
            return;

        if (!adapter.TryEndRangeEvent(_activeRangeHandle))
            return;

        _activeRangeHandle = default;
        if (
            adapter.TryAddInstantaneousEvent(
                markerText.Title,
                markerText.Description,
                markerText.Icon,
                markerText.Priority,
                markerText.ClipPriority
            )
        )
        {
            LogLifecycle(SteamTimelineLogEventKind.Battle, SteamTimelineLogOutcome.Completed);
        }
    }

    private void InterruptBattleRange(
        SteamTimelineBattleContext battle,
        ISteamTimelineAdapter adapter
    )
    {
        if (!_activeRangeHandle.IsValid)
            return;

        var text = SteamTimelineTextFormatter.BattleInterrupted(battle, _locale);
        if (
            !adapter.TryUpdateRangeEvent(
                _activeRangeHandle,
                text.Title,
                text.Description,
                text.Icon,
                text.Priority,
                text.ClipPriority
            ) || !adapter.TryEndRangeEvent(_activeRangeHandle)
        )
        {
            return;
        }

        _activeRangeHandle = default;
        LogLifecycle(SteamTimelineLogEventKind.Battle, SteamTimelineLogOutcome.Interrupted);
    }

    private void AddLevelMarker(SteamTimelineCommand command, ISteamTimelineAdapter adapter)
    {
        var level = command.Level.GetValueOrDefault();
        TryAddHeroTag();
        var text = SteamTimelineTextFormatter.LevelReached(
            level,
            command.Day,
            command.Hero,
            _locale
        );
        if (
            adapter.TryAddInstantaneousEvent(
                text.Title,
                text.Description,
                text.Icon,
                text.Priority,
                text.ClipPriority
            )
        )
        {
            LogLifecycle(SteamTimelineLogEventKind.Level, SteamTimelineLogOutcome.Marked, level);
        }
    }

    private void TryAddHeroTag()
    {
        if (_heroTagAdded || !CanEmit() || _adapter == null)
            return;

        TryAddHeroTag(_adapter);
    }

    private void TryAddHeroTag(ISteamTimelineAdapter adapter)
    {
        if (_heroTagAdded || _runtimeDisabled || !adapter.IsOperational)
            return;

        var hero = SteamTimelineTextFormatter.CleanDynamicLabel(_gameProbe.ReadRunSummary().Hero);
        if (hero == null)
            return;

        _heroTagAdded = adapter.TryAddGamePhaseTag(
            hero,
            "steam_person",
            SteamTimelineTextFormatter.HeroGroup(_locale),
            SteamTimelineTextFormatter.MetadataPriority
        );
    }

    private void ApplyRunSummary(SteamTimelineRunExit exit, ISteamTimelineAdapter adapter)
    {
        TryAddHeroTag(adapter);
        var summary = _gameProbe.ReadRunSummary();
        if (summary.Day is > 0)
        {
            adapter.TrySetGamePhaseAttribute(
                SteamTimelineTextFormatter.FinalDayGroup(_locale),
                SteamTimelineTextFormatter.DayLabel(summary.Day)!,
                SteamTimelineTextFormatter.MetadataPriority
            );
        }
        if (summary.Wins is >= 0)
        {
            adapter.TrySetGamePhaseAttribute(
                SteamTimelineTextFormatter.WinsGroup(_locale),
                summary.Wins.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SteamTimelineTextFormatter.MetadataPriority
            );
        }

        var resultLabel = SteamTimelineTextFormatter.RunResultLabel(exit, _locale);
        if (resultLabel.Length > 0)
        {
            adapter.TryAddGamePhaseTag(
                resultLabel,
                exit == SteamTimelineRunExit.Completed ? "steam_completed" : "steam_caution",
                SteamTimelineTextFormatter.RunResultGroup(_locale),
                SteamTimelineTextFormatter.MetadataPriority
            );
        }
    }

    private void OnAdapterFailure(SteamTimelineAdapterFailure failure)
    {
        _runtimeDisabled = true;
        _activeRangeHandle = default;
        _core.Reset();
        BppLog.WarnEvent(
            SteamTimelineLogEvents.Degraded,
            failure.Exception,
            SteamTimelineLogEvents.Operation.Bind(failure.Operation),
            SteamTimelineLogEvents.ReasonCode.Bind(
                IsUnsupportedApiFailure(failure.Exception)
                    ? SteamTimelineLogReasonCode.UnsupportedApi
                    : SteamTimelineLogReasonCode.ApiException
            )
        );
    }

    private static bool IsUnsupportedApiFailure(Exception exception) =>
        exception
            is NotSupportedException
                or TypeLoadException
                or MissingMethodException
                or EntryPointNotFoundException;

    private static void LogLifecycle(
        SteamTimelineLogEventKind eventKind,
        SteamTimelineLogOutcome outcome,
        int? level = null
    ) =>
        BppLog.DebugEvent(
            SteamTimelineLogEvents.LifecycleChanged,
            () =>
                [
                    SteamTimelineLogEvents.EventKind.Bind(eventKind),
                    SteamTimelineLogEvents.Outcome.Bind(outcome),
                    SteamTimelineLogEvents.Level.Bind(level),
                ]
        );
}
