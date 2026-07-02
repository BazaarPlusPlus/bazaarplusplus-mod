#nullable enable

using BazaarPlusPlus.Infrastructure.UiTokens;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal sealed class ItemBoardPreviewOptions
{
    private const float DefaultSlotGridMaxHeightRatio =
        (
            ItemBoardSocketLayout.NativeSocketHeightPixels
            * ItemBoardSocketLayout.FrameHeightOverSocket
            / ItemBoardSocketLayout.NativeSocketPitchPixels
        )
        * (ItemBoardSocketLayout.NativeBoardWidth / (float)ItemBoardSocketLayout.SocketCount)
        / ItemBoardSocketLayout.NativeBoardHeight;

    // Stricter preset than the default (which reproduces the native board's designed ~4.9%
    // card-body touch, where frame borders visibly interleave): scale so the widest
    // frame-per-span (medium) exactly fills its two slots — silver borders never cross,
    // small cards get a slight gap. Large's decorative side flourish still overhangs by
    // native design. Consumers opt in per surface; ≈ 0.81692.
    public const float FrameSeparationSlotGridMaxHeightRatio =
        2f
        * (ItemBoardSocketLayout.NativeBoardWidth / (float)ItemBoardSocketLayout.SocketCount)
        * ItemBoardSocketLayout.FrameHeightOverSocket
        / (
            ItemBoardSocketLayout.NativeMediumFrameWidthOverRoot
            * ItemBoardSocketLayout.NativeMediumBodyAspect
            * ItemBoardSocketLayout.NativeBoardHeight
        );

    public int Layer { get; init; } = 30;

    public int SortingOrder { get; init; } = BppOverlaySorting.NativeCardPreview;

    public ItemBoardPreviewLayoutMode LayoutMode { get; init; } =
        ItemBoardPreviewLayoutMode.Socketed;

    public bool ShowHover { get; init; } = true;

    public bool UseCanvasGroup { get; init; }

    public string LogComponent { get; init; } = "ItemBoardPreviewSurface";

    public float SlotGridHorizontalInsetPixels { get; init; } = 8f;

    public float SlotGridVerticalInsetPixels { get; init; } = 6f;

    public float SlotGridMaxHeightRatio { get; init; } = DefaultSlotGridMaxHeightRatio;

    public float SlotGridMaxScale { get; init; } = 10f;
}
