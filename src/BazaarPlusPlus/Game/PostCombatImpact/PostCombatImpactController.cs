#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.Game.PostCombatImpact.Ui;
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
        if (
            cardController.CurrentTooltipData is not CardTooltipData tooltipData
            || recapVisual == null
        )
            return;

        var target =
            recapVisual.GetComponent<PostCombatImpactRecapClickTarget>()
            ?? recapVisual.gameObject.AddComponent<PostCombatImpactRecapClickTarget>();
        target.Initialize(this, card, tooltipData, cardController.TooltipOffset);
    }

    internal void ShowDetails(
        Card card,
        Transform anchor,
        Vector3 offset,
        CardTooltipData tooltipData
    )
    {
        var boardManager = Singleton<BoardManager>.Instance;
        if (
            _module == null
            || !_module.TryGetSource(card.InstanceId.Value, out var source)
            || boardManager == null
            || !boardManager.IsRecapViewOpen
        )
            return;

        if (_pendingShow != null)
        {
            StopCoroutine(_pendingShow);
            _pendingShow = null;
        }

        var recapVisual = anchor.GetComponent<RecapItemVisualController>();
        var tooltip = TheBazaar.Data.TooltipParentComponent?.GetCardTooltipController(card);
        if (tooltip != null)
        {
            ShowInNativeTooltip(tooltip, source, recapVisual);
            return;
        }

        ClearSelection();
        TheBazaar.Data.TooltipParentComponent?.ShowCardTooltipController(
            anchor,
            offset,
            tooltipData
        );
        _pendingShow = StartCoroutine(ShowWhenReady(card, source, recapVisual));
    }

    private System.Collections.IEnumerator ShowWhenReady(
        Card card,
        CombatImpactSource source,
        RecapItemVisualController? recapVisual
    )
    {
        const int maxFrames = 60;
        for (var frame = 0; frame < maxFrames; frame++)
        {
            var tooltip = TheBazaar.Data.TooltipParentComponent?.GetCardTooltipController(card);
            if (tooltip != null)
            {
                ShowInNativeTooltip(tooltip, source, recapVisual);
                _pendingShow = null;
                yield break;
            }
            yield return null;
        }

        _pendingShow = null;
    }

    private void ShowInNativeTooltip(
        CardTooltipController tooltip,
        CombatImpactSource source,
        RecapItemVisualController? recapVisual
    )
    {
        if (string.Equals(_selectedSourceId, source.Entity.Id, StringComparison.Ordinal))
            return;

        ClearSelection();
        if (_view?.Show(tooltip, source) != true)
            return;

        _selectedRecapVisual = recapVisual;
        _selectedSourceId = source.Entity.Id;
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
