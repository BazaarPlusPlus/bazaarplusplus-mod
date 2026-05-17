#pragma warning disable CS0436
#nullable enable
using System;
using System.Linq;
using BazaarBattleService.Models;
using BazaarGameClient.Domain.Models;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Runs;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches.ShopForecast;

[HarmonyPatch(
    typeof(DataExtensions),
    nameof(DataExtensions.Update),
    new[] { typeof(RunState), typeof(SimUpdateRunState) }
)]
internal static class ShopForecastLogPatch
{
    private const string Component = "ShopForecast";

    [HarmonyPostfix]
    private static void Postfix(RunState state, SimUpdateRunState snapshot)
    {
        try
        {
            if (snapshot == null)
                return;

            LogActiveSnapshot(snapshot);

            if (snapshot.StateName == ERunState.Choice)
                LogChoiceOptions(snapshot);
        }
        catch (Exception ex)
        {
            BppLog.Error(Component, "SimUpdateRunState postfix failed", ex);
        }
    }

    private static void LogActiveSnapshot(SimUpdateRunState snapshot)
    {
        var encRaw = snapshot.CurrentEncounterId ?? "(none)";
        var cost = snapshot.RerollCost?.ToString() ?? "-";
        var remaining = snapshot.RerollsRemaining?.ToString() ?? "-";
        var selection = snapshot.SelectionSet?.Count ?? 0;
        var enrich = SafeBuildEnrichment(snapshot.CurrentEncounterId);

        BppLog.Info(
            Component,
            $"snapshot state={snapshot.StateName} encounter={encRaw} "
                + $"selection={selection} cost={cost} remaining={remaining}{enrich}"
        );
    }

    private static void LogChoiceOptions(SimUpdateRunState snapshot)
    {
        if (snapshot.SelectionSet == null || snapshot.SelectionSet.Count == 0)
            return;

        for (var i = 0; i < snapshot.SelectionSet.Count; i++)
        {
            var optionId = snapshot.SelectionSet[i];
            var enrich = SafeBuildEnrichment(optionId);
            BppLog.Info(Component, $"  option[{i}] id={optionId}{enrich}");
        }
    }

    private static string SafeBuildEnrichment(string? rawId)
    {
        try
        {
            return BuildEnrichment(rawId);
        }
        catch (Exception ex)
        {
            return $" enrich=exception({ex.GetType().Name}:{ex.Message})";
        }
    }

    private static string BuildEnrichment(string? rawId)
    {
        if (string.IsNullOrEmpty(rawId))
            return "";

        var dealer = Singleton<GameServiceManager>.Instance?.CardDealer;
        if (dealer == null)
            return " enrich=no-dealer";
        if (dealer.cardRepo == null)
            return " enrich=cardrepo-null";

        var (template, lookupNote) = ResolveTemplate(rawId!, dealer);
        if (template == null)
            return $" enrich={lookupNote}";

        var f = template.SpawningFilters;
        if (f == null)
            return $" spawner='{template.Name}'(no-filters)";

        var tierFilter = (f.ItemTierFilters?.Count ?? 0) > 0
            ? "[" + string.Join(",", f.ItemTierFilters) + "]"
            : "any";
        var idCount = f.CardIdFilters?.Count ?? 0;
        var repeats = f.Rerolls?.RerollRepeats ?? false;

        var spawner =
            $" spawner='{template.Name}'(type={template.CardType},n={f.NumberCardsToSpawn},"
            + $"tier={tierFilter},ids={idCount},repeats={repeats})";

        var battlePlayer = dealer.GetBazaarBattlePlayer();
        var player = battlePlayer != null
            ? $" player=({battlePlayer.Hero},day={battlePlayer.Day},hour={battlePlayer.Hour})"
            : " player=null";

        var excl = dealer.GameStateMetadata?.DealtCardForReRollExclusion?.Count ?? -1;

        return spawner + player + $" excl={excl}";
    }

    private static (BazaarCard? template, string note) ResolveTemplate(
        string rawId,
        BazaarBattleService.BazaarCardDealer dealer
    )
    {
        if (Guid.TryParse(rawId, out var directGuid))
        {
            var direct = dealer.cardRepo.GetCardById(directGuid);
            if (direct != null && direct.IsOk && direct.Value != null)
                return (direct.Value, "");
            return (null, "guid-not-in-repo");
        }

        var instanceId = new InstanceId(rawId);
        if (
            Data.Entities == null
            || !Data.Entities.TryGetValue(instanceId, out var clientCard)
            || clientCard == null
        )
            return (null, "instance-not-in-entities");

        var via = dealer.cardRepo.GetCardById(clientCard.TemplateId);
        if (via == null || via.IsFail || via.Value == null)
            return (null, "templateid-not-in-repo");
        return (via.Value, "");
    }
}
