#nullable enable
using System.Runtime.InteropServices;

namespace BazaarPlusPlus.Infrastructure.Files;

internal static class PhysicalPathPolicy
{
    internal static StringComparison PathComparison =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    internal static bool IsBelowRoot(string normalizedRoot, string candidatePath)
    {
        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(prefix, PathComparison);
    }

    internal static string TrimEndingSeparators(string path)
    {
        var root = Path.GetPathRoot(path);
        while (
            path.Length > (root?.Length ?? 0)
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
}
