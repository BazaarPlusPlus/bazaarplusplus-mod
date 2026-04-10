#nullable enable
using System;
using System.IO;

namespace BazaarPlusPlus.Game.Identity;

internal sealed class IdentityPaths
{
    private IdentityPaths(string rootDirectoryPath)
    {
        RootDirectoryPath = rootDirectoryPath;
        PlayerObservationPath = Path.Combine(rootDirectoryPath, "player-observation.bpp");
        InstallationRecordPath = Path.Combine(rootDirectoryPath, "installation.bpp");
        InstallationPrivateKeyPath = Path.Combine(rootDirectoryPath, "installation.key");
    }

    public string RootDirectoryPath { get; }

    public string PlayerObservationPath { get; }

    public string InstallationRecordPath { get; }

    public string InstallationPrivateKeyPath { get; }

    public static IdentityPaths? TryCreate(string? gameRootPath)
    {
        if (string.IsNullOrWhiteSpace(gameRootPath))
            return null;

        return new IdentityPaths(Path.Combine(gameRootPath.Trim(), "BazaarPlusPlus", "Identity"));
    }
}
