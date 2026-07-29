#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

public enum BazaarAgentActionObservationStatus
{
    Pending,
    Confirmed,
    TimedOut,
}

public static class BazaarAgentActionObservation
{
    public static BazaarAgentActionObservationStatus Evaluate(
        BazaarAgentContext baseline,
        BazaarAgentContext? current,
        double nowSeconds,
        double deadlineSeconds
    )
    {
        if (
            current is not null
            && BazaarAgentContextSnapshotPublisher.HasGameplayStateChanged(baseline, current)
        )
        {
            return BazaarAgentActionObservationStatus.Confirmed;
        }

        return nowSeconds >= deadlineSeconds
            ? BazaarAgentActionObservationStatus.TimedOut
            : BazaarAgentActionObservationStatus.Pending;
    }
}
