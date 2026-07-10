#nullable enable

namespace BazaarPlusPlus.Game.Settings;

internal static class BppSettingsDockGeometry
{
    internal static float CalculatePanelLocalScale(
        float targetOnScreenScale,
        float cloneLocalScale
    ) => cloneLocalScale > 0.0001f ? targetOnScreenScale / cloneLocalScale : targetOnScreenScale;

    internal static bool ShouldSyncForScreenSize(
        int lastWidth,
        int lastHeight,
        int currentWidth,
        int currentHeight
    ) => lastWidth != currentWidth || lastHeight != currentHeight;
}
