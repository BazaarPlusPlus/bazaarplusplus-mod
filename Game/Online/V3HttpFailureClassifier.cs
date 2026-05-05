#nullable enable
namespace BazaarPlusPlus.Game.Online;

internal readonly struct V3HttpFailureDecision
{
    public V3HttpFailureDecision(bool shouldFallback)
    {
        ShouldFallback = shouldFallback;
    }

    public bool ShouldFallback { get; }
}

internal static class V3HttpFailureClassifier
{
    public static V3HttpFailureDecision Classify(int statusCode) =>
        new(shouldFallback: statusCode >= 500 || statusCode == 429);
}
