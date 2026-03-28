#nullable enable
namespace BazaarPlusPlus.Game.CombatLog;

internal static class CombatLogEventTextBuilder
{
    internal static string BuildPrimaryText(CombatLogEventEntry entry)
    {
        var data = entry.FormatData;
        return entry.EventType switch
        {
            "EffectExecuted" => FormatEffectExecuted(entry, data),
            "CombatantDied" => CombatLogTextLocalizer.CombatantDied(GetTargetText(entry)),
            "MonsterGoldReceived" or "MonsterXpReceived" => FormatReward(data),
            "EffectTriggered" => CombatLogTextLocalizer.EffectTriggered(
                data?.EffectId ?? "unknown-effect",
                GetSourceText(entry),
                GetTargetText(entry)
            ),
            "EffectAuraExecuted" => FormatAura(entry, data),
            "CardEnchanted" => FormatEnchantment(entry, data),
            "CardTransformed" => FormatTransform(entry, data),
            "CardTransformReverted" => FormatTransformReverted(data),
            "CardQuestCompleted" => FormatQuest(entry, data, completed: true),
            "CardQuestUpdated" => FormatQuest(entry, data, completed: false),
            "SandstormCountdownStarted" => CombatLogTextLocalizer.SystemEventCountdownStarted(),
            "SandstormStarted" => CombatLogTextLocalizer.SystemEventStarted(),
            _ => data?.FallbackText ?? entry.Text,
        };
    }

    private static string FormatEffectExecuted(
        CombatLogEventEntry entry,
        CombatLogEventFormatData? data
    )
    {
        return CombatLogTextLocalizer.EffectExecuted(
            data?.ActionType,
            data?.EffectId ?? "unknown-effect",
            GetSourceText(entry),
            GetTargetText(entry)
        );
    }

    private static string FormatReward(CombatLogEventFormatData? data)
    {
        var amount = data?.Amount ?? 0d;
        return CombatLogTextLocalizer.Reward(data?.RewardKind ?? "Reward", amount);
    }

    private static string FormatAura(CombatLogEventEntry entry, CombatLogEventFormatData? data)
    {
        return CombatLogTextLocalizer.Aura(
            data?.EffectId ?? "unknown-effect",
            GetSourceText(entry),
            data?.AuraAppliedDisplay,
            data?.AuraRemovedDisplay
        );
    }

    private static string FormatEnchantment(
        CombatLogEventEntry entry,
        CombatLogEventFormatData? data
    )
    {
        return CombatLogTextLocalizer.Enchantment(
            GetSourceText(entry),
            data?.EnchantmentLabel ?? "none",
            data?.IsReverted == true
        );
    }

    private static string FormatTransform(CombatLogEventEntry entry, CombatLogEventFormatData? data)
    {
        var transformedCards =
            data?.RelatedDisplayItems.Count > 0
                ? string.Join(", ", data.RelatedDisplayItems)
                : "none";
        return CombatLogTextLocalizer.Transform(GetSourceText(entry), transformedCards);
    }

    private static string FormatTransformReverted(CombatLogEventFormatData? data)
    {
        var original = data?.OriginalDisplayText ?? "unknown";
        var revertedIds =
            data?.RelatedRawItems.Count > 0 ? string.Join(", ", data.RelatedRawItems) : "none";
        return CombatLogTextLocalizer.TransformReverted(original, revertedIds);
    }

    private static string FormatQuest(
        CombatLogEventEntry entry,
        CombatLogEventFormatData? data,
        bool completed
    )
    {
        if (completed)
        {
            return CombatLogTextLocalizer.QuestCompleted(
                GetSourceText(entry),
                data?.QuestGroupIndex ?? 0,
                data?.QuestEntryIndex ?? 0
            );
        }

        return CombatLogTextLocalizer.QuestUpdated(
            GetSourceText(entry),
            data?.QuestGroupIndex ?? 0,
            data?.QuestEntryIndex ?? 0,
            data?.PreviousProgress ?? 0,
            data?.CurrentProgress ?? 0
        );
    }

    private static string GetSourceText(CombatLogEventEntry entry)
    {
        return entry.SourceDisplayName ?? entry.SourceId ?? "unknown-source";
    }

    private static string GetTargetText(CombatLogEventEntry entry)
    {
        return entry.TargetDisplayName ?? entry.TargetId ?? "unknown-target";
    }
}
