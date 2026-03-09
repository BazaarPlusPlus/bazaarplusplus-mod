#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Newtonsoft.Json;

namespace BazaarPlusPlus;

internal static class MonsterDatabase
{
    // Key: encounter card InternalName (e.g. "WildMonster_Troll_Day3")
    // Value: { items: [...], skills: [...] }
    private static Dictionary<string, MonsterEntry> _db = new Dictionary<string, MonsterEntry>();

    public class MonsterEntry
    {
        [JsonProperty("items")]
        public List<string> Items { get; set; } = new List<string>();

        [JsonProperty("skills")]
        public List<string> Skills { get; set; } = new List<string>();
    }

    public static void Load()
    {
        var path = GetPath();
        try
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "{}");
                ModState.Logger.LogInfo("[MonsterDatabase] Created empty monsters.json at " + path);
                return;
            }

            var json = File.ReadAllText(path);
            _db =
                JsonConvert.DeserializeObject<Dictionary<string, MonsterEntry>>(json)
                ?? new Dictionary<string, MonsterEntry>();
            ModState.Logger.LogInfo(
                $"[MonsterDatabase] Loaded {_db.Count} entries from monsters.json"
            );
        }
        catch (Exception ex)
        {
            ModState.Logger.LogError($"[MonsterDatabase] Failed to load: {ex.Message}");
            _db = new Dictionary<string, MonsterEntry>();
        }
    }

    /// <summary>Returns null if the encounter has no entry in the local database.</summary>
    public static MonsterEntry TryGet(string encounterInternalName)
    {
        if (string.IsNullOrEmpty(encounterInternalName))
            return null;
        _db.TryGetValue(encounterInternalName, out var entry);
        return entry;
    }

    private static string GetPath() =>
        Path.Combine(Paths.ConfigPath, "BazaarPlusPlus_monsters.json");
}
