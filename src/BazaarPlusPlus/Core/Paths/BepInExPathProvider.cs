#nullable enable
using BazaarPlusPlus.Storage.Paths;

namespace BazaarPlusPlus.Core.Paths;

internal sealed class BepInExPathProvider : IPathProvider
{
    public string? DataRootDirectoryPath { get; private set; }

    public string? RunLogDatabasePath { get; private set; }

    public string? CombatReplayDirectoryPath { get; private set; }

    public string? ScreenshotsDirectoryPath { get; private set; }

    public string? CombatReplayVideoDirectoryPath { get; private set; }

    public string? PluginsDirectoryPath { get; private set; }

    public void Initialize()
    {
        var dataRootDirectoryPath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlusV4"
        );
        DataRootDirectoryPath = dataRootDirectoryPath;
        RunLogDatabasePath = System.IO.Path.Combine(
            dataRootDirectoryPath,
            PathConstants.RunLogDatabaseFileName
        );
        CombatReplayDirectoryPath = System.IO.Path.Combine(dataRootDirectoryPath, "CombatReplays");
        ScreenshotsDirectoryPath = System.IO.Path.Combine(dataRootDirectoryPath, "Screenshots");
        CombatReplayVideoDirectoryPath = System.IO.Path.Combine(
            dataRootDirectoryPath,
            "CombatReplayVideos"
        );
        PluginsDirectoryPath = BepInEx.Paths.PluginPath;
    }
}
