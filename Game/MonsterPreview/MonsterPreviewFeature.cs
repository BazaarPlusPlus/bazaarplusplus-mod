#nullable enable
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal static class MonsterPreviewFeature
{
    public static bool IsEnabled => BppRuntimeHost.Config.EnableMonsterPreviewConfig?.Value ?? true;
}
