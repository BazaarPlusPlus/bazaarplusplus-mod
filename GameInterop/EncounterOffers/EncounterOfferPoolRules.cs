#nullable enable
using System;
using System.Collections.Generic;
using BazaarBattleService;
using BazaarBattleService.Models;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.EncounterOffers;

internal enum EncounterOfferHeroFilterStatus
{
    Ready,
    EmptyIntersection,
    UnsupportedHero,
}

internal sealed class EncounterOfferHeroFilterResult
{
    private EncounterOfferHeroFilterResult(
        EncounterOfferHeroFilterStatus status,
        IReadOnlyList<BazaarTypes.EBazaarHero> runtimeHeroes,
        string? reason
    )
    {
        Status = status;
        RuntimeHeroes = runtimeHeroes;
        Reason = reason;
    }

    public EncounterOfferHeroFilterStatus Status { get; }

    public IReadOnlyList<BazaarTypes.EBazaarHero> RuntimeHeroes { get; }

    public string? Reason { get; }

    public static EncounterOfferHeroFilterResult Ready(
        IReadOnlyList<BazaarTypes.EBazaarHero> runtimeHeroes
    ) => new(EncounterOfferHeroFilterStatus.Ready, runtimeHeroes, null);

    public static EncounterOfferHeroFilterResult EmptyIntersection() =>
        new(
            EncounterOfferHeroFilterStatus.EmptyIntersection,
            Array.Empty<BazaarTypes.EBazaarHero>(),
            null
        );

    public static EncounterOfferHeroFilterResult UnsupportedHero(string reason) =>
        new(
            EncounterOfferHeroFilterStatus.UnsupportedHero,
            Array.Empty<BazaarTypes.EBazaarHero>(),
            reason
        );
}

internal static class EncounterOfferPoolRules
{
    public static EncounterOfferHeroFilterResult ResolveRuntimeHeroFilters(
        IReadOnlyCollection<BazaarTypes.EBazaarHero> sourceMerchantHeroFilters,
        IReadOnlyCollection<EHero> uiHeroFilters
    )
    {
        var sourceUiHeroes = new List<EHero>();
        foreach (var sourceHero in sourceMerchantHeroFilters)
        {
            if (!EncounterOfferHeroMapper.TryFromRuntime(sourceHero, out var uiHero))
                return EncounterOfferHeroFilterResult.UnsupportedHero(
                    $"unsupported-source-hero:{sourceHero}"
                );
            AddDistinct(sourceUiHeroes, uiHero);
        }

        var uiHeroes = new List<EHero>();
        foreach (var uiHero in uiHeroFilters)
        {
            if (!EncounterOfferHeroMapper.TryToRuntime(uiHero, out _))
                return EncounterOfferHeroFilterResult.UnsupportedHero(
                    $"unsupported-ui-hero:{uiHero}"
                );
            AddDistinct(uiHeroes, uiHero);
        }

        if (sourceUiHeroes.Count == 0 && uiHeroes.Count == 0)
            return EncounterOfferHeroFilterResult.Ready(Array.Empty<BazaarTypes.EBazaarHero>());

        if (sourceUiHeroes.Count == 0)
            return TryMapUiHeroes(uiHeroes);

        if (uiHeroes.Count == 0)
            return TryMapUiHeroes(sourceUiHeroes);

        var intersection = new List<EHero>();
        foreach (var uiHero in uiHeroes)
        {
            if (sourceUiHeroes.Contains(uiHero))
                intersection.Add(uiHero);
        }

        if (intersection.Count == 0)
            return EncounterOfferHeroFilterResult.EmptyIntersection();

        return TryMapUiHeroes(intersection);
    }

    public static bool IsCandidateTierEligible(
        BazaarCard.EItemTier candidateStartingTier,
        IReadOnlyCollection<BazaarCard.EItemTier> sourceItemTierFilters
    )
    {
        if (sourceItemTierFilters.Count == 0)
            return true;

        foreach (var sourceTier in sourceItemTierFilters)
        {
            if (!IsRealTier(sourceTier))
                continue;
            if (candidateStartingTier <= sourceTier)
                return true;
        }
        return false;
    }

    private static EncounterOfferHeroFilterResult TryMapUiHeroes(IReadOnlyList<EHero> uiHeroes)
    {
        var runtimeHeroes = new List<BazaarTypes.EBazaarHero>(uiHeroes.Count);
        foreach (var uiHero in uiHeroes)
        {
            if (!EncounterOfferHeroMapper.TryToRuntime(uiHero, out var runtimeHero))
                return EncounterOfferHeroFilterResult.UnsupportedHero(
                    $"unsupported-ui-hero:{uiHero}"
                );
            AddDistinct(runtimeHeroes, runtimeHero);
        }
        return EncounterOfferHeroFilterResult.Ready(runtimeHeroes);
    }

    private static void AddDistinct<T>(List<T> values, T value)
    {
        if (!values.Contains(value))
            values.Add(value);
    }

    private static bool IsRealTier(BazaarCard.EItemTier tier) =>
        tier != BazaarCard.EItemTier.Invalid && tier != BazaarCard.EItemTier.Count;
}
