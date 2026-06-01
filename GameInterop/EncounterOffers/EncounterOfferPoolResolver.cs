#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarBattleService.Models;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.AppFramework;

namespace BazaarPlusPlus.GameInterop.EncounterOffers;

internal static class EncounterOfferPoolResolver
{
    private const string LogComponent = "EncounterOffers";

    private static string? _lastProbeState;

    public static EncounterOfferPoolResult ResolveOfferedTemplateIds(
        IReadOnlyList<Guid> sourceTemplateIds,
        IReadOnlyList<BazaarGameShared.Domain.Core.Types.EHero> uiHeroFilters
    )
    {
        if (sourceTemplateIds.Count == 0)
            return EncounterOfferPoolResult.Unavailable("source-template-ids-empty");

        var manager = Singleton<GameServiceManager>.Instance;
        var dealer = manager?.CardDealer;
        var repo = dealer?.cardRepo;
        LogRuntimeProbe(manager != null, dealer != null, repo != null);

        if (manager == null)
            return EncounterOfferPoolResult.Loading("game-service-manager-not-ready");
        if (dealer == null)
            return EncounterOfferPoolResult.Loading("card-dealer-not-ready");
        if (repo == null)
            return EncounterOfferPoolResult.Loading("old-runtime-card-repo-not-ready");

        var offeredTemplateIds = new HashSet<Guid>();
        var sourceCardsResolved = 0;

        foreach (var sourceTemplateId in sourceTemplateIds)
        {
            BazaarCard? sourceCard;
            try
            {
                var sourceResult = repo.GetCardById(sourceTemplateId);
                if (sourceResult.IsFail || sourceResult.Value == null)
                {
                    BppLog.Warn(
                        LogComponent,
                        $"Source card unavailable sourceTemplateId={sourceTemplateId}"
                    );
                    continue;
                }
                sourceCard = sourceResult.Value;
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    LogComponent,
                    $"Source card lookup failed sourceTemplateId={sourceTemplateId}: {ex.Message}"
                );
                continue;
            }

            sourceCardsResolved++;
            var rawSourceHeroFilters = sourceCard.SpawningFilters?.MerchantHeroFilters;
            IReadOnlyCollection<BazaarBattleService.BazaarTypes.EBazaarHero> sourceHeroFilters =
                rawSourceHeroFilters != null
                    ? rawSourceHeroFilters
                    : Array.Empty<BazaarBattleService.BazaarTypes.EBazaarHero>();
            var heroFilterResult = EncounterOfferPoolRules.ResolveRuntimeHeroFilters(
                sourceHeroFilters,
                uiHeroFilters
            );

            if (heroFilterResult.Status == EncounterOfferHeroFilterStatus.UnsupportedHero)
            {
                var reason = heroFilterResult.Reason ?? "unsupported-hero";
                BppLog.Warn(LogComponent, $"Cannot resolve source offer pool: {reason}");
                return EncounterOfferPoolResult.Unavailable(reason);
            }

            if (heroFilterResult.Status == EncounterOfferHeroFilterStatus.EmptyIntersection)
                continue;

            try
            {
                foreach (
                    var candidate in repo.FilterCards(
                        sourceCard,
                        heroFilterResult.RuntimeHeroes.ToList()
                    )
                )
                {
                    var rawItemTierFilters = sourceCard.SpawningFilters?.ItemTierFilters;
                    IReadOnlyCollection<BazaarCard.EItemTier> itemTierFilters =
                        rawItemTierFilters != null
                            ? rawItemTierFilters
                            : Array.Empty<BazaarCard.EItemTier>();

                    if (
                        !EncounterOfferPoolRules.IsCandidateTierEligible(
                            candidate.Value.StartingTier,
                            itemTierFilters
                        )
                    )
                        continue;

                    offeredTemplateIds.Add(candidate.Key);
                }
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    LogComponent,
                    $"FilterCards failed sourceTemplateId={sourceTemplateId}: {ex.Message}"
                );
                return EncounterOfferPoolResult.Unavailable("filter-cards-threw");
            }
        }

        if (sourceCardsResolved == 0)
            return EncounterOfferPoolResult.Unavailable("source-cards-unavailable");

        return EncounterOfferPoolResult.Ready(offeredTemplateIds);
    }

    private static void LogRuntimeProbe(bool hasManager, bool hasDealer, bool hasRepo)
    {
        var state = $"manager={hasManager} dealer={hasDealer} cardRepo={hasRepo}";
        if (string.Equals(_lastProbeState, state, StringComparison.Ordinal))
            return;

        _lastProbeState = state;
        BppLog.Info(LogComponent, $"Runtime availability probe {state}");
    }
}
