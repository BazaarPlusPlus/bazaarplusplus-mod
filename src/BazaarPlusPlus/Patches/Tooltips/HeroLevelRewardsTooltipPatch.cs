#nullable enable
#pragma warning disable CS0436
using System;
using BazaarGameShared.Domain.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Patches.Tooltips;

// Appends a readable reward breakdown below the native hero-level tooltip
// ("NEXT LEVEL REWARDS" icon strip). HandleTooltip starts with ResetValues, which
// routes through RenderPassiveEffectTextBlock(empty) and hides all BPP sections, so
// showing the level section here needs no extra teardown elsewhere.
[HarmonyPatch(typeof(HeroLevelTooltipTypeHandler), nameof(HeroLevelTooltipTypeHandler.HandleTooltip))]
internal static class HeroLevelRewardsTooltipPatch
{
    internal const string SectionKey = "level-rewards";

    [HarmonyPostfix]
    private static void Postfix(
        CardTooltipController controller,
        Transform parent,
        Vector3 offset,
        ITooltipData tooltipData
    )
    {
        try
        {
            if (tooltipData is not HeroLevelTooltipData heroLevelTooltipData)
            {
                BppLog.Debug("LevelTooltip", "Skipped: not hero level data");
                return;
            }

            // Native gap: HandleTooltip resets the view via ResetValues, which clears
            // every section except the quest rows — a pooled controller that last
            // showed a quest item (e.g. a shop basket) leaks its "Sell N Food" rows
            // into the hero-level tooltip. Clear them the way the card path does.
            controller._questDisplayService?.BuildDisplay(null, null);

            var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
            var content = CollectionLevelUpTooltipText.Build(
                heroLevelTooltipData._nextLevelUp,
                id => ResolveTemplate(staticData, id),
                EncounterEventTooltipPatch.TryReadCurrentHero(),
                BppTooltipText.ColorKeywords
            );
            if (string.IsNullOrEmpty(content))
            {
                BppLog.Debug(
                    "LevelTooltip",
                    $"No content: nextLevelUp={(heroLevelTooltipData._nextLevelUp == null ? "null" : heroLevelTooltipData._nextLevelUp.Level.ToString())}, hero={EncounterEventTooltipPatch.TryReadCurrentHero()}"
                );
                return;
            }

            var anchor = controller._heroLevelTooltipViewComponent?._parent;
            var shown = BppTooltipSections.TryShow(controller, SectionKey, anchor, content);
            BppLog.Debug(
                "LevelTooltip",
                $"content={content.Length}ch anchor={(anchor == null ? "null" : anchor.name)} shown={shown}"
            );
            if (!shown)
                return;

            // The native handler positioned the tooltip before this section existed.
            controller.PositionTooltip(parent, offset);
        }
        catch (Exception ex)
        {
            BppLog.Error("LevelTooltip", "Failed to render level-up rewards section", ex);
        }
    }

    private static TCardBase? ResolveTemplate(object? staticData, Guid templateId) =>
        BppStaticDataAccess.GetCardTemplate(staticData, templateId);
}
