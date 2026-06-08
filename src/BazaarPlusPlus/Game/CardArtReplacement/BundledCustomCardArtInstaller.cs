#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BazaarPlusPlus.Game.CardArtReplacement;

internal sealed class BundledCustomCardArtInstaller
{
    private const string ResourcePrefix = "BazaarPlusPlus.Resources.CustomCardArt.";
    private const string PngExtension = ".png";

    private readonly Func<IReadOnlyList<string>> _resourceNames;
    private readonly Func<string, Stream?> _openResource;

    public BundledCustomCardArtInstaller()
        : this(
            () => Assembly.GetExecutingAssembly().GetManifestResourceNames(),
            name => Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ) { }

    internal BundledCustomCardArtInstaller(
        Func<IReadOnlyList<string>> resourceNames,
        Func<string, Stream?> openResource
    )
    {
        _resourceNames = resourceNames ?? throw new ArgumentNullException(nameof(resourceNames));
        _openResource = openResource ?? throw new ArgumentNullException(nameof(openResource));
    }

    public BundledCustomCardArtInstallResult InstallMissing(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("Directory path is required.", nameof(directoryPath));

        Directory.CreateDirectory(directoryPath);

        var result = new BundledCustomCardArtInstallResult();
        foreach (var resourceName in FindCustomArtResources())
        {
            result.ResourceCount++;
            var fileName = ResourceNameToFileName(resourceName);
            if (!IsTemplateIdPngFileName(fileName))
            {
                result.FailedCount++;
                continue;
            }

            var targetPath = Path.Combine(directoryPath, fileName);
            if (File.Exists(targetPath))
            {
                result.ExistingCount++;
                continue;
            }

            if (TryWriteResource(resourceName, targetPath))
                result.WrittenCount++;
            else
                result.FailedCount++;
        }

        return result;
    }

    private IEnumerable<string> FindCustomArtResources() =>
        _resourceNames()
            .Where(name =>
                name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(PngExtension, StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(name => name, StringComparer.Ordinal);

    private static string ResourceNameToFileName(string resourceName) =>
        resourceName.Substring(ResourcePrefix.Length);

    private static bool IsTemplateIdPngFileName(string fileName)
    {
        if (
            !string.Equals(
                Path.GetExtension(fileName),
                PngExtension,
                StringComparison.OrdinalIgnoreCase
            )
        )
            return false;

        return Guid.TryParse(Path.GetFileNameWithoutExtension(fileName), out var templateId)
            && templateId != Guid.Empty;
    }

    private bool TryWriteResource(string resourceName, string targetPath)
    {
        var tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using var resource = _openResource(resourceName);
            if (resource == null)
                return false;

            using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write))
            {
                resource.CopyTo(output);
            }

            if (File.Exists(targetPath))
            {
                File.Delete(tempPath);
                return true;
            }

            File.Move(tempPath, targetPath);
            return true;
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best-effort cleanup; startup should not fail because a temp file remains.
            }

            return false;
        }
    }
}

internal struct BundledCustomCardArtInstallResult
{
    public int ResourceCount;
    public int WrittenCount;
    public int ExistingCount;
    public int FailedCount;
}
