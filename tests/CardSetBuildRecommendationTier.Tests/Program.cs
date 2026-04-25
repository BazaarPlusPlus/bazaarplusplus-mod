using System.Reflection;

RegisterAssemblyResolution();
TestRecommendationTierMapping();
TestFreshFinalBuildCacheIsUsedWithoutRemoteDownload();
TestExpiredFinalBuildCacheDownloadsRemotePayload();

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

static void TestFreshFinalBuildCacheIsUsedWithoutRemoteDownload()
{
    var repositoryType = GetRepositoryType();
    var now = new DateTime(2026, 04, 25, 12, 0, 0, DateTimeKind.Utc);
    var cachePath = Path.Combine(
        Path.GetTempPath(),
        $"bpp-final-build-cache-{Guid.NewGuid():N}.json"
    );
    var selectedCardId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    File.WriteAllText(
        cachePath,
        CreateFinalBuildPayload("RemoteHero", selectedCardId, "fresh-cache")
    );
    File.SetLastWriteTimeUtc(cachePath, now.AddHours(-19));

    ConfigureFinalBuildRemoteForTests(
        repositoryType,
        cachePath,
        now,
        _ => throw new InvalidOperationException("Fresh cache should not download remote data.")
    );

    try
    {
        var sources = LoadFinalBuildSources(repositoryType, "RemoteHero");

        Assert(sources.Count == 1, "Fresh cached final builds should be loadable.");
        Assert(
            sources[0] == "fresh-cache",
            "Fresh cached final builds should override the embedded resource."
        );
    }
    finally
    {
        ResetFinalBuildRemoteForTests(repositoryType);
        TryDelete(cachePath);
    }
}

static void TestExpiredFinalBuildCacheDownloadsRemotePayload()
{
    var repositoryType = GetRepositoryType();
    var now = new DateTime(2026, 04, 25, 12, 0, 0, DateTimeKind.Utc);
    var cachePath = Path.Combine(
        Path.GetTempPath(),
        $"bpp-final-build-cache-{Guid.NewGuid():N}.json"
    );
    var selectedCardId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    File.WriteAllText(
        cachePath,
        CreateFinalBuildPayload("StaleHero", selectedCardId, "stale-cache")
    );
    File.SetLastWriteTimeUtc(cachePath, now.AddHours(-21));

    var downloaded = false;
    var remotePayload = CreateFinalBuildPayload("RemoteHero", selectedCardId, "remote-download");
    ConfigureFinalBuildRemoteForTests(
        repositoryType,
        cachePath,
        now,
        _ =>
        {
            downloaded = true;
            return remotePayload;
        }
    );

    try
    {
        var sources = LoadFinalBuildSources(repositoryType, "RemoteHero");

        Assert(downloaded, "Expired final build cache should trigger a remote download.");
        Assert(sources.Count == 1, "Downloaded final builds should be loadable.");
        Assert(
            sources[0] == "remote-download",
            "Downloaded final builds should override stale cache data."
        );
        Assert(
            File.ReadAllText(cachePath) == remotePayload,
            "Downloaded final builds should be persisted to the disk cache."
        );
    }
    finally
    {
        ResetFinalBuildRemoteForTests(repositoryType);
        TryDelete(cachePath);
    }
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
    playerCardEntryType
        .GetProperty("CardId")!
        .SetValue(entry, "11111111-1111-1111-1111-111111111111");
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

static Type GetRepositoryType()
{
    var assembly = typeof(BazaarPlusPlus.RunInfo).Assembly;
    return assembly.GetType("BazaarPlusPlus.Game.MonsterPreview.CardSetBuildDataRepository")!;
}

static void ConfigureFinalBuildRemoteForTests(
    Type repositoryType,
    string cachePath,
    DateTime utcNow,
    Func<string, string> downloadJson
)
{
    InvokeStatic(
        repositoryType,
        "ConfigureFinalBuildRemoteForTests",
        cachePath,
        (Func<DateTime>)(() => utcNow),
        downloadJson
    );
}

static void ResetFinalBuildRemoteForTests(Type repositoryType)
{
    InvokeStatic(repositoryType, "ResetFinalBuildRemoteForTests");
}

static List<string> LoadFinalBuildSources(Type repositoryType, string hero)
{
    var root = InvokeStaticValue(repositoryType, "EnsureFinalRoot")!;
    var heroes = (System.Collections.IDictionary)
        root.GetType().GetProperty("Heroes")!.GetValue(root)!;
    Assert(heroes.Contains(hero), $"Expected final build data to contain hero {hero}.");

    var bucket = heroes[hero]!;
    var builds = (System.Collections.IEnumerable)
        bucket.GetType().GetProperty("Builds")!.GetValue(bucket)!;
    return builds
        .Cast<object>()
        .Select(
            build => (string?)build.GetType().GetProperty("Source")!.GetValue(build) ?? string.Empty
        )
        .ToList();
}

static string CreateFinalBuildPayload(string hero, Guid selectedCardId, string source)
{
    return $$"""
        {
          "generatedAt": "2026-04-25T12:00:00Z",
          "heroes": {
            "{{hero}}": {
              "builds": [
                {
                  "source": "{{source}}",
                  "setSignature": "{{selectedCardId}}",
                  "goldScore": 10.0,
                  "playerCards": [
                    {
                      "cardId": "{{selectedCardId}}",
                      "slot": 0,
                      "tier": 1,
                      "enchant": "None"
                    }
                  ]
                }
              ],
              "cardIndex": {
                "{{selectedCardId}}": [0]
              },
              "subsetIndex": {
                "{{selectedCardId}}": {
                  "matchedBuildIds": [0]
                }
              }
            }
          }
        }
        """;
}

static void InvokeStatic(Type type, string methodName, params object[] parameters)
{
    var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
    Assert(method != null, $"Expected {type.FullName}.{methodName} to exist.");
    method!.Invoke(null, parameters);
}

static object? InvokeStaticValue(Type type, string methodName, params object[] parameters)
{
    var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
    Assert(method != null, $"Expected {type.FullName}.{methodName} to exist.");
    return method!.Invoke(null, parameters);
}

static void TryDelete(string path)
{
    try
    {
        if (File.Exists(path))
            File.Delete(path);
    }
    catch
    {
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
