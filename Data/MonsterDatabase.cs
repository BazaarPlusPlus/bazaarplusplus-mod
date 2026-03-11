#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace BazaarPlusPlus;

internal static class MonsterDatabase
{
    private static readonly Dictionary<Guid, MonsterInfo> _db = new Dictionary<Guid, MonsterInfo>();

    private sealed class MonsterRecordDto
    {
        [JsonProperty("encounter_id")]
        public string EncounterId { get; set; } = string.Empty;

        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("base_tier")]
        public string BaseTier { get; set; } = string.Empty;

        [JsonProperty("rewards")]
        public MonsterRewardsDto Rewards { get; set; }

        [JsonProperty("combatant")]
        public MonsterCombatantDto Combatant { get; set; }

        [JsonProperty("monster_metadata")]
        public MonsterMetadataDto MonsterMetadata { get; set; }
    }

    private sealed class MonsterRewardsDto
    {
        [JsonProperty("gold")]
        public int? Gold { get; set; }

        [JsonProperty("xp")]
        public int? Xp { get; set; }
    }

    private sealed class MonsterCombatantDto
    {
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("level")]
        public int? Level { get; set; }
    }

    private sealed class MonsterMetadataDto
    {
        [JsonProperty("available")]
        public string Available { get; set; } = string.Empty;

        [JsonProperty("day")]
        public int? Day { get; set; }

        [JsonProperty("health")]
        public int? Health { get; set; }

        [JsonProperty("board")]
        public List<MonsterBoardCardDto> Board { get; set; } = new List<MonsterBoardCardDto>();

        [JsonProperty("skills")]
        public List<MonsterSkillDto> Skills { get; set; } = new List<MonsterSkillDto>();
    }

    private sealed class MonsterBoardCardDto
    {
        [JsonProperty("cardid")]
        public string CardId { get; set; } = string.Empty;

        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("tier")]
        public string Tier { get; set; } = string.Empty;

        [JsonProperty("size")]
        public string Size { get; set; } = string.Empty;

        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
    }

    private sealed class MonsterSkillDto
    {
        [JsonProperty("skill_id")]
        public string SkillId { get; set; } = string.Empty;

        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("tier")]
        public string Tier { get; set; } = string.Empty;

        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
    }

    public static void Load()
    {
        var path = GetPath();
        try
        {
            if (!File.Exists(path))
            {
                ModState.Logger.LogWarning("[MonsterDatabase] Missing monster database at " + path);
                return;
            }

            var json = File.ReadAllText(path);
            var raw =
                JsonConvert.DeserializeObject<Dictionary<string, MonsterRecordDto>>(json)
                ?? new Dictionary<string, MonsterRecordDto>();

            _db.Clear();
            foreach (var pair in raw)
            {
                if (!Guid.TryParse(pair.Key, out var encounterId))
                    continue;

                var monster = MapMonster(encounterId, pair.Value);
                if (monster != null)
                    _db[encounterId] = monster;
            }

            ModState.Logger.LogInfo(
                $"[MonsterDatabase] Loaded {_db.Count} entries from monsters_bazaardb.json"
            );
        }
        catch (Exception ex)
        {
            ModState.Logger.LogError($"[MonsterDatabase] Failed to load: {ex.Message}");
            _db.Clear();
        }
    }

    public static bool TryGetByEncounterId(Guid encounterId, out MonsterInfo monster)
    {
        return _db.TryGetValue(encounterId, out monster);
    }

    public static LegacyMonsterEntry TryGet(string encounterInternalName)
    {
        return null;
    }

    public static IReadOnlyCollection<MonsterInfo> GetAll()
    {
        return _db.Values.ToList();
    }

    internal sealed class LegacyMonsterEntry
    {
        public List<string> Items { get; set; }

        public List<string> Skills { get; set; }
    }

    private static MonsterInfo MapMonster(Guid encounterId, MonsterRecordDto dto)
    {
        if (dto == null)
            return null;

        return new MonsterInfo
        {
            EncounterId = encounterId,
            Title = dto.Title ?? string.Empty,
            BaseTier = dto.BaseTier ?? string.Empty,
            CombatLevel = dto.Combatant?.Level,
            Health = dto.MonsterMetadata?.Health,
            RewardGold = dto.Rewards?.Gold,
            RewardXp = dto.Rewards?.Xp,
            BoardCards = dto.MonsterMetadata?.Board?.Select(MapBoardCard).Where(card => card != null).ToList()
                ?? new List<MonsterBoardCardInfo>(),
            Skills = dto.MonsterMetadata?.Skills?.Select(MapSkill).Where(skill => skill != null).ToList()
                ?? new List<MonsterSkillInfo>(),
        };
    }

    private static MonsterBoardCardInfo MapBoardCard(MonsterBoardCardDto dto)
    {
        if (dto == null || !Guid.TryParse(dto.CardId, out var cardId))
            return null;

        return new MonsterBoardCardInfo
        {
            CardId = cardId,
            Title = dto.Title ?? string.Empty,
            Tier = dto.Tier ?? string.Empty,
            Size = dto.Size ?? string.Empty,
            Type = dto.Type ?? string.Empty,
        };
    }

    private static MonsterSkillInfo MapSkill(MonsterSkillDto dto)
    {
        if (dto == null || !Guid.TryParse(dto.SkillId, out var skillId))
            return null;

        return new MonsterSkillInfo
        {
            SkillId = skillId,
            Title = dto.Title ?? string.Empty,
            Tier = dto.Tier ?? string.Empty,
            Type = dto.Type ?? string.Empty,
        };
    }

    private static string GetPath()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        return Path.Combine(projectRoot, "Data", "monsters_bazaardb.json");
    }
}
