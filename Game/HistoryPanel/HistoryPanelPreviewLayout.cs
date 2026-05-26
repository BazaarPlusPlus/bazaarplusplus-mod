#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

// Builds 10 socket RectTransforms under a host Canvas. Sockets use anchored positions in
// normalised canvas space so the layout reflows when the canvas (and RT) resizes — no
// explicit rebuild needed when container geometry changes.
//
// Each socket's intrinsic size (the card frame) is captured from the real
// MonsterBoardTooltip prefab in HistoryPanelPreviewCardPool, so cards are sized the way
// the game itself sizes them. Only horizontal placement is overridden to fill the canvas.
internal static class HistoryPanelPreviewLayout
{
    public const int SocketCount = 10;
    private const float HorizontalPaddingFraction = 0.05f;
    private const float FallbackSocketWidthPixels = 240f;
    private const float FallbackSocketHeightPixels = 320f;

    public static RectTransform[] BuildSockets(RectTransform parent, int layer)
    {
        var sockets = new RectTransform[SocketCount];
        var templates = HistoryPanelPreviewCardPool.TryGetSocketTemplates();

        var step = (1f - HorizontalPaddingFraction * 2f) / SocketCount;
        var firstCenter = HorizontalPaddingFraction + step * 0.5f;

        for (var i = 0; i < SocketCount; i++)
        {
            var go = new GameObject($"HistoryPanelPreviewSocket_{i}", typeof(RectTransform));
            go.layer = layer;
            var socket = go.GetComponent<RectTransform>();
            socket.SetParent(parent, worldPositionStays: false);

            Vector2 sizeDelta;
            Vector2 pivot;
            if (templates != null && i < templates.Length)
            {
                sizeDelta = templates[i].SizeDelta;
                pivot = templates[i].Pivot;
            }
            else
            {
                sizeDelta = new Vector2(FallbackSocketWidthPixels, FallbackSocketHeightPixels);
                pivot = new Vector2(0.5f, 0.5f);
            }

            var anchorX = firstCenter + step * i;
            socket.anchorMin = new Vector2(anchorX, 0.5f);
            socket.anchorMax = new Vector2(anchorX, 0.5f);
            socket.pivot = pivot;
            socket.sizeDelta = sizeDelta;
            socket.anchoredPosition = Vector2.zero;

            sockets[i] = socket;
        }

        return sockets;
    }
}
