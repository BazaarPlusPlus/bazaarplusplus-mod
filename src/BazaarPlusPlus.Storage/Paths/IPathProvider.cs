#nullable enable
namespace BazaarPlusPlus.Storage.Paths;

public interface IPathProvider
{
    string? DataRootDirectoryPath { get; }

    string? RunLogDatabasePath { get; }

    string? CombatReplayDirectoryPath { get; }

    string? ScreenshotsDirectoryPath { get; }

    string? CombatReplayVideoDirectoryPath { get; }

    string? PluginsDirectoryPath { get; }
}
