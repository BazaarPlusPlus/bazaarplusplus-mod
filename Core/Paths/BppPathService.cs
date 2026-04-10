#nullable enable
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;

namespace BazaarPlusPlus.Core.Paths;

internal sealed class BppPathService : IPathService
{
    public string? CardsJsonPath { get; private set; }

    public string? RunLogDatabasePath { get; private set; }

    public string? CombatReplayDirectoryPath { get; private set; }

    public string? ScreenshotsDirectoryPath { get; private set; }

    public string? IdentityDirectoryPath { get; private set; }

    public string? PlayerObservationPath { get; private set; }

    public string? InstallationRecordPath { get; private set; }

    public string? InstallationPrivateKeyPath { get; private set; }

    public void Initialize()
    {
        CardsJsonPath = CardJsonPathResolver.GetCardsJsonPath();
        RunLogDatabasePath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlus",
            RunLogSqliteSchema.DatabaseFileName
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
        PlayerObservationPath = System.IO.Path.Combine(
            IdentityDirectoryPath,
            "player-observation.bpp"
        );
        InstallationRecordPath = System.IO.Path.Combine(
            IdentityDirectoryPath,
            "installation.bpp"
        );
        InstallationPrivateKeyPath = System.IO.Path.Combine(
            IdentityDirectoryPath,
            "installation.key"
        );
    }
}
