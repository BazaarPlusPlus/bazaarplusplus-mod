#nullable enable
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal static class MonsterPreviewFeature
{
    public static bool IsEnabled => BppRuntimeHost.Config.EnableMonsterPreviewConfig?.Value ?? true;

    public static bool UseNativePreview =>
        BppRuntimeHost.Config.UseNativeMonsterPreviewConfig?.Value ?? false;

    public static bool UseCustomLivePreview => IsEnabled && !UseNativePreview;
}
