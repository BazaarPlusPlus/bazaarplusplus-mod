using BazaarPlusPlus.Game.CollectionPanel.DealerModel;

Check.Section("state enum");
Check.Equal(
    0,
    (int)CollectionDealerProbabilityState.NotInPool,
    "NotInPool should be the default zero state."
);

Check.Finish();

internal static class Check
{
    private static int _failures;

    public static void Section(string name)
    {
        Console.WriteLine($"== {name} ==");
    }

    public static void True(bool condition, string message)
    {
        if (condition)
        {
            return;
        }

        _failures++;
        Console.Error.WriteLine($"FAIL: {message}");
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
        {
            return;
        }

        _failures++;
        Console.Error.WriteLine($"FAIL: {message} Expected={expected} Actual={actual}");
    }

    public static void Finish()
    {
        if (_failures == 0)
        {
            Console.WriteLine("All checks passed.");
            return;
        }

        Console.Error.WriteLine($"{_failures} check(s) failed.");
        Environment.Exit(1);
    }
}
