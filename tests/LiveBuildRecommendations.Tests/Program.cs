using System.Collections;
using System.Reflection;

// Behavior tests for the analyzer-v4 ten-win build corpus consumed by LiveBuildPanel.
// The payload is the compact string-table + schema-driven array-row format emitted by
// bazaarplusplus-analyzers/src/bpp/stages/analyze/mod_builds.py at
// analyzer-v4/mod/tenwin_builds.json. Every assertion drives the public
// BuildRecommendationRepository.FindRecommendations surface (parse + recall + scoring +
// board projection) or the static cache/remote hooks, via reflection over the internal type.

TenWinBuildTests.Run();

internal static class TenWinBuildTests
{
    private const string GuidA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string GuidB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string GuidC = "cccccccc-cccc-cccc-cccc-cccccccccccc";
    private const string GuidD = "dddddddd-dddd-dddd-dddd-dddddddddddd";

    public static void Run()
    {
        RegisterAssemblyResolution();

        TestFindRecommendationsHasNoRatingTierParam();
        TestDefaultCachePathUsesTenwinBuildsFileName();
        TestSingleCardSelectionResolvesViaCardIndex();
        TestMultiCardSelectionPrefersIntersection();
        TestUnionFallbackWhenIntersectionEmptyButAllCovered();
        TestUnionFallbackKeepsResultsWhenOneSelectedCardUncovered();
        TestSelectingOnlyUncoveredCardsReturnsEmpty();
        TestLiveStateRankingOutranksScore();
        TestBoardContractMapsTierEnchantSize();
        TestNullSlotAndTierDoNotCrash();
        TestFreshCacheIsUsedWithoutRemoteDownload();
        TestStaleCacheUsesStaleAndQueuesBackgroundRefresh();
        TestManualRefreshBypassesFreshCache();
        TestRefreshServiceWrapsManualRefreshOutcome();
        TestColdStartWithNoCacheNorEmbeddedReturnsEmptyAndQueuesRefresh();
        TestColdStartFallsBackToEmbeddedThenRemote();
        TestEmbeddedSeedResourceIsBundledAndParses();

        Console.WriteLine("LiveBuild recommendation checks passed.");
    }

    // ---- Tests ------------------------------------------------------------

    private static void TestFindRecommendationsHasNoRatingTierParam()
    {
        var repositoryType = GetRepositoryType();

        var find = repositoryType.GetMethod("FindRecommendations");
        Assert(find != null, "Repository should expose FindRecommendations.");
        var parameters = find!.GetParameters();
        Assert(
            parameters.Length == 3,
            "FindRecommendations should take (hero, selectedTemplateIds, liveState)."
        );
        Assert(
            parameters[0].ParameterType == typeof(string),
            "First parameter should be the hero string."
        );
        Assert(
            !parameters.Skip(1).Any(p => p.ParameterType == typeof(string)),
            "FindRecommendations must not take a string rating-tier parameter."
        );

        Assert(
            repositoryType.GetMethod("FindFinalRecommendations") == null,
            "Legacy tier-based FindFinalRecommendations must be removed."
        );
        Assert(
            repositoryType.GetMethod("TryFindFinalRecommendation") == null,
            "Legacy TryFindFinalRecommendation must be removed."
        );

        var ratingTierType = repositoryType.Assembly.GetType(
            "BazaarPlusPlus.Game.LiveBuildPanel.Recommendations.BuildRatingTier"
        );
        Assert(
            ratingTierType == null,
            "BuildRatingTier must be deleted from the production assembly."
        );
    }

    private static void TestDefaultCachePathUsesTenwinBuildsFileName()
    {
        var repositoryType = GetRepositoryType();
        var buildPathMethod = repositoryType.GetMethod(
            "BuildDefaultTenWinCacheFilePath",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert(
            buildPathMethod != null,
            "Repository should expose default ten-win cache path construction."
        );

        var gameRootPath = Path.Combine(Path.GetTempPath(), $"bpp-game-root-{Guid.NewGuid():N}");
        var cachePath = (string)buildPathMethod!.Invoke(null, [gameRootPath])!;

        Assert(
            cachePath == Path.Combine(gameRootPath, "BazaarPlusPlusV4", "tenwin_builds.json"),
            "Ten-win build cache should live under GameRoot/BazaarPlusPlusV4/tenwin_builds.json."
        );
    }

    private static void TestSingleCardSelectionResolvesViaCardIndex()
    {
        WithCorpus(
            MainRecallPayload("RecallHero"),
            (repositoryType, repository) =>
            {
                var scores = ScoresOf(Find(repositoryType, repository, "RecallHero", [GuidA]));
                // cardIndex[A] = [build0(score100), build1(score200)] -> ranked by score desc.
                Assert(
                    scores.SequenceEqual([200L, 100L]),
                    $"Single-card recall should return A's builds by score; got [{string.Join(",", scores)}]."
                );
            }
        );
    }

    private static void TestMultiCardSelectionPrefersIntersection()
    {
        WithCorpus(
            MainRecallPayload("RecallHero"),
            (repositoryType, repository) =>
            {
                var scores = ScoresOf(
                    Find(repositoryType, repository, "RecallHero", [GuidA, GuidB])
                );
                // cardIndex[A]=[0,1], cardIndex[B]=[0,2] -> intersection {0} (score100).
                Assert(
                    scores.SequenceEqual([100L]),
                    $"Intersection of A and B should return only build0; got [{string.Join(",", scores)}]."
                );
            }
        );
    }

    private static void TestUnionFallbackWhenIntersectionEmptyButAllCovered()
    {
        // cards A,B both covered but with disjoint build sets -> intersection empty -> union.
        var payload = Payload(
            cards: $"[\"{GuidA}\",\"{GuidB}\"]",
            enchantments: "[null]",
            hero: "DisjointHero",
            builds: $"[{Build("[0]", "[[0,0,1,0,1]]", 10)},{Build("[1]", "[[1,0,1,0,1]]", 20)}]",
            cardIndex: "[[0,[0]],[1,[1]]]"
        );
        WithCorpus(
            payload,
            (repositoryType, repository) =>
            {
                var scores = ScoresOf(
                    Find(repositoryType, repository, "DisjointHero", [GuidA, GuidB])
                );
                Assert(
                    scores.Count == 2,
                    $"Disjoint intersection should fall back to the union of both builds; got {scores.Count}."
                );
            }
        );
    }

    private static void TestUnionFallbackKeepsResultsWhenOneSelectedCardUncovered()
    {
        WithCorpus(
            MainRecallPayload("RecallHero"),
            (repositoryType, repository) =>
            {
                // C is present in cards[] but absent from this hero's cardIndex -> uncovered.
                // The literal intersection is empty, so union over the covered card (A) must still return results.
                var scores = ScoresOf(
                    Find(repositoryType, repository, "RecallHero", [GuidA, GuidC])
                );
                Assert(
                    scores.Count == 2,
                    $"An uncovered selected card must not empty the result while A has coverage; got {scores.Count}."
                );
            }
        );
    }

    private static void TestSelectingOnlyUncoveredCardsReturnsEmpty()
    {
        WithCorpus(
            MainRecallPayload("RecallHero"),
            (repositoryType, repository) =>
            {
                // D is absent from cards[] entirely; C is in cards[] but not in cardIndex. Neither has coverage.
                Assert(
                    ScoresOf(Find(repositoryType, repository, "RecallHero", [GuidD])).Count == 0,
                    "An unknown selected card should return no recommendation."
                );
                Assert(
                    ScoresOf(Find(repositoryType, repository, "RecallHero", [GuidC])).Count == 0,
                    "A selected card with no historical coverage should return no recommendation."
                );
            }
        );
    }

    private static void TestLiveStateRankingOutranksScore()
    {
        WithCorpus(
            MainRecallPayload("RecallHero"),
            (repositoryType, repository) =>
            {
                // Selecting A returns build0(score100, contains B) and build1(score200, A only).
                // With no live state, build1 wins on score.
                Assert(
                    ScoresOf(Find(repositoryType, repository, "RecallHero", [GuidA])).First()
                        == 200L,
                    "Without live state the higher-score build ranks first."
                );

                // Put B on the board: build0 gains live-state weight and must outrank the higher-score build1.
                var liveState = LiveState(repositoryType, board: [GuidB], stash: [], shop: []);
                var ranked = ScoresOf(
                    Find(repositoryType, repository, "RecallHero", [GuidA], liveState)
                );
                Assert(
                    ranked.First() == 100L,
                    $"A board match should outrank a higher analyzer score; got first={ranked.First()}."
                );
            }
        );
    }

    private static void TestBoardContractMapsTierEnchantSize()
    {
        var payload = Payload(
            cards: $"[\"{GuidA}\"]",
            enchantments: "[null,\"Fiery\"]",
            hero: "BoardHero",
            // layout: cardRef0, slot3, tier5(Legendary), enchantRef1(Fiery), size2(Medium)
            builds: $"[{Build("[0]", "[[0,3,5,1,2]]", 1)}]",
            cardIndex: "[[0,[0]]]"
        );
        WithCorpus(
            payload,
            (repositoryType, repository) =>
            {
                var recommendation = Find(repositoryType, repository, "BoardHero", [GuidA])
                    .Cast<object>()
                    .Single();
                var board = Prop(recommendation, "Board")!;
                Assert(
                    Prop(board, "Id")!.ToString() == "FinalBuild",
                    "Recommendation board Id should be FinalBuild."
                );
                Assert(
                    Prop(board, "Type")!.ToString() == "Reference",
                    "Recommendation board Type should be Reference."
                );

                var cards = ((IEnumerable)Prop(board, "Cards")!).Cast<object>().ToList();
                Assert(cards.Count == 1, "Board should expose the single layout card.");
                var card = cards[0];
                Assert(
                    Prop(card, "Tier")!.ToString() == "Legendary",
                    "Tier value 5 should map to ETier.Legendary."
                );
                Assert(
                    Prop(card, "Size")!.ToString() == "Medium",
                    "Size 2 should map to ECardSize.Medium."
                );
                Assert(
                    Prop(card, "EnchantmentType")?.ToString() == "Fiery",
                    "enchantRef 1 should resolve to EEnchantmentType.Fiery."
                );
                Assert(
                    Prop(card, "SourceSocketId")?.ToString() == "Socket_3",
                    "Slot 3 should map to EContainerSocketId.Socket_3."
                );

                // tenWinRunCount / score survive into the recommendation DTO for UI evidence.
                Assert(
                    (int)Prop(recommendation, "TenWinRunCount")! == 80,
                    "TenWinRunCount should be carried from stats."
                );
                Assert(
                    (long)Prop(recommendation, "Score")! == 1L,
                    "Score should be carried from stats."
                );
            }
        );
    }

    private static void TestNullSlotAndTierDoNotCrash()
    {
        var payload = Payload(
            cards: $"[\"{GuidA}\"]",
            enchantments: "[null]",
            hero: "NullHero",
            // layout: cardRef0, slot null, tier null, enchantRef0, size1
            builds: $"[{Build("[0]", "[[0,null,null,0,1]]", 5)}]",
            cardIndex: "[[0,[0]]]"
        );
        WithCorpus(
            payload,
            (repositoryType, repository) =>
            {
                var recommendation = Find(repositoryType, repository, "NullHero", [GuidA])
                    .Cast<object>()
                    .Single();
                var board = Prop(recommendation, "Board")!;
                var card = ((IEnumerable)Prop(board, "Cards")!).Cast<object>().Single();
                Assert(
                    Prop(card, "Tier")!.ToString() == "Bronze",
                    "A null tier should clamp to ETier.Bronze."
                );
                Assert(
                    Prop(card, "EnchantmentType") == null,
                    "enchantRef 0 should resolve to no enchantment."
                );
            }
        );
    }

    private static void TestFreshCacheIsUsedWithoutRemoteDownload()
    {
        var repositoryType = GetRepositoryType();
        var now = new DateTime(2026, 06, 07, 12, 0, 0, DateTimeKind.Utc);
        var cachePath = TempCachePath();
        File.WriteAllText(cachePath, ScorePayload("CacheHero", 111));
        File.SetLastWriteTimeUtc(cachePath, now.AddHours(-19));

        Configure(
            repositoryType,
            cachePath,
            now,
            _ => throw new InvalidOperationException("Fresh cache should not download.")
        );
        try
        {
            var scores = ScoresOf(
                Find(repositoryType, NewRepository(repositoryType), "CacheHero", [GuidA])
            );
            Assert(
                scores.SequenceEqual([111L]),
                "Fresh cache should answer recommendations without a download."
            );
        }
        finally
        {
            Reset(repositoryType);
            TryDelete(cachePath);
        }
    }

    private static void TestStaleCacheUsesStaleAndQueuesBackgroundRefresh()
    {
        var repositoryType = GetRepositoryType();
        var now = new DateTime(2026, 06, 07, 12, 0, 0, DateTimeKind.Utc);
        var cachePath = TempCachePath();
        File.WriteAllText(cachePath, ScorePayload("CacheHero", 111));
        File.SetLastWriteTimeUtc(cachePath, now.AddHours(-21));

        var downloaded = false;
        Action? queuedRefresh = null;
        var queuedRefreshCount = 0;
        var remotePayload = ScorePayload("CacheHero", 222);
        ConfigureWithBackgroundRefresh(
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
            var stale = ScoresOf(
                Find(repositoryType, NewRepository(repositoryType), "CacheHero", [GuidA])
            );
            Assert(!downloaded, "Stale cache should not block on a synchronous download.");
            Assert(
                queuedRefreshCount == 1,
                "Stale cache should queue exactly one background refresh."
            );
            Assert(queuedRefresh != null, "The queued refresh should be executable.");
            Assert(
                stale.SequenceEqual([111L]),
                "Stale cache data should answer until the refresh completes."
            );
            Assert(
                File.ReadAllText(cachePath) != remotePayload,
                "Normal loading should not rewrite the disk cache."
            );

            queuedRefresh!();
            var refreshed = ScoresOf(
                Find(repositoryType, NewRepository(repositoryType), "CacheHero", [GuidA])
            );
            Assert(downloaded, "The queued refresh should download remote data.");
            Assert(
                refreshed.SequenceEqual([222L]),
                "The background refresh should replace the in-memory corpus."
            );
            Assert(
                File.ReadAllText(cachePath) == remotePayload,
                "The background refresh should replace the disk cache."
            );
        }
        finally
        {
            Reset(repositoryType);
            TryDelete(cachePath);
        }
    }

    private static void TestManualRefreshBypassesFreshCache()
    {
        var repositoryType = GetRepositoryType();
        var now = new DateTime(2026, 06, 07, 12, 0, 0, DateTimeKind.Utc);
        var cachePath = TempCachePath();
        File.WriteAllText(cachePath, ScorePayload("CacheHero", 111));
        File.SetLastWriteTimeUtc(cachePath, now.AddHours(-1));

        var downloaded = false;
        var remotePayload = ScorePayload("CacheHero", 222);
        Configure(
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
            var cached = ScoresOf(
                Find(repositoryType, NewRepository(repositoryType), "CacheHero", [GuidA])
            );
            Assert(
                cached.SequenceEqual([111L]),
                "Fresh cache should load before a manual refresh."
            );
            Assert(!downloaded, "Loading a fresh cache should not download.");

            var refreshed = ManualRefresh(repositoryType, out var error);
            Assert(refreshed, $"Manual refresh should succeed: {error}");
            Assert(downloaded, "Manual refresh should download remote data.");

            var after = ScoresOf(
                Find(repositoryType, NewRepository(repositoryType), "CacheHero", [GuidA])
            );
            Assert(
                after.SequenceEqual([222L]),
                "Manual refresh should replace the in-memory corpus."
            );
            Assert(
                File.ReadAllText(cachePath) == remotePayload,
                "Manual refresh should replace the disk cache."
            );
        }
        finally
        {
            Reset(repositoryType);
            TryDelete(cachePath);
        }
    }

    // The LiveBuildPanel manual pull consumes the shared refresh service; its result wrapper must
    // surface the repository's failure detail instead of swallowing it.
    private static void TestRefreshServiceWrapsManualRefreshOutcome()
    {
        var repositoryType = GetRepositoryType();
        var now = new DateTime(2026, 06, 07, 12, 0, 0, DateTimeKind.Utc);
        var cachePath = TempCachePath(); // no cache on disk; downloads drive the outcome.

        Configure(
            repositoryType,
            cachePath,
            now,
            _ => throw new InvalidOperationException("refresh-boom")
        );

        try
        {
            var failure = RunRefreshService(repositoryType);
            Assert(!GetResultSucceeded(failure), "A throwing download should fail the refresh.");
            Assert(
                GetResultError(failure)?.Contains("refresh-boom") == true,
                "The refresh failure should carry the underlying error detail."
            );

            Configure(repositoryType, cachePath, now, _ => ScorePayload("CacheHero", 333));
            var success = RunRefreshService(repositoryType);
            Assert(GetResultSucceeded(success), "A valid download should succeed the refresh.");
            Assert(
                GetResultError(success) == null,
                "A successful refresh should not carry an error."
            );

            var after = ScoresOf(
                Find(repositoryType, NewRepository(repositoryType), "CacheHero", [GuidA])
            );
            Assert(
                after.SequenceEqual([333L]),
                "A successful service refresh should update the shared corpus."
            );
        }
        finally
        {
            Reset(repositoryType);
            TryDelete(cachePath);
        }
    }

    private static object RunRefreshService(Type repositoryType)
    {
        var serviceType = repositoryType.Assembly.GetType(
            "BazaarPlusPlus.Game.LiveBuildPanel.Recommendations.BuildRecommendationRefreshService"
        );
        Assert(serviceType != null, "BuildRecommendationRefreshService should exist.");
        var service =
            Activator.CreateInstance(serviceType!)
            ?? throw new InvalidOperationException("Refresh service should be constructible.");
        var refreshAsync = serviceType!.GetMethod("RefreshAsync");
        Assert(refreshAsync != null, "Refresh service should expose RefreshAsync.");

        var task = (System.Threading.Tasks.Task)
            refreshAsync!.Invoke(service, [System.Threading.CancellationToken.None])!;
        task.GetAwaiter().GetResult();
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static bool GetResultSucceeded(object result) =>
        (bool)result.GetType().GetProperty("Succeeded")!.GetValue(result)!;

    private static string? GetResultError(object result) =>
        (string?)result.GetType().GetProperty("Error")!.GetValue(result);

    private static void TestColdStartWithNoCacheNorEmbeddedReturnsEmptyAndQueuesRefresh()
    {
        var repositoryType = GetRepositoryType();
        var now = new DateTime(2026, 06, 07, 12, 0, 0, DateTimeKind.Utc);
        var cachePath = TempCachePath(); // never created on disk -> cold start.

        Action? queuedRefresh = null;
        var queuedRefreshCount = 0;
        var remotePayload = ScorePayload("CacheHero", 222);
        ConfigureWithBackgroundRefresh(
            repositoryType,
            cachePath,
            now,
            _ => remotePayload,
            refresh =>
            {
                queuedRefreshCount++;
                queuedRefresh = refresh;
            }
        );
        SetEmbedded(repositoryType, () => null); // no embedded seed available either.

        try
        {
            var cold = ScoresOf(
                Find(repositoryType, NewRepository(repositoryType), "CacheHero", [GuidA])
            );
            Assert(
                cold.Count == 0,
                "A cold start with no cache and no embedded seed should return no recommendation."
            );
            Assert(queuedRefreshCount == 1, "A cold start should queue a background refresh.");

            queuedRefresh!();
            var afterRefresh = ScoresOf(
                Find(repositoryType, NewRepository(repositoryType), "CacheHero", [GuidA])
            );
            Assert(
                afterRefresh.SequenceEqual([222L]),
                "After the cold-start refresh the corpus should populate."
            );
        }
        finally
        {
            Reset(repositoryType);
            TryDelete(cachePath);
        }
    }

    private static void TestColdStartFallsBackToEmbeddedThenRemote()
    {
        var repositoryType = GetRepositoryType();
        var now = new DateTime(2026, 06, 07, 12, 0, 0, DateTimeKind.Utc);
        var cachePath = TempCachePath(); // never created on disk -> cold start.

        Action? queuedRefresh = null;
        var remotePayload = ScorePayload("CacheHero", 222);
        ConfigureWithBackgroundRefresh(
            repositoryType,
            cachePath,
            now,
            _ => remotePayload,
            refresh => queuedRefresh = refresh
        );
        SetEmbedded(repositoryType, () => ScorePayload("CacheHero", 555));

        try
        {
            // No cache on disk: the bundled seed answers immediately while a refresh is queued.
            var cold = ScoresOf(
                Find(repositoryType, NewRepository(repositoryType), "CacheHero", [GuidA])
            );
            Assert(
                cold.SequenceEqual([555L]),
                "A cold start with no cache should fall back to the embedded seed."
            );

            queuedRefresh!();
            var afterRefresh = ScoresOf(
                Find(repositoryType, NewRepository(repositoryType), "CacheHero", [GuidA])
            );
            Assert(
                afterRefresh.SequenceEqual([222L]),
                "The background refresh should replace the embedded seed with remote data."
            );
        }
        finally
        {
            Reset(repositoryType);
            TryDelete(cachePath);
        }
    }

    private static void TestEmbeddedSeedResourceIsBundledAndParses()
    {
        var repositoryType = GetRepositoryType();
        var readSeed = repositoryType.GetMethod(
            "ReadEmbeddedSeedForTests",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert(readSeed != null, "Repository should expose the embedded seed reader.");
        var json = (string?)readSeed!.Invoke(null, null);
        Assert(
            !string.IsNullOrWhiteSpace(json),
            "The bundled tenwin_builds.json seed should be embedded in the assembly."
        );

        var corpusType = repositoryType.Assembly.GetType(
            "BazaarPlusPlus.Game.LiveBuildPanel.Recommendations.TenWinBuildCorpus"
        )!;
        var parse = corpusType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static)!;
        var corpus = parse.Invoke(null, [json]);
        Assert(corpus != null, "The bundled seed should parse with the production corpus parser.");
        var heroCount = (int)corpusType.GetProperty("HeroCount")!.GetValue(corpus)!;
        Assert(heroCount > 0, "The bundled seed should contain at least one hero.");
        Assert(
            corpusType.GetProperty("GeneratedAtUtc")!.GetValue(corpus) != null,
            "The bundled seed should carry a parseable generatedAt timestamp."
        );
        var buildCount = (int)corpusType.GetProperty("BuildCount")!.GetValue(corpus)!;
        Assert(buildCount > 0, "The bundled seed should count at least one build.");
    }

    // ---- Payload builders -------------------------------------------------

    private static string MainRecallPayload(string hero) =>
        Payload(
            cards: $"[\"{GuidA}\",\"{GuidB}\",\"{GuidC}\"]",
            enchantments: "[null,\"Fiery\"]",
            hero: hero,
            builds: $"[{Build("[0,1]", "[[0,0,1,0,1],[1,1,1,0,1]]", 100)},"
                + $"{Build("[0]", "[[0,0,1,0,1]]", 200)},"
                + $"{Build("[1]", "[[1,0,1,0,1]]", 150)}]",
            // A(ref0) -> builds 0,1 ; B(ref1) -> builds 0,2 ; C(ref2) absent (uncovered for this hero).
            cardIndex: "[[0,[0,1]],[1,[0,2]]]"
        );

    private static string ScorePayload(string hero, long score) =>
        Payload(
            cards: $"[\"{GuidA}\"]",
            enchantments: "[null]",
            hero: hero,
            builds: $"[{Build("[0]", "[[0,0,1,0,1]]", score)}]",
            cardIndex: "[[0,[0]]]"
        );

    private static string Build(
        string cardRefs,
        string layout,
        long score,
        string selection = "[0,null]"
    ) => $"[{cardRefs},{layout},[300,80,2667,118,13,21,45,12,2667,112,{score}],{selection}]";

    private static string Payload(
        string cards,
        string enchantments,
        string hero,
        string builds,
        string cardIndex
    ) =>
        $$"""
            {
              "schema_version": 2,
              "kind": "mod_tenwin_builds",
              "cards": {{cards}},
              "enchantments": {{enchantments}},
              "schemas": {
                "build": ["card_refs", "layout", "stats", "selection"],
                "layout": ["card_ref", "slot", "tier", "enchant_ref", "size"],
                "stats": ["completed_run_count", "ten_win_run_count", "ten_win_rate_bps", "avg_ten_win_final_day_tenth", "p75_ten_win_final_day", "avg_ten_win_final_losses_tenth", "elite_completed_run_count", "elite_ten_win_run_count", "elite_ten_win_rate_bps", "elite_avg_ten_win_final_day_tenth", "score"],
                "selection": ["reason", "covered_card_ref"]
              },
              "selection_reasons": ["core", "coverage"],
              "heroes": {
                "{{hero}}": {
                  "builds": {{builds}},
                  "card_index": {{cardIndex}}
                }
              }
            }
            """;

    // ---- Reflection plumbing ----------------------------------------------

    private static void RegisterAssemblyResolution()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var assemblyName = new AssemblyName(args.Name);
            if (
                !string.Equals(
                    assemblyName.Name,
                    "UnityEngine.CoreModule",
                    StringComparison.Ordinal
                )
            )
                return null;

            var assemblyPath = Path.Combine(AppContext.BaseDirectory, "UnityEngine.CoreModule.dll");
            return File.Exists(assemblyPath) ? Assembly.LoadFrom(assemblyPath) : null;
        };
    }

    private static Type GetRepositoryType()
    {
        var assembly = Assembly.Load("BazaarPlusPlus");
        return assembly.GetType(
            "BazaarPlusPlus.Game.LiveBuildPanel.Recommendations.BuildRecommendationRepository"
        )!;
    }

    private static object NewRepository(Type repositoryType) =>
        Activator.CreateInstance(repositoryType)!;

    private static void WithCorpus(string payload, Action<Type, object> body)
    {
        var repositoryType = GetRepositoryType();
        var now = new DateTime(2026, 06, 07, 12, 0, 0, DateTimeKind.Utc);
        var cachePath = TempCachePath();
        File.WriteAllText(cachePath, payload);
        File.SetLastWriteTimeUtc(cachePath, now.AddHours(-1));
        Configure(
            repositoryType,
            cachePath,
            now,
            _ => throw new InvalidOperationException("Fresh cache should not download.")
        );
        try
        {
            body(repositoryType, NewRepository(repositoryType));
        }
        finally
        {
            Reset(repositoryType);
            TryDelete(cachePath);
        }
    }

    private static IEnumerable Find(
        Type repositoryType,
        object repository,
        string hero,
        string[] templateIds,
        object? liveState = null
    )
    {
        var method = repositoryType.GetMethod("FindRecommendations")!;
        var ids = templateIds.Select(Guid.Parse).ToArray();
        return (IEnumerable)method.Invoke(repository, [hero, ids, liveState])!;
    }

    private static object LiveState(
        Type repositoryType,
        string[] board,
        string[] stash,
        string[] shop
    )
    {
        var liveStateType = repositoryType.Assembly.GetType(
            "BazaarPlusPlus.Game.LiveBuildPanel.Recommendations.BuildLiveState"
        )!;
        var from = liveStateType.GetMethod("From")!;
        return from.Invoke(
            null,
            [
                board.Select(Guid.Parse).ToArray(),
                stash.Select(Guid.Parse).ToArray(),
                shop.Select(Guid.Parse).ToArray(),
            ]
        )!;
    }

    private static List<long> ScoresOf(IEnumerable recommendations) =>
        recommendations.Cast<object>().Select(r => (long)Prop(r, "Score")!).ToList();

    private static object? Prop(object target, string name) =>
        target.GetType().GetProperty(name)!.GetValue(target);

    private static string TempCachePath() =>
        Path.Combine(Path.GetTempPath(), $"bpp-tenwin-{Guid.NewGuid():N}.json");

    private static void Configure(
        Type repositoryType,
        string cachePath,
        DateTime utcNow,
        Func<string, string> downloadJson
    ) =>
        InvokeStatic(
            repositoryType,
            "ConfigureTenWinRemoteForTests",
            cachePath,
            (Func<DateTime>)(() => utcNow),
            downloadJson
        );

    private static void ConfigureWithBackgroundRefresh(
        Type repositoryType,
        string cachePath,
        DateTime utcNow,
        Func<string, string> downloadJson,
        Action<Action> queueBackgroundRefresh
    ) =>
        InvokeStatic(
            repositoryType,
            "ConfigureTenWinRemoteForTests",
            cachePath,
            (Func<DateTime>)(() => utcNow),
            downloadJson,
            queueBackgroundRefresh
        );

    private static void Reset(Type repositoryType) =>
        InvokeStatic(repositoryType, "ResetTenWinRemoteForTests");

    private static void SetEmbedded(Type repositoryType, Func<string?> loader) =>
        InvokeStatic(repositoryType, "SetEmbeddedJsonForTests", loader);

    private static bool ManualRefresh(Type repositoryType, out string? error)
    {
        var method = repositoryType.GetMethod(
            "TryRefreshFinalBuildsFromRemote",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert(method != null, "Repository should expose a manual remote refresh.");
        object?[] parameters = [null];
        var refreshed = (bool)method!.Invoke(null, parameters)!;
        error = (string?)parameters[0];
        return refreshed;
    }

    private static void InvokeStatic(Type type, string methodName, params object[] parameters)
    {
        var method = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(m =>
                m.Name == methodName && m.GetParameters().Length == parameters.Length
            );
        Assert(
            method != null,
            $"Expected {type.FullName}.{methodName} ({parameters.Length} args) to exist."
        );
        method!.Invoke(null, parameters);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
