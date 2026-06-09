#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BazaarPlusPlus.Game.CardArtReplacement;

internal sealed class CustomCardArtCatalog
{
    private readonly Dictionary<Guid, string> _pathsByTemplateId = new();

    public CustomCardArtCatalog(string? directoryPath)
    {
        DirectoryPath = directoryPath;
        Refresh();
    }

    public string? DirectoryPath { get; }

    public int Count => _pathsByTemplateId.Count;

    public void Refresh()
    {
        _pathsByTemplateId.Clear();
        if (string.IsNullOrWhiteSpace(DirectoryPath) || !Directory.Exists(DirectoryPath))
            return;

        // Ordinal-sorted + first-wins so resolution is deterministic when both
        // <id>.jpg and <id>.png exist: ".jpg" sorts before ".png", so the shipped
        // .jpg wins over a stale .png left over from an earlier build. To override
        // art, replace the file in place rather than adding a second extension.
        foreach (var path in Directory.EnumerateFiles(DirectoryPath).OrderBy(p => p, StringComparer.Ordinal))
        {
            if (!CustomCardArtImageFormats.TryGetTemplateId(path, out var templateId))
                continue;

            _pathsByTemplateId.TryAdd(templateId, path);
        }
    }

    public bool TryGetArtPath(Guid templateId, out string path)
    {
        if (templateId != Guid.Empty && _pathsByTemplateId.TryGetValue(templateId, out path!))
            return true;

        path = string.Empty;
        return false;
    }
}
