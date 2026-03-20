#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.RunLogging.Models;
using TheBazaar;

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
    private PendingSelectionContext? _pendingSelection;

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
            ensureActiveRunFromGame
            ?? throw new ArgumentNullException(nameof(ensureActiveRunFromGame));
    }

    public void Start()
    {
        _pendingSelection = PendingSelectionContext.From(
            _sessionManager.ActiveSession?.PendingSelection
        );
        _selectionSubscription = _eventBus.Subscribe<SelectionObserved>(OnSelectionObserved);
        _syncSubscription = _eventBus.Subscribe<RunLoggingSyncRequested>(OnRunLoggingSyncRequested);
        _pvpBattleSubscription = _eventBus.Subscribe<PvpBattleRecorded>(OnPvpBattleRecorded);
        Events.CardSelected.AddListener(OnCardSelected);
    }

    public void Stop()
    {
        Events.CardSelected.RemoveListener(OnCardSelected);
        _pendingSelection = null;
        _pvpBattleSubscription?.Dispose();
        _pvpBattleSubscription = null;
        _syncSubscription?.Dispose();
        _syncSubscription = null;
        _selectionSubscription?.Dispose();
        _selectionSubscription = null;
    }

    private void OnRunLoggingSyncRequested(RunLoggingSyncRequested request)
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
                    if (!RunLoggingGameDataReader.TryBuildRunLogSelectionSnapshot(out _))
                        TryCaptureInferredChoice(transitionedAway: true);

                    if (RunLoggingGameDataReader.TryBuildRunLogStateSnapshot(out var stateInput))
                        _core.AcceptStateSnapshot(stateInput);
                }
            }
            else if (_wasInRunLastTick && _sessionManager.HasActiveSession)
            {
                ResolvePendingSelectionOnBoundary("run_state_exit");
                completionAttempted = true;
                _core.CompleteRun(RunLoggingGameDataReader.BuildRunLogCompletion("run_state_exit"));
                completionSucceeded = true;
                _pendingSelection = null;
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
            else if (
                !completionAttempted
                || completionSucceeded
                || !_sessionManager.HasActiveSession
            )
            {
                _wasInRunLastTick = false;
            }
        }
    }

    private void OnSelectionObserved(SelectionObserved observed)
    {
        try
        {
            if (!BppRuntimeHost.RunContext.IsInGameRun)
                return;

            if (_ensureActiveRunFromGame() == null)
                return;

            if (!RunLoggingGameDataReader.TryBuildRunLogSelectionSnapshot(out var selectionInput))
                return;

            var selectionEvent = _core.AcceptSelectionSnapshot(selectionInput);
            if (selectionEvent == null)
                return;

            if (_pendingSelection != null && _pendingSelection.SelectionSeq != selectionEvent.Seq)
                ResolvePendingSelectionOnBoundary("superseded_by_new_selection");

            _pendingSelection = PendingSelectionContext.From(selectionEvent);
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

    private void OnCardSelected()
    {
        OnCardSelected(null);
    }

    private void OnCardSelected(object? payload)
    {
        try
        {
            if (!BppRuntimeHost.RunContext.IsInGameRun || _pendingSelection == null)
                return;

            var directChoice = BuildChoiceEventFromSelectedCard(payload as Card);
            if (directChoice != null && _core.AcceptChoiceMade(directChoice) != null)
            {
                _pendingSelection = null;
                return;
            }

            TryCaptureInferredChoice(transitionedAway: false);
        }
        catch (Exception ex)
        {
            BppLog.Error("RunLoggingModule", $"Choice capture failed: {ex}");
        }
    }

    private void TryCaptureInferredChoice(bool transitionedAway)
    {
        var pendingSelection = _pendingSelection;
        if (pendingSelection == null)
            return;

        if (transitionedAway)
        {
            if (pendingSelection.InferenceAttemptedAfterTransition)
                return;
        }
        else if (pendingSelection.InferenceAttemptedWithoutTransition)
        {
            return;
        }

        var inferredChoice = RunLoggingController.Instance?.InferenceService?.InferChoice(
            new RunLogChoiceInferenceInput
            {
                Day = pendingSelection.Day,
                Hour = pendingSelection.Hour,
                State = pendingSelection.State,
                EncounterId = pendingSelection.EncounterId,
                ParentEncounterId = pendingSelection.ParentEncounterId,
                SelectionSeq = pendingSelection.SelectionSeq,
                TransitionedAway = transitionedAway,
                Options = pendingSelection.Options,
                ResultingInstanceIds = RunLoggingGameDataReader.GetCurrentSelectionSetInstanceIds(),
            }
        );

        if (inferredChoice != null && _core.AcceptChoiceMade(inferredChoice) != null)
        {
            _pendingSelection = null;
            return;
        }

        if (transitionedAway)
            pendingSelection.InferenceAttemptedAfterTransition = true;
        else
            pendingSelection.InferenceAttemptedWithoutTransition = true;
    }

    private RunLogEvent? BuildChoiceEventFromSelectedCard(Card? selectedCard)
    {
        var pendingSelection = _pendingSelection;
        if (pendingSelection == null || selectedCard == null)
            return null;

        var selectedInstanceId = selectedCard.GetInstanceId().ToString();
        var selectedTemplateId = selectedCard.TemplateId.ToString();
        var matchedOption = pendingSelection.Options.FirstOrDefault(option =>
            string.Equals(option.InstanceId, selectedInstanceId, StringComparison.Ordinal)
            || string.Equals(option.TemplateId, selectedTemplateId, StringComparison.Ordinal)
        );
        if (matchedOption == null)
            return null;

        return new RunLogEvent
        {
            Kind = ResolveChoiceEventKind(pendingSelection.State),
            Day = pendingSelection.Day,
            Hour = pendingSelection.Hour,
            State = pendingSelection.State,
            EncounterId = pendingSelection.EncounterId,
            ParentEncounterId = pendingSelection.ParentEncounterId,
            SelectionSeq = pendingSelection.SelectionSeq,
            SelectedInstanceId = matchedOption.InstanceId ?? selectedInstanceId,
            SelectedTemplateId = matchedOption.TemplateId ?? selectedTemplateId,
            SelectedEncounterId = ResolveSelectedEncounterId(
                pendingSelection.State,
                matchedOption.TemplateId ?? selectedTemplateId
            ),
            SelectedName = matchedOption.Name ?? selectedCard.Name,
            SelectedTier = matchedOption.Tier,
            SelectedEnchant = matchedOption.Enchant,
        };
    }

    private void ResolvePendingSelectionOnBoundary(string reason)
    {
        var pendingSelection = _pendingSelection;
        if (pendingSelection == null)
            return;

        TryCaptureInferredChoice(transitionedAway: true);
        if (_pendingSelection == null)
            return;

        var abandonedEvent = new RunLogEvent
        {
            Kind = "selection_abandoned",
            Day = pendingSelection.Day,
            Hour = pendingSelection.Hour,
            State = pendingSelection.State,
            EncounterId = pendingSelection.EncounterId,
            ParentEncounterId = pendingSelection.ParentEncounterId,
            SelectionSeq = pendingSelection.SelectionSeq,
            AbandonedReason = reason,
        };

        if (_core.AcceptSelectionAbandoned(abandonedEvent) != null)
            _pendingSelection = null;
    }

    private sealed class PendingSelectionContext
    {
        public int? Day { get; set; }

        public int? Hour { get; set; }

        public string? State { get; set; }

        public string? EncounterId { get; set; }

        public string? ParentEncounterId { get; set; }

        public long SelectionSeq { get; set; }

        public bool InferenceAttemptedWithoutTransition { get; set; }

        public bool InferenceAttemptedAfterTransition { get; set; }

        public IList<RunLogOptionSnapshot> Options { get; set; } =
            Array.Empty<RunLogOptionSnapshot>();

        public static PendingSelectionContext From(RunLogEvent selectionEvent)
        {
            return new PendingSelectionContext
            {
                Day = selectionEvent.Day,
                Hour = selectionEvent.Hour,
                State = selectionEvent.State,
                EncounterId = selectionEvent.EncounterId,
                ParentEncounterId = selectionEvent.ParentEncounterId,
                SelectionSeq = selectionEvent.Seq,
                Options = selectionEvent.Options.ToList(),
            };
        }

        public static PendingSelectionContext? From(RunLogPendingSelectionState? pendingSelection)
        {
            if (pendingSelection == null)
                return null;

            return new PendingSelectionContext
            {
                Day = pendingSelection.Day,
                Hour = pendingSelection.Hour,
                State = pendingSelection.State,
                EncounterId = pendingSelection.EncounterId,
                ParentEncounterId = pendingSelection.ParentEncounterId,
                SelectionSeq = pendingSelection.SelectionSeq,
                Options = pendingSelection.Options.ToList(),
            };
        }
    }

    private static string ResolveChoiceEventKind(string? state)
    {
        return state switch
        {
            "Encounter" => "encounter_selected",
            "Choice" => "choice_selected",
            "Loot" => "loot_selected",
            "Pedestal" => "pedestal_selected",
            _ => "choice_made",
        };
    }

    private static string? ResolveSelectedEncounterId(string? state, string? selectedTemplateId)
    {
        if (string.Equals(state, "Encounter", StringComparison.Ordinal))
            return selectedTemplateId;

        return null;
    }
}
