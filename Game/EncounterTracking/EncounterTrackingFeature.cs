#nullable enable
using System;
using BazaarGameShared.Domain.Runs;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Core.RunContext;

namespace BazaarPlusPlus.Game.EncounterTracking;

internal sealed class EncounterTrackingFeature : IBppFeature
{
    private readonly EncounterTrackingModule _module;
    private readonly EncounterTrackingController _controller;
    private bool _started;

    public EncounterTrackingFeature(
        IBppEventBus eventBus,
        IRunContext runContext,
        IMonsterCatalog monsterCatalog
    )
    {
        if (eventBus == null)
            throw new ArgumentNullException(nameof(eventBus));
        if (runContext == null)
            throw new ArgumentNullException(nameof(runContext));
        if (monsterCatalog == null)
            throw new ArgumentNullException(nameof(monsterCatalog));

        _module = new EncounterTrackingModule(eventBus);
        _controller = new EncounterTrackingController(_module, runContext, monsterCatalog);
    }

    public IEncounterSelectionQuery SelectionQuery => _module.Query;

    public void Start()
    {
        if (_started)
            return;

        _module.Start();
        _controller.Start();
        _started = true;
    }

    public void Stop()
    {
        if (_started)
        {
            _controller.Stop();
            _module.Stop();
            _started = false;
        }

        _controller.ResetEncounterState("Encounter tracking feature stopped");
    }

    public bool IsSupportedSelectionState(ERunState stateName)
    {
        return EncounterTrackingController.IsSupportedSelectionState(stateName);
    }

    public void ResetEncounterState(string reason)
    {
        _controller.ResetEncounterState(reason);
    }
}
