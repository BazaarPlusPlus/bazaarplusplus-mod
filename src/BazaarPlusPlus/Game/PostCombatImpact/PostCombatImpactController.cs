#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.Game.PostCombatImpact.Ui;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Game.PostCombatImpact;

internal sealed class PostCombatImpactController : MonoBehaviour
{
    private PostCombatImpactModule? _module;
    private IPostCombatImpactTooltipView? _view;
    private Coroutine? _pendingShow;
    private RecapItemVisualController? _selectedRecapVisual;
    private string? _selectedSourceId;

    internal void Initialize(PostCombatImpactModule module, IPostCombatImpactTooltipView view)
    {
        _module = module;
        _view = view;
        module.AttachRuntime(this);
        Events.RecapEnded.AddListener(OnRecapEnded, this);
    }

    internal void BindRecapCard(
        RecapItemVisualController recapVisual,
        Card card,
        CardController cardController
    )
    {
        if (recapVisual == null)
            return;

        var tooltipData =
            cardController.CurrentTooltipData as CardTooltipData
            ?? CardTooltipData.CreateCardTooltipData(card);
        if (tooltipData == null)
        {
            LogInteraction(PostCombatImpactReasonCode.TooltipDataUnavailable);
            return;
        }

        var target =
            recapVisual.GetComponent<PostCombatImpactRecapClickTarget>()
            ?? recapVisual.gameObject.AddComponent<PostCombatImpactRecapClickTarget>();
        target.Initialize(this, card, tooltipData, cardController.TooltipOffset);
        DebugInteraction(PostCombatImpactReasonCode.RecapCardBound);
    }

    internal void ShowRecapCardDetails(
        Card card,
        Transform anchor,
        Vector3 offset,
        CardTooltipData tooltipData
    )
    {
        DebugInteraction(PostCombatImpactReasonCode.RecapPointerDownReceived);
        ShowDetails(card, anchor, offset, tooltipData);
    }

    internal void ShowDetails(
        Card card,
        Transform anchor,
        Vector3 offset,
        CardTooltipData tooltipData
    )
    {
        var boardManager = Singleton<BoardManager>.Instance;
        if (boardManager == null || !boardManager.IsRecapViewOpen)
        {
            LogInteraction(PostCombatImpactReasonCode.RecapClosed);
            return;
        }

        if (_module == null)
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

        if (_pendingShow != null)
        {
            StopCoroutine(_pendingShow);
            _pendingShow = null;
        }

        var recapVisual = anchor.GetComponent<RecapItemVisualController>();
        var tooltip = TheBazaar.Data.TooltipParentComponent?.GetCardTooltipController(card);
        if (tooltip != null)
        {
            ShowInNativeTooltip(tooltip, sourceId, source, recapVisual);
            return;
        }

        ClearSelection();
        TheBazaar.Data.TooltipParentComponent?.ShowCardTooltipController(
            anchor,
            offset,
            tooltipData
        );
        _pendingShow = StartCoroutine(ShowWhenReady(card, sourceId, source, recapVisual));
    }

    private System.Collections.IEnumerator ShowWhenReady(
        Card card,
        string sourceId,
        CombatImpactSource? source,
        RecapItemVisualController? recapVisual
    )
    {
        const int maxFrames = 60;
        for (var frame = 0; frame < maxFrames; frame++)
        {
            var tooltip = TheBazaar.Data.TooltipParentComponent?.GetCardTooltipController(card);
            if (tooltip != null)
            {
                ShowInNativeTooltip(tooltip, sourceId, source, recapVisual);
                _pendingShow = null;
                yield break;
            }
            yield return null;
        }

        _pendingShow = null;
        LogInteraction(PostCombatImpactReasonCode.NativeTooltipCreateTimedOut);
    }

    private void ShowInNativeTooltip(
        CardTooltipController tooltip,
        string sourceId,
        CombatImpactSource? source,
        RecapItemVisualController? recapVisual
    )
    {
        if (string.Equals(_selectedSourceId, sourceId, StringComparison.Ordinal))
            return;

        ClearSelection();
        if (_view?.Show(tooltip, sourceId, source) != true)
        {
            LogInteraction(PostCombatImpactReasonCode.TooltipSectionUnavailable);
            return;
        }

        _selectedRecapVisual = recapVisual;
        _selectedSourceId = sourceId;
        LogInteraction(
            source == null
                ? PostCombatImpactReasonCode.ShownWithoutAttributedImpact
                : PostCombatImpactReasonCode.Shown
        );
    }

    internal void OnNativeTooltipChanging(CardTooltipController controller)
    {
        if (_view?.OnNativeTooltipChanging(controller) == true)
            RestoreSelectedRecapVisual();
    }

    internal void HideDetails()
    {
        if (_pendingShow != null)
        {
            StopCoroutine(_pendingShow);
            _pendingShow = null;
        }
        ClearSelection();
    }

    private void ClearSelection()
    {
        _view?.Hide();
        RestoreSelectedRecapVisual();
    }

    private void RestoreSelectedRecapVisual()
    {
        if (_selectedRecapVisual != null)
            _selectedRecapVisual.Move();
        _selectedRecapVisual = null;
        _selectedSourceId = null;
    }

    private void OnRecapEnded() => HideDetails();

    private static void LogInteraction(PostCombatImpactReasonCode reasonCode) =>
        BppLog.InfoEvent(
            PostCombatImpactLogEvents.InteractionObserved,
            PostCombatImpactLogEvents.ReasonCode.Bind(reasonCode)
        );

    private static void DebugInteraction(PostCombatImpactReasonCode reasonCode) =>
        BppLog.DebugEvent(
            PostCombatImpactLogEvents.InteractionObserved,
            () => [PostCombatImpactLogEvents.ReasonCode.Bind(reasonCode)]
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
