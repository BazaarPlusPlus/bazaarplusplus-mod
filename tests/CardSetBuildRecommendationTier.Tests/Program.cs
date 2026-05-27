using System.Reflection;

RegisterAssemblyResolution();
TestRecommendationTierMapping();
TestDefaultFinalBuildCachePathUsesGameRootDirectory();
TestFreshFinalBuildCacheIsUsedWithoutRemoteDownload();
TestExpiredFinalBuildCacheUsesStaleCacheAndQueuesRemoteRefresh();
TestManualFinalBuildRefreshBypassesFreshCache();

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
        "BazaarPlusPlus.Game.CardSetPreview.CardSetBuildDataRepository"
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

static void TestDefaultFinalBuildCachePathUsesGameRootDirectory()
{
    var repositoryType = GetRepositoryType();
    var buildPathMethod = repositoryType.GetMethod(
        "BuildDefaultFinalBuildsCacheFilePath",
        BindingFlags.NonPublic | BindingFlags.Static
    );
    Assert(
        buildPathMethod != null,
        "Expected CardSetBuildDataRepository to expose default cache path construction."
    );

    var gameRootPath = Path.Combine(Path.GetTempPath(), $"bpp-game-root-{Guid.NewGuid():N}");
    var cachePath = (string)buildPathMethod!.Invoke(null, [gameRootPath])!;

    Assert(
        cachePath == Path.Combine(gameRootPath, "BazaarPlusPlusV4", "final_builds_for_mod.json"),
        "Final build cache should live under GameRoot/BazaarPlusPlusV4/final_builds_for_mod.json."
    );
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

static void TestExpiredFinalBuildCacheUsesStaleCacheAndQueuesRemoteRefresh()
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
    Action? queuedRefresh = null;
    var queuedRefreshCount = 0;
    var remotePayload = CreateFinalBuildPayload("RemoteHero", selectedCardId, "remote-download");
    ConfigureFinalBuildRemoteWithBackgroundRefreshForTests(
        repositoryType,
        cachePath,
        now,
        _ =>
        {
            downloaded = true;
            return remotePayload;
        },
        refresh =>
        {
            queuedRefreshCount++;
            queuedRefresh = refresh;
        }
    );

    try
    {
        var sources = LoadFinalBuildSources(repositoryType, "StaleHero");

        Assert(!downloaded, "Expired final build cache should not block on a remote download.");
        Assert(
            queuedRefreshCount == 1,
            "Expired final build cache should queue a background refresh."
        );
        Assert(
            queuedRefresh != null,
            "Queued background refresh should be executable by the scheduler."
        );
        Assert(sources.Count == 1, "Stale cached final builds should remain loadable.");
        Assert(
            sources[0] == "stale-cache",
            "Stale cached final builds should be used instead of synchronously downloading remote data."
        );
        Assert(
            File.ReadAllText(cachePath) != remotePayload,
            "Normal final build loading should not rewrite the disk cache from remote data."
        );

        queuedRefresh!();
        var refreshedSources = LoadFinalBuildSources(repositoryType, "RemoteHero");
        Assert(downloaded, "Queued background refresh should download remote final build data.");
        Assert(
            refreshedSources[0] == "remote-download",
            "Queued background refresh should replace the in-memory final build data."
        );
        Assert(
            File.ReadAllText(cachePath) == remotePayload,
            "Queued background refresh should replace the disk cache."
        );
    }
    finally
    {
        ResetFinalBuildRemoteForTests(repositoryType);
        TryDelete(cachePath);
    }
}

static void TestManualFinalBuildRefreshBypassesFreshCache()
{
    var repositoryType = GetRepositoryType();
    var now = new DateTime(2026, 04, 25, 12, 0, 0, DateTimeKind.Utc);
    var cachePath = Path.Combine(
        Path.GetTempPath(),
        $"bpp-final-build-cache-{Guid.NewGuid():N}.json"
    );
    var selectedCardId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    var cachedPayload = CreateFinalBuildPayload("ManualHero", selectedCardId, "fresh-cache");
    File.WriteAllText(cachePath, cachedPayload);
    File.SetLastWriteTimeUtc(cachePath, now.AddHours(-1));

    var downloaded = false;
    var remotePayload = CreateFinalBuildPayload("ManualHero", selectedCardId, "manual-remote");
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
        var cachedSources = LoadFinalBuildSources(repositoryType, "ManualHero");
        Assert(cachedSources[0] == "fresh-cache", "Fresh cache should load before manual refresh.");
        Assert(!downloaded, "Initial load from a fresh cache should not download remote data.");

        var refreshed = RefreshFinalBuildsFromRemote(repositoryType, out var error);
        var refreshedSources = LoadFinalBuildSources(repositoryType, "ManualHero");

        Assert(refreshed, $"Manual final build refresh should succeed: {error}");
        Assert(downloaded, "Manual final build refresh should download remote data.");
        Assert(
            refreshedSources[0] == "manual-remote",
            "Manual final build refresh should replace the in-memory final build data."
        );
        Assert(
            File.ReadAllText(cachePath) == remotePayload,
            "Manual final build refresh should replace the disk cache."
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
    return assembly.GetType("BazaarPlusPlus.Game.CardSetPreview.CardSetBuildDataRepository")!;
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

static void ConfigureFinalBuildRemoteWithBackgroundRefreshForTests(
    Type repositoryType,
    string cachePath,
    DateTime utcNow,
    Func<string, string> downloadJson,
    Action<Action> queueBackgroundRefresh
)
{
    InvokeStatic(
        repositoryType,
        "ConfigureFinalBuildRemoteForTests",
        cachePath,
        (Func<DateTime>)(() => utcNow),
        downloadJson,
        queueBackgroundRefresh
    );
}

static void ResetFinalBuildRemoteForTests(Type repositoryType)
{
    InvokeStatic(repositoryType, "ResetFinalBuildRemoteForTests");
}

static bool RefreshFinalBuildsFromRemote(Type repositoryType, out string? error)
{
    var method = repositoryType.GetMethod(
        "TryRefreshFinalBuildsFromRemote",
        BindingFlags.NonPublic | BindingFlags.Static
    );
    Assert(method != null, "Expected CardSetBuildDataRepository to expose manual refresh.");
    object?[] parameters = [null];
    var refreshed = (bool)method!.Invoke(null, parameters)!;
    error = (string?)parameters[0];
    return refreshed;
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
        .Select(build =>
            (string?)build.GetType().GetProperty("Source")!.GetValue(build) ?? string.Empty
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
    var method = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
        .FirstOrDefault(method =>
            method.Name == methodName && method.GetParameters().Length == parameters.Length
        );
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
    catch { }
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
