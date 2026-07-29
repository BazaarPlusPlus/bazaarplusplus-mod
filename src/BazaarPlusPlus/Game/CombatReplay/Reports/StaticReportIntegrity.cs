#nullable enable
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.Infrastructure.Files;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

internal static class StaticReportIntegrity
{
    public static string Sha256(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return Sha256(stream);
    }

    public static string Sha256File(string filePath)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.SequentialScan
        );
        return Sha256(stream);
    }

    public static string Sha256Utf8(string value) => Sha256(Encoding.UTF8.GetBytes(value));

    private static string Sha256(Stream stream)
    {
        using var algorithm = SHA256.Create();
        var digest = algorithm.ComputeHash(stream);
        var builder = new StringBuilder(digest.Length * 2);
        for (var index = 0; index < digest.Length; index++)
            builder.Append(digest[index].ToString("x2"));
        return builder.ToString();
    }
}

internal static class ReportPhysicalFile
{
    public static void RequireBelowRoot(
        string rootDirectoryPath,
        string filePath,
        string description
    )
    {
        if (string.IsNullOrWhiteSpace(rootDirectoryPath))
            throw new ArgumentException(
                "A physical file root is required.",
                nameof(rootDirectoryPath)
            );
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("A physical file path is required.", nameof(filePath));

        var root = PhysicalPathPolicy.TrimEndingSeparators(Path.GetFullPath(rootDirectoryPath));
        var candidate = Path.GetFullPath(filePath);
        if (!PhysicalPathPolicy.IsBelowRoot(root, candidate))
            throw new ArtifactPublicationException(
                ArtifactPublicationFailureKind.PathEscapesRoot,
                description + " escaped its allowed root."
            );

        RequireDirectory(root, description + " root");
        var relative = candidate
            .Substring(root.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var segments = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries
        );
        if (segments.Length == 0)
            throw new ArtifactPublicationException(
                ArtifactPublicationFailureKind.InvalidDestinationType,
                description + " must be a file below its allowed root."
            );

        var current = root;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            current = Path.Combine(current, segments[index]);
            RequireDirectory(current, description + " directory");
        }

        Require(candidate, description);
    }

    public static void Require(string filePath, string description)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException(description + " does not exist.", filePath);

        var attributes = File.GetAttributes(filePath);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new ArtifactPublicationException(
                ArtifactPublicationFailureKind.SymbolicLinkOrReparsePoint,
                description + " must be a physical file, not a symbolic link."
            );
        if ((attributes & FileAttributes.Directory) != 0)
            throw new ArtifactPublicationException(
                ArtifactPublicationFailureKind.InvalidDestinationType,
                description + " must be a file."
            );
    }

    private static void RequireDirectory(string directoryPath, string description)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException(description + " does not exist.");

        var attributes = File.GetAttributes(directoryPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArtifactPublicationException(
                ArtifactPublicationFailureKind.SymbolicLinkOrReparsePoint,
                description + " must be a physical directory."
            );
        }
        if ((attributes & FileAttributes.Directory) == 0)
            throw new ArtifactPublicationException(
                ArtifactPublicationFailureKind.InvalidDestinationType,
                description + " must be a directory."
            );
    }
}
