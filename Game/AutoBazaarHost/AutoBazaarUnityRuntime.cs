#nullable enable
using System;
using BazaarPlusPlus.AutoBazaar;
using BazaarPlusPlus.Core.Runtime;
using BepInEx.Configuration;
using UnityEngine;

namespace BazaarPlusPlus.Game.AutoBazaarHost;

internal sealed class AutoBazaarUnityRuntime : MonoBehaviour
{
    private AutoBazaarRuntimeController? _controller;

    internal AutoBazaarContextSnapshot? CurrentSnapshot => _controller?.CurrentSnapshot;

    public void Initialize(
        IBppServices services,
        ConfigFile configFile,
        Func<bool> isReplayStartInProgress
    )
    {
        var logger = new AutoBazaarBppLogger();
        var options = new AutoBazaarBepInExOptions(configFile);
        var contextReader = new AutoBazaarGameContextReader(services, options, logger);
        var dispatcher = new AutoBazaarGameActionDispatcher(logger);
        var uiPlumbing = new AutoBazaarUiPlumbing(logger, isReplayStartInProgress);

        _controller = new AutoBazaarRuntimeController(
            options,
            contextReader,
            dispatcher,
            logger,
            new SystemAutoBazaarClock(),
            uiPlumbing.Tick
        );

        logger.Info("AutoBazaarUnityRuntime initialized");
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
