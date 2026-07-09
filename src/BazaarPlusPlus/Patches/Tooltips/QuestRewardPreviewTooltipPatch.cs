#nullable enable
#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BazaarGameShared.Domain.Tooltips;
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
            if (DescriptionTextField?.GetValue(__instance) is not TMP_Text descriptionText)
                return;

            var questTooltips = entry.QuestEntry.Localization?.Tooltips;
            var rewardTooltips = entry.QuestEntry.Reward?.Localization?.Tooltips;
            if (questTooltips == null || rewardTooltips == null)
                return;

            var questText = currentTooltipData.RenderQuestTooltips(
                questTooltips,
                ETooltipType.Passive
            );
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
            BppLog.Error("QuestTooltip", "Failed to append quest reward preview", ex);
        }
    }
}

internal static class BppQuestRewardPreviewText
{
    private const int RewardSizePercent = 55;
    private const float RewardInlineSizeScale = RewardSizePercent / 100f;

    private static readonly Regex SizeTagRegex = new Regex(
        "<size=(\\d+)%>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

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
            var line = ScaleInlineSizes(NormalizeInline(value), RewardInlineSizeScale);
            if (line.Length == 0 || !seen.Add(line))
                continue;
            lines.Add(line);
        }

        return string.Join(" / ", lines);
    }

    private static string ScaleInlineSizes(string text, float scale)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return SizeTagRegex.Replace(
            text,
            match =>
            {
                if (!int.TryParse(match.Groups[1].Value, out var size))
                    return match.Value;

                var scaledSize = Math.Max(1, (int)Math.Round(size * scale));
                return $"<size={scaledSize}%>";
            }
        );
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
