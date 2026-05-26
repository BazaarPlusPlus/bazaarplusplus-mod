#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.ItemBoard;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryBattlePreviewData
{
    public static readonly HistoryBattlePreviewData Empty = new(
        Array.Empty<ItemBoardItemSpec>(),
        string.Empty
    );

    public HistoryBattlePreviewData(IReadOnlyList<ItemBoardItemSpec> items, string signature)
    {
        Items = items ?? Array.Empty<ItemBoardItemSpec>();
        Signature = signature ?? string.Empty;
    }

    public IReadOnlyList<ItemBoardItemSpec> Items { get; }

    public string Signature { get; }

    public bool HasRenderableCards => Items.Count > 0;
}
