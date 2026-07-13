#nullable enable
#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BazaarGameShared.Domain.Tooltips;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.QuestRewardPreview;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using TMPro;

namespace BazaarPlusPlus.Patches.Tooltips;

[HarmonyPatch(typeof(TooltipQuestEntry), nameof(TooltipQuestEntry.SetData))]
internal static class QuestRewardPreviewTooltipPatch
{
    private static readonly FieldInfo? DescriptionTextField = AccessTools.Field(
        typeof(TooltipQuestEntry),
        "_descriptionText"
    );

    [HarmonyPostfix]
    private static void Postfix(
        TooltipQuestEntry __instance,
        CardQuestEntryData entry,
        CardTooltipData currentTooltipData
    )
    {
        try
        {
            if (!QuestRewardPreviewGate.IsEnabled())
                return;

            // The native icon remains useful as a compact reward marker, but unlocked tooltips
            // cannot enter its nested hover. Keep the reward text inline on every tooltip surface.
            if (DescriptionTextField?.GetValue(__instance) is not TMP_Text descriptionText)
                return;

            var rewardTooltips = entry.QuestEntry.Reward?.Localization?.Tooltips;
            if (rewardTooltips == null)
                return;

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

            if (!string.Equals(text, questText, StringComparison.Ordinal))
                descriptionText.text = text;
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                TooltipLogEvents.SectionDegraded,
                ex,
                TooltipLogEvents.SectionDegradedSectionId.Bind(TooltipSectionId.QuestRewardPreview),
                TooltipLogEvents.SectionDegradedReasonCode.Bind(
                    TooltipLogReasonCode.RenderException
                )
            );
        }
    }
}

internal static class BppQuestRewardPreviewText
{
    private const int RewardSizePercent = 55;
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
