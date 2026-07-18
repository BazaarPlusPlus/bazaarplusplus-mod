using System.Collections;
using System.Reflection;
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;

// Behavior tests for the analyzer-v4 ten-win build corpus consumed by LiveBuildPanel.
// The payload is the compact string-table + schema-driven array-row format emitted by
// bazaarplusplus-analyzers/src/bpp/stages/analyze/mod_builds.py at
// analyzer-v4/mod/tenwin_builds.json. Every assertion drives the public
// BuildRecommendationRepository.FindRecommendations surface (parse + recall + scoring +
// board projection). Catalog lifecycle is tested through IRemoteEmbeddedCatalog in the dedicated
// RemoteEmbeddedCatalog.Tests project; this feature project keeps parser/query/summary and manual
// refresh product policy coverage.

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
        TestRefreshServicePreservesSessionProductSemantics();
        TestPanelInvalidationRejectsUiContinuationWithoutCancelingCatalogRefresh();
        TestEmbeddedSeedResourceIsBundledAndParses();
        TestCorpusSummaryIncludesPerHeroBuildCounts();

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
        var gameRootPath = Path.Combine(Path.GetTempPath(), $"bpp-game-root-{Guid.NewGuid():N}");
        var cachePath = TenWinBuildCatalogFactory.BuildCacheFilePath(gameRootPath);

        Assert(
            cachePath == Path.Combine(gameRootPath, "BazaarPlusPlusV4", "tenwin_builds.json"),
            "Ten-win build cache should live under GameRoot/BazaarPlusPlusV4/tenwin_builds.json."
        );
    }

    private static void TestRefreshServicePreservesSessionProductSemantics()
    {
        var corpus = TenWinBuildCorpus.Parse(ScorePayload("CacheHero", 333))!;
        using var catalog = new StubCatalog(corpus);
        var repository = new BuildRecommendationRepository(catalog);
        var service = new BuildRecommendationRefreshService();

        catalog.Enqueue(
            CatalogRefreshResult<TenWinBuildCorpus>.Failure(
                new CatalogIssue(
                    CatalogIssueKind.RemoteDownloadFailed,
                    new InvalidOperationException("refresh-boom")
                )
            )
        );
        var failure = service
            .RefreshAsync(repository, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert(!failure.Succeeded, "A failed pull should surface as a product failure.");
        Assert(
            failure.Error?.Contains("refresh-boom") == true,
            "The failure should retain its typed diagnostic detail."
        );

        catalog.Enqueue(
            CatalogRefreshResult<TenWinBuildCorpus>.Published(
                Snapshot(corpus, CatalogSource.Remote),
                degraded: false
            )
        );
        var firstSuccess = service
            .RefreshAsync(repository, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert(
            firstSuccess.Outcome == BuildRecommendationRefreshOutcome.Updated,
            "The first successful remote pull must be Updated even when the payload is identical."
        );
        Assert(catalog.RefreshCount == 2, "Failure must not consume the session pull allowance.");

        var gated = service
            .RefreshAsync(repository, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert(
            gated.Outcome == BuildRecommendationRefreshOutcome.NoChange,
            "Only a session-gated pull should report NoChange."
        );
        Assert(catalog.RefreshCount == 2, "The gated pull must perform zero downloads.");
    }

    private static void TestPanelInvalidationRejectsUiContinuationWithoutCancelingCatalogRefresh()
    {
        var initialCorpus = TenWinBuildCorpus.Parse(ScorePayload("InitialHero", 111))!;
        var refreshedCorpus = TenWinBuildCorpus.Parse(ScorePayload("RefreshedHero", 222))!;
        using var catalog = new BlockingCatalog(initialCorpus);
        var repository = new BuildRecommendationRepository(catalog);
        var service = new BuildRecommendationRefreshService();
        var continuation = new LiveBuildRefreshContinuationGate();
        var operationVersion = continuation.Capture();

        var refresh = service.RefreshAsync(repository, CancellationToken.None);
        catalog.Started.GetAwaiter().GetResult();
        continuation.Invalidate();
        catalog.Complete(refreshedCorpus);

        var result = refresh.GetAwaiter().GetResult();
        Assert(
            result.Succeeded,
            "The plugin-lifetime catalog refresh should finish after panel loss."
        );
        Assert(
            !continuation.IsCurrent(operationVersion),
            "The destroyed panel must reject its stale UI continuation."
        );
        var summary = repository.GetCorpusSummary();
        Assert(summary.HasValue, "The shared repository should retain the refreshed snapshot.");
        var loadedSummary = summary.GetValueOrDefault();
        Assert(
            loadedSummary.HeroBuildCounts.Any(row => row.Hero == "RefreshedHero"),
            "The shared snapshot should publish even though the initiating panel was destroyed."
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

    private static void TestEmbeddedSeedResourceIsBundledAndParses()
    {
        var source = new AssemblyResourceCatalogSource(
            typeof(TenWinBuildCorpus).Assembly,
            TenWinBuildCatalogFactory.EmbeddedResourceName
        );
        var json = source.ReadAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        Assert(
            !string.IsNullOrWhiteSpace(json),
            "The bundled tenwin_builds.json seed should be embedded in the assembly."
        );

        var corpus = TenWinBuildCorpus.Parse(json);
        Assert(corpus != null, "The bundled seed should parse with the production corpus parser.");
        Assert(corpus!.HeroCount > 0, "The bundled seed should contain at least one hero.");
        Assert(
            corpus.GeneratedAtUtc != null,
            "The bundled seed should carry a parseable generatedAt timestamp."
        );
        Assert(corpus.BuildCount > 0, "The bundled seed should count at least one build.");
    }

    private static void TestCorpusSummaryIncludesPerHeroBuildCounts()
    {
        WithCorpus(
            TwoHeroSummaryPayload(),
            (repositoryType, repository) =>
            {
                var getSummary = repositoryType.GetMethod("GetCorpusSummary");
                Assert(getSummary != null, "Repository should expose corpus summary.");
                var summary = getSummary!.Invoke(repository, null);
                Assert(summary != null, "Loaded corpus summary should not be null.");

                Assert((int)Prop(summary!, "HeroCount")! == 2, "Summary should count both heroes.");
                Assert(
                    (int)Prop(summary!, "BuildCount")! == 3,
                    "Summary should count all build rows across heroes."
                );

                var heroCounts = ((IEnumerable)Prop(summary!, "HeroBuildCounts")!)
                    .Cast<object>()
                    .Select(row => ((string)Prop(row, "Hero")!, (int)Prop(row, "BuildCount")!))
                    .ToList();
                Assert(heroCounts.Count == 2, "Summary should expose both hero count rows.");
                Assert(
                    heroCounts[0] == ("Vanessa", 2),
                    "Hero count rows should sort by descending build count."
                );
                Assert(
                    heroCounts[1] == ("Dooley", 1),
                    "Hero count rows should preserve exact hero names and counts."
                );
            }
        );
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

    private static string TwoHeroSummaryPayload()
    {
        var dooleyBuild = Build("[0]", "[[0,0,1,0,1]]", 111);
        var vanessaBuildA = Build("[0]", "[[0,0,1,0,1]]", 222);
        var vanessaBuildB = Build("[1]", "[[1,1,1,0,1]]", 333);

        return PayloadWithHeroes(
            cards: $"[\"{GuidA}\",\"{GuidB}\"]",
            enchantments: "[null]",
            heroes: $$"""
                "Dooley": {
                  "builds": [{{dooleyBuild}}],
                  "card_index": [[0,[0]]]
                },
                "Vanessa": {
                  "builds": [{{vanessaBuildA}},{{vanessaBuildB}}],
                  "card_index": [[0,[0]],[1,[1]]]
                }
            """
        );
    }

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
        PayloadWithHeroes(
            cards,
            enchantments,
            $$"""
                "{{hero}}": {
                  "builds": {{builds}},
                  "card_index": {{cardIndex}}
                }
            """
        );

    private static string PayloadWithHeroes(string cards, string enchantments, string heroes) =>
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
                {{heroes}}
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

    private static void WithCorpus(string payload, Action<Type, object> body)
    {
        var repositoryType = GetRepositoryType();
        var corpus = TenWinBuildCorpus.Parse(payload)!;
        using var catalog = new StubCatalog(corpus);
        body(repositoryType, new BuildRecommendationRepository(catalog));
    }

    private static CatalogSnapshot<TenWinBuildCorpus> Snapshot(
        TenWinBuildCorpus corpus,
        CatalogSource source
    ) =>
        new(
            corpus,
            source,
            new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc),
            IsStale: false,
            Issue: null
        );

    private sealed class StubCatalog : IRemoteEmbeddedCatalog<TenWinBuildCorpus>
    {
        private readonly Queue<CatalogRefreshResult<TenWinBuildCorpus>> _refreshes = new();
        private CatalogSnapshot<TenWinBuildCorpus>? _snapshot;

        internal StubCatalog(TenWinBuildCorpus corpus)
        {
            _snapshot = Snapshot(corpus, CatalogSource.Cache);
        }

        internal int RefreshCount { get; private set; }

        internal void Enqueue(CatalogRefreshResult<TenWinBuildCorpus> result) =>
            _refreshes.Enqueue(result);

        public bool TryGet(out CatalogSnapshot<TenWinBuildCorpus> snapshot)
        {
            if (_snapshot.HasValue)
            {
                snapshot = _snapshot.Value;
                return true;
            }

            snapshot = default;
            return false;
        }

        public ValueTask WarmAsync(CancellationToken cancellationToken = default) => default;

        public ValueTask<CatalogRefreshResult<TenWinBuildCorpus>> RefreshAsync(
            CancellationToken cancellationToken = default
        )
        {
            RefreshCount++;
            var result = _refreshes.Dequeue();
            if (result.Snapshot.HasValue)
                _snapshot = result.Snapshot.Value;
            return ValueTask.FromResult(result);
        }

        public void Dispose() => _snapshot = null;
    }

    private sealed class BlockingCatalog : IRemoteEmbeddedCatalog<TenWinBuildCorpus>
    {
        private readonly TaskCompletionSource<bool> _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<CatalogRefreshResult<TenWinBuildCorpus>> _refresh =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CatalogSnapshot<TenWinBuildCorpus>? _snapshot;

        internal BlockingCatalog(TenWinBuildCorpus corpus)
        {
            _snapshot = Snapshot(corpus, CatalogSource.Cache);
        }

        internal Task Started => _started.Task;

        internal void Complete(TenWinBuildCorpus corpus) =>
            _refresh.SetResult(
                CatalogRefreshResult<TenWinBuildCorpus>.Published(
                    Snapshot(corpus, CatalogSource.Remote),
                    degraded: false
                )
            );

        public bool TryGet(out CatalogSnapshot<TenWinBuildCorpus> snapshot)
        {
            if (_snapshot.HasValue)
            {
                snapshot = _snapshot.Value;
                return true;
            }

            snapshot = default;
            return false;
        }

        public ValueTask WarmAsync(CancellationToken cancellationToken = default) => default;

        public async ValueTask<CatalogRefreshResult<TenWinBuildCorpus>> RefreshAsync(
            CancellationToken cancellationToken = default
        )
        {
            _started.TrySetResult(true);
            var result = await _refresh.Task.ConfigureAwait(false);
            if (result.Snapshot.HasValue)
                _snapshot = result.Snapshot.Value;
            return result;
        }

        public void Dispose() => _snapshot = null;
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

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
