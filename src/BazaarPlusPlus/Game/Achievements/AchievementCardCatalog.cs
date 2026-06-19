#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.CustomCards;
using BazaarPlusPlus.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.Achievements;

internal sealed class AchievementCardCatalog
{
    private const int ExpectedSchemaVersion = 1;
    private const string ResourceSuffix = "achievement-cards.json";

    private readonly IReadOnlyList<AchievementCardDefinition> _cards;

    public AchievementCardCatalog(IReadOnlyList<AchievementCardDefinition> cards)
    {
        _cards = cards ?? throw new ArgumentNullException(nameof(cards));
    }

    public IReadOnlyList<AchievementCardDefinition> Cards => _cards;

    public static AchievementCardCatalog LoadEmbedded() => new(Build(ReadEmbeddedJson()));

    internal static IReadOnlyList<AchievementCardDefinition> Build(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Achievement card catalog JSON is empty.");

        var dto = JsonConvert.DeserializeObject<AchievementCardCatalogDto>(json);
        if (dto == null)
            throw new InvalidOperationException("Achievement card catalog JSON is empty.");
        if (dto.SchemaVersion != ExpectedSchemaVersion)
            throw new InvalidOperationException(
                $"Achievement card catalog schemaVersion must be {ExpectedSchemaVersion}; got {dto.SchemaVersion}."
            );
        if (dto.Cards == null)
            throw new InvalidOperationException("Achievement card catalog cards are missing.");

        var achievementIds = new HashSet<string>(StringComparer.Ordinal);
        var templateIds = new HashSet<Guid>();
        var result = new List<AchievementCardDefinition>(dto.Cards.Count);
        for (var i = 0; i < dto.Cards.Count; i++)
        {
            var card = dto.Cards[i];
            if (card == null)
                throw new InvalidOperationException($"Achievement card {i} is null.");

            var path = $"cards[{i}]";
            var achievementId = RequiredText(card.AchievementId, $"{path}.achievementId");
            if (!achievementIds.Add(achievementId))
                throw new InvalidOperationException(
                    $"Duplicate achievementId '{achievementId}' in achievement card catalog."
                );

            var templateId = ParseGuid(card.TemplateId, $"{path}.templateId");
            if (!templateIds.Add(templateId))
                throw new InvalidOperationException(
                    $"Duplicate templateId '{templateId}' in achievement card catalog."
                );

            var expectedTemplateId = BppCustomCardIds.ForAchievement(achievementId);
            if (templateId != expectedTemplateId)
                throw new InvalidOperationException(
                    $"{path}.templateId must be UUIDv5({BppCustomCardIds.Namespace}, achievement:{achievementId}); expected {expectedTemplateId}, got {templateId}."
                );
            if (card.RuleParams == null)
                throw Required($"{path}.ruleParams");

            result.Add(
                new AchievementCardDefinition
                {
                    AchievementId = achievementId,
                    TemplateId = templateId,
                    InternalName = RequiredText(card.InternalName, $"{path}.internalName"),
                    Title = ParseLocalizedText(card.Title, $"{path}.title"),
                    Description = ParseLocalizedText(card.Description, $"{path}.description"),
                    Category = RequiredText(card.Category, $"{path}.category"),
                    RuleKind = RequiredText(card.RuleKind, $"{path}.ruleKind"),
                    Target = card.Target ?? throw Required($"{path}.target"),
                    DisplayTier = ParseEnum<ETier>(card.DisplayTier, $"{path}.displayTier"),
                    DisplaySize = ParseEnum<ECardSize>(card.DisplaySize, $"{path}.displaySize"),
                    SortKey = card.SortKey ?? throw Required($"{path}.sortKey"),
                    HiddenUntilUnlocked =
                        card.HiddenUntilUnlocked ?? throw Required($"{path}.hiddenUntilUnlocked"),
                }
            );
        }

        return result.OrderBy(card => card.SortKey).ThenBy(card => card.InternalName).ToArray();
    }

    private static string ReadEmbeddedJson()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal));
        if (resourceName == null)
            throw new InvalidOperationException(
                $"Embedded achievement card catalog resource '*{ResourceSuffix}' was not found."
            );

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException(
                $"Embedded achievement card catalog resource '{resourceName}' could not be opened."
            );
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static LocalizedTextSet ParseLocalizedText(LocalizedTextDto? dto, string path)
    {
        if (dto == null)
            throw Required(path);
        var english = RequiredText(dto.En, $"{path}.en");
        var zhHans = RequiredText(dto.ZhHans, $"{path}.zhHans");
        var zhHant = RequiredText(dto.ZhHant, $"{path}.zhHant");
        return new LocalizedTextSet(english, zhHans, zhHant);
    }

    private static T ParseEnum<T>(string? value, string path)
        where T : struct
    {
        var text = RequiredText(value, path);
        if (Enum.TryParse<T>(text, ignoreCase: true, out var result))
            return result;
        throw new InvalidOperationException($"{path} has unsupported {typeof(T).Name} '{text}'.");
    }

    private static Guid ParseGuid(string? value, string path)
    {
        var text = RequiredText(value, path);
        if (Guid.TryParse(text, out var result))
            return result;
        throw new InvalidOperationException($"{path} must be a GUID; got '{text}'.");
    }

    private static string RequiredText(string? value, string path)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value!;
        throw Required(path);
    }

    private static InvalidOperationException Required(string path) => new($"{path} is required.");

    private sealed class AchievementCardCatalogDto
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonProperty("cards")]
        public List<AchievementCardDto?>? Cards { get; set; }
    }

    private sealed class AchievementCardDto
    {
        [JsonProperty("achievementId")]
        public string? AchievementId { get; set; }

        [JsonProperty("templateId")]
        public string? TemplateId { get; set; }

        [JsonProperty("internalName")]
        public string? InternalName { get; set; }

        [JsonProperty("title")]
        public LocalizedTextDto? Title { get; set; }

        [JsonProperty("description")]
        public LocalizedTextDto? Description { get; set; }

        [JsonProperty("category")]
        public string? Category { get; set; }

        [JsonProperty("ruleKind")]
        public string? RuleKind { get; set; }

        [JsonProperty("ruleParams")]
        public JObject? RuleParams { get; set; }

        [JsonProperty("target")]
        public int? Target { get; set; }

        [JsonProperty("displayTier")]
        public string? DisplayTier { get; set; }

        [JsonProperty("displaySize")]
        public string? DisplaySize { get; set; }

        [JsonProperty("sortKey")]
        public int? SortKey { get; set; }

        [JsonProperty("hiddenUntilUnlocked")]
        public bool? HiddenUntilUnlocked { get; set; }
    }

    private sealed class LocalizedTextDto
    {
        [JsonProperty("en")]
        public string? En { get; set; }

        [JsonProperty("zhHans")]
        public string? ZhHans { get; set; }

        [JsonProperty("zhHant")]
        public string? ZhHant { get; set; }
    }
}
