using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus;

internal static class ItemDatabase
{
    public static void Load()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream("BazaarPlusPlus.Data.items.js"))
            using (var reader = new StreamReader(stream))
            {
                string content = reader.ReadToEnd();
                content = content.Replace("export const items =", "").Trim().TrimEnd(';');

                var items = JObject.Parse(content);
                ModState.BaseItemTags = items
                    .Properties()
                    .ToDictionary(
                        prop => prop.Name,
                        prop => prop.Value["tags"].Select(t => t.ToString()).ToList()
                    );

                ModState.Logger.LogInfo(
                    $"Loaded tags for {ModState.BaseItemTags.Count} base items"
                );
            }
        }
        catch (Exception ex)
        {
            ModState.Logger.LogError($"Failed to load base items: {ex.Message}");
            ModState.BaseItemTags = new Dictionary<string, List<string>>();
        }
    }

    public static bool HasNewTags(string itemName, List<string> currentTags)
    {
        if (!ModState.BaseItemTags.TryGetValue(itemName, out var baseTags))
            return true;

        if (currentTags == null || currentTags.Count == 0)
            return false;

        return currentTags.Any(tag => !baseTags.Contains(tag));
    }
}
