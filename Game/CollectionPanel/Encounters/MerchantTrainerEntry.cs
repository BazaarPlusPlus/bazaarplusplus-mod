#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Encounters;

/// <summary>
/// A single curated merchant/trainer identity. One identity can map to several card-template ids
/// (e.g. "Aila" has 8 template-id variants), which is why the catalog is keyed by template id rather
/// than by display name.
/// </summary>
internal sealed class MerchantTrainerEntry
{
    public MerchantTrainerEntry(
        string sourceKey,
        string name,
        EncounterPortraitKind kind,
        string tier,
        IReadOnlyList<EHero> heroes,
        string description,
        IReadOnlyList<Guid> templateIds
    )
    {
        SourceKey = sourceKey;
        Name = name;
        Kind = kind;
        Tier = tier;
        Heroes = heroes;
        Description = description;
        TemplateIds = templateIds;
    }

    /// <summary>Stable UI/cache identity for this curated source entry.</summary>
    public string SourceKey { get; }

    public string Name { get; }

    public EncounterPortraitKind Kind { get; }

    /// <summary>Bronze/Silver/Gold/Diamond/Legendary, as labelled by the source roster.</summary>
    public string Tier { get; }

    /// <summary>
    /// Heroes this encounter appears for. An EMPTY list means Common — it appears for all heroes
    /// (mirrors the game's <c>Common</c>-or-empty hero predicate in BazaarCardDealer).
    /// </summary>
    public IReadOnlyList<EHero> Heroes { get; }

    /// <summary>Human label from the source roster, e.g. "Sells Crit items" / "Teaches Freeze skills".</summary>
    public string Description { get; }

    /// <summary>All card-template ids (GUIDs) that resolve to this merchant/trainer.</summary>
    public IReadOnlyList<Guid> TemplateIds { get; }

    /// <summary>True when this entry should be shown for <paramref name="hero"/> (empty Heroes == all).</summary>
    public bool AppliesToHero(EHero hero) => Heroes.Count == 0 || Heroes.Contains(hero);
}
