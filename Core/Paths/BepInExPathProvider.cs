#nullable enable
using BazaarPlusPlus.Storage.Paths;

namespace BazaarPlusPlus.Core.Paths;

internal sealed class BepInExPathProvider : IPathProvider
{
    public string? RunLogDatabasePath { get; private set; }

    public string? CombatReplayDirectoryPath { get; private set; }

    public string? ScreenshotsDirectoryPath { get; private set; }

    public string? IdentityDirectoryPath { get; private set; }

    public string? CombatReplayVideoDirectoryPath { get; private set; }

    public string? ToolsDirectoryPath { get; private set; }

    public void Initialize()
    {
        RunLogDatabasePath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlus",
            PathConstants.RunLogDatabaseFileName
        );
        CombatReplayDirectoryPath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlus",
            "CombatReplays"
        );
        ScreenshotsDirectoryPath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlus",
            "Screenshots"
        );
        IdentityDirectoryPath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlus",
            "Identity"
        );
        CombatReplayVideoDirectoryPath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlus",
            "CombatReplayVideos"
        );
        ToolsDirectoryPath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlus",
            "tools"
        );
    }
}
