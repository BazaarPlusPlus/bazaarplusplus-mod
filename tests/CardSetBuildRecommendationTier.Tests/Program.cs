using System.Reflection;

RegisterAssemblyResolution();
TestRecommendationTierMapping();

Console.WriteLine("CardSetBuildRecommendationTier checks passed.");

static void RegisterAssemblyResolution()
{
    AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
    {
        var assemblyName = new AssemblyName(args.Name);
        if (!string.Equals(assemblyName.Name, "UnityEngine.CoreModule", StringComparison.Ordinal))
            return null;

        var assemblyPath = Path.Combine(AppContext.BaseDirectory, "UnityEngine.CoreModule.dll");
        return File.Exists(assemblyPath) ? Assembly.LoadFrom(assemblyPath) : null;
    };
}

static void TestRecommendationTierMapping()
{
    var assembly = typeof(BazaarPlusPlus.RunInfo).Assembly;
    var repositoryType = assembly.GetType(
        "BazaarPlusPlus.Game.MonsterPreview.CardSetBuildDataRepository"
    )!;
    var playerCardEntryType = repositoryType.GetNestedType(
        "PlayerCardEntry",
        BindingFlags.NonPublic
    )!;
    var projectMethod = repositoryType.GetMethod(
        "ProjectPlayerCards",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    AssertMappedTier(projectMethod, playerCardEntryType, 1, "Bronze");
    AssertMappedTier(projectMethod, playerCardEntryType, 4, "Diamond");
    AssertMappedTier(projectMethod, playerCardEntryType, 5, "Legendary");
}

static void AssertMappedTier(
    MethodInfo projectMethod,
    Type playerCardEntryType,
    int rawTier,
    string expected
)
{
    var entries = Array.CreateInstance(playerCardEntryType, 1);
    var entry = Activator.CreateInstance(playerCardEntryType)!;
    playerCardEntryType.GetProperty("CardId")!.SetValue(
        entry,
        "11111111-1111-1111-1111-111111111111"
    );
    playerCardEntryType.GetProperty("Slot")!.SetValue(entry, 0);
    playerCardEntryType.GetProperty("Tier")!.SetValue(entry, rawTier);
    entries.SetValue(entry, 0);

    var result = (System.Collections.IEnumerable)projectMethod.Invoke(null, [entries])!;
    var projected = result.Cast<object>().Single();
    var actual = projected.GetType().GetProperty("Tier")!.GetValue(projected)!.ToString();

    Assert(
        actual == expected,
        $"Expected recommendation tier {rawTier} to map to {expected}, but was {actual}."
    );
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
