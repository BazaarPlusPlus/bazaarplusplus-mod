#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Runs;
using BazaarPlusPlus.Core.Events;

namespace BazaarPlusPlus.Game.EncounterTracking;

internal sealed class EncounterTrackingModule
{
    private readonly IBppEventBus _eventBus;
    private readonly EncounterTrackingStateStore _stateStore = new();
    private IDisposable? _runLifecycleSubscription;

    public EncounterTrackingModule(IBppEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public IEncounterSelectionQuery Query => _stateStore;

    public void Start()
    {
        _runLifecycleSubscription = _eventBus.Subscribe<RunLifecycleChanged>(
            OnRunLifecycleChanged
        );
    }

    public void Stop()
    {
        _runLifecycleSubscription?.Dispose();
        _runLifecycleSubscription = null;
    }

    public void UpdateSelection(
        ERunState stateName,
        List<RunInfo.CardInfo> cardInfos,
        List<RunInfo.MonsterPreview>? monsterPreviews
    )
    {
        if (stateName == ERunState.Encounter)
        {
            _stateStore.UpdateMapEncounters(cardInfos, monsterPreviews);
        }
        else
        {
            _stateStore.UpdateEncounterChoices(cardInfos, monsterPreviews);
        }

        _eventBus.Publish(
            new SelectionObserved { StateName = stateName.ToString(), Snapshot = _stateStore.GetSnapshot() }
        );
    }

    public void Clear()
    {
        _stateStore.Clear();
    }

    private void OnRunLifecycleChanged(RunLifecycleChanged change)
    {
        if (!change.IsInGameRun)
            Clear();
    }
}
