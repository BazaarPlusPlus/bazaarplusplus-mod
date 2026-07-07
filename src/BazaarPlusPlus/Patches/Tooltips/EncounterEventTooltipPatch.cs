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

    static EncounterEventTooltipPatch()
    {
        // The data layer resolves ability units ("gains 20 Heal") language-neutrally;
        // route its unit words through the game's localized keyword table.
        CollectionLocalizationResolver.AttributeUnitLocalizer = BppTooltipText.TryLocalizeKeyword;
    }

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
                    : BuildContent(__instance);
            if (string.IsNullOrEmpty(content))
            {
                BppTooltipSections.HideAll(__instance);
                return;
            }

            if (
                !BppTooltipSections.TryShow(
                    __instance,
                    SectionKey,
                    __instance.passiveEffectParent,
                    content!
                )
            )
                return;
            BppTooltipSections.Hide(__instance, HeroLevelRewardsTooltipPatch.SectionKey);
            DumpLayoutOnce(__instance, text);
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

    // Diagnostic (remove once the tooltip layout settles): once per card template per
    // session, log the native text verbatim plus a post-layout height dump of the
    // tooltip tree, so oversized empty regions can be attributed from LogOutput.log.
    private static readonly HashSet<Guid> DumpedLayouts = new();

    private static void DumpLayoutOnce(CardTooltipController controller, string nativeText)
    {
        var templateId = controller._currentCard?.TemplateId;
        if (templateId == null || !DumpedLayouts.Add(templateId.Value))
            return;
        controller.StartCoroutine(DumpAtEndOfFrame(controller, templateId.Value, nativeText));
    }

    private static System.Collections.IEnumerator DumpAtEndOfFrame(
        CardTooltipController controller,
        Guid templateId,
        string nativeText
    )
    {
        // Heights are only meaningful after this frame's layout pass.
        yield return new UnityEngine.WaitForEndOfFrame();
        try
        {
            var builder = new System.Text.StringBuilder();
            builder
                .Append("layout for ")
                .Append(templateId)
                .Append(" nativeText=[")
                .Append(Escape(nativeText))
                .Append(']');
            DumpNode(builder, controller.transform, 0);
            BppLog.Info("EncounterTooltip", builder.ToString());
        }
        catch (Exception ex)
        {
            BppLog.Warn("EncounterTooltip", $"Layout dump failed: {ex.Message}");
        }
    }

    private static void DumpNode(
        System.Text.StringBuilder builder,
        UnityEngine.Transform node,
        int depth
    )
    {
        if (depth > 5)
            return;
        builder.Append('\n').Append(' ', depth * 2).Append(node.name);
        if (!node.gameObject.activeSelf)
            builder.Append(" [inactive]");
        if (node is UnityEngine.RectTransform rect)
            builder.Append(" h=").Append(rect.rect.height.ToString("0.#"));
        if (node.TryGetComponent<UnityEngine.UI.LayoutElement>(out var layoutElement))
            builder
                .Append(" le(ignore=")
                .Append(layoutElement.ignoreLayout)
                .Append(",min=")
                .Append(layoutElement.minHeight)
                .Append(",pref=")
                .Append(layoutElement.preferredHeight)
                .Append(')');
        if (node.TryGetComponent<TMPro.TextMeshProUGUI>(out var textComponent))
            builder
                .Append(" tmp(pref=")
                .Append(textComponent.preferredHeight.ToString("0.#"))
                .Append(",text=[")
                .Append(Escape(Truncate(textComponent.text)))
                .Append("])");
        for (var i = 0; i < node.childCount; i++)
            DumpNode(builder, node.GetChild(i), depth + 1);
    }

    private static string Escape(string text) =>
        text.Replace("\r", "\\r").Replace("\n", "\\n");

    private static string Truncate(string text) =>
        text.Length <= 60 ? text : text[..60] + "…";

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
            if (languageCode.Length == 0
                || languageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase))
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
