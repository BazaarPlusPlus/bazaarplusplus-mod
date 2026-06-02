#nullable enable

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal sealed class ItemBoardPreviewOptions
{
    public int Layer { get; init; } = 30;

    public int SortingOrder { get; init; } = 27;

    public ItemBoardPreviewLayoutMode LayoutMode { get; init; } =
        ItemBoardPreviewLayoutMode.Socketed;

    public bool ShowHover { get; init; } = true;

    public bool UseCanvasGroup { get; init; }

    public string LogComponent { get; init; } = "ItemBoardPreviewSurface";
}
