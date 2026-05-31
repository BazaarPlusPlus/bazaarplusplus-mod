#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Centralizes the catalog/filter facts that are not direct UI selections. Merchant
// filtering is intentionally fed by facts on the VM so future hand-authored rules can
// live here instead of leaking into the panel view or the filter engine.
internal static class CollectionCardClassifier
{
    private static readonly string[] PackageNameMarkers = { "Package" };
    private static readonly string[] NonCatalogNameMarkers = { "[DEBUG]", "[TEMPLATE]" };

    private static readonly Dictionary<Guid, CollectionMerchantKind[]> ManualMerchantIdRules =
        new();

    private static readonly Dictionary<
        string,
        CollectionMerchantKind[]
    > ManualMerchantInternalNameRules = new(StringComparer.OrdinalIgnoreCase);

    private static readonly (EHiddenTag Tag, CollectionMerchantKind Merchant)[] MerchantHiddenTags =
    {
        (EHiddenTag.BurnMerchant, CollectionMerchantKind.Burn),
        (EHiddenTag.PoisonMerchant, CollectionMerchantKind.Poison),
        (EHiddenTag.FreezeMerchant, CollectionMerchantKind.Freeze),
        (EHiddenTag.SlowMerchant, CollectionMerchantKind.Slow),
        (EHiddenTag.HasteMerchant, CollectionMerchantKind.Haste),
        (EHiddenTag.SpeedMerchant, CollectionMerchantKind.Speed),
        (EHiddenTag.ToughnessMerchant, CollectionMerchantKind.Toughness),
        (EHiddenTag.StrengthMerchant, CollectionMerchantKind.Strength),
        (EHiddenTag.HealMerchant, CollectionMerchantKind.Heal),
        (EHiddenTag.EconomyMerchant, CollectionMerchantKind.Economy),
        (EHiddenTag.ShieldMerchant, CollectionMerchantKind.Shield),
        (EHiddenTag.HealthMerchant, CollectionMerchantKind.Health),
        (EHiddenTag.JoyMerchant, CollectionMerchantKind.Joy),
        (EHiddenTag.FlyingMerchant, CollectionMerchantKind.Flying),
        (EHiddenTag.Merchant, CollectionMerchantKind.General),
    };

    public static bool IsCatalogCard(TCardBase template) =>
        IsCatalogCard(template.Type, template.ArtKey, template.InternalName);

    public static bool IsCatalogCard(ECardType type, string? artKey, string? internalName)
    {
        if (type != ECardType.Item && type != ECardType.Skill)
            return false;
        if (!HasValidArtKey(artKey))
            return false;
        return !ContainsAnyMarker(internalName, NonCatalogNameMarkers);
    }

    public static bool IsPackage(TCardBase template) => IsPackageName(template.InternalName);

    public static bool IsPackageName(string? internalName) =>
        ContainsAnyMarker(internalName, PackageNameMarkers);

    public static IReadOnlyCollection<CollectionMerchantKind> ResolveMerchants(
        TCardBase template
    ) => ResolveMerchants(template.Tags, template.HiddenTags, template.Id, template.InternalName);

    public static IReadOnlyCollection<CollectionMerchantKind> ResolveMerchants(
        IReadOnlyCollection<ECardTag>? tags,
        IReadOnlyCollection<EHiddenTag>? hiddenTags
    ) => ResolveMerchants(tags, hiddenTags, Guid.Empty, string.Empty);

    private static IReadOnlyCollection<CollectionMerchantKind> ResolveMerchants(
        IReadOnlyCollection<ECardTag>? tags,
        IReadOnlyCollection<EHiddenTag>? hiddenTags,
        Guid id,
        string? internalName
    )
    {
        List<CollectionMerchantKind>? merchants = null;

        if (id != Guid.Empty && ManualMerchantIdRules.TryGetValue(id, out var idRules))
            AddRange(ref merchants, idRules);

        if (
            !string.IsNullOrWhiteSpace(internalName)
            && ManualMerchantInternalNameRules.TryGetValue(internalName!, out var nameRules)
        )
            AddRange(ref merchants, nameRules);

        if (Contains(tags, ECardTag.Merchant))
            Add(ref merchants, CollectionMerchantKind.General);

        if (hiddenTags != null)
        {
            foreach (var pair in MerchantHiddenTags)
            {
                if (Contains(hiddenTags, pair.Tag))
                    Add(ref merchants, pair.Merchant);
            }
        }

        if (merchants == null)
            return Array.Empty<CollectionMerchantKind>();
        return merchants;
    }

    private static bool HasValidArtKey(string? artKey) =>
        !string.IsNullOrEmpty(artKey)
        && !string.Equals(artKey, "Invalid", StringComparison.Ordinal);

    private static bool ContainsAnyMarker(string? value, IReadOnlyList<string> markers)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (var marker in markers)
        {
            if (value!.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static bool Contains<T>(IReadOnlyCollection<T>? values, T expected)
    {
        if (values == null)
            return false;
        var comparer = EqualityComparer<T>.Default;
        foreach (var value in values)
        {
            if (comparer.Equals(value, expected))
                return true;
        }
        return false;
    }

    private static void AddRange(
        ref List<CollectionMerchantKind>? merchants,
        IReadOnlyCollection<CollectionMerchantKind> values
    )
    {
        foreach (var value in values)
            Add(ref merchants, value);
    }

    private static void Add(
        ref List<CollectionMerchantKind>? merchants,
        CollectionMerchantKind value
    )
    {
        merchants ??= new List<CollectionMerchantKind>();
        if (!merchants.Contains(value))
            merchants.Add(value);
    }
}
