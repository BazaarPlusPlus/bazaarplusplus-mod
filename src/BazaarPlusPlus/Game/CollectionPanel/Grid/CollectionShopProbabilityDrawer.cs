#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.DealerModel;
using BazaarPlusPlus.Infrastructure.Fonts;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

internal static class CollectionShopProbabilityDrawer
{
    private const string DrawerName = "BppCollectionShopProbabilityDrawer";
    private const string TextName = "BppCollectionShopProbabilityDrawerText";

    public static void Show(
        GameObject host,
        CollectionDealerCardExplain explain,
        EstimateBucket? bucket
    )
    {
        var drawer = EnsureDrawer(host);
        drawer.SetActive(true);
        var label = drawer.GetComponentInChildren<Text>(includeInactive: true);
        if (label != null)
            label.text = CollectionPanelText.ShopProbabilityDrawerText(explain, bucket);
    }

    public static void Hide(GameObject host)
    {
        var existing = host.transform.Find(DrawerName);
        if (existing != null)
            existing.gameObject.SetActive(false);
    }

    private static GameObject EnsureDrawer(GameObject host)
    {
        var existing = host.transform.Find(DrawerName);
        if (existing != null)
            return existing.gameObject;

        var drawer = new GameObject(DrawerName, typeof(RectTransform), typeof(Image));
        drawer.transform.SetParent(host.transform, worldPositionStays: false);
        var rect = drawer.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(10f, -44f);
        rect.sizeDelta = new Vector2(270f, 178f);
        rect.localScale = Vector3.one;

        var image = drawer.GetComponent<Image>();
        image.color = new Color(0.06f, 0.07f, 0.09f, 0.96f);
        image.raycastTarget = false;

        var labelObject = new GameObject(TextName, typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(drawer.transform, worldPositionStays: false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(10f, 8f);
        labelRect.offsetMax = new Vector2(-10f, -8f);
        labelRect.localScale = Vector3.one;

        var label = labelObject.GetComponent<Text>();
        label.font = BppUiFont.Default;
        label.fontSize = Sizes.FontSmall;
        label.fontStyle = FontStyle.Normal;
        label.alignment = TextAnchor.UpperLeft;
        label.color = Colors.HistoryChipText;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.raycastTarget = false;
        return drawer;
    }
}
