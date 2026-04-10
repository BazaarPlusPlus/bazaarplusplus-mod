#nullable enable
namespace BazaarPlusPlus.Core.Paths;

internal interface IPathService
{
    string? CardsJsonPath { get; }

    string? RunLogDatabasePath { get; }

    string? CombatReplayDirectoryPath { get; }

    string? ScreenshotsDirectoryPath { get; }

    string? IdentityDirectoryPath { get; }

    string? PlayerObservationPath { get; }

    string? InstallationRecordPath { get; }

    string? InstallationPrivateKeyPath { get; }

}
