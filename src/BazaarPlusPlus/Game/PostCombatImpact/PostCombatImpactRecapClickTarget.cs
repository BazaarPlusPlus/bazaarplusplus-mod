#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using TheBazaar.Tooltips;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BazaarPlusPlus.Game.PostCombatImpact;

internal sealed class PostCombatImpactRecapClickTarget : MonoBehaviour, IPointerDownHandler
{
    private PostCombatImpactController? _owner;
    private Card? _card;
    private CardTooltipData? _tooltipData;
    private Vector3 _tooltipOffset;

    internal void Initialize(
        PostCombatImpactController owner,
        Card card,
        CardTooltipData tooltipData,
        Vector3 tooltipOffset
    )
    {
        _owner = owner;
        _card = card;
        _tooltipData = tooltipData;
        _tooltipOffset = tooltipOffset;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (
            eventData.button != PointerEventData.InputButton.Right
            || _owner == null
            || _card == null
            || _tooltipData == null
        )
            return;

        _owner.ShowRecapCardDetails(_card, transform, _tooltipOffset, _tooltipData);
    }
}
