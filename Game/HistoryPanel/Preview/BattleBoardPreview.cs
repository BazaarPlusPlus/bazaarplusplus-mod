#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel.Preview;

internal enum BattleBoardRenderPhase
{
    Empty,
    InitFailed,
    Loading,
    Done,
}

internal sealed class BattleBoardPreview : IDisposable
{
    private const int DefaultLayer = 30;
    private const int OverlaySortingOrder = 27;

    private readonly ItemBoardPreviewSurface _surface = new();
    private readonly ItemBoardPreviewOptions _options;

    public BattleBoardPreview(int layer = DefaultLayer)
    {
        _options = new ItemBoardPreviewOptions
        {
            Layer = layer,
            SortingOrder = OverlaySortingOrder,
            LayoutMode = ItemBoardPreviewLayoutMode.Packed,
            ShowHover = true,
            LogComponent = "BattleBoardPreview",
        };
    }

    public void CancelPending() => _surface.CancelPending();

    public void SetPosition(Vector2 position) => _surface.SetPosition(position);

    public void SetClipSize(Vector2 size) => _surface.SetClipSize(size);

    public bool SetCardScale(float scale) => _surface.SetCardScale(scale);

    public IEnumerator Render(
        IReadOnlyList<HistoryItemSpec>? cards,
        string? signature = null,
        Action<BattleBoardRenderPhase>? onPhase = null,
        Action? onComplete = null
    )
    {
        return _surface.Render(
            MapSpecs(cards),
            _options,
            signature,
            phase => onPhase?.Invoke(MapPhase(phase)),
            onComplete
        );
    }

    public void PollHover(Vector2 mousePixels) => _surface.PollHover(mousePixels);

    public void Hide() => _surface.Hide();

    public void Dispose() => _surface.Dispose();

    private static IReadOnlyList<NativeCardPreviewSpec> MapSpecs(
        IReadOnlyList<HistoryItemSpec>? cards
    )
    {
        if (cards == null || cards.Count == 0)
            return Array.Empty<NativeCardPreviewSpec>();

        var specs = new List<NativeCardPreviewSpec>(cards.Count);
        foreach (var card in cards)
        {
            if (card == null)
                continue;

            specs.Add(
                new NativeCardPreviewSpec
                {
                    TemplateId = card.TemplateId,
                    Tier = card.Tier,
                    SocketId = card.SocketId,
                    EnchantmentType = card.EnchantmentType,
                    Attributes = card.Attributes,
                    InstanceIdPrefix = "bpp-battleboard",
                }
            );
        }

        return specs;
    }

    private static BattleBoardRenderPhase MapPhase(ItemBoardPreviewPhase phase) =>
        phase switch
        {
            ItemBoardPreviewPhase.Empty => BattleBoardRenderPhase.Empty,
            ItemBoardPreviewPhase.InitFailed => BattleBoardRenderPhase.InitFailed,
            ItemBoardPreviewPhase.Loading => BattleBoardRenderPhase.Loading,
            _ => BattleBoardRenderPhase.Done,
        };
}
