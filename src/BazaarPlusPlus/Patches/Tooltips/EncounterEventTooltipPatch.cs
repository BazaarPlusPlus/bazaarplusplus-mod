#nullable enable
#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Game.EventPreview;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

// Builds the event-choice breakdown consumed by the shared BPP tooltip-section renderer.
internal static class EncounterEventTooltipPatch
{
    internal const string SectionKey = "encounter";

    internal static string? BuildContent(CardTooltipController controller)
    {
        if (Data.IsInCombat || !EventPreviewGate.IsEnabled())
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
            TryBuildInventory(),
            TryReadCurrentDay()
        );
        if (option == null || (!option.HasChoiceDetails && !option.HasOutcomeGroups))
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

    // Per-card snapshot of the player's current inventory (hand + stash items and
    // skills), used to evaluate ownership prerequisites — including count
    // comparisons ("Equal 0" = only while you do NOT own it) and per-card tag
    // operators. Null (out of run / read failure) means eligibility is not evaluated.
    private static CollectionEncounterInventory? TryBuildInventory()
    {
        try
        {
            var player = Data.Run?.Player;
            if (player == null)
                return null;

            var cards = new List<CollectionEncounterInventoryCard>();
            AddCards(cards, player.Hand?.GetItemsAsEnumerable());
            AddCards(cards, player.Stash?.GetItemsAsEnumerable());
            AddCards(cards, player.Skills);
            return new CollectionEncounterInventory(cards);
        }
        catch (Exception ex)
        {
            BppLog.Warn("EncounterTooltip", $"Inventory read failed: {ex.Message}");
            return null;
        }
    }

    private static void AddCards(
        List<CollectionEncounterInventoryCard> cards,
        System.Collections.IEnumerable? source
    )
    {
        if (source == null)
            return;
        foreach (var card in source)
        {
            if (card is not BazaarGameClient.Domain.Models.Cards.Card typed)
                continue;
            var tagNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tag in typed.Tags)
                tagNames.Add(tag.ToString());
            foreach (var hiddenTag in typed.HiddenTags)
                tagNames.Add(hiddenTag.ToString());
            cards.Add(new CollectionEncounterInventoryCard(typed.TemplateId, tagNames));
        }
    }

    private static ETier? TryReadDayTierCeiling()
    {
        var day = TryReadCurrentDay();
        return day.HasValue ? DayTierSchedule.CeilingTier(day.Value) : null;
    }

    private static int? TryReadCurrentDay()
    {
        try
        {
            return (int?)Data.Run?.Day;
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

    // Localized display word for a canonical attribute keyword ("Heal" -> "治疗" on zh
    // clients) from the game's keyword table; null when unknown so callers fall back
    // to the English name.
    public static string? TryLocalizeKeyword(string canonicalName)
    {
        try
        {
            // On English clients the canonical name IS the display word; the keyword
            // table's extra entries are matching variants ("Heals"/"Healing") that
            // must not leak into appended text.
            var languageCode = PlayerPreferences.Data.LanguageCode ?? string.Empty;
            if (
                languageCode.Length == 0
                || languageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            )
                return null;

            var typography = Data.TooltipTypography;
            if (typography == null)
                return null;

            // Prefer an actual translation over entries that merely echo the
            // canonical English name (the primary entry often does on zh clients).
            string? echo = null;
            foreach (
                var translation in typography.GetKeywordTranslations(
                    $"{{keyword.{canonicalName.ToLowerInvariant()}}}",
                    includePrimaryTranslation: true
                )
            )
            {
                if (string.IsNullOrWhiteSpace(translation))
                    continue;
                if (string.Equals(translation, canonicalName, StringComparison.OrdinalIgnoreCase))
                {
                    echo ??= translation;
                    continue;
                }
                return translation;
            }
            return echo;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
