#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus;

internal static class LocalCardTemplateCatalog
{
    private static readonly Lazy<HashSet<Guid>> TemplateIds = new(LoadTemplateIds);

    public static bool Contains(Guid templateId)
    {
        return templateId != Guid.Empty && TemplateIds.Value.Contains(templateId);
    }

    private static HashSet<Guid> LoadTemplateIds()
    {
        var path = ModState.CardsJsonPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new HashSet<Guid>();

        var root = JObject.Parse(File.ReadAllText(path));
        var versionNode = root["5.0.0"] as JArray ?? root.Properties().FirstOrDefault()?.Value as JArray;
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
