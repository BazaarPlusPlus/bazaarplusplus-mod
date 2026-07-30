#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.GameInterop.Events;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Game.PostCombatImpact;

internal interface IPostCombatImpactModule
{
    bool TryGetSource(string instanceId, out CombatImpactSource source);

    void BindRecapCard(
        RecapItemVisualController recapVisual,
        Card card,
        CardController cardController
    );

    void ShowDetails(Card card, Transform anchor, Vector3 offset, CardTooltipData tooltipData);

    void OnNativeTooltipChanging(CardTooltipController controller);
}

internal sealed class PostCombatImpactModule : IBppFeature, IPostCombatImpactModule
{
    private readonly IBppEventBus _eventBus;
    private IDisposable? _subscription;
    private PostCombatImpactController? _runtime;

    internal PostCombatImpactModule(IBppEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    internal CombatImpactReport LatestReport { get; private set; } = CombatImpactReport.Empty;

    internal void AttachRuntime(PostCombatImpactController runtime)
    {
        _runtime = runtime;
    }

    internal void DetachRuntime(PostCombatImpactController runtime)
    {
        if (ReferenceEquals(_runtime, runtime))
            _runtime = null;
    }

    public bool TryGetSource(string instanceId, out CombatImpactSource source)
    {
        var match = LatestReport.Sources.FirstOrDefault(candidate =>
            string.Equals(candidate.Entity.Id, instanceId, StringComparison.Ordinal)
        );
        if (match == null)
        {
            source = null!;
            return false;
        }

        source = match;
        return true;
    }

    public void BindRecapCard(
        RecapItemVisualController recapVisual,
        Card card,
        CardController cardController
    ) => _runtime?.BindRecapCard(recapVisual, card, cardController);

    public void ShowDetails(
        Card card,
        Transform anchor,
        Vector3 offset,
        CardTooltipData tooltipData
    ) => _runtime?.ShowDetails(card, anchor, offset, tooltipData);

    public void OnNativeTooltipChanging(CardTooltipController controller) =>
        _runtime?.OnNativeTooltipChanging(controller);

    public void Start()
    {
        _subscription = _eventBus.Subscribe<CombatSimObserved>(OnCombatSimObserved);
    }

    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
        LatestReport = CombatImpactReport.Empty;
        _runtime?.HideDetails();
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
