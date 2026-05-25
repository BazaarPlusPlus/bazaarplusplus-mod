#nullable enable
namespace BazaarPlusPlus.Storage.Paths;

public interface IPathProvider
{
    string? RunLogDatabasePath { get; }

    string? CombatReplayDirectoryPath { get; }

    string? ScreenshotsDirectoryPath { get; }

    string? IdentityDirectoryPath { get; }

    string? CombatReplayVideoDirectoryPath { get; }

    string? ToolsDirectoryPath { get; }
}
