#nullable enable

namespace BazaarPlusPlus.Infrastructure.UiTokens;

internal static class BppOverlaySorting
{
    public const int PanelUiToolkit = 26;
    public const int NativeCardPreview = 27;
    public const int PanelForeground = 28;

    // Game screen canvases reserve 100 for always-on-top UI; BPP-extended native
    // tooltips need to clear fixed screen panels while the preview text is visible.
    public const int NativeTooltipForeground = 101;
}
