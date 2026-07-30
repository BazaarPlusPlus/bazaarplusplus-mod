#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.GameInterop.Events;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.PostCombatImpact;

internal sealed class PostCombatImpactModule : IBppFeature
{
    private readonly IBppEventBus _eventBus;
    private IDisposable? _subscription;

    internal PostCombatImpactModule(IBppEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    internal CombatImpactReport LatestReport { get; private set; } = CombatImpactReport.Empty;

    public void Start()
    {
        _subscription = _eventBus.Subscribe<CombatSimObserved>(OnCombatSimObserved);
    }

    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
        LatestReport = CombatImpactReport.Empty;
    }

    private void OnCombatSimObserved(CombatSimObserved observed)
    {
        try
        {
            var simulation = observed.Message.Data;
            LatestReport =
                simulation == null
                    ? CombatImpactReport.Empty
                    : CombatImpactProjector.Project(
                        simulation,
                        CombatImpactEntitySnapshotReader.Read()
                    );
        }
        catch (Exception ex)
        {
            LatestReport = CombatImpactReport.Empty;
            BppLog.WarnEvent(
                PostCombatImpactLogEvents.ProjectionDegraded,
                ex,
                PostCombatImpactLogEvents.ReasonCode.Bind(
                    PostCombatImpactReasonCode.ProjectionException
                )
            );
        }
    }
}
