#nullable enable
#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

// Renders the event-choice breakdown as its own tooltip section (a clone of the
// tooltip's native passive-text block) right below the description.
// RenderPassiveEffectTextBlock runs for every card rendered into the pooled tooltip
// (and with empty text on ResetValues), so all BPP sections are re-evaluated — and
// hidden for non-event cards — on each render.
[HarmonyPatch(
    typeof(CardTooltipController),
    nameof(CardTooltipController.RenderPassiveEffectTextBlock)
)]
internal static class EncounterEventTooltipPatch
{
    private const string SectionKey = "encounter";

    [HarmonyPostfix]
    private static void Postfix(CardTooltipController __instance, string text)
    {
        try
        {
            // Empty text is the ResetValues path (or a card with no description);
            // never show a section there — _currentCard may be stale.
            var content = string.IsNullOrEmpty(text) ? null : BuildContent(__instance);
            if (string.IsNullOrEmpty(content))
            {
                BppTooltipSections.HideAll(__instance);
                return;
            }

            if (!BppTooltipSections.TryShow(
                    __instance,
                    SectionKey,
                    __instance.passiveEffectParent,
                    content!
                ))
                return;
            BppTooltipSections.Hide(__instance, HeroLevelRewardsTooltipPatch.SectionKey);
        }
        catch (Exception ex)
        {
            BppLog.Error("EncounterTooltip", "Failed to render encounter event section", ex);
        }
    }

    private static string? BuildContent(CardTooltipController controller)
    {
        if (Data.IsInCombat)
            return null;

        var card = controller._currentCard;
        if (card == null || card.Type != ECardType.EventEncounter)
            return null;

        var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
        var template = BppStaticDataAccess.GetCardTemplate(staticData, card.TemplateId);
        if (template == null)
            return null;

        var option = CollectionEncounterEventDetailResolver.TryResolve(
            template,
            staticData,
            TryReadCurrentHero(),
            TryBuildOwnedTemplateChecker()
        );
        if (option == null || !option.HasChoiceDetails)
            return null;

        return CollectionEncounterGameTooltipText.Build(
            option,
            BppTooltipText.ColorKeywords,
            TryReadDayTierCeiling()
        );
    }

    internal static EHero? TryReadCurrentHero()
    {
        var runHero = Data.Run?.Player?.Hero;
        if (CollectionPanelOpenSelectionResolver.IsConcreteHero(runHero))
            return runHero;

        var selectedHero = Data.SelectedHero;
        return CollectionPanelOpenSelectionResolver.IsConcreteHero(selectedHero)
            ? selectedHero
            : null;
    }

    // Snapshot of the player's current inventory (hand + stash items and skills) by
    // template id, used to grey out options whose card prerequisites are unmet.
    // Null (out of run / read failure) means eligibility is not evaluated.
    private static Func<Guid, bool>? TryBuildOwnedTemplateChecker()
    {
        try
        {
            var player = Data.Run?.Player;
            if (player == null)
                return null;

            var owned = new HashSet<Guid>();
            AddTemplateIds(owned, player.Hand?.GetItemsAsEnumerable());
            AddTemplateIds(owned, player.Stash?.GetItemsAsEnumerable());
            AddTemplateIds(owned, player.Skills);
            return owned.Contains;
        }
        catch (Exception ex)
        {
            BppLog.Warn("EncounterTooltip", $"Inventory read failed: {ex.Message}");
            return null;
        }
    }

    private static void AddTemplateIds(HashSet<Guid> owned, System.Collections.IEnumerable? cards)
    {
        if (cards == null)
            return;
        foreach (var card in cards)
        {
            if (card is BazaarGameClient.Domain.Models.Cards.Card typed)
                owned.Add(typed.TemplateId);
        }
    }

    private static ETier? TryReadDayTierCeiling()
    {
        try
        {
            var day = (int?)Data.Run?.Day;
            return day.HasValue ? DayTierSchedule.CeilingTier(day.Value) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

// Same keyword coloring the game applies to tooltip text (e.g. blue item tags).
internal static class BppTooltipText
{
    public static string ColorKeywords(string text)
    {
        try
        {
            return Data.TooltipTypography?.ColorKeywords(text) ?? text;
        }
        catch (Exception)
        {
            return text;
        }
    }
}
