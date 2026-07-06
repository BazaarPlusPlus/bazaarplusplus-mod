using System.Reflection;
using System.Net;
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
    public void Runtime_settings_defaults_use_center_position_and_larger_chinese_font_scale()
    {
        var settingsType = GetRequiredType(
            "BazaarPlusPlus.Game.VoiceSubtitles.Settings.VoiceLineSettings"
        );

        var defaultPosition = GetRequiredStaticField(settingsType, "DefaultPosition").GetValue(null);

        Assert.Equal("TopCenter", defaultPosition?.ToString());
        Assert.Equal(1.0f, GetStaticSingle(settingsType, "DefaultEnglishFontScale"), precision: 2);
        Assert.Equal(1.25f, GetStaticSingle(settingsType, "DefaultChineseFontScale"), precision: 2);
    }

    [Fact]
    public void Cache_loads_and_requests_background_refresh()
    {
        WithRepositoryCache(
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
    public void Remote_not_modified_uses_conditional_headers_without_rewriting_cache()
    {
        WithRepositoryCache(
            (repositoryType, repository, cachePath, downloadCalled, queuedRefreshes) =>
            {
                var buildMetadataPath = GetRequiredInstanceMethod(
                    repositoryType,
                    "BuildCacheMetadataFilePathForTests"
                );
                var loadRemote = GetRequiredInstanceMethod(repositoryType, "LoadRemoteForTests");
                var metadataPath = Assert.IsType<string>(buildMetadataPath.Invoke(repository, null));
                var originalCacheJson = File.ReadAllText(cachePath, new UTF8Encoding(false));
                File.WriteAllText(
                    metadataPath,
                    "{\"etag\":\"\\\"etag-a\\\"\",\"lastModified\":\"Fri, 03 Jul 2026 16:31:36 GMT\",\"contentHash\":\""
                        + GoldenContentHash
                        + "\",\"checkedAtUtc\":\"2026-07-01T00:00:00.0000000Z\"}",
                    new UTF8Encoding(false)
                );

                var resultTask = Assert.IsAssignableFrom<Task>(loadRemote.Invoke(repository, null));
                resultTask.GetAwaiter().GetResult();
                var result = GetTaskResult(resultTask);

                Assert.True(downloadCalled());
                Assert.True(GetBool(result!, "Item3"));
                Assert.Equal(originalCacheJson, File.ReadAllText(cachePath, new UTF8Encoding(false)));
                Assert.Contains("etag-a", File.ReadAllText(metadataPath, new UTF8Encoding(false)));
            }
        );
    }

    [Fact]
    public void Remote_ok_writes_cache_and_metadata()
    {
        WithRepositoryCache(
            (repositoryType, repository, cachePath, downloadCalled, queuedRefreshes) =>
            {
                var buildMetadataPath = GetRequiredInstanceMethod(
                    repositoryType,
                    "BuildCacheMetadataFilePathForTests"
                );
                var loadRemote = GetRequiredInstanceMethod(repositoryType, "LoadRemoteForTests");
                var metadataPath = Assert.IsType<string>(buildMetadataPath.Invoke(repository, null));
                var remoteJson = BuildVoiceLinesJson(
                    count: 1,
                    contentHash: ContentHashFor(("stem", "English", "中文", 1.25)),
                    ("stem", "English", "中文", 1.25)
                );
                ConfigureDownloadForTests(
                    repositoryType,
                    repository,
                    cachePath,
                    (etag, lastModified) =>
                        Task.FromResult(
                            (
                                HttpStatusCode.OK,
                                (string?)remoteJson,
                                (string?)"\"etag-b\"",
                                (string?)"Mon, 06 Jul 2026 09:33:04 GMT"
                            )
                        )
                );

                var resultTask = Assert.IsAssignableFrom<Task>(loadRemote.Invoke(repository, null));
                resultTask.GetAwaiter().GetResult();
                var result = GetTaskResult(resultTask);

                Assert.False(GetBool(result!, "Item3"));
                Assert.Equal(remoteJson, File.ReadAllText(cachePath, new UTF8Encoding(false)));
                var metadataJson = File.ReadAllText(metadataPath, new UTF8Encoding(false));
                Assert.Contains("etag-b", metadataJson);
                Assert.Contains(ContentHashFor(("stem", "English", "中文", 1.25)), metadataJson);
            }
        );
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

    [Fact]
    public void Catalog_resolves_file_extension_suffix_like_bare_stem()
    {
        var catalogType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLineCatalog");
        var resolveDetailed = GetRequiredStaticMethod(catalogType, "ResolveDetailed");

        try
        {
            ReplaceCatalog(
                catalogType,
                "suffix-test",
                ("001_VanessaPvPDefeat1", "Defeat does not defeat me.", "失败不会打败我。", 2.15f)
            );

            var bare = Resolve(catalogType, "001_VanessaPvPDefeat1", "Hero", "Tutorial");
            var withExtension = Resolve(
                catalogType,
                "event:/VO/Vanessa/001-VanessaPvPDefeat1.wav",
                "Hero",
                "Tutorial"
            );

            Assert.Equal(
                GetString(GetPropertyValue(bare, "Line")!, "Stem"),
                GetString(GetPropertyValue(withExtension, "Line")!, "Stem")
            );
            Assert.Equal(
                "001_VanessaPvPDefeat1",
                GetString(GetPropertyValue(withExtension, "Line")!, "Stem")
            );
            Assert.Equal("event-stem", GetString(withExtension, "Strategy"));
        }
        finally
        {
            ResetCatalog(catalogType);
        }
    }

    [Fact]
    public void Catalog_resolves_unique_character_hook_fallback()
    {
        var catalogType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLineCatalog");

        try
        {
            ReplaceCatalog(
                catalogType,
                "hook-test",
                ("001_VanessaPvPDefeat1", "Defeat does not defeat me.", "失败不会打败我。", 2.15f),
                ("002_VanessaIdle1", "Still sailing.", "继续航行。", 1.5f)
            );

            var resolution = Resolve(
                catalogType,
                "event:/VO/Vanessa/UnmappedCategory",
                "Hero",
                "OnPvPVictoryDefeat"
            );
            var line = GetPropertyValue(resolution, "Line");

            Assert.Equal("001_VanessaPvPDefeat1", GetString(line!, "Stem"));
            Assert.Equal("character-hook-unique", GetString(resolution, "Strategy"));
            Assert.Equal("Vanessa:PvPDefeat", GetString(resolution, "MatchedToken"));
            Assert.Equal(1, GetInt32(resolution, "CandidateCount"));
        }
        finally
        {
            ResetCatalog(catalogType);
        }
    }

    [Fact]
    public void Catalog_reports_ambiguous_character_hook_fallback()
    {
        var catalogType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLineCatalog");

        try
        {
            ReplaceCatalog(
                catalogType,
                "hook-ambiguous-test",
                ("001_VanessaPvPDefeat1", "Defeat does not defeat me.", "失败不会打败我。", 2.15f),
                ("002_VanessaPvPDefeat2", "The sea remembers.", "海会记住。", 1.75f)
            );

            var resolution = Resolve(
                catalogType,
                "event:/VO/Vanessa/UnmappedCategory",
                "Hero",
                "OnPvPVictoryDefeat"
            );
            var line = GetPropertyValue(resolution, "Line");

            Assert.True(string.IsNullOrEmpty(GetNullableString(line!, "Stem")));
            Assert.Equal("character-hook-ambiguous", GetString(resolution, "Strategy"));
            Assert.Equal("Vanessa:PvPDefeat", GetString(resolution, "MatchedToken"));
            Assert.Equal(2, GetInt32(resolution, "CandidateCount"));
        }
        finally
        {
            ResetCatalog(catalogType);
        }
    }

    [Fact]
    public void Catalog_reset_swaps_to_empty_snapshot()
    {
        var catalogType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLineCatalog");

        try
        {
            ReplaceCatalog(
                catalogType,
                "reset-test",
                ("999_CustomResetLine1", "Reset line.", "重置台词。", 1.25f)
            );
            ResetCatalog(catalogType);

            var resolution = Resolve(
                catalogType,
                "event:/VO/Vanessa/999_CustomResetLine1",
                "Hero",
                "Tutorial"
            );
            var line = GetPropertyValue(resolution, "Line");

            Assert.True(string.IsNullOrEmpty(GetNullableString(line!, "Stem")));
            Assert.Equal("unresolved", GetString(resolution, "Strategy"));
        }
        finally
        {
            ResetCatalog(catalogType);
        }
    }

    private static void WithRepositoryCache(
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
        var embeddedJson = Assert.IsType<string>(readEmbeddedJson.Invoke(null, null));
        var now = new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);
        var cachePath = Path.Combine(Path.GetTempPath(), $"voice-lines-{Guid.NewGuid():N}.json");
        var queuedRefreshes = new List<Func<Task>>();
        var downloadCalled = false;

        try
        {
            File.WriteAllText(cachePath, embeddedJson, new UTF8Encoding(false));

            ConfigureDownloadForTests(
                repositoryType,
                repository,
                cachePath,
                (etag, lastModified) =>
                {
                    downloadCalled = true;
                    Assert.Equal("\"etag-a\"", etag);
                    Assert.Equal("Fri, 03 Jul 2026 16:31:36 GMT", lastModified);
                    return Task.FromResult(
                        (
                            HttpStatusCode.NotModified,
                            (string?)null,
                            (string?)"\"etag-a\"",
                            (string?)"Fri, 03 Jul 2026 16:31:36 GMT"
                        )
                    );
                },
                now,
                queuedRefreshes
            );

            run(repositoryType, repository, cachePath, () => downloadCalled, queuedRefreshes);
        }
        finally
        {
            if (File.Exists(cachePath))
                File.Delete(cachePath);
            var metadataPath = Path.Combine(
                Path.GetDirectoryName(cachePath) ?? string.Empty,
                "voice-lines.meta.json"
            );
            if (File.Exists(metadataPath))
                File.Delete(metadataPath);
        }
    }

    private static void ConfigureDownloadForTests(
        Type repositoryType,
        object repository,
        string cachePath,
        Func<
            string?,
            string?,
            Task<(HttpStatusCode StatusCode, string? Body, string? ETag, string? LastModified)>
        > downloadAsync,
        DateTime? now = null,
        List<Func<Task>>? queuedRefreshes = null
    )
    {
        var configure = GetRequiredInstanceMethod(
            repositoryType,
            "ConfigureVoiceLinesRemoteForTests"
        );
        Func<DateTime> utcNow = () => now ?? new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);
        Action<Func<Task>> queueBackgroundRefresh = refresh => queuedRefreshes?.Add(refresh);
        configure.Invoke(repository, [cachePath, utcNow, downloadAsync, queueBackgroundRefresh]);
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

    private static void ReplaceCatalog(
        Type catalogType,
        string catalogName,
        params (string Stem, string English, string Chinese, float DurationSeconds)[] lines
    )
    {
        var voiceLineType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLine");
        var voiceLineArray = Array.CreateInstance(voiceLineType, lines.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            var line =
                Activator.CreateInstance(
                    voiceLineType,
                    lines[i].Stem,
                    lines[i].English,
                    lines[i].Chinese,
                    lines[i].DurationSeconds
                ) ?? throw new InvalidOperationException("Could not create VoiceLine.");
            voiceLineArray.SetValue(line, i);
        }

        GetRequiredStaticMethod(catalogType, "ReplaceCatalog")
            .Invoke(null, new object[] { voiceLineArray, catalogName });
    }

    private static object Resolve(
        Type catalogType,
        string lookupText,
        string sourceLabel,
        string hookName
    )
    {
        var resolution = GetRequiredStaticMethod(catalogType, "ResolveDetailed")
            .Invoke(null, new object[] { lookupText, sourceLabel, hookName });
        return resolution ?? throw new InvalidOperationException("ResolveDetailed returned null.");
    }

    private static void ResetCatalog(Type catalogType)
    {
        GetRequiredStaticMethod(catalogType, "Reset").Invoke(null, null);
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

    private static FieldInfo GetRequiredStaticField(Type type, string name)
    {
        return type.GetField(
                name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException($"Missing field {type.FullName}.{name}");
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

    private static object? GetTaskResult(Task task)
    {
        var resultProperty = task.GetType().GetProperty("Result");
        return resultProperty?.GetValue(task);
    }

    private static string GetString(object instance, string name)
    {
        return Assert.IsType<string>(GetPropertyValue(instance, name));
    }

    private static string? GetNullableString(object instance, string name)
    {
        return GetPropertyValue(instance, name) as string;
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

    private static float GetStaticSingle(Type type, string name)
    {
        return Assert.IsType<float>(GetRequiredStaticField(type, name).GetValue(null));
    }
}
