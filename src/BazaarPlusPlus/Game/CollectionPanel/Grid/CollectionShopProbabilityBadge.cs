#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.DealerModel;
using BazaarPlusPlus.Infrastructure.Fonts;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

internal static class CollectionShopProbabilityBadge
{
    private const string BadgeName = "BppCollectionShopProbabilityBadge";
    private const string LabelName = "BppCollectionShopProbabilityLabel";

    public static void Bind(GameObject host, CollectionDealerCardExplain? explain)
    {
        var badge = EnsureBadge(host);
        if (explain == null || explain.State == CollectionDealerProbabilityState.NotInPool)
        {
            badge.SetActive(false);
            return;
        }

        badge.SetActive(true);
        var label = badge.GetComponentInChildren<Text>(includeInactive: true);
        if (label != null)
            label.text = CollectionPanelText.ShopProbabilityBadge(explain);
    }

    private static GameObject EnsureBadge(GameObject host)
    {
        var existing = host.transform.Find(BadgeName);
        if (existing != null)
            return existing.gameObject;

        var badge = new GameObject(BadgeName, typeof(RectTransform), typeof(Image));
        badge.transform.SetParent(host.transform, worldPositionStays: false);
        var rect = badge.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(10f, -12f);
        rect.sizeDelta = new Vector2(86f, 28f);
        rect.localScale = Vector3.one;

        var image = badge.GetComponent<Image>();
        image.color = new Color(0.08f, 0.10f, 0.13f, 0.92f);
        image.raycastTarget = false;

        var labelObject = new GameObject(LabelName, typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(badge.transform, worldPositionStays: false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(6f, 0f);
        labelRect.offsetMax = new Vector2(-6f, 0f);
        labelRect.localScale = Vector3.one;

        var label = labelObject.GetComponent<Text>();
        label.font = BppUiFont.Default;
        label.fontSize = Sizes.FontSmall;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Colors.HistoryTitleText;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.raycastTarget = false;
        return badge;
    }
}
