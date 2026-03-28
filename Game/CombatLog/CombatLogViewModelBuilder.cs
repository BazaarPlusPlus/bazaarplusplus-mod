#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace BazaarPlusPlus.Game.CombatLog;

internal sealed class CombatLogViewModelBuilder
{
    private static readonly CombatLogFormatter.ICombatLogLineFormatter ReleaseFormatter =
        new CombatLogReleaseFormatter();
    private static readonly CombatLogFormatter.ICombatLogLineFormatter DebugFormatter =
        new CombatLogDebugFormatter();

    public IReadOnlyList<CombatLogFrameGroupViewModel> Build(
        CombatLogTimeline timeline,
        CombatLogDisplayOptions options,
        IReadOnlyCollection<int> expandedFrames,
        int processedFrameCount = int.MaxValue
    )
    {
        var expandedFrameSet = new HashSet<int>(expandedFrames);
        var groups = new List<CombatLogFrameGroupViewModel>(timeline.Frames.Count);
        foreach (var frame in timeline.Frames)
        {
            var totalVisibleRowCount = CountVisibleRows(frame, options);
            if (totalVisibleRowCount == 0 && !options.ShowEmptyFrames)
                continue;

            var visualState = CombatLogPlaybackState.GetVisualState(
                frame.FrameIndex,
                processedFrameCount,
                timeline.PlaybackPass
            );
            var expanded =
                expandedFrameSet.Contains(frame.FrameIndex)
                || options.Verbosity != CombatLogVerbosity.Verbose;
            IReadOnlyList<CombatLogDisplayRowViewModel> rows = expanded
                ? BuildExpandedRows(frame, options, visualState)
                : System.Array.Empty<CombatLogDisplayRowViewModel>();
            groups.Add(
                new CombatLogFrameGroupViewModel(
                    frame.FrameIndex,
                    frame.LogicalTime,
                    CombatLogFrameSummaryBuilder.Build(frame, options),
                    visualState,
                    expanded,
                    rows.Count,
                    totalVisibleRowCount,
                    rows
                )
            );
        }

        return groups;
    }

    private List<CombatLogDisplayRowViewModel> BuildExpandedRows(
        CombatLogFrame frame,
        CombatLogDisplayOptions options,
        CombatLogRowVisualState visualState
    )
    {
        var rows = new List<CombatLogDisplayRowViewModel>();
        var formatter = GetFormatter(options.Mode);

        foreach (var entry in frame.Events)
        {
            var category = CombatLogFormatter.GetEventCategory(entry.EventType);
            if (!CombatLogFormatter.ShouldInclude(options, category))
                continue;

            rows.Add(WithVisualState(formatter.FormatEvent(frame, entry, options), visualState));
        }

        AppendSideRows(rows, formatter, frame, frame.Player, options, visualState);
        AppendSideRows(rows, formatter, frame, frame.Opponent, options, visualState);

        if (options.ShowCards)
        {
            foreach (var cardUpdate in frame.CardUpdates.OrderBy(item => item.Card.DisplayName))
            {
                foreach (var attribute in cardUpdate.Attributes.OrderBy(item => item.AttributeKey))
                    rows.Add(
                        WithVisualState(
                            formatter.FormatCard(frame, cardUpdate, attribute),
                            visualState
                        )
                    );

                foreach (var detail in cardUpdate.Details)
                    rows.Add(
                        WithVisualState(
                            formatter.FormatCardDetail(frame, cardUpdate, detail),
                            visualState
                        )
                    );
            }
        }

        return rows;
    }

    private int CountVisibleRows(CombatLogFrame frame, CombatLogDisplayOptions options)
    {
        var count = 0;
        foreach (var entry in frame.Events)
        {
            var category = CombatLogFormatter.GetEventCategory(entry.EventType);
            if (CombatLogFormatter.ShouldInclude(options, category))
                count++;
        }

        if (options.ShowCombatants)
        {
            count += CountSideRows(frame.Player);
            count += CountSideRows(frame.Opponent);
        }

        if (options.ShowCards)
        {
            foreach (var cardUpdate in frame.CardUpdates)
                count += cardUpdate.Attributes.Count + cardUpdate.Details.Count;
        }

        return count;
    }

    private static void AppendSideRows(
        List<CombatLogDisplayRowViewModel> rows,
        CombatLogFormatter.ICombatLogLineFormatter formatter,
        CombatLogFrame frame,
        CombatLogSideUpdate? side,
        CombatLogDisplayOptions options,
        CombatLogRowVisualState visualState
    )
    {
        if (side == null || !options.ShowCombatants)
            return;

        foreach (var health in side.HealthAdjustments)
            rows.Add(WithVisualState(formatter.FormatCombatant(frame, side.Side, health), visualState));

        foreach (var attribute in side.Attributes)
            rows.Add(
                WithVisualState(formatter.FormatCombatant(frame, side.Side, attribute), visualState)
            );

        foreach (var detail in side.Details)
        {
            rows.Add(
                new CombatLogDisplayRowViewModel(
                    $"{side.Side} {detail}",
                    options.Mode == CombatLogDisplayMode.Debug ? $"frame={frame.FrameIndex}" : null,
                    false,
                    visualState
                )
            );
        }
    }

    private static int CountSideRows(CombatLogSideUpdate? side)
    {
        if (side == null)
            return 0;

        return side.HealthAdjustments.Count + side.Attributes.Count + side.Details.Count;
    }

    private static CombatLogFormatter.ICombatLogLineFormatter GetFormatter(
        CombatLogDisplayMode mode
    )
    {
        return mode == CombatLogDisplayMode.Debug ? DebugFormatter : ReleaseFormatter;
    }

    private static CombatLogDisplayRowViewModel WithVisualState(
        CombatLogDisplayRowViewModel row,
        CombatLogRowVisualState visualState
    )
    {
        return new CombatLogDisplayRowViewModel(
            row.PrimaryText,
            row.SecondaryText,
            row.Emphasize,
            visualState
        );
    }
}
