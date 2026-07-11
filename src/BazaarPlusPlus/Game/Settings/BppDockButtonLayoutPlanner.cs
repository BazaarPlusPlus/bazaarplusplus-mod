#nullable enable
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.Settings;

internal enum BppDockButtonLayoutSlot
{
    LeftOfGear,
    AboveCollection,
}

internal enum BppDockButtonLayoutFailureReason
{
    None,
    MeasurementUnavailable,
    CollectionUnavailable,
    CollectionBlocked,
    NoSettingsSlot,
}

internal readonly struct BppDockButtonLayoutPlan(
    bool canApply,
    BppDockButtonBounds collectionBounds,
    BppDockButtonBounds settingsBounds,
    BppDockButtonLayoutSlot settingsSlot,
    string? blockerName,
    bool wasAdjusted,
    BppDockButtonLayoutFailureReason failureReason = BppDockButtonLayoutFailureReason.None
)
{
    internal bool CanApply { get; } = canApply;
    internal BppDockButtonBounds CollectionBounds { get; } = collectionBounds;
    internal BppDockButtonBounds SettingsBounds { get; } = settingsBounds;
    internal BppDockButtonLayoutSlot SettingsSlot { get; } = settingsSlot;
    internal string? BlockerName { get; } = blockerName;
    internal bool WasAdjusted { get; } = wasAdjusted;
    internal BppDockButtonLayoutFailureReason FailureReason { get; } = failureReason;

    internal static BppDockButtonLayoutPlan MeasurementFailure() =>
        new(
            canApply: false,
            collectionBounds: default,
            settingsBounds: default,
            BppDockButtonLayoutSlot.LeftOfGear,
            blockerName: null,
            wasAdjusted: false,
            failureReason: BppDockButtonLayoutFailureReason.MeasurementUnavailable
        );
}

internal static class BppDockButtonLayoutPlanner
{
    internal static BppDockButtonLayoutPlan Resolve(
        BppDockButtonBounds viewportBounds,
        BppDockButtonBounds gearBounds,
        float collectionWidth,
        float collectionHeight,
        float settingsWidth,
        float settingsHeight,
        float gap,
        IReadOnlyList<BppDockButtonObstacle> blockers
    )
    {
        var safeGap = gap > 0f ? gap : 0f;
        var collectionBounds = BppDockButtonBounds.FromCenter(
            gearBounds.CenterX,
            gearBounds.MaxY + safeGap + collectionHeight * 0.5f,
            collectionWidth,
            collectionHeight
        );
        var leftSettingsBounds = BppDockButtonBounds.FromCenter(
            gearBounds.MinX - safeGap - settingsWidth * 0.5f,
            gearBounds.CenterY,
            settingsWidth,
            settingsHeight
        );

        if (
            !viewportBounds.IsValid
            || !gearBounds.IsValid
            || !collectionBounds.IsValid
            || !leftSettingsBounds.IsValid
            || collectionWidth <= 0f
            || collectionHeight <= 0f
            || settingsWidth <= 0f
            || settingsHeight <= 0f
        )
            return BppDockButtonLayoutPlan.MeasurementFailure();

        if (!viewportBounds.Contains(collectionBounds))
        {
            return new BppDockButtonLayoutPlan(
                canApply: false,
                collectionBounds,
                leftSettingsBounds,
                BppDockButtonLayoutSlot.LeftOfGear,
                blockerName: null,
                wasAdjusted: false,
                failureReason: BppDockButtonLayoutFailureReason.CollectionUnavailable
            );
        }

        var collectionBlocker = FindBlocker(collectionBounds, blockers);
        if (collectionBlocker != null)
        {
            return new BppDockButtonLayoutPlan(
                canApply: false,
                collectionBounds,
                leftSettingsBounds,
                BppDockButtonLayoutSlot.LeftOfGear,
                collectionBlocker,
                wasAdjusted: false,
                failureReason: BppDockButtonLayoutFailureReason.CollectionBlocked
            );
        }

        var leftBlocker = FindBlocker(leftSettingsBounds, blockers);
        if (viewportBounds.Contains(leftSettingsBounds) && leftBlocker == null)
        {
            return new BppDockButtonLayoutPlan(
                canApply: true,
                collectionBounds,
                leftSettingsBounds,
                BppDockButtonLayoutSlot.LeftOfGear,
                blockerName: null,
                wasAdjusted: false
            );
        }

        for (var index = 0; ; index++)
        {
            var stackedSettingsBounds = BppDockButtonBounds.FromCenter(
                collectionBounds.CenterX,
                collectionBounds.MaxY
                    + safeGap
                    + settingsHeight * 0.5f
                    + index * (settingsHeight + safeGap),
                settingsWidth,
                settingsHeight
            );
            if (stackedSettingsBounds.MinY > viewportBounds.MaxY)
                break;

            if (!viewportBounds.Contains(stackedSettingsBounds))
                continue;

            if (FindBlocker(stackedSettingsBounds, blockers) != null)
                continue;

            return new BppDockButtonLayoutPlan(
                canApply: true,
                collectionBounds,
                stackedSettingsBounds,
                BppDockButtonLayoutSlot.AboveCollection,
                leftBlocker,
                wasAdjusted: true
            );
        }

        return new BppDockButtonLayoutPlan(
            canApply: false,
            collectionBounds,
            leftSettingsBounds,
            BppDockButtonLayoutSlot.LeftOfGear,
            leftBlocker,
            wasAdjusted: false,
            failureReason: BppDockButtonLayoutFailureReason.NoSettingsSlot
        );
    }

    private static string? FindBlocker(
        BppDockButtonBounds candidateBounds,
        IReadOnlyList<BppDockButtonObstacle> blockers
    )
    {
        for (var index = 0; index < blockers.Count; index++)
        {
            var blocker = blockers[index];
            if (blocker.IsActive && candidateBounds.Overlaps(blocker.Bounds))
                return blocker.Name;
        }

        return null;
    }
}
