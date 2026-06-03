#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Infrastructure.Fonts;

internal static class BppTmpFontPolicy
{
    public static bool ShouldUseEmbeddedCjkFont(string? text) => CjkDetection.ContainsCjk(text);
}
