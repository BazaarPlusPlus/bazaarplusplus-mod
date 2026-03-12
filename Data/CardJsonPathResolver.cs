#nullable enable
using System;
using System.Runtime.InteropServices;

namespace BazaarPlusPlus;

internal static class CardJsonPathResolver
{
    public static string? GetCardsJsonPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return GetCardsJsonPath("mac");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetCardsJsonPath("windows");

        return null;
    }

    internal static string? GetCardsJsonPath(string platform)
    {
        if (string.Equals(platform, "mac", StringComparison.OrdinalIgnoreCase))
            return "/Users/yxinyu/codes/BazaarPlusPlus/cards.json";

        if (string.Equals(platform, "windows", StringComparison.OrdinalIgnoreCase))
            return @"C:\Users\yxinyu\codes\BazaarPlusPlus\cards.json";

        return null;
    }
}
