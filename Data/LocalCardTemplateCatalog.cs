#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BazaarPlusPlus.Core.Runtime;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus;

internal static class LocalCardTemplateCatalog
{
    private static readonly object SyncRoot = new();
    private static HashSet<Guid> _templateIds = new();
    private static string? _loadedPath;

    public static bool Contains(Guid templateId)
    {
        return templateId != Guid.Empty && EnsureLoaded() && _templateIds.Contains(templateId);
    }

    internal static bool Warm()
    {
        return EnsureLoaded();
    }

    internal static void ResetForTests()
    {
        lock (SyncRoot)
        {
            _templateIds = new HashSet<Guid>();
            _loadedPath = null;
        }
    }

    private static bool EnsureLoaded()
    {
        var path = BppRuntimeHost.Paths.CardsJsonPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        if (string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
            return true;

        lock (SyncRoot)
        {
            if (string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
                return true;

            try
            {
                _templateIds = LoadTemplateIds(path);
                _loadedPath = path;
                return true;
            }
            catch (Exception ex)
            {
                BppLog.Error(
                    "LocalCardTemplateCatalog",
                    $"Failed to load local card template catalog from '{path}'",
                    ex
                );
                return false;
            }
        }
    }

    private static HashSet<Guid> LoadTemplateIds(string path)
    {
        var root = JObject.Parse(File.ReadAllText(path));
        var versionNode =
            root["5.0.0"] as JArray ?? root.Properties().FirstOrDefault()?.Value as JArray;
        if (versionNode == null)
            return new HashSet<Guid>();

        var result = new HashSet<Guid>();
        foreach (var token in versionNode.OfType<JObject>())
        {
            var idText = token.Value<string>("Id");
            if (Guid.TryParse(idText, out var templateId))
                result.Add(templateId);
        }

        return result;
    }
}
