using BepInEx.Logging;

namespace BazaarPlusPlus;

internal static class ModState
{
    public static ManualLogSource? Logger { get; set; }
    public static string? CardsJsonPath { get; set; }
}
