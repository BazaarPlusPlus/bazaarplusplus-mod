using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.Game.VoiceSubtitles;
using BazaarPlusPlus.Infrastructure.Logging;
using BepInEx.Logging;
using Xunit;

namespace VoiceSubtitles.Tests;

public sealed class VoiceSubtitlesTests
{
    private const string GoldenContentHash =
        "sha256:8f1f4107e74dbb691dc0acea22eca5cdd2645a24bcb9223618e073036e505911";

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
            parse.Invoke(null, [json, VoiceCatalogSource.Embedded])
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
            parse.Invoke(null, [json, VoiceCatalogSource.Embedded])
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
    public void Runtime_settings_default_chinese_font_scale_matches_english()
    {
        var settingsType = GetRequiredType(
            "BazaarPlusPlus.Game.VoiceSubtitles.Settings.VoiceLineSettings"
        );

        Assert.Equal(1.0f, GetStaticSingle(settingsType, "DefaultEnglishFontScale"), precision: 2);
        Assert.Equal(1.0f, GetStaticSingle(settingsType, "DefaultChineseFontScale"), precision: 2);
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
    public void Fresh_catalog_warm_up_emits_one_structured_ready_event()
    {
        WithRepositoryCache(
            cacheAge: TimeSpan.FromHours(1),
            (repositoryType, repository, cachePath, downloadCalled, queuedRefreshes) =>
            {
                using var capture = new LogCapture();

                GetRequiredInstanceMethod(repositoryType, "LoadVoiceLinesInBackground")
                    .Invoke(repository, null);

                var ready = Assert.Single(capture.Events("voice_subtitles.catalog.ready"));
                Assert.Equal(LogLevel.Info, ready.Level);
                Assert.Contains("source=cache", ready.Data?.ToString());
                Assert.Contains("line_count=5032", ready.Data?.ToString());
                Assert.DoesNotContain(cachePath, ready.Data?.ToString());
                Assert.Empty(capture.Events("voice_subtitles.catalog.degraded"));
                Assert.Empty(capture.Events("voice_subtitles.catalog.failed"));
            }
        );
    }

    [Fact]
    public void Catalog_event_catalog_matches_the_locked_manifest()
    {
        var actual = typeof(VoiceCatalogLogEvents)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .Select(field => (BppLogEventDefinition)field.GetValue(null)!)
            .ToDictionary(
                definition => definition.EventId,
                definition =>
                    string.Join(
                        "|",
                        definition.Fields.Select(field =>
                            $"{field.Name}:{field.Privacy}:{field.Cardinality}:{field.Correlation}"
                        )
                    ),
                StringComparer.Ordinal
            );

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["voice_subtitles.catalog.started"] = "",
                ["voice_subtitles.catalog.ready"] =
                    "source:Public:Low:None|line_count:Public:High:None",
                ["voice_subtitles.catalog.degraded"] =
                    "reason_code:Public:Low:None|source:Public:Low:None|endpoint:Public:Low:None",
                ["voice_subtitles.catalog.failed"] =
                    "reason_code:Public:Low:None|source:Public:Low:None",
                ["voice_subtitles.catalog_refresh.started"] =
                    "reason_code:Public:Low:None|endpoint:Public:Low:None",
                ["voice_subtitles.catalog.recovered"] =
                    "reason_code:Public:Low:None|source:Public:Low:None|line_count:Public:High:None",
                ["voice_subtitles.catalog_cache.degraded"] = "reason_code:Public:Low:None",
                ["voice_subtitles.catalog_row.skipped"] =
                    "source:Public:Low:None|row_number:Public:High:None|reason_code:Public:Low:None|stem:UntrustedText:High:None",
            },
            actual
        );
        Assert.Equal(
            ["reason_code", "source"],
            VoiceCatalogLogEvents.CatalogDegraded.StormPolicy!.KeyFields.Select(field => field.Name)
        );
        Assert.Equal(
            ["reason_code"],
            VoiceCatalogLogEvents.CatalogCacheDegraded.StormPolicy!.KeyFields.Select(field =>
                field.Name
            )
        );
    }

    [Fact]
    public void Stale_catalog_emits_one_degradation_then_one_remote_recovery()
    {
        WithRepositoryCache(
            cacheAge: TimeSpan.FromHours(21),
            (repositoryType, repository, cachePath, downloadCalled, queuedRefreshes) =>
            {
                using var capture = new LogCapture();

                GetRequiredInstanceMethod(repositoryType, "LoadVoiceLinesInBackground")
                    .Invoke(repository, null);

                var degraded = Assert.Single(capture.Events("voice_subtitles.catalog.degraded"));
                Assert.Equal(LogLevel.Warning, degraded.Level);
                Assert.Contains("reason_code=cache_stale", degraded.Data?.ToString());
                Assert.Contains("source=cache", degraded.Data?.ToString());
                Assert.Contains("endpoint=voice_catalog", degraded.Data?.ToString());
                Assert.Empty(capture.Events("voice_subtitles.catalog.ready"));
                Assert.DoesNotContain(cachePath, degraded.Data?.ToString());
                Assert.DoesNotContain("expiresAtUtc", degraded.Data?.ToString());

                Assert.Single(queuedRefreshes)().GetAwaiter().GetResult();

                var recovered = Assert.Single(capture.Events("voice_subtitles.catalog.recovered"));
                Assert.Equal(LogLevel.Info, recovered.Level);
                Assert.Contains("reason_code=cache_stale", recovered.Data?.ToString());
                Assert.Contains("source=cache", recovered.Data?.ToString());
                Assert.Contains("line_count=5032", recovered.Data?.ToString());
                Assert.DoesNotContain("http", recovered.Data?.ToString());
                Assert.True(downloadCalled());
            }
        );
    }

    [Fact]
    public void No_usable_catalog_emits_one_terminal_failed_event()
    {
        var repositoryType = typeof(VoiceLinesRepository);
        var repository = new VoiceLinesRepository();
        var now = new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);
        var cachePath = Path.Combine(Path.GetTempPath(), $"voice-lines-{Guid.NewGuid():N}.json");
        var queuedRefreshes = new List<Func<Task>>();
        GetRequiredInstanceMethod(repositoryType, "ConfigureVoiceLinesSourcesForTests")
            .Invoke(
                repository,
                [
                    cachePath,
                    (Func<DateTime>)(() => now),
                    (Func<string, Task<string>>)(_ => Task.FromResult(string.Empty)),
                    (Func<string?>)(() => null),
                    (Action<Func<Task>>)(refresh => queuedRefreshes.Add(refresh)),
                ]
            );
        using var capture = new LogCapture();

        GetRequiredInstanceMethod(repositoryType, "LoadVoiceLinesInBackground")
            .Invoke(repository, null);

        var failed = Assert.Single(capture.Events("voice_subtitles.catalog.failed"));
        Assert.Equal(LogLevel.Error, failed.Level);
        Assert.Contains("reason_code=no_usable_catalog", failed.Data?.ToString());
        Assert.Contains("source=embedded", failed.Data?.ToString());
        Assert.Empty(capture.Events("voice_subtitles.catalog.ready"));
        Assert.Empty(capture.Events("voice_subtitles.catalog.degraded"));
        Assert.Single(queuedRefreshes);
    }

    [Fact]
    public async Task Cache_write_failure_is_independent_from_catalog_health()
    {
        var repositoryType = typeof(VoiceLinesRepository);
        var repository = new VoiceLinesRepository();
        var embeddedJson = Assert.IsType<string>(
            GetRequiredStaticMethod(repositoryType, "ReadEmbeddedSeedJsonForTests")
                .Invoke(null, null)
        );
        var cacheDirectory = Path.Combine(
            Path.GetTempPath(),
            $"voice-lines-cache-directory-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(cacheDirectory);
        var queuedRefreshes = new List<Func<Task>>();
        try
        {
            GetRequiredInstanceMethod(repositoryType, "ConfigureVoiceLinesSourcesForTests")
                .Invoke(
                    repository,
                    [
                        cacheDirectory,
                        (Func<DateTime>)(() => DateTime.UtcNow),
                        (Func<string, Task<string>>)(_ => Task.FromResult(embeddedJson)),
                        (Func<string?>)(() => embeddedJson),
                        (Action<Func<Task>>)(refresh => queuedRefreshes.Add(refresh)),
                    ]
                );
            using var capture = new LogCapture();

            GetRequiredInstanceMethod(repositoryType, "LoadVoiceLinesInBackground")
                .Invoke(repository, null);
            await Assert.Single(queuedRefreshes)();

            var cacheDegraded = Assert.Single(
                capture.Events("voice_subtitles.catalog_cache.degraded")
            );
            Assert.Equal(LogLevel.Warning, cacheDegraded.Level);
            Assert.Contains("reason_code=write_failed", cacheDegraded.Data?.ToString());
            Assert.DoesNotContain(cacheDirectory, cacheDegraded.Data?.ToString());
            Assert.Empty(capture.Events("voice_subtitles.catalog.degraded"));
            Assert.Empty(capture.Events("voice_subtitles.catalog.recovered"));
            Assert.Single(capture.Events("voice_subtitles.catalog.ready"));
        }
        finally
        {
            Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    [Fact]
    public void Malformed_rows_emit_typed_debug_skips_without_operational_warnings()
    {
        var documentType = typeof(VoiceLinesDocument);
        var parse = GetRequiredStaticMethod(documentType, "Parse");
        var json = BuildVoiceLinesJson(
            count: 1,
            contentHash: ContentHashFor(("valid", "English", "中文", 1.25)),
            ("", "missing", "缺失", 1),
            ("valid", "English", "中文", 1.25),
            ("valid", "duplicate", "重复", 1),
            ("empty", "", "", 1)
        );
        using var capture = new LogCapture();

        var lines = Assert.IsAssignableFrom<Array>(
            parse.Invoke(null, [json, VoiceCatalogSource.Remote])
        );

        Assert.Single(lines);
#if DEBUG
        var skipped = capture.Events("voice_subtitles.catalog_row.skipped");
        Assert.Equal(3, skipped.Count);
        Assert.All(skipped, entry => Assert.Equal(LogLevel.Debug, entry.Level));
        Assert.Contains(
            skipped,
            entry => entry.Data?.ToString()?.Contains("reason_code=missing_stem") == true
        );
        Assert.Contains(
            skipped,
            entry => entry.Data?.ToString()?.Contains("reason_code=duplicate_stem") == true
        );
        Assert.Contains(
            skipped,
            entry => entry.Data?.ToString()?.Contains("reason_code=empty_text") == true
        );
#else
        Assert.Empty(capture.Events("voice_subtitles.catalog_row.skipped"));
#endif
        Assert.DoesNotContain(
            capture.All,
            entry => entry.Level is LogLevel.Warning or LogLevel.Error
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

    private sealed class LogCapture : IDisposable
    {
        private readonly ManualLogSource _source = new("VoiceSubtitles.Tests");
        private readonly List<LogEventArgs> _events = [];

        internal LogCapture()
        {
            _source.LogEvent += OnLogEvent;
            var bppLogType = GetRequiredType("BazaarPlusPlus.Infrastructure.BppLog");
            GetRequiredStaticMethod(bppLogType, "Install").Invoke(null, [_source]);
        }

        internal IReadOnlyList<LogEventArgs> Events(string eventId) =>
            _events
                .Where(entry =>
                    entry.Data?.ToString()?.Contains("event=" + eventId, StringComparison.Ordinal)
                    == true
                )
                .ToArray();

        internal IReadOnlyList<LogEventArgs> All => _events;

        public void Dispose()
        {
            _source.LogEvent -= OnLogEvent;
            _source.Dispose();
        }

        private void OnLogEvent(object? sender, LogEventArgs args) => _events.Add(args);
    }
}
