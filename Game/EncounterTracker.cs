#pragma warning disable CS0436
#nullable enable
using System;
using BazaarGameShared.Domain.Runs;
using BazaarPlusPlus;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.RunContext;

namespace BazaarPlusPlus.Game.EncounterTracking;

internal static class EncounterTracker
{
    private static EncounterTrackingFeature? Feature;

    internal static IEncounterSelectionQuery SelectionQuery => RequireFeature().SelectionQuery;

    internal static void Initialize(
        IBppEventBus eventBus,
        IRunContext runContext,
        IMonsterCatalog monsterCatalog
    )
    {
        if (eventBus == null)
            throw new ArgumentNullException(nameof(eventBus));
        if (runContext == null)
            throw new ArgumentNullException(nameof(runContext));
        if (monsterCatalog == null)
            throw new ArgumentNullException(nameof(monsterCatalog));
        ReplaceFeature(
            new EncounterTrackingFeature(eventBus, runContext, monsterCatalog)
        );
    }

    internal static void AttachFeature(EncounterTrackingFeature feature)
    {
        if (feature == null)
            throw new ArgumentNullException(nameof(feature));

        ReplaceFeature(feature);
    }

    internal static void DetachFeature()
    {
        ReplaceFeature(null);
    }

    public static void Subscribe()
    {
        RequireFeature().Start();
    }

    internal static bool IsSupportedSelectionState(ERunState stateName)
    {
        return RequireFeature().IsSupportedSelectionState(stateName);
    }

    internal static void ResetEncounterState(string reason)
    {
        RequireFeature().ResetEncounterState(reason);
    }

    private static EncounterTrackingFeature RequireFeature()
    {
        return Feature
            ?? throw new InvalidOperationException("EncounterTracker is not initialized.");
    }

    private static void ReplaceFeature(EncounterTrackingFeature? nextFeature)
    {
        var previousFeature = Feature;
        if (ReferenceEquals(previousFeature, nextFeature))
            return;

        if (previousFeature != null)
            previousFeature.Stop();

        Feature = nextFeature;
    }
}
