#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

internal sealed class CollectionSourceCatalogDto
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonProperty("groups")]
    public List<string>? Groups { get; set; }

    [JsonProperty("entries")]
    public List<CollectionSourceEntryDto>? Entries { get; set; }
}

internal sealed class CollectionSourceEntryDto
{
    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("kind")]
    public string? Kind { get; set; }

    [JsonProperty("group")]
    public string? Group { get; set; }

    [JsonProperty("order")]
    public int? Order { get; set; }

    [JsonProperty("availableHeroes")]
    public List<string>? AvailableHeroes { get; set; }

    [JsonProperty("description")]
    public string? Description { get; set; }

    [JsonProperty("portraitTemplateId")]
    public string? PortraitTemplateId { get; set; }

    [JsonProperty("sourceTemplateIds")]
    public List<string>? SourceTemplateIds { get; set; }

    [JsonProperty("offerRule")]
    public CollectionSourceOfferRuleDto? OfferRule { get; set; }
}

internal sealed class CollectionSourceOfferRuleDto
{
    [JsonProperty("heroMode")]
    public string? HeroMode { get; set; }

    [JsonProperty("hero")]
    public string? Hero { get; set; }

    [JsonProperty("startingTier")]
    public CollectionSourceStartingTierRuleDto? StartingTier { get; set; }

    [JsonProperty("sizesAny")]
    public List<string>? SizesAny { get; set; }

    [JsonProperty("tagsAny")]
    public List<string>? TagsAny { get; set; }

    [JsonProperty("tagsNone")]
    public List<string>? TagsNone { get; set; }

    [JsonProperty("hiddenTagsAny")]
    public List<string>? HiddenTagsAny { get; set; }

    [JsonProperty("enchantableOnly")]
    public bool EnchantableOnly { get; set; }
}

internal sealed class CollectionSourceStartingTierRuleDto
{
    [JsonProperty("mode")]
    public string? Mode { get; set; }

    [JsonProperty("tier")]
    public string? Tier { get; set; }
}
