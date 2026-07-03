using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace VoiceSubtitles.Tests;

public sealed class VoiceSubtitlesTests
{
    private const string GoldenContentHash =
        "sha256:0bb0ce57361dfbcefc64173370891bacf9b7aa85b5e3fc225422f4b8a299aec0";

    [Fact]
    public void Embedded_seed_loads_expected_voice_lines()
    {
        var repositoryType = GetRequiredType(
            "BazaarPlusPlus.Game.VoiceSubtitles.VoiceLinesRepository"
        );
        var loadEmbeddedSeed = GetRequiredStaticMethod(repositoryType, "LoadEmbeddedSeed");

        var lines = Assert.IsAssignableFrom<Array>(loadEmbeddedSeed.Invoke(null, null));

        Assert.Equal(5032, lines.Length);
        var first = lines.GetValue(0);
        Assert.NotNull(first);
        Assert.Equal("001_Dooley_V5_RunDefeat_03", GetString(first, "Stem"));
        Assert.Equal("001_Dooley_V5_RunDefeat_03", GetString(first, "English"));
        Assert.Equal("「这次运算结果，不太理想。」", GetString(first, "Chinese"));
        Assert.Equal(2.08f, GetSingle(first, "DurationSeconds"), precision: 2);
    }

    [Fact]
    public void Embedded_seed_content_hash_matches_golden_hash()
    {
        var repositoryType = GetRequiredType(
            "BazaarPlusPlus.Game.VoiceSubtitles.VoiceLinesRepository"
        );
        var documentType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLinesDocument");
        var loadEmbeddedSeed = GetRequiredStaticMethod(repositoryType, "LoadEmbeddedSeed");
        var computeContentHash = GetRequiredStaticMethod(documentType, "ComputeContentHash");
        var lines = Assert.IsAssignableFrom<Array>(loadEmbeddedSeed.Invoke(null, null));

        var contentHash = Assert.IsType<string>(computeContentHash.Invoke(null, [lines]));

        Assert.Equal(GoldenContentHash, contentHash);
    }

    [Fact]
    public void Parser_rejects_count_mismatch()
    {
        var documentType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLinesDocument");
        var parse = GetRequiredStaticMethod(documentType, "Parse");
        var json = BuildVoiceLinesJson(
            count: 2,
            contentHash: ContentHashFor(("stem", "English", "中文", 1.25)),
            ("stem", "English", "中文", 1.25)
        );

        var ex = Assert.Throws<TargetInvocationException>(() =>
            parse.Invoke(null, [json, "count-test"])
        );
        Assert.Contains("count", ex.InnerException?.Message);
    }

    [Fact]
    public void Parser_rejects_content_hash_mismatch()
    {
        var documentType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLinesDocument");
        var parse = GetRequiredStaticMethod(documentType, "Parse");
        var json = BuildVoiceLinesJson(
            count: 1,
            contentHash: "sha256:0000000000000000000000000000000000000000000000000000000000000000",
            ("stem", "English", "中文", 1.25)
        );

        var ex = Assert.Throws<TargetInvocationException>(() =>
            parse.Invoke(null, [json, "hash-test"])
        );
        Assert.Contains("contentHash", ex.InnerException?.Message);
    }

    [Fact]
    public void Default_cache_path_uses_game_root_data_directory()
    {
        var repositoryType = GetRequiredType(
            "BazaarPlusPlus.Game.VoiceSubtitles.VoiceLinesRepository"
        );
        var buildCachePath = GetRequiredStaticMethod(
            repositoryType,
            "BuildDefaultVoiceLinesCacheFilePath"
        );
        var gameRoot = Path.Combine(Path.GetTempPath(), $"bpp-game-root-{Guid.NewGuid():N}");

        var cachePath = Assert.IsType<string>(buildCachePath.Invoke(null, [gameRoot]));

        Assert.Equal(Path.Combine(gameRoot, "BazaarPlusPlusV4", "voice-lines.json"), cachePath);
    }

    [Fact]
    public void Fresh_cache_loads_without_remote_refresh()
    {
        WithRepositoryCache(
            cacheAge: TimeSpan.FromHours(1),
            (repositoryType, repository, cachePath, downloadCalled, queuedRefreshes) =>
            {
                var loadForTests = GetRequiredInstanceMethod(
                    repositoryType,
                    "LoadVoiceLinesForTests"
                );

                var result = loadForTests.Invoke(repository, null);
                Assert.NotNull(result);

                Assert.Equal(5032, GetInt32(result!, "Item1"));
                Assert.Equal("cache", GetString(result!, "Item2"));
                Assert.False(GetBool(result!, "Item3"));
                Assert.False(downloadCalled());
                Assert.Empty(queuedRefreshes);
            }
        );
    }

    [Fact]
    public void Stale_cache_loads_and_requests_background_refresh()
    {
        WithRepositoryCache(
            cacheAge: TimeSpan.FromHours(21),
            (repositoryType, repository, cachePath, downloadCalled, queuedRefreshes) =>
            {
                var loadForTests = GetRequiredInstanceMethod(
                    repositoryType,
                    "LoadVoiceLinesForTests"
                );

                var result = loadForTests.Invoke(repository, null);
                Assert.NotNull(result);

                Assert.Equal(5032, GetInt32(result!, "Item1"));
                Assert.Equal("cache", GetString(result!, "Item2"));
                Assert.True(GetBool(result!, "Item3"));
                Assert.False(downloadCalled());
                Assert.Empty(queuedRefreshes);
            }
        );
    }

    [Fact]
    public void VoiceLineSettings_saves_key_value_config_without_reload_on_read()
    {
        var settingsType = GetRequiredType(
            "BazaarPlusPlus.Game.VoiceSubtitles.Settings.VoiceLineSettings"
        );
        var positionType = GetRequiredType(
            "BazaarPlusPlus.Game.VoiceSubtitles.Settings.SubtitlePosition"
        );
        var configure = GetRequiredStaticMethod(settingsType, "ConfigureForTests");
        var reset = GetRequiredStaticMethod(settingsType, "ResetForTests");
        var setPosition = GetRequiredStaticMethod(settingsType, "SetPosition");
        var settingsPath = Path.Combine(Path.GetTempPath(), $"BazaarLine-{Guid.NewGuid():N}.cfg");

        try
        {
            configure.Invoke(null, [settingsPath, null]);

            setPosition.Invoke(null, [Enum.Parse(positionType, "TopRight")]);

            var current = GetStaticPropertyValue(settingsType, "Current");
            Assert.Equal("TopRight", GetEnumName(current, "Position"));
            Assert.Contains("position=top-right", File.ReadAllText(settingsPath));

            File.WriteAllText(settingsPath, "position=top-center\n", new UTF8Encoding(false));

            current = GetStaticPropertyValue(settingsType, "Current");
            Assert.Equal("TopRight", GetEnumName(current, "Position"));

            reset.Invoke(null, null);
            configure.Invoke(null, [settingsPath, null]);

            current = GetStaticPropertyValue(settingsType, "Current");
            Assert.Equal("TopCenter", GetEnumName(current, "Position"));
        }
        finally
        {
            reset.Invoke(null, null);
            if (File.Exists(settingsPath))
                File.Delete(settingsPath);
        }
    }

    [Fact]
    public void VoiceLineSettings_migrates_legacy_plugin_config_to_bepinex_config()
    {
        var settingsType = GetRequiredType(
            "BazaarPlusPlus.Game.VoiceSubtitles.Settings.VoiceLineSettings"
        );
        var configure = GetRequiredStaticMethod(settingsType, "ConfigureForTests");
        var reset = GetRequiredStaticMethod(settingsType, "ResetForTests");
        var tempRoot = Path.Combine(Path.GetTempPath(), $"bpp-voice-settings-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(tempRoot, "config", "BazaarLine.cfg");
        var legacyPath = Path.Combine(tempRoot, "plugins", "BazaarLine", "settings.cfg");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            File.WriteAllText(
                legacyPath,
                """
                position=top-center
                language=english
                englishFontScale=1.75
                chineseFontScale=1.25
                """,
                new UTF8Encoding(false)
            );

            configure.Invoke(null, [settingsPath, legacyPath]);

            var current = GetStaticPropertyValue(settingsType, "Current");

            Assert.True(File.Exists(settingsPath));
            Assert.Equal("TopCenter", GetEnumName(current, "Position"));
            Assert.Equal("EnglishOnly", GetEnumName(current, "LanguageMode"));
            Assert.Equal(1.75f, GetSingle(current, "EnglishFontScale"), precision: 2);
            Assert.Equal(1.25f, GetSingle(current, "ChineseFontScale"), precision: 2);
        }
        finally
        {
            reset.Invoke(null, null);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Catalog_resolves_exact_stem_from_embedded_seed()
    {
        var repositoryType = GetRequiredType(
            "BazaarPlusPlus.Game.VoiceSubtitles.VoiceLinesRepository"
        );
        var catalogType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLineCatalog");
        var loadEmbeddedSeed = GetRequiredStaticMethod(repositoryType, "LoadEmbeddedSeed");
        var replaceCatalog = GetRequiredStaticMethod(catalogType, "ReplaceCatalog");
        var resolveDetailed = GetRequiredStaticMethod(catalogType, "ResolveDetailed");
        var lines = Assert.IsAssignableFrom<Array>(loadEmbeddedSeed.Invoke(null, null));

        replaceCatalog.Invoke(null, new object[] { lines, "embedded-test" });
        var resolution = resolveDetailed.Invoke(
            null,
            new object[] { "event:/VO/Dooley/001_Dooley_V5_RunDefeat_03", "Hero", "Tutorial" }
        );
        Assert.NotNull(resolution);
        var line = GetPropertyValue(resolution, "Line");
        Assert.NotNull(line);

        Assert.Equal("001_Dooley_V5_RunDefeat_03", GetString(line, "Stem"));
        Assert.Equal("event-stem", GetString(resolution, "Strategy"));
        Assert.Equal("embedded-test", GetString(resolution, "CatalogName"));
    }

    private static void WithRepositoryCache(
        TimeSpan cacheAge,
        Action<Type, object, string, Func<bool>, List<Func<Task>>> run
    )
    {
        var repositoryType = GetRequiredType(
            "BazaarPlusPlus.Game.VoiceSubtitles.VoiceLinesRepository"
        );
        var repository =
            Activator.CreateInstance(repositoryType)
            ?? throw new InvalidOperationException("Could not create VoiceLinesRepository.");
        var readEmbeddedJson = GetRequiredStaticMethod(
            repositoryType,
            "ReadEmbeddedSeedJsonForTests"
        );
        var configure = GetRequiredInstanceMethod(
            repositoryType,
            "ConfigureVoiceLinesRemoteForTests"
        );
        var embeddedJson = Assert.IsType<string>(readEmbeddedJson.Invoke(null, null));
        var now = new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);
        var cachePath = Path.Combine(Path.GetTempPath(), $"voice-lines-{Guid.NewGuid():N}.json");
        var queuedRefreshes = new List<Func<Task>>();
        var downloadCalled = false;

        try
        {
            File.WriteAllText(cachePath, embeddedJson, new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(cachePath, now.Subtract(cacheAge));

            Func<DateTime> utcNow = () => now;
            Func<string, Task<string>> downloadJsonAsync = _ =>
            {
                downloadCalled = true;
                return Task.FromResult(embeddedJson);
            };
            Action<Func<Task>> queueBackgroundRefresh = refresh => queuedRefreshes.Add(refresh);
            configure.Invoke(
                repository,
                [cachePath, utcNow, downloadJsonAsync, queueBackgroundRefresh]
            );

            run(repositoryType, repository, cachePath, () => downloadCalled, queuedRefreshes);
        }
        finally
        {
            if (File.Exists(cachePath))
                File.Delete(cachePath);
        }
    }

    private static string BuildVoiceLinesJson(
        int count,
        string contentHash,
        params (string Stem, string English, string Chinese, double DurationSeconds)[] lines
    )
    {
        var lineJson = string.Join(
            ",",
            lines.Select(line =>
                "{\"stem\":\""
                + line.Stem
                + "\",\"english\":\""
                + line.English
                + "\",\"chinese\":\""
                + line.Chinese
                + "\",\"durationSeconds\":"
                + line.DurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "}"
            )
        );
        return "{\"schemaVersion\":1,\"contentHash\":\""
            + contentHash
            + "\",\"generatedAt\":\"2026-07-03T00:00:00Z\",\"count\":"
            + count
            + ",\"lines\":["
            + lineJson
            + "]}";
    }

    private static string ContentHashFor(
        params (string Stem, string English, string Chinese, double DurationSeconds)[] lines
    )
    {
        var records = lines.Select(line =>
            string.Join(
                "\x1f",
                line.Stem,
                line.English,
                line.Chinese,
                ((long)Math.Round(line.DurationSeconds * 100.0)).ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                )
            )
        );
        var canonical = string.Join("\x1e", records);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static Type GetRequiredType(string name)
    {
        return Assembly.Load("BazaarPlusPlus").GetType(name)
            ?? throw new InvalidOperationException($"Missing type {name}");
    }

    private static MethodInfo GetRequiredStaticMethod(Type type, string name)
    {
        return type.GetMethod(
                name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException($"Missing method {type.FullName}.{name}");
    }

    private static MethodInfo GetRequiredInstanceMethod(Type type, string name)
    {
        return type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException($"Missing method {type.FullName}.{name}");
    }

    private static object? GetPropertyValue(object instance, string name)
    {
        var type = instance.GetType();
        if (type.GetProperty(name) is { } property)
            return property.GetValue(instance);
        if (type.GetField(name) is { } field)
            return field.GetValue(instance);

        throw new InvalidOperationException($"Missing property or field {type.FullName}.{name}");
    }

    private static object GetStaticPropertyValue(Type type, string name)
    {
        var property =
            type.GetProperty(
                name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            )
            ?? throw new InvalidOperationException(
                $"Missing static property {type.FullName}.{name}"
            );
        return property.GetValue(null)
            ?? throw new InvalidOperationException(
                $"Static property {type.FullName}.{name} is null"
            );
    }

    private static string GetEnumName(object instance, string name)
    {
        return Assert.IsAssignableFrom<Enum>(GetPropertyValue(instance, name)).ToString();
    }

    private static string GetString(object instance, string name)
    {
        return Assert.IsType<string>(GetPropertyValue(instance, name));
    }

    private static bool GetBool(object instance, string name)
    {
        return Assert.IsType<bool>(GetPropertyValue(instance, name));
    }

    private static int GetInt32(object instance, string name)
    {
        return Assert.IsType<int>(GetPropertyValue(instance, name));
    }

    private static float GetSingle(object instance, string name)
    {
        return Assert.IsType<float>(GetPropertyValue(instance, name));
    }
}
