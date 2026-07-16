#nullable enable
#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Game.EventPreview;
using BazaarPlusPlus.Game.Tooltips;
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
    private static readonly BppTooltipSections.Style SectionStyle = new()
    {
        ParagraphSpacing = 0f,
        SourceBottomPaddingScale = 0.6f,
        NativeSectionBottomPaddingScale = 1.2f,
    };

    [HarmonyPostfix]
    private static void Postfix(CardTooltipController __instance, string text)
    {
        try
        {
            // Empty text is the ResetValues path (or a card with no description);
            // never show a section there — _currentCard may be stale.
            var content =
                string.IsNullOrEmpty(text) || !EventPreviewGate.IsEnabled()
                    ? null
                    : BuildContent(__instance, text);
            if (string.IsNullOrEmpty(content))
            {
                BppTooltipSections.Hide(__instance, SectionKey);
                BppTooltipSections.Hide(__instance, HeroLevelRewardsTooltipPatch.SectionKey);
                return;
            }

            if (
                !BppTooltipSections.TryShow(
                    __instance,
                    SectionKey,
                    __instance.passiveEffectParent,
                    content!,
                    SectionStyle
                )
            )
                return;
            BppTooltipSections.Hide(__instance, HeroLevelRewardsTooltipPatch.SectionKey);
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                TooltipLogEvents.EncounterSectionDegraded,
                ex,
                TooltipLogEvents.EncounterSectionReasonCode.Bind(
                    TooltipLogReasonCode.RenderException
                )
            );
        }
    }

    private static string? BuildContent(CardTooltipController controller, string resultText)
    {
        if (Data.IsInCombat)
            return null;

        var card = controller._currentCard;
        if (
            card == null
            || (card.Type != ECardType.EventEncounter && card.Type != ECardType.EncounterStep)
        )
            return null;

        var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
        if (staticData == null)
            return null;

        var currentDay = TryReadCurrentDay();
        var dayTierCeiling = EncounterTierRuntime.ReadDayTierCeiling(currentDay);
        var dayTierDistribution = EncounterTierRuntime.ReadDayTierDistribution(
            staticData,
            currentDay
        );
        var template = BppStaticDataAccess.GetCardTemplate(staticData, card.TemplateId);
        if (card.Type == ECardType.EncounterStep)
        {
            return EventPreviewPlanRuntime.TryGetTemplate(
                staticData,
                card.TemplateId,
                out var stepPlan
            )
                ? CollectionEncounterGameTooltipText.BuildRewardQualityLine(
                    stepPlan.RewardFilter,
                    resultText,
                    dayTierDistribution,
                    dayTierCeiling
                )
                : null;
        }

        if (template?.Tags?.Contains(ECardTag.Merchant) == true)
        {
            var policy = CollectionMerchantTierResolver.Resolve(template);
            if (!policy.FixedTier.HasValue && !policy.UsesDayDistribution)
                return null;

            return CollectionEncounterGameTooltipText.BuildQualityLine(
                policy.UsesDayDistribution ? dayTierDistribution : null,
                policy.FixedTier,
                policy.UsesDayDistribution ? dayTierCeiling : null
            );
        }

        if (
            !EventPreviewPlanRuntime.TryGet(
                staticData,
                card.TemplateId,
                out var eventPlan,
                out var snapshot
            )
        )
            return null;

        var option = CollectionEncounterEventDetailResolver.TryResolve(
            eventPlan,
            snapshot,
            TryReadCurrentHero(),
            TryBuildInventory(),
            currentDay
        );
        if (option == null || (!option.HasChoiceDetails && !option.HasOutcomeGroups))
            return null;

        return CollectionEncounterGameTooltipText.Build(
            option,
            BppTooltipText.ColorKeywords,
            dayTierCeiling,
            dayTierDistribution
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
            BppLog.WarnEvent(
                TooltipLogEvents.EncounterInventoryDegraded,
                ex,
                TooltipLogEvents.EncounterInventoryReasonCode.Bind(
                    TooltipLogReasonCode.InventoryReadException
                )
            );
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

internal static class EncounterTierRuntime
{
    public static ETier? ReadDayTierCeiling(int? currentRunDay) =>
        currentRunDay.HasValue ? DayTierSchedule.CeilingTier(currentRunDay.Value) : null;

    public static CollectionTierDistribution? ReadDayTierDistribution(
        object staticData,
        int? currentRunDay
    )
    {
        if (!currentRunDay.HasValue)
            return null;

        try
        {
            var run = Data.Run;
            if (run == null)
                return null;

            var weights = BppStaticDataAccess.GetItemSkillSpawnTierProbabilities(
                staticData,
                run.GameModeId,
                currentRunDay.Value
            );
            return weights == null
                ? null
                : CollectionTierDistribution.FromWeights(
                    weights.Bronze,
                    weights.Silver,
                    weights.Gold,
                    weights.Diamond
                );
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
