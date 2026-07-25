#nullable enable
using System.Runtime.InteropServices;

namespace BazaarPlusPlus.Infrastructure.Files;

internal enum ReplaceableArtifactPublishResult
{
    Created,
    Reused,
    Replaced,
}

/// <summary>
/// Atomically publishes a replaceable file below a caller-owned physical root. Existing identical
/// bytes are reused; different bytes are replaced without allowing links in the destination chain.
/// </summary>
internal sealed class ReplaceableArtifactPublisher
{
    private static readonly object[] PublishStripes = CreatePublishStripes();
    private readonly Action<string, string> _moveFile;

    internal ReplaceableArtifactPublisher()
        : this((source, destination) => File.Move(source, destination)) { }

    internal ReplaceableArtifactPublisher(Action<string, string> moveFile)
    {
        _moveFile = moveFile ?? throw new ArgumentNullException(nameof(moveFile));
    }

    internal ReplaceableArtifactPublishResult PublishBelowRoot(
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
            throw new InvalidOperationException(
                "Replaceable artifact destination escaped its root."
            );

        lock (PublishStripes[
            (StringComparer.Ordinal.GetHashCode(destination) & 0x7fffffff) % PublishStripes.Length
        ])
            return PublishCore(root, destination, bytes);
    }

    private ReplaceableArtifactPublishResult PublishCore(
        string root,
        string destination,
        byte[] bytes
    )
    {
        var parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(parent))
            throw new ArgumentException(
                "Replaceable artifact destination requires a parent directory.",
                nameof(destination)
            );

        EnsurePhysicalDirectoryChain(root, parent);
        if (File.Exists(destination))
        {
            RequirePhysicalFile(destination);
            if (ByteArraysEqual(File.ReadAllBytes(destination), bytes))
                return ReplaceableArtifactPublishResult.Reused;
        }

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

            EnsurePhysicalDirectoryChain(root, parent);
            if (File.Exists(destination))
            {
                RequirePhysicalFile(destination);
                if (ByteArraysEqual(File.ReadAllBytes(destination), bytes))
                    return ReplaceableArtifactPublishResult.Reused;

                File.Replace(tempPath, destination, null, ignoreMetadataErrors: true);
                tempPath = string.Empty;
                return ReplaceableArtifactPublishResult.Replaced;
            }

            try
            {
                _moveFile(tempPath, destination);
                tempPath = string.Empty;
                return ReplaceableArtifactPublishResult.Created;
            }
            catch (IOException) when (File.Exists(destination))
            {
                // Another process can win after the existence check but before the atomic move.
                // Revalidate the winner before either reusing it or replacing it with our fully
                // flushed temporary file.
                RequirePhysicalFile(destination);
                if (ByteArraysEqual(File.ReadAllBytes(destination), bytes))
                    return ReplaceableArtifactPublishResult.Reused;

                File.Replace(tempPath, destination, null, ignoreMetadataErrors: true);
                tempPath = string.Empty;
                return ReplaceableArtifactPublishResult.Replaced;
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static object[] CreatePublishStripes()
    {
        var stripes = new object[256];
        for (var index = 0; index < stripes.Length; index++)
            stripes[index] = new object();
        return stripes;
    }

    private static string RequirePhysicalRoot(string rootDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(rootDirectoryPath))
            throw new ArgumentException(
                "A replaceable artifact root is required.",
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
            throw new InvalidOperationException("Replaceable artifact directory escaped its root.");

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
            throw new DirectoryNotFoundException("Replaceable artifact directory is unavailable.");

        var attributes = File.GetAttributes(path);
        if (
            (attributes & FileAttributes.Directory) == 0
            || (attributes & FileAttributes.ReparsePoint) != 0
        )
        {
            throw new IOException(
                "Replaceable artifact directory cannot be a link or reparse point."
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
                "Replaceable artifact cannot be a directory, link, or reparse point."
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
