#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.HistoryPanel.Data;

internal sealed class HistoryBattlePreviewData
{
    public static readonly HistoryBattlePreviewData Empty = new(
        Array.Empty<HistoryItemSpec>(),
        string.Empty
    );

    public HistoryBattlePreviewData(IReadOnlyList<HistoryItemSpec> items, string signature)
    {
        Items = items ?? Array.Empty<HistoryItemSpec>();
        Signature = signature ?? string.Empty;
    }

    public IReadOnlyList<HistoryItemSpec> Items { get; }

    public string Signature { get; }

    public bool HasRenderableCards => Items.Count > 0;
}
