#nullable enable
using System;
using System.Threading;
using BazaarPlusPlus.Game.CollectionPanel;

namespace BazaarPlusPlus.Game.EventPreview;

internal static class EventPreviewPlanRuntime
{
    private static CollectionEncounterPreviewPlanRegistry? _registry;

    public static void Install(CollectionEncounterPreviewPlanRegistry registry)
    {
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));
        Volatile.Write(ref _registry, registry);
    }

    public static void Reset(CollectionEncounterPreviewPlanRegistry registry)
    {
        if (ReferenceEquals(Volatile.Read(ref _registry), registry))
            Volatile.Write(ref _registry, null);
    }

    public static bool TryGet(
        object source,
        Guid eventTemplateId,
        out CollectionEncounterPreviewEventPlan eventPlan,
        out CollectionEncounterPreviewSnapshot snapshot
    )
    {
        var registry = Volatile.Read(ref _registry);
        if (
            registry != null
            && registry.TryGet(source, out snapshot)
            && snapshot.TryGetEvent(eventTemplateId, out eventPlan)
        )
            return true;

        eventPlan = null!;
        snapshot = null!;
        return false;
    }

    public static bool TryGetLevelUp(
        object source,
        int currentLevel,
        out CollectionLevelUpPreviewPlan levelUpPlan,
        out CollectionEncounterPreviewSnapshot snapshot
    )
    {
        var registry = Volatile.Read(ref _registry);
        if (
            registry != null
            && registry.TryGet(source, out snapshot)
            && snapshot.TryGetLevelUp(currentLevel, out levelUpPlan)
        )
            return true;

        levelUpPlan = null!;
        snapshot = null!;
        return false;
    }

    public static bool TryGetTemplate(
        object source,
        Guid templateId,
        out CollectionEncounterPreviewTemplatePlan templatePlan
    )
    {
        var registry = Volatile.Read(ref _registry);
        if (
            registry != null
            && registry.TryGet(source, out var snapshot)
            && snapshot.TryGetTemplate(templateId, out templatePlan)
        )
            return true;

        templatePlan = null!;
        return false;
    }
}
