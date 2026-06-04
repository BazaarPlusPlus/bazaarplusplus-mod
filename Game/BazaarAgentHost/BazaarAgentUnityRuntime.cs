#nullable enable
using System;
using BazaarPlusPlus.BazaarAgent;
using BazaarPlusPlus.Core.Runtime;
using BepInEx.Configuration;
using UnityEngine;

namespace BazaarPlusPlus.Game.BazaarAgentHost;

internal sealed class BazaarAgentUnityRuntime : MonoBehaviour
{
    private BazaarAgentRuntimeController? _controller;

    internal BazaarAgentContextSnapshot? CurrentSnapshot => _controller?.CurrentSnapshot;

    public void Initialize(
        IBppServices services,
        ConfigFile configFile,
        Func<bool> isReplayStartInProgress
    )
    {
        var logger = new BazaarAgentBppLogger();
        var options = new BazaarAgentBepInExOptions(configFile);
        var contextReader = new BazaarAgentGameContextReader(services, options, logger);
        var dispatcher = new BazaarAgentGameActionDispatcher(logger);
        var uiPlumbing = new BazaarAgentUiPlumbing(logger, isReplayStartInProgress);

        _controller = new BazaarAgentRuntimeController(
            options,
            contextReader,
            dispatcher,
            logger,
            new SystemBazaarAgentClock(),
            uiPlumbing.Tick
        );

        logger.Info("BazaarAgentUnityRuntime initialized");
    }

    private void Update()
    {
        _controller?.Tick();
    }

    private void OnDestroy()
    {
        _controller?.Dispose();
        _controller = null;
    }
}
