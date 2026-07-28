#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages.CombatSimEvents;

namespace BazaarPlusPlus.Game.CombatReplay.ReportData;

internal sealed record CombatReportCardStatusInterval(
    string TargetEntityId,
    ECardAttributeType AttributeType,
    int StartFrame,
    int StartMs,
    int EndMs,
    int RawIndex
);

/// <summary>
/// Reduces the complete raw Haste/Slow/Freeze clock stream to one interval per active span.
/// Every observation participates in the derived end, so extensions, partial reductions,
/// frame-zero active state, terminal zero, and battle-end clamping remain distinguishable.
/// </summary>
internal sealed class CombatReportCardStatusIntervalBuilder
{
    private sealed class ActiveInterval
    {
        internal ActiveInterval(
            string targetEntityId,
            ECardAttributeType attributeType,
            int startFrame,
            int startMs,
            int rawIndex
        )
        {
            TargetEntityId = targetEntityId;
            AttributeType = attributeType;
            StartFrame = startFrame;
            StartMs = startMs;
            RawIndex = rawIndex;
            LastObservedMs = startMs;
        }

        internal string TargetEntityId { get; }

        internal ECardAttributeType AttributeType { get; }

        internal int StartFrame { get; }

        internal int StartMs { get; }

        internal int RawIndex { get; }

        internal int LastObservedMs { get; set; }

        internal long LastObservedValue { get; set; }
    }

    private readonly int _frameDurationMs;
    private readonly Dictionary<
        (string Target, ECardAttributeType Attribute),
        ActiveInterval
    > _active = new();
    private readonly List<CombatReportCardStatusInterval> _completed = new();
    private int _nextRawIndex;

    internal CombatReportCardStatusIntervalBuilder(int frameDurationMs)
    {
        if (frameDurationMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameDurationMs));
        _frameDurationMs = frameDurationMs;
    }

    internal static bool IsTracked(ECardAttributeType type) =>
        type is ECardAttributeType.Haste or ECardAttributeType.Slow or ECardAttributeType.Freeze;

    internal void Observe(int frame, string targetEntityId, CombatSimCardAttributeUpdate update)
    {
        if (
            frame < 0
            || string.IsNullOrWhiteSpace(targetEntityId)
            || update == null
            || !IsTracked(update.AttributeType)
        )
        {
            return;
        }

        var key = (targetEntityId, update.AttributeType);
        var combatMs = checked(frame * _frameDurationMs);
        _active.TryGetValue(key, out var active);

        if (update.CurrentValue > 0)
        {
            if (active == null || update.PreviousValue <= 0)
            {
                if (active != null)
                    Close(key, active, combatMs);
                active = Start(
                    targetEntityId,
                    update.AttributeType,
                    update.PreviousValue > 0 ? 0 : frame,
                    update.PreviousValue > 0 ? 0 : combatMs
                );
                _active[key] = active;
            }

            active.LastObservedMs = combatMs;
            active.LastObservedValue = update.CurrentValue;
            return;
        }

        if (active == null && update.PreviousValue > 0)
        {
            active = Start(targetEntityId, update.AttributeType, 0, 0);
            _active[key] = active;
        }
        if (active == null)
            return;

        active.LastObservedMs = combatMs;
        active.LastObservedValue = 0;
        Close(key, active, combatMs);
    }

    internal IReadOnlyList<CombatReportCardStatusInterval> Complete(int durationMs)
    {
        if (durationMs < 0)
            throw new ArgumentOutOfRangeException(nameof(durationMs));

        foreach (var pair in _active.ToList())
        {
            var active = pair.Value;
            var projectedEndMs = Math.Min(
                (long)durationMs,
                active.LastObservedMs + Math.Max(0L, active.LastObservedValue)
            );
            Close(pair.Key, active, (int)projectedEndMs);
        }

        return _completed
            .OrderBy(interval => interval.StartMs)
            .ThenBy(interval => interval.TargetEntityId, StringComparer.Ordinal)
            .ThenBy(interval => (int)interval.AttributeType)
            .ThenBy(interval => interval.RawIndex)
            .ToList();
    }

    private ActiveInterval Start(
        string targetEntityId,
        ECardAttributeType attributeType,
        int startFrame,
        int startMs
    ) => new(targetEntityId, attributeType, startFrame, startMs, _nextRawIndex++);

    private void Close(
        (string Target, ECardAttributeType Attribute) key,
        ActiveInterval active,
        int endMs
    )
    {
        _active.Remove(key);
        _completed.Add(
            new CombatReportCardStatusInterval(
                active.TargetEntityId,
                active.AttributeType,
                active.StartFrame,
                active.StartMs,
                Math.Max(active.StartMs + 1, endMs),
                active.RawIndex
            )
        );
    }
}
