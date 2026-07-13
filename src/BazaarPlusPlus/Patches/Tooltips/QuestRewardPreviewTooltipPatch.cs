#nullable enable
#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BazaarGameShared.Domain.Tooltips;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.QuestRewardPreview;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using TMPro;
using UnityEngine;

namespace BazaarPlusPlus.Patches.Tooltips;

[HarmonyPatch(typeof(TooltipQuestEntry), nameof(TooltipQuestEntry.SetData))]
internal static class QuestRewardPreviewTooltipPatch
{
    private static readonly FieldInfo? DescriptionTextField = AccessTools.Field(
        typeof(TooltipQuestEntry),
        "_descriptionText"
    );
    private static readonly ConditionalWeakTable<TMP_Text, TextLayoutBaseline>
        DescriptionLayoutBaselines = new();

    [HarmonyPostfix]
    private static void Postfix(
        TooltipQuestEntry __instance,
        CardQuestEntryData entry,
        CardTooltipData currentTooltipData
    )
    {
        try
        {
            // The native icon remains useful as a compact reward marker, but unlocked tooltips
            // cannot enter its nested hover. Keep the reward text inline on every tooltip surface.
            if (DescriptionTextField?.GetValue(__instance) is not TMP_Text descriptionText)
                return;

            if (!QuestRewardPreviewGate.IsEnabled())
            {
                RestoreNativeTextLayout(descriptionText);
                return;
            }

            var rewardTooltips = entry.QuestEntry.Reward?.Localization?.Tooltips;
            if (rewardTooltips == null)
            {
                RestoreNativeTextLayout(descriptionText);
                return;
            }

            // Native SetData unconditionally rendered the quest text into the description
            // field right before this postfix, so it is read back instead of paying a
            // second RenderQuestTooltips pass.
            var questText = descriptionText.text;
            var passiveRewardText = currentTooltipData.RenderQuestTooltips(
                rewardTooltips,
                ETooltipType.Passive
            );
            var activeRewardText = currentTooltipData.RenderQuestTooltips(
                rewardTooltips,
                ETooltipType.Active
            );
            var text = BppQuestRewardPreviewText.AppendRewardPreview(
                questText,
                passiveRewardText,
                activeRewardText
            );

            if (string.Equals(text, questText, StringComparison.Ordinal))
            {
                RestoreNativeTextLayout(descriptionText);
                return;
            }

            var hasInlineSprite = BppQuestRewardPreviewText.ContainsInlineSprite(
                questText,
                passiveRewardText,
                activeRewardText
            );
            ApplyPreviewTextLayout(descriptionText, hasInlineSprite);
            descriptionText.text = text;
            descriptionText.ForceMeshUpdate();
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(QuestRewardPreviewLogEvents.AppendFailed, ex);
        }
    }

    private static void ApplyPreviewTextLayout(
        TMP_Text descriptionText,
        bool hasInlineSprite
    )
    {
        var baseline = DescriptionLayoutBaselines.GetValue(
            descriptionText,
            text => new TextLayoutBaseline(text.margin, text.lineSpacing)
        );
        var nativeMargins = baseline.Margin;
        descriptionText.margin = new Vector4(
            nativeMargins.x,
            nativeMargins.y * BppQuestRewardPreviewText.VerticalMarginScale,
            nativeMargins.z,
            nativeMargins.w * BppQuestRewardPreviewText.VerticalMarginScale
        );
        descriptionText.lineSpacing = hasInlineSprite
            ? baseline.LineSpacing
            : baseline.LineSpacing + BppQuestRewardPreviewText.RewardLineSpacingIncrease;
    }

    private static void RestoreNativeTextLayout(TMP_Text descriptionText)
    {
        if (
            DescriptionLayoutBaselines.TryGetValue(descriptionText, out var baseline)
            && descriptionText.margin != baseline.Margin
        )
            descriptionText.margin = baseline.Margin;
        if (
            DescriptionLayoutBaselines.TryGetValue(descriptionText, out baseline)
            && !Mathf.Approximately(descriptionText.lineSpacing, baseline.LineSpacing)
        )
            descriptionText.lineSpacing = baseline.LineSpacing;
    }

    private sealed class TextLayoutBaseline(Vector4 margin, float lineSpacing)
    {
        internal Vector4 Margin { get; } = margin;
        internal float LineSpacing { get; } = lineSpacing;
    }
}

// QuestDisplayService only rebuilds the outer quest-group parent after populating its rows.
// Our entry postfix applies the preview layout or restores the native baseline after each native
// SetData call, so pooled entries can retain the previous card's height in either transition.
// Rebuild the group once all entries have been populated; the native outer-parent rebuild that
// follows then consumes the corrected sizes.
[HarmonyPatch(typeof(TooltipQuestGroup), nameof(TooltipQuestGroup.SetData))]
internal static class QuestRewardPreviewQuestGroupLayoutPatch
{
    [HarmonyPostfix]
    private static void Postfix(TooltipQuestGroup __instance)
    {
        try
        {
            __instance.ForceRebuildLayout();
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(QuestRewardPreviewLogEvents.LayoutRebuildFailed, ex);
        }
    }
}

internal static class BppQuestRewardPreviewText
{
    private const string SpriteMarkupPrefix = "<sprite";
    private const int RewardSizePercent = 76;
    internal const float VerticalMarginScale = 0.5f;
    internal const float RewardLineSpacingIncrease = 12f;
    private const float RewardInlineSizeScale = RewardSizePercent / 100f;

    public static string AppendRewardPreview(
        string? questText,
        string? passiveRewardText,
        string? activeRewardText
    )
    {
        var normalizedQuestText = (questText ?? string.Empty).Trim();
        if (normalizedQuestText.Length == 0)
            return normalizedQuestText;

        var rewardText = BuildRewardText(passiveRewardText, activeRewardText);
        if (rewardText.Length == 0)
            return normalizedQuestText;

        return $"{normalizedQuestText}\n<size={RewardSizePercent}%>{rewardText}</size>";
    }

    internal static bool ContainsInlineSprite(params string?[] values)
    {
        foreach (var value in values)
        {
            if (
                value?.IndexOf(SpriteMarkupPrefix, StringComparison.OrdinalIgnoreCase)
                >= 0
            )
                return true;
        }

        return false;
    }

    private static string BuildRewardText(params string?[] values)
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var line = ItemEnchantPreviewFormatting.ScaleInlineSizes(
                NormalizeInline(value),
                RewardInlineSizeScale
            );
            if (line.Length == 0 || !seen.Add(line))
                continue;
            lines.Add(line);
        }

        return string.Join(" / ", lines);
    }

    private static string NormalizeInline(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return string.Join(
            " ",
            text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
        );
    }
}
