#nullable enable

namespace BazaarPlusPlus.Game.CombatLog;

internal sealed class CombatLogReleaseFormatter : CombatLogFormatter.ICombatLogLineFormatter
{
    public CombatLogDisplayRowViewModel FormatEvent(
        CombatLogFrame frame,
        CombatLogEventEntry entry,
        CombatLogDisplayOptions options
    )
    {
        return new CombatLogDisplayRowViewModel(
            CombatLogEventTextBuilder.BuildPrimaryText(entry),
            null,
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
            null,
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
            null,
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
            null,
            false
        );
    }

    public CombatLogDisplayRowViewModel FormatCardDetail(
        CombatLogFrame frame,
        CombatLogCardUpdateEntry entry,
        string detail
    )
    {
        return new CombatLogDisplayRowViewModel($"{entry.Card.DisplayName} {detail}", null, false);
    }
}
