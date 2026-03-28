#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace BazaarPlusPlus.Game.CombatLog;

internal static class CombatLogFormatter
{
    internal interface ICombatLogLineFormatter
    {
        CombatLogDisplayRowViewModel FormatEvent(
            CombatLogFrame frame,
            CombatLogEventEntry entry,
            CombatLogDisplayOptions options
        );

        CombatLogDisplayRowViewModel FormatCombatant(
            CombatLogFrame frame,
            string side,
            CombatLogHealthAdjustment health
        );

        CombatLogDisplayRowViewModel FormatCombatant(
            CombatLogFrame frame,
            string side,
            CombatLogAttributeChange attribute
        );

        CombatLogDisplayRowViewModel FormatCard(
            CombatLogFrame frame,
            CombatLogCardUpdateEntry entry,
            CombatLogAttributeChange attribute
        );

        CombatLogDisplayRowViewModel FormatCardDetail(
            CombatLogFrame frame,
            CombatLogCardUpdateEntry entry,
            string detail
        );
    }

    internal static IReadOnlyList<CombatLogRow> BuildRows(IReadOnlyList<CombatLogFrame> frames)
    {
        var rows = new List<CombatLogRow>();
        foreach (var frame in frames)
            AppendFrameRows(rows, frame);

        return rows;
    }

    private static void AppendFrameRows(List<CombatLogRow> rows, CombatLogFrame frame)
    {
        AppendEventRows(rows, frame);
        AppendSideRows(rows, frame, frame.Player);
        AppendSideRows(rows, frame, frame.Opponent);
        AppendCardRows(rows, frame);
    }

    private static void AppendEventRows(List<CombatLogRow> rows, CombatLogFrame frame)
    {
        foreach (var entry in frame.Events)
        {
            var category = GetEventCategory(entry.EventType);
            rows.Add(
                new CombatLogRow(
                    frame.FrameIndex,
                    frame.LogicalTime,
                    category,
                    CombatLogEventTextBuilder.BuildPrimaryText(entry),
                    BuildEventSecondaryText(entry)
                )
            );
        }
    }

    private static void AppendSideRows(
        List<CombatLogRow> rows,
        CombatLogFrame frame,
        CombatLogSideUpdate? side
    )
    {
        if (side == null)
            return;

        foreach (var health in side.HealthAdjustments)
        {
            var suffix =
                health.IsCrit ? " crit"
                : health.IsReduced ? " reduced"
                : string.Empty;
            rows.Add(
                new CombatLogRow(
                    frame.FrameIndex,
                    frame.LogicalTime,
                    CombatLogRowCategory.Health,
                    $"{side.Side} {health.HealthType} {health.Amount:+#;-#;0}{suffix}".TrimEnd()
                )
            );
        }

        foreach (var attribute in side.Attributes)
        {
            rows.Add(
                new CombatLogRow(
                    frame.FrameIndex,
                    frame.LogicalTime,
                    CombatLogRowCategory.Attribute,
                    $"{side.Side} {attribute.AttributeKey} {attribute.PreviousValue} -> {attribute.CurrentValue}"
                )
            );
        }

        foreach (var detail in side.Details)
        {
            rows.Add(
                new CombatLogRow(
                    frame.FrameIndex,
                    frame.LogicalTime,
                    CombatLogRowCategory.Attribute,
                    $"{side.Side} {detail}"
                )
            );
        }
    }

    private static void AppendCardRows(List<CombatLogRow> rows, CombatLogFrame frame)
    {
        foreach (var entry in frame.CardUpdates.OrderBy(item => item.Card.DisplayName))
        {
            foreach (var attribute in entry.Attributes.OrderBy(item => item.AttributeKey))
            {
                rows.Add(
                    new CombatLogRow(
                        frame.FrameIndex,
                        frame.LogicalTime,
                        CombatLogRowCategory.CardAttribute,
                        $"{entry.Card.DisplayName} {attribute.AttributeKey} {attribute.PreviousValue} -> {attribute.CurrentValue}",
                        BuildCardSecondaryText(entry.Card)
                    )
                );
            }

            foreach (var detail in entry.Details)
            {
                rows.Add(
                    new CombatLogRow(
                        frame.FrameIndex,
                        frame.LogicalTime,
                        CombatLogRowCategory.CardAttribute,
                        $"{entry.Card.DisplayName} {detail}",
                        BuildCardSecondaryText(entry.Card)
                    )
                );
            }
        }
    }

    private static string? BuildEventSecondaryText(CombatLogEventEntry entry)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.ExecutionContextId))
            parts.Add($"ctx: {entry.ExecutionContextId}");
        if (!string.IsNullOrWhiteSpace(entry.SourceId))
            parts.Add($"src: {entry.SourceId}");
        if (!string.IsNullOrWhiteSpace(entry.TriggerSourceId))
            parts.Add($"trigger: {entry.TriggerSourceId}");
        if (!string.IsNullOrWhiteSpace(entry.TargetId))
            parts.Add($"target: {entry.TargetId}");
        if (!string.IsNullOrWhiteSpace(entry.TargetKind))
            parts.Add($"kind: {entry.TargetKind}");

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string BuildCardSecondaryText(CombatLogCardDisplayInfo card)
    {
        var parts = new List<string> { $"id: {card.InstanceId}" };
        if (!string.IsNullOrWhiteSpace(card.TemplateId))
            parts.Add($"tpl: {card.TemplateId}");

        return string.Join(" | ", parts);
    }

    internal static CombatLogRowCategory GetEventCategory(string eventType)
    {
        return eventType switch
        {
            "CombatantDied" => CombatLogRowCategory.Death,
            "MonsterGoldReceived" or "MonsterXpReceived" => CombatLogRowCategory.Reward,
            "SandstormCountdownStarted" or "SandstormStarted" => CombatLogRowCategory.System,
            "EffectExecuted"
            or "EffectTriggered"
            or "EffectAuraExecuted"
            or "CardEnchanted"
            or "CardTransformed"
            or "CardTransformReverted"
            or "CardQuestCompleted"
            or "CardQuestUpdated" => CombatLogRowCategory.Event,
            _ => CombatLogRowCategory.Unknown,
        };
    }

    internal static bool ShouldInclude(CombatLogDisplayOptions options, CombatLogRowCategory category)
    {
        return category switch
        {
            CombatLogRowCategory.Event or CombatLogRowCategory.Death => options.ShowEvents,
            CombatLogRowCategory.Health or CombatLogRowCategory.Attribute => options.ShowCombatants,
            CombatLogRowCategory.CardAttribute => options.ShowCards,
            CombatLogRowCategory.Reward => options.ShowRewards,
            CombatLogRowCategory.System => options.ShowSystem,
            CombatLogRowCategory.Unknown => options.ShowUnknown,
            _ => true,
        };
    }
}
