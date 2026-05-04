#nullable enable
namespace BazaarPlusPlus.Game.Online;

internal readonly struct V3HttpFailureDecision
{
    public V3HttpFailureDecision(bool shouldFallback, bool shouldReRegister)
    {
        ShouldFallback = shouldFallback;
        ShouldReRegister = shouldReRegister;
    }

    public bool ShouldFallback { get; }
    public bool ShouldReRegister { get; }
}

internal static class V3HttpFailureClassifier
{
    public static V3HttpFailureDecision Classify(int statusCode) =>
        new(
            shouldFallback: statusCode >= 500 || statusCode == 429,
            shouldReRegister: statusCode == 401 || statusCode == 403
        );
}
