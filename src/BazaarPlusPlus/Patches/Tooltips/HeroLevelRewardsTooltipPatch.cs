#nullable enable
#pragma warning disable CS0436
using System;
using System.Collections.Generic;
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

            var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
            var content = CollectionLevelUpTooltipText.Build(
                heroLevelTooltipData._nextLevelUp,
                id => ResolveTemplate(staticData, id),
                EncounterEventTooltipPatch.TryReadCurrentHero(),
                BppTooltipText.ColorKeywords
            );
            if (string.IsNullOrEmpty(content))
            {
                BppLog.Info(
                    "LevelTooltip",
                    $"No content: nextLevelUp={(heroLevelTooltipData._nextLevelUp == null ? "null" : heroLevelTooltipData._nextLevelUp.Level.ToString())}, hero={EncounterEventTooltipPatch.TryReadCurrentHero()}"
                );
                return;
            }

            var anchor = controller._heroLevelTooltipViewComponent?._parent;
            var shown = BppTooltipSections.TryShow(controller, SectionKey, anchor, content);
            BppLog.Info(
                "LevelTooltip",
                $"content={content.Length}ch anchor={(anchor == null ? "null" : DescribeAnchor(anchor))} shown={shown}"
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

    private static string DescribeAnchor(GameObject anchor)
    {
        var parentChain = anchor.transform.parent == null
            ? "<root>"
            : anchor.transform.parent.name;
        var rect = anchor.GetComponent<RectTransform>();
        return $"'{anchor.name}' parent='{parentChain}' active={anchor.activeInHierarchy} rect={(rect == null ? "none" : rect.rect.size.ToString())}\n"
            + DescribeContainer(anchor.transform.parent);
    }

    private static string DescribeContainer(Transform? container)
    {
        if (container == null)
            return "container=<null>";

        var lines = new List<string>
        {
            $"container '{container.name}': layout={ListComponents(container.gameObject)}",
        };
        for (var i = 0; i < container.childCount; i++)
        {
            var child = container.GetChild(i);
            var rect = child as RectTransform;
            var layoutElement = child.GetComponent<UnityEngine.UI.LayoutElement>();
            lines.Add(
                $"  [{i}] '{child.name}' active={child.gameObject.activeSelf} pos={(rect == null ? "?" : rect.anchoredPosition.ToString())} size={(rect == null ? "?" : rect.rect.size.ToString())} ignoreLayout={(layoutElement == null ? "-" : layoutElement.ignoreLayout.ToString())}"
            );
        }
        return string.Join("\n", lines);
    }

    private static string ListComponents(GameObject target)
    {
        var names = new List<string>();
        foreach (var component in target.GetComponents<Component>())
            if (component != null)
                names.Add(component.GetType().Name);
        return string.Join(",", names);
    }
}
