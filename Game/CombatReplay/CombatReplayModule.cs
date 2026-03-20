#nullable enable
using System;
using BazaarPlusPlus.Core.Events;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayModule
{
    private readonly IBppEventBus _eventBus;
    private IDisposable? _messageSubscription;

    public CombatReplayModule(IBppEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public void Start()
    {
        _messageSubscription = _eventBus.Subscribe<NetMessageObserved>(OnNetMessageObserved);
    }

    public void Stop()
    {
        _messageSubscription?.Dispose();
        _messageSubscription = null;
    }

    private static void OnNetMessageObserved(NetMessageObserved observed)
    {
        CombatReplayRuntime.Instance?.ObserveMessage(observed.Message);
    }
}
