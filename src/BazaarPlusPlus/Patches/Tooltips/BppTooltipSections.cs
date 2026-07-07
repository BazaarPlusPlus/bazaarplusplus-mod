#nullable enable
#pragma warning disable CS0436
using System.Collections.Generic;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Patches.Tooltips;

// Manages BPP-owned text sections cloned into the pooled native tooltip. Each section
// is a clone of the tooltip's passive-text block (native typography), keyed per
// controller + purpose, inserted after a caller-supplied anchor sibling.
internal static class BppTooltipSections
{
    // Clearly below the native body size so appended blocks read as secondary info.
    private const float FontScale = 0.75f;

    internal sealed class Section
    {
        public GameObject Block = null!;
        public CardEffectTooltipController Text = null!;
    }

    private static readonly Dictionary<(CardTooltipController, string), Section> Sections = new();

    public static bool TryShow(
        CardTooltipController controller,
        string key,
        GameObject? anchor,
        string content
    )
    {
        if (anchor == null)
            return false;

        var section = Ensure(controller, key, anchor);
        if (section == null)
            return false;

        section.Text.SetText(content);
        section.Block.transform.SetSiblingIndex(anchor.transform.GetSiblingIndex() + 1);
        section.Block.SetActive(true);
        return true;
    }

    public static void Hide(CardTooltipController controller, string key)
    {
        if (!Sections.TryGetValue((controller, key), out var section))
            return;
        if (section.Block == null)
        {
            Sections.Remove((controller, key));
            return;
        }
        section.Block.SetActive(false);
    }

    public static void HideAll(CardTooltipController controller)
    {
        // Controllers are pooled but not immortal: prune entries whose controller or
        // cloned block has been destroyed so the static cache cannot grow across
        // scene reloads. (Unity's overloaded == treats destroyed objects as null.)
        List<(CardTooltipController, string)>? stale = null;
        foreach (var entry in Sections)
        {
            if (entry.Key.Item1 == null || entry.Value.Block == null)
            {
                stale ??= new List<(CardTooltipController, string)>();
                stale.Add(entry.Key);
                continue;
            }

            if (ReferenceEquals(entry.Key.Item1, controller))
                entry.Value.Block.SetActive(false);
        }

        if (stale == null)
            return;
        foreach (var key in stale)
            Sections.Remove(key);
    }

    private static Section? Ensure(CardTooltipController controller, string key, GameObject anchor)
    {
        if (Sections.TryGetValue((controller, key), out var existing))
        {
            if (existing.Block != null && existing.Text != null)
                return existing;
            Sections.Remove((controller, key));
        }

        var passiveBlock = controller.passiveEffectParent;
        if (passiveBlock == null)
        {
            BppLog.Info("TooltipSection", $"Ensure({key}) failed: passiveEffectParent null");
            return null;
        }

        var blockClone = Object.Instantiate(passiveBlock, anchor.transform.parent);
        blockClone.name = $"BppTooltipSection_{key}";

        var textController = blockClone.GetComponentInChildren<CardEffectTooltipController>(
            includeInactive: true
        );
        if (textController == null)
        {
            BppLog.Info("TooltipSection", $"Ensure({key}) failed: no text controller in clone");
            Object.Destroy(blockClone);
            return null;
        }

        // The source block's visibility state (animated alpha) is cloned as-is; force
        // the clone fully opaque so SetActive toggling is the only visibility gate.
        foreach (var group in blockClone.GetComponentsInChildren<CanvasGroup>(true))
            group.alpha = 1f;

        // The source block's LayoutElement.ignoreLayout is toggled together with its
        // visibility; a clone taken while the source was hidden (e.g. the hero-level
        // tooltip, where the passive box is off) inherits ignore=true, so the tooltip's
        // vertical layout/fitter reserves no space and the frame fails to grow around
        // the section. Always participate in layout.
        var rootLayoutElement =
            blockClone.GetComponent<UnityEngine.UI.LayoutElement>()
            ?? blockClone.AddComponent<UnityEngine.UI.LayoutElement>();
        rootLayoutElement.ignoreLayout = false;

        var label = textController.textObject;
        if (label != null)
        {
            if (label.enableAutoSizing)
            {
                label.fontSizeMax *= FontScale;
                label.fontSizeMin = Mathf.Min(label.fontSizeMin, label.fontSizeMax);
            }
            else
            {
                label.fontSize *= FontScale;
            }
        }

        var section = new Section { Block = blockClone, Text = textController };
        Sections[(controller, key)] = section;
        return section;
    }
}
