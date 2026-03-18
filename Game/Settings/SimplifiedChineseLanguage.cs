using System;

namespace BazaarPlusPlus.Game.Settings;

internal static class SimplifiedChineseLanguage
{
    internal static bool Matches(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return false;

        return string.Equals(languageCode, "zh-Hans", StringComparison.OrdinalIgnoreCase)
            || string.Equals(languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase);
    }
}
