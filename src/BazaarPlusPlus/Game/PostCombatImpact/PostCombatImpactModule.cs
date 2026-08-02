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

    bool TryGetReceived(string instanceId, out CombatImpactReceived received);

    void SetHoveredRecapCard(
        RecapItemVisualController recapVisual,
        Card card,
        CardTooltipData? tooltipData,
        Vector3 tooltipOffset
    );

    void ClearHoveredRecapCard(
        RecapItemVisualController recapVisual,
        PostCombatImpactHoverExitOrigin origin,
        bool nativeTooltipLocked
    );

    void SetHoveredSkill(
        SkillProxyRenderer skill,
        Card card,
        CardTooltipData? tooltipData,
        Vector3 tooltipOffset
    );

    void ClearHoveredSkill(
        SkillProxyRenderer skill,
        PostCombatImpactHoverExitOrigin origin,
        bool nativeTooltipLocked
    );

    void OnNativeTooltipPreparing(CardTooltipController controller, ITooltipData tooltipData);

    void OnNativeTooltipChanging(CardTooltipController controller);

    void OnNativeAuxiliaryTooltipShowing(
        AuxiliaryTooltipController controller,
        Transform anchor,
        string header
    );

    void OnNativeAuxiliaryTooltipHiding(AuxiliaryTooltipController controller);
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

    public bool TryGetReceived(string instanceId, out CombatImpactReceived received)
    {
        var match = LatestReport.Received.FirstOrDefault(candidate =>
            string.Equals(candidate.Entity.Id, instanceId, StringComparison.Ordinal)
        );
        if (match == null)
        {
            received = null!;
            return false;
        }

        received = match;
        return true;
    }

    public void SetHoveredRecapCard(
        RecapItemVisualController recapVisual,
        Card card,
        CardTooltipData? tooltipData,
        Vector3 tooltipOffset
    ) => _runtime?.SetHoveredRecapCard(recapVisual, card, tooltipData, tooltipOffset);

    public void ClearHoveredRecapCard(
        RecapItemVisualController recapVisual,
        PostCombatImpactHoverExitOrigin origin,
        bool nativeTooltipLocked
    ) => _runtime?.ClearHoveredRecapCard(recapVisual, origin, nativeTooltipLocked);

    public void SetHoveredSkill(
        SkillProxyRenderer skill,
        Card card,
        CardTooltipData? tooltipData,
        Vector3 tooltipOffset
    ) => _runtime?.SetHoveredSkill(skill, card, tooltipData, tooltipOffset);

    public void ClearHoveredSkill(
        SkillProxyRenderer skill,
        PostCombatImpactHoverExitOrigin origin,
        bool nativeTooltipLocked
    ) => _runtime?.ClearHoveredSkill(skill, origin, nativeTooltipLocked);

    public void OnNativeTooltipPreparing(
        CardTooltipController controller,
        ITooltipData tooltipData
    ) => _runtime?.OnNativeTooltipPreparing(controller, tooltipData);

    public void OnNativeTooltipChanging(CardTooltipController controller) =>
        _runtime?.OnNativeTooltipChanging(controller);

    public void OnNativeAuxiliaryTooltipShowing(
        AuxiliaryTooltipController controller,
        Transform anchor,
        string header
    ) => _runtime?.OnNativeAuxiliaryTooltipShowing(controller, anchor, header);

    public void OnNativeAuxiliaryTooltipHiding(AuxiliaryTooltipController controller) =>
        _runtime?.OnNativeAuxiliaryTooltipHiding(controller);

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
