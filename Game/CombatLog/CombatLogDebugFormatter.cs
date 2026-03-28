#nullable enable
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CombatLog;

internal sealed class CombatLogDebugFormatter : CombatLogFormatter.ICombatLogLineFormatter
{
    public CombatLogDisplayRowViewModel FormatEvent(
        CombatLogFrame frame,
        CombatLogEventEntry entry,
        CombatLogDisplayOptions options
    )
    {
        var secondary = BuildEventSecondary(entry);
        return new CombatLogDisplayRowViewModel(
            CombatLogEventTextBuilder.BuildPrimaryText(entry),
            secondary,
            false
        );
    }

    public CombatLogDisplayRowViewModel FormatCombatant(
        CombatLogFrame frame,
        string side,
        CombatLogHealthAdjustment health
    )
    {
        var suffix =
            health.IsCrit ? " crit"
            : health.IsReduced ? " reduced"
            : string.Empty;
        return new CombatLogDisplayRowViewModel(
            $"{side} {health.HealthType} {health.Amount:+#;-#;0}{suffix}".TrimEnd(),
            $"frame={frame.FrameIndex}",
            false
        );
    }

    public CombatLogDisplayRowViewModel FormatCombatant(
        CombatLogFrame frame,
        string side,
        CombatLogAttributeChange attribute
    )
    {
        return new CombatLogDisplayRowViewModel(
            $"{side} {attribute.AttributeKey} {attribute.PreviousValue} -> {attribute.CurrentValue}",
            $"frame={frame.FrameIndex}",
            false
        );
    }

    public CombatLogDisplayRowViewModel FormatCard(
        CombatLogFrame frame,
        CombatLogCardUpdateEntry entry,
        CombatLogAttributeChange attribute
    )
    {
        return new CombatLogDisplayRowViewModel(
            $"{entry.Card.DisplayName} {attribute.AttributeKey} {attribute.PreviousValue} -> {attribute.CurrentValue}",
            BuildCardSecondary(entry.Card),
            false
        );
    }

    public CombatLogDisplayRowViewModel FormatCardDetail(
        CombatLogFrame frame,
        CombatLogCardUpdateEntry entry,
        string detail
    )
    {
        return new CombatLogDisplayRowViewModel(
            $"{entry.Card.DisplayName} {detail}",
            BuildCardSecondary(entry.Card),
            false
        );
    }

    private static string? BuildEventSecondary(CombatLogEventEntry entry)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.ExecutionContextId))
            parts.Add($"ctx={entry.ExecutionContextId}");
        if (!string.IsNullOrWhiteSpace(entry.SourceId))
            parts.Add($"src={entry.SourceId}");
        if (!string.IsNullOrWhiteSpace(entry.TriggerSourceId))
            parts.Add($"trigger={entry.TriggerSourceId}");
        if (!string.IsNullOrWhiteSpace(entry.TargetId))
            parts.Add($"target={entry.TargetId}");
        if (!string.IsNullOrWhiteSpace(entry.TargetKind))
            parts.Add($"targetKind={entry.TargetKind}");
        if (entry.FormatData?.RelatedRawItems.Count > 0)
            parts.Add($"related={string.Join(", ", entry.FormatData.RelatedRawItems)}");

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string BuildCardSecondary(CombatLogCardDisplayInfo card)
    {
        var parts = new List<string> { $"id={card.InstanceId}" };
        if (!string.IsNullOrWhiteSpace(card.TemplateId))
            parts.Add($"tpl={card.TemplateId}");

        return string.Join(" | ", parts);
    }
}
