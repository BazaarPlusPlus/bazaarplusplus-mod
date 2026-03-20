#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Paths;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatStatusBar;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Core.Runtime;

internal sealed class BppRuntimeHost
{
    private static readonly IBppEventBus DetachedEventBus = new InMemoryBppEventBus();
    private readonly List<IDisposable> _subscriptions = new();
    private readonly GameObject _hostObject;
    private readonly ManualLogSource _logger;
    private readonly ConfigFile _configFile;
    private readonly InMemoryBppEventBus _eventBus = new();

    public BppRuntimeHost(GameObject hostObject, ManualLogSource logger, ConfigFile configFile)
    {
        _hostObject = hostObject ?? throw new ArgumentNullException(nameof(hostObject));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configFile = configFile ?? throw new ArgumentNullException(nameof(configFile));
    }

    public static BppRuntimeHost? Current { get; private set; }

    public static IBppEventBus EventBus => Current?._eventBus ?? DetachedEventBus;

    public void Install()
    {
        Current = this;
        ModState.Logger = _logger;
        BppLog.Info("RuntimeHost", "Installed runtime host");
    }

    public void Start()
    {
        _subscriptions.Add(EventBus.Subscribe<RunInitializedObserved>(OnRunInitializedObserved));
        _subscriptions.Add(EventBus.Subscribe<NetMessageObserved>(OnNetMessageObserved));
        _subscriptions.Add(EventBus.Subscribe<CombatSimObserved>(OnCombatSimObserved));
        _subscriptions.Add(EventBus.Subscribe<CombatFrameAdvanced>(OnCombatFrameAdvanced));
        BppLog.Info("RuntimeHost", "Started runtime host");
    }

    public void Stop()
    {
        for (var i = _subscriptions.Count - 1; i >= 0; i--)
        {
            _subscriptions[i].Dispose();
        }

        _subscriptions.Clear();
        if (ReferenceEquals(Current, this))
            Current = null;
    }

    private static void OnRunInitializedObserved(RunInitializedObserved observed)
    {
        ModState.CurrentServerRunId = observed.RunId;
        BppLog.Info("RunLogging", $"Captured server run id: {observed.RunId}");
    }

    private static void OnNetMessageObserved(NetMessageObserved observed)
    {
        CombatReplayRuntime.Instance?.ObserveMessage(observed.Message);
    }

    private static void OnCombatSimObserved(CombatSimObserved observed)
    {
        var message = observed.Message;
        if (ModState.LastMessageId == message.MessageId)
            return;

        ModState.LastMessageId = message.MessageId;
        CombatStatusBar.SetCombatFrameTotal(message.Data?.Frames?.Count ?? 0);
        var winner = message.Data?.Winner;
        ModState.LastVictoryCondition =
            winner == ECombatantId.Player
                ? EVictoryCondition.Win
                : EVictoryCondition.Lose;
    }

    private static void OnCombatFrameAdvanced(CombatFrameAdvanced _)
    {
        CombatStatusBar.AdvanceCombatFrame();
    }
}
