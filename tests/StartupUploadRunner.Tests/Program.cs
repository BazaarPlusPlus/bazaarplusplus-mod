#nullable enable
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.Upload;

Assert(
    Enum.GetValues<UploadFeedKind>().SequenceEqual([UploadFeedKind.Bundle]),
    "V5 must expose exactly one upload feed kind."
);
Assert(
    typeof(IUploadFeed).IsAssignableFrom(typeof(BundleUploadFeed)),
    "The bundle upload feed must implement the shared feed contract."
);
Assert(BundleUploadFeed.MaximumAttemptBatch == 3, "Each attempt must upload at most 3 bundles.");

var cadence = new UploadPumpCadence(20, 180);
Assert(cadence.StartupDelaySeconds == 20, "Startup cadence drifted.");
Assert(cadence.RetryIntervalSeconds == 180, "Retry cadence drifted.");

var gate = new StartupUploadAttemptGate(cadence.StartupDelaySeconds, cadence.RetryIntervalSeconds);
Assert(
    gate.Poll(19, liveRunActive: false) == StartupUploadAttemptDecision.Wait,
    "Startup delay must be honored."
);
Assert(
    gate.Poll(20, liveRunActive: true) == StartupUploadAttemptDecision.SkipLiveRun,
    "Live runs must defer upload."
);
Assert(
    gate.Poll(20, liveRunActive: false) == StartupUploadAttemptDecision.Start,
    "The first eligible attempt must start."
);
Assert(
    gate.Poll(199, liveRunActive: false) == StartupUploadAttemptDecision.Wait,
    "Retry interval must be fixed at 180 seconds."
);
Assert(
    gate.Poll(200, liveRunActive: false) == StartupUploadAttemptDecision.Start,
    "The retry attempt must become eligible."
);

var armed = new StartupUploadAttemptGate(20, 180);
armed.ArmImmediateAttempt(3);
Assert(
    armed.Poll(3, liveRunActive: false) == StartupUploadAttemptDecision.Start,
    "An arm signal must wake the single feed."
);

Console.WriteLine("Startup upload runner tests passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
