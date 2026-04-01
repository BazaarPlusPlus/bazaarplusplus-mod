#nullable enable

Assert(
    Type.GetType("BazaarPlusPlus.Game.RunLogging.RunLogInferenceService, BazaarPlusPlus") == null,
    "RunLogInferenceService should be removed when selection inference is no longer supported."
);
Assert(
    Type.GetType("BazaarPlusPlus.Game.RunLogging.RunLogChoiceInferenceInput, BazaarPlusPlus")
        == null,
    "RunLogChoiceInferenceInput should be removed with selection inference."
);

Console.WriteLine("RunLogging inference removal checks passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
