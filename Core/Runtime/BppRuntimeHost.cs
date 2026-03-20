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
using BazaarPlusPlus.Game.RunLifecycle;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Core.Runtime;

internal sealed class BppRuntimeHost
{
    private static readonly IBppEventBus DetachedEventBus = new InMemoryBppEventBus();
    private static readonly BppConfig DetachedConfig = new();
    private static readonly BppPathService DetachedPaths = new();
    private static readonly RunContextStore DetachedRunContext = new();
    private static readonly GameStateProbe DetachedGameStateProbe = new();
    private readonly ManualLogSource _logger;
    private readonly InMemoryBppEventBus _eventBus = new();
    private readonly BppConfig _config = new();
    private readonly BppPathService _paths = new();
    private readonly RunContextStore _runContext = new();
    private readonly GameStateProbe _gameStateProbe = new();
    private readonly RunLifecycleModule _runLifecycle;
    private readonly CombatReplayModule _combatReplayModule;
    private readonly CombatStatusBarModule _combatStatusBarModule;

    public BppRuntimeHost(GameObject hostObject, ManualLogSource logger, ConfigFile configFile)
    {
        if (hostObject == null)
            throw new ArgumentNullException(nameof(hostObject));
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));
        if (configFile == null)
            throw new ArgumentNullException(nameof(configFile));

        _logger = logger;
        _config.Initialize(configFile);
        _paths.Initialize();
        _runContext.Reset();
        _runLifecycle = new RunLifecycleModule(_eventBus, _gameStateProbe, _runContext);
        _combatReplayModule = new CombatReplayModule(_eventBus);
        _combatStatusBarModule = new CombatStatusBarModule(_eventBus, _runContext);
    }

    public static BppRuntimeHost? Current { get; private set; }

    public static IBppEventBus EventBus => Current?._eventBus ?? DetachedEventBus;

    public static ManualLogSource? Logger => Current?._logger;

    public static IBppConfig Config => Current?._config ?? DetachedConfig;

    public static IPathService Paths => Current?._paths ?? DetachedPaths;

    public static IRunContext RunContext => Current?._runContext ?? DetachedRunContext;

    public static IGameStateProbe GameStateProbe => Current?._gameStateProbe ?? DetachedGameStateProbe;

    public static RunLifecycleModule RunLifecycle =>
        Current?._runLifecycle
        ?? throw new InvalidOperationException("Runtime host is not installed.");

    public void Install()
    {
        Current = this;
        BppLog.Info("RuntimeHost", "Installed runtime host");
    }

    public void Start()
    {
        _runLifecycle.Start();
        _combatReplayModule.Start();
        _combatStatusBarModule.Start();
        BppLog.Info("RuntimeHost", "Started runtime host");
    }

    public void Stop()
    {
        _combatStatusBarModule.Stop();
        _combatReplayModule.Stop();
        _runLifecycle.Stop();
        if (ReferenceEquals(Current, this))
            Current = null;
    }
}
