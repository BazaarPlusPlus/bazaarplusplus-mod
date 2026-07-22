#nullable enable
using System.Runtime.InteropServices;

namespace BazaarPlusPlus.Infrastructure.Files;

internal enum ImmutableArtifactCommitResult
{
    Created,
    Reused,
}

/// <summary>
/// Durably publishes an immutable file below a caller-owned root. An existing byte-identical
/// object is reusable; an existing object with different bytes is a hard identity conflict.
/// </summary>
internal sealed class ImmutableArtifactCommitter
{
    private static readonly object[] CommitStripes = CreateCommitStripes();

    internal ImmutableArtifactCommitResult CommitBelowRoot(
        string rootDirectoryPath,
        string destinationFilePath,
        byte[] bytes
    )
    {
        if (bytes == null)
            throw new ArgumentNullException(nameof(bytes));

        var root = RequirePhysicalRoot(rootDirectoryPath);
        var destination = Path.GetFullPath(destinationFilePath);
        if (!IsBelowRoot(root, destination))
            throw new InvalidOperationException("Immutable artifact destination escaped its root.");

        lock (CommitStripes[
            (StringComparer.Ordinal.GetHashCode(destination) & 0x7fffffff) % CommitStripes.Length
        ])
            return CommitCore(root, destination, bytes);
    }

    private static ImmutableArtifactCommitResult CommitCore(
        string root,
        string destination,
        byte[] bytes
    )
    {
        var parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(parent))
            throw new ArgumentException(
                "Immutable artifact destination requires a parent directory.",
                nameof(destination)
            );

        EnsurePhysicalDirectoryChain(root, parent);
        if (File.Exists(destination))
            return VerifyExisting(destination, bytes);

        var tempPath = Path.Combine(
            parent,
            "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp"
        );
        try
        {
            using (
                var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.SequentialScan
                )
            )
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            // Recheck immediately before publishing. This prevents a pre-existing link from being
            // mistaken for a valid immutable object and makes a same-process directory swap fail.
            EnsurePhysicalDirectoryChain(root, parent);
            if (File.Exists(destination))
                return VerifyExisting(destination, bytes);

            try
            {
                File.Move(tempPath, destination);
                tempPath = string.Empty;
                return ImmutableArtifactCommitResult.Created;
            }
            catch (IOException) when (File.Exists(destination))
            {
                return VerifyExisting(destination, bytes);
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static object[] CreateCommitStripes()
    {
        var stripes = new object[256];
        for (var index = 0; index < stripes.Length; index++)
            stripes[index] = new object();
        return stripes;
    }

    private static ImmutableArtifactCommitResult VerifyExisting(string filePath, byte[] expected)
    {
        RequirePhysicalFile(filePath);
        var actual = File.ReadAllBytes(filePath);
        if (!ByteArraysEqual(actual, expected))
        {
            throw new IOException(
                "An immutable artifact already exists at this identity with different bytes."
            );
        }

        return ImmutableArtifactCommitResult.Reused;
    }

    private static string RequirePhysicalRoot(string rootDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(rootDirectoryPath))
            throw new ArgumentException(
                "An immutable artifact root is required.",
                nameof(rootDirectoryPath)
            );

        var root = TrimEndingSeparators(Path.GetFullPath(rootDirectoryPath));
        Directory.CreateDirectory(root);
        RequirePhysicalDirectory(root);
        return root;
    }

    private static void EnsurePhysicalDirectoryChain(string root, string directoryPath)
    {
        var directory = TrimEndingSeparators(Path.GetFullPath(directoryPath));
        if (!string.Equals(root, directory, PathComparison) && !IsBelowRoot(root, directory))
            throw new InvalidOperationException("Immutable artifact directory escaped its root.");

        RequirePhysicalDirectory(root);
        if (string.Equals(root, directory, PathComparison))
            return;

        var relative = directory
            .Substring(root.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = root;
        foreach (
            var segment in relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current))
            {
                try
                {
                    Directory.CreateDirectory(current);
                }
                catch (IOException) when (Directory.Exists(current))
                {
                    // A concurrent creator won. It is validated below before use.
                }
            }
            RequirePhysicalDirectory(current);
        }
    }

    private static void RequirePhysicalDirectory(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException("Immutable artifact directory is unavailable.");

        var attributes = File.GetAttributes(path);
        if (
            (attributes & FileAttributes.Directory) == 0
            || (attributes & FileAttributes.ReparsePoint) != 0
        )
        {
            throw new IOException(
                "Immutable artifact directory cannot be a link or reparse point."
            );
        }
    }

    private static void RequirePhysicalFile(string path)
    {
        var attributes = File.GetAttributes(path);
        if (
            (attributes & FileAttributes.Directory) != 0
            || (attributes & FileAttributes.ReparsePoint) != 0
        )
        {
            throw new IOException(
                "Immutable artifact cannot be a directory, link, or reparse point."
            );
        }
    }

    private static bool ByteArraysEqual(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
            return false;

        var difference = 0;
        for (var index = 0; index < left.Length; index++)
            difference |= left[index] ^ right[index];
        return difference == 0;
    }

    private static bool IsBelowRoot(string root, string candidatePath)
    {
        var prefix = root + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(prefix, PathComparison);
    }

    private static string TrimEndingSeparators(string path)
    {
        var pathRoot = Path.GetPathRoot(path);
        while (
            path.Length > (pathRoot?.Length ?? 0)
            && (
                path[path.Length - 1] == Path.DirectorySeparatorChar
                || path[path.Length - 1] == Path.AltDirectorySeparatorChar
            )
        )
        {
            path = path.Substring(0, path.Length - 1);
        }

        return path;
    }

    private static StringComparison PathComparison =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
