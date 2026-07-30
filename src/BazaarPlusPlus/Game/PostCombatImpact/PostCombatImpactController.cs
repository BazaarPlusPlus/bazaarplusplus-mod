#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.Game.PostCombatImpact.Ui;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus.Game.PostCombatImpact;

internal sealed class PostCombatImpactController : MonoBehaviour
{
    private const int NativeTooltipWaitFrames = 60;

    private readonly WaitForEndOfFrame _waitForEndOfFrame = new();
    private PostCombatImpactModule? _module;
    private IPostCombatImpactTooltipView? _view;
    private Coroutine? _pendingShow;
    private CardTooltipController? _pendingPrimaryTooltip;
    private Transform? _pendingAuxiliaryAnchor;
    private string? _pendingAuxiliaryHeader;
    private AuxiliaryTooltipController? _pendingAuxiliaryController;
    private RecapItemVisualController? _selectedRecapVisual;
    private string? _selectedSourceId;
    private Component? _hoveredOwner;
    private RecapItemVisualController? _hoveredRecapVisual;
    private Card? _hoveredCard;
    private Transform? _hoveredAnchor;
    private CardTooltipData? _hoveredTooltipData;
    private Vector3 _hoveredTooltipOffset;
    private bool _mouseDeviceUnavailableLogged;

    internal void Initialize(PostCombatImpactModule module, IPostCombatImpactTooltipView view)
    {
        _module = module;
        _view = view;
        module.AttachRuntime(this);
        Events.RecapEnded.AddListener(OnRecapEnded, this);
    }

    internal void SetHoveredRecapCard(
        RecapItemVisualController recapVisual,
        Card card,
        CardTooltipData? tooltipData,
        Vector3 tooltipOffset
    ) =>
        SetHoveredSource(
            recapVisual,
            recapVisual,
            card,
            recapVisual.transform,
            tooltipData,
            tooltipOffset
        );

    internal void ClearHoveredRecapCard(RecapItemVisualController recapVisual) =>
        ClearHoveredSource(recapVisual);

    internal void SetHoveredSkill(
        SkillProxyRenderer skill,
        Card card,
        CardTooltipData? tooltipData,
        Vector3 tooltipOffset
    ) => SetHoveredSource(skill, null, card, skill.transform, tooltipData, tooltipOffset);

    internal void ClearHoveredSkill(SkillProxyRenderer skill) => ClearHoveredSource(skill);

    private void SetHoveredSource(
        Component owner,
        RecapItemVisualController? recapVisual,
        Card card,
        Transform anchor,
        CardTooltipData? tooltipData,
        Vector3 tooltipOffset
    )
    {
        tooltipData ??= CardTooltipData.CreateCardTooltipData(card);
        if (tooltipData == null)
        {
            LogInteraction(PostCombatImpactReasonCode.TooltipDataUnavailable);
            return;
        }

        _hoveredOwner = owner;
        _hoveredRecapVisual = recapVisual;
        _hoveredCard = card;
        _hoveredAnchor = anchor;
        _hoveredTooltipData = tooltipData;
        _hoveredTooltipOffset = tooltipOffset;
        LogInteraction(PostCombatImpactReasonCode.RecapHoverObserved);
    }

    private void ClearHoveredSource(Component owner)
    {
        if (ReferenceEquals(_hoveredOwner, owner))
            ClearHoveredSource();
    }

    private void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null)
        {
            if (
                !_mouseDeviceUnavailableLogged
                && (_hoveredOwner != null || _selectedSourceId != null)
            )
            {
                _mouseDeviceUnavailableLogged = true;
                LogInteraction(PostCombatImpactReasonCode.MouseDeviceUnavailable);
            }
            return;
        }

        _mouseDeviceUnavailableLogged = false;
        if (mouse.rightButton.wasPressedThisFrame)
        {
            if (_hoveredCard != null && _hoveredAnchor != null && _hoveredTooltipData != null)
            {
                LogInteraction(PostCombatImpactReasonCode.RecapRightButtonObserved);
                ShowDetails(
                    _hoveredCard,
                    _hoveredAnchor,
                    _hoveredTooltipOffset,
                    _hoveredTooltipData,
                    _hoveredRecapVisual
                );
            }
            else if (_selectedSourceId != null || _pendingShow != null)
            {
                CancelAndClearSelection();
                LogInteraction(PostCombatImpactReasonCode.Dismissed);
            }
            return;
        }

        if (
            mouse.leftButton.wasPressedThisFrame
            && (_selectedSourceId != null || _pendingShow != null)
        )
        {
            CancelAndClearSelection();
            LogInteraction(PostCombatImpactReasonCode.Dismissed);
        }
    }

    private void ShowDetails(
        Card card,
        Transform anchor,
        Vector3 offset,
        CardTooltipData tooltipData,
        RecapItemVisualController? recapVisual
    )
    {
        if (!IsRecapOpen())
        {
            LogInteraction(PostCombatImpactReasonCode.RecapClosed);
            return;
        }

        if (_module == null || _view == null)
        {
            LogInteraction(PostCombatImpactReasonCode.RuntimeUnavailable);
            return;
        }

        if (card.InstanceId.Value is not { Length: > 0 } sourceId)
        {
            LogInteraction(PostCombatImpactReasonCode.SourceIdUnavailable);
            return;
        }

        CombatImpactSource? source = null;
        if (_module.TryGetSource(sourceId, out var matchedSource))
            source = matchedSource;

        CancelAndClearSelection();
        var tooltipParent = TheBazaar.Data.TooltipParentComponent;
        if (tooltipParent == null)
        {
            LogInteraction(PostCombatImpactReasonCode.RuntimeUnavailable);
            return;
        }

        if (tooltipParent.GetCardTooltipController(card) == null)
            tooltipParent.ShowCardTooltipController(anchor, offset, tooltipData);

        _pendingShow = StartCoroutine(
            ShowWhenReady(card, anchor, offset, sourceId, source, recapVisual)
        );
    }

    private System.Collections.IEnumerator ShowWhenReady(
        Card card,
        Transform anchor,
        Vector3 offset,
        string sourceId,
        CombatImpactSource? source,
        RecapItemVisualController? recapVisual
    )
    {
        // StartCoroutine advances immediately to the first yield before its return value can be
        // assigned to _pendingShow. Defer native tooltip creation so synchronous reuse of an
        // already-active auxiliary host can still identify this request.
        yield return null;

        var tooltipParent = TheBazaar.Data.TooltipParentComponent;
        CardTooltipController? primary = null;
        for (var frame = 0; frame < NativeTooltipWaitFrames; frame++)
        {
            if (!IsRecapOpen())
            {
                FinishPendingShow();
                LogInteraction(PostCombatImpactReasonCode.RecapClosed);
                yield break;
            }

            primary = tooltipParent?.GetCardTooltipController(card);
            if (primary != null)
                break;
            yield return null;
        }

        if (primary == null || tooltipParent == null || _view == null)
        {
            FinishPendingShow();
            LogInteraction(PostCombatImpactReasonCode.PrimaryTooltipCreateTimedOut);
            yield break;
        }

        _pendingPrimaryTooltip = primary;
        primary.SetLockedFlag(true);
        _pendingAuxiliaryAnchor = anchor;
        _pendingAuxiliaryHeader = _view.Header;
        _pendingAuxiliaryController = null;
        tooltipParent.ShowAuxiliaryTooltipController(
            anchor,
            offset,
            _pendingAuxiliaryHeader,
            itemTier: card.Tier
        );

        AuxiliaryTooltipController? auxiliary = null;
        for (var frame = 0; frame < NativeTooltipWaitFrames; frame++)
        {
            auxiliary = tooltipParent.AuxiliaryTooltipController;
            if (
                auxiliary != null
                && ReferenceEquals(auxiliary, _pendingAuxiliaryController)
                && tooltipParent.IsAuxiliaryTooltipDisplayed
                && auxiliary!._coroutine == null
            )
                break;
            yield return null;
        }

        if (
            auxiliary == null
            || !tooltipParent.IsAuxiliaryTooltipDisplayed
            || auxiliary!._coroutine != null
        )
        {
            FinishPendingShow();
            tooltipParent.HideAuxiliaryTooltipController();
            LogInteraction(PostCombatImpactReasonCode.AuxiliaryTooltipCreateTimedOut);
            yield break;
        }

        if (
            !IsRecapOpen()
            || !ReferenceEquals(tooltipParent.GetCardTooltipController(card), primary)
        )
        {
            FinishPendingShow();
            tooltipParent.HideAuxiliaryTooltipController();
            LogInteraction(PostCombatImpactReasonCode.RecapClosed);
            yield break;
        }

        ClearPendingAuxiliaryRequest();
        bool shown;
        try
        {
            shown = _view.Show(auxiliary!, primary, source);
        }
        catch (Exception ex)
        {
            _view.Hide();
            FinishPendingShow();
            LogInteractionFailure(PostCombatImpactReasonCode.TooltipRenderException, ex);
            yield break;
        }

        if (!shown)
        {
            FinishPendingShow();
            tooltipParent.HideAuxiliaryTooltipController();
            LogInteraction(PostCombatImpactReasonCode.AuxiliaryTooltipContentUnavailable);
            yield break;
        }

        _pendingPrimaryTooltip = null;
        _selectedRecapVisual = recapVisual;
        _selectedSourceId = sourceId;
        yield return _waitForEndOfFrame;
        bool positioned;
        try
        {
            positioned = _view.Position(auxiliary!, primary);
        }
        catch (Exception ex)
        {
            _pendingShow = null;
            HideActiveSelection();
            LogInteractionFailure(PostCombatImpactReasonCode.TooltipRenderException, ex);
            yield break;
        }

        if (!positioned)
        {
            _pendingShow = null;
            HideActiveSelection();
            LogInteraction(PostCombatImpactReasonCode.AuxiliaryTooltipPositionUnavailable);
            yield break;
        }

        _pendingShow = null;
        LogInteraction(
            source == null
                ? PostCombatImpactReasonCode.ShownWithoutAttributedImpact
                : PostCombatImpactReasonCode.Shown
        );
    }

    internal void OnNativeTooltipChanging(CardTooltipController controller)
    {
        if (ReferenceEquals(_pendingPrimaryTooltip, controller))
        {
            CancelPendingShow(hidePrimary: false);
            return;
        }

        if (_view?.OnNativeTooltipChanging(controller) != true)
            return;

        if (_pendingShow != null)
            CancelPendingShow(hidePrimary: false, hideAuxiliary: false);
        RestoreSelectedRecapVisual();
    }

    internal void OnNativeAuxiliaryTooltipShowing(
        AuxiliaryTooltipController controller,
        Transform anchor,
        string header
    )
    {
        if (
            _pendingShow != null
            && ReferenceEquals(_pendingAuxiliaryAnchor, anchor)
            && string.Equals(_pendingAuxiliaryHeader, header, StringComparison.Ordinal)
        )
        {
            _pendingAuxiliaryController = controller;
            return;
        }

        if (_pendingShow != null)
            CancelPendingShow(hidePrimary: true, hideAuxiliary: false);
        HandleNativeAuxiliaryTakeover(
            controller,
            PostCombatImpactReasonCode.NativeAuxiliaryDisplaced
        );
    }

    internal void OnNativeAuxiliaryTooltipHiding(AuxiliaryTooltipController controller) =>
        HandleNativeAuxiliaryTakeover(controller, PostCombatImpactReasonCode.NativeAuxiliaryHidden);

    private void HandleNativeAuxiliaryTakeover(
        AuxiliaryTooltipController controller,
        PostCombatImpactReasonCode reasonCode
    )
    {
        if (_view?.OnNativeAuxiliaryTooltipChanging(controller) != true)
            return;

        if (_pendingShow != null)
            CancelPendingShow(hidePrimary: false, hideAuxiliary: false);
        TheBazaar.Data.TooltipParentComponent?.HideCardTooltipController();
        RestoreSelectedRecapVisual();
        LogInteraction(reasonCode);
    }

    internal void HideDetails()
    {
        ClearHoveredSource();
        CancelAndClearSelection();
    }

    private void ClearHoveredSource()
    {
        _hoveredOwner = null;
        _hoveredRecapVisual = null;
        _hoveredCard = null;
        _hoveredAnchor = null;
        _hoveredTooltipData = null;
        _hoveredTooltipOffset = Vector3.zero;
    }

    private void CancelAndClearSelection()
    {
        CancelPendingShow();
        HideActiveSelection();
    }

    private void CancelPendingShow(bool hidePrimary = true, bool hideAuxiliary = true)
    {
        var pendingPrimary = _pendingPrimaryTooltip;
        var pendingAuxiliary = _pendingAuxiliaryController;
        if (_pendingShow != null)
        {
            StopCoroutine(_pendingShow);
            _pendingShow = null;
        }

        if (pendingPrimary != null)
            pendingPrimary.SetLockedFlag(false);
        _pendingPrimaryTooltip = null;
        ClearPendingAuxiliaryRequest();
        if (hideAuxiliary && pendingAuxiliary != null)
            TheBazaar.Data.TooltipParentComponent?.HideAuxiliaryTooltipController();
        if (hidePrimary && pendingPrimary != null)
            TheBazaar.Data.TooltipParentComponent?.HideCardTooltipController();
    }

    private void FinishPendingShow()
    {
        var pendingPrimary = _pendingPrimaryTooltip;
        _pendingShow = null;
        if (pendingPrimary != null)
            pendingPrimary.SetLockedFlag(false);
        _pendingPrimaryTooltip = null;
        ClearPendingAuxiliaryRequest();
        if (pendingPrimary != null)
            TheBazaar.Data.TooltipParentComponent?.HideCardTooltipController();
    }

    private void ClearPendingAuxiliaryRequest()
    {
        _pendingAuxiliaryAnchor = null;
        _pendingAuxiliaryHeader = null;
        _pendingAuxiliaryController = null;
    }

    private void HideActiveSelection()
    {
        var hadSelection = _selectedSourceId != null;
        _view?.Hide();
        if (hadSelection)
            TheBazaar.Data.TooltipParentComponent?.HideCardTooltipController();
        RestoreSelectedRecapVisual();
    }

    private void RestoreSelectedRecapVisual()
    {
        if (_selectedRecapVisual != null)
            _selectedRecapVisual.Move();
        _selectedRecapVisual = null;
        _selectedSourceId = null;
    }

    private static bool IsRecapOpen()
    {
        var boardManager = Singleton<BoardManager>.Instance;
        return boardManager != null && boardManager.IsRecapViewOpen;
    }

    private void OnRecapEnded() => HideDetails();

    private static void LogInteraction(PostCombatImpactReasonCode reasonCode) =>
        BppLog.InfoEvent(
            PostCombatImpactLogEvents.InteractionObserved,
            PostCombatImpactLogEvents.ReasonCode.Bind(reasonCode)
        );

    private static void LogInteractionFailure(
        PostCombatImpactReasonCode reasonCode,
        Exception exception
    ) =>
        BppLog.WarnEvent(
            PostCombatImpactLogEvents.InteractionDegraded,
            exception,
            PostCombatImpactLogEvents.ReasonCode.Bind(reasonCode)
        );

    private void OnDestroy()
    {
        Events.RecapEnded.RemoveListener(OnRecapEnded);
        HideDetails();
        _view?.Dispose();
        _view = null;
        _module?.DetachRuntime(this);
        _module = null;
    }
}
