#nullable enable
using System.Collections;
using System.Reflection;

var modAssembly = Assembly.Load("BazaarPlusPlus");
var stateType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelState");
var runRecordType = RequireType("BazaarPlusPlus.Game.HistoryPanel.Data.HistoryRunRecord");
var battleRecordType = RequireType("BazaarPlusPlus.Game.HistoryPanel.Data.HistoryBattleRecord");
var snapshotCountsType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.Data.HistoryBattleSnapshotCounts"
);
var battleSourceType = RequireType("BazaarPlusPlus.Game.HistoryPanel.Data.HistoryBattleSource");
var ghostFilterType = RequireType("BazaarPlusPlus.Game.HistoryPanel.GhostBattleFilter");
var ghostBattleFilterType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.HistoryPanelGhostBattleFilter"
);
var runHeroFilterType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelRunHeroFilter");
var heroPresentationType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.HistoryPanelHeroPresentation"
);
var coordinatorType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelCoordinator");
var dependenciesType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelDependencies");
var dataServiceType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.Storage.HistoryPanelDataService"
);

TestRunHeroFilterShowsAllWhenEmpty();
TestRunHeroFilterMatchesCaseInsensitiveHeroOnly();
TestRunHeroFilterMatchesLegacyOnlyHistory();
TestRunHeroFilterMatchesCanonicalOnlyHistory();
TestRunHeroFilterMatchesMixedAliasHistoryWithoutRewritingRecords();
TestRunHeroFilterRejectsHistoryWithoutTheDragons();
TestRunHeroRosterAddsOneCanonicalTheDragonsAfterTheExistingSeven();
TestRunHeroPresentationTreatsAliasesAsSelectedAndDisplaysCanonicalName();
TestCoordinatorCanonicalizesAliasFilterState();
TestStateSelectedRunUsesFilteredRunList();
TestCoordinatorRunSelectionUsesFilteredSpace();
TestReplayReturnPreservesSelectionAndFilters();
TestGhostDayFilterRequiresDayTenOrLaterAndKeepsOutcomeFilter();

Console.WriteLine("HistoryPanelFiltering checks passed.");

void TestRunHeroFilterShowsAllWhenEmpty()
{
    var run = CreateRun("run-1", "Vanessa");

    Assert(RunHeroMatches(null, run), "Null hero filter should include every run.");
    Assert(RunHeroMatches("", run), "Empty hero filter should include every run.");
}

void TestRunHeroFilterMatchesCaseInsensitiveHeroOnly()
{
    var vanessa = CreateRun("run-1", "Vanessa");
    var mak = CreateRun("run-2", "Mak");

    Assert(RunHeroMatches("vanessa", vanessa), "Hero matching should ignore case.");
    Assert(!RunHeroMatches("Vanessa", mak), "Hero filter should exclude other heroes.");
}

void TestRunHeroFilterMatchesLegacyOnlyHistory()
{
    var legacy = CreateRun("legacy", "Hero8");

    Assert(
        RunHeroMatches("TheDragons", legacy),
        "The canonical filter should include legacy-only The Dragons history."
    );
}

void TestRunHeroFilterMatchesCanonicalOnlyHistory()
{
    var canonical = CreateRun("canonical", "TheDragons");

    Assert(
        RunHeroMatches("Hero8", canonical),
        "The legacy filter input should include canonical-only The Dragons history."
    );
}

void TestRunHeroFilterMatchesMixedAliasHistoryWithoutRewritingRecords()
{
    var legacy = CreateRun("legacy", "Hero8");
    var canonical = CreateRun("canonical", "TheDragons");
    var records = new[] { legacy, canonical };

    var matches = records.Where(run => RunHeroMatches("TheDragons", run)).ToArray();

    Assert(matches.Length == 2, "A canonical filter should include both alias forms.");
    Assert(GetString(legacy, "Hero") == "Hero8", "Filtering must not rewrite the legacy run hero.");
    Assert(
        GetString(canonical, "Hero") == "TheDragons",
        "Filtering must not rewrite the canonical run hero."
    );
}

void TestRunHeroFilterRejectsHistoryWithoutTheDragons()
{
    var vanessa = CreateRun("vanessa", "Vanessa");

    Assert(
        !RunHeroMatches("TheDragons", vanessa),
        "The Dragons filter should reject history with no matching alias."
    );
}

void TestRunHeroRosterAddsOneCanonicalTheDragonsAfterTheExistingSeven()
{
    var roster = (IEnumerable)
        heroPresentationType
            .GetProperty(
                "RunFilterHeroIds",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            )!
            .GetValue(null)!;
    var heroIds = roster.Cast<string>().ToArray();

    Assert(
        heroIds.SequenceEqual([
            "Vanessa",
            "Pygmalien",
            "Dooley",
            "Mak",
            "Jules",
            "Karnok",
            "Stelle",
            "TheDragons",
        ]),
        "History should preserve the existing seven hero order and append one canonical The Dragons chip."
    );
    Assert(
        heroIds.Count(hero => hero == "TheDragons") == 1,
        "History should expose exactly one canonical The Dragons chip."
    );
    Assert(
        !heroIds.Contains("Hero8"),
        "History should never expose a separate legacy The Dragons chip."
    );
}

void TestRunHeroPresentationTreatsAliasesAsSelectedAndDisplaysCanonicalName()
{
    Assert(
        (bool)InvokeStatic(heroPresentationType, "IsSelected", "Hero8", "TheDragons")!,
        "A canonical chip should stay selected for legacy filter state."
    );
    Assert(
        (bool)InvokeStatic(heroPresentationType, "IsSelected", "TheDragons", "Hero8")!,
        "Alias-selected state should be symmetric."
    );
    Assert(
        (string)InvokeStatic(heroPresentationType, "DisplayName", "Hero8")! == "The Dragons",
        "Legacy history should display the canonical native The Dragons name."
    );
    Assert(
        (string)InvokeStatic(heroPresentationType, "DisplayName", "TheDragons")! == "The Dragons",
        "Canonical history should display the native The Dragons name."
    );
}

void TestCoordinatorCanonicalizesAliasFilterState()
{
    var state =
        Activator.CreateInstance(stateType)
        ?? throw new InvalidOperationException("HistoryPanelState should construct.");
    var dataService = Construct(dataServiceType, null, null);
    // Single 7-arg ctor arity match (issue #167): runState, dataService, replayService,
    // serverHealthProbe, accountLinkClient, isBazaarDbAccountLinkAvailable, combatReplayDirectoryPath.
    var dependencies = Construct(dependenciesType, null, dataService, null, null, null, null, null);
    var coordinator =
        Activator.CreateInstance(
            coordinatorType,
            state,
            dependencies,
            (Action)(() => { }),
            (Action)(() => { }),
            (Action<bool>)(_ => { })
        ) ?? throw new InvalidOperationException("HistoryPanelCoordinator should construct.");

    Invoke(coordinatorType, coordinator, "SetRunHeroFilter", "Hero8");
    Assert(
        GetNullableString(state, "SelectedRunHero") == "TheDragons",
        "Legacy filter input should persist as canonical History UI state."
    );

    Invoke(coordinatorType, coordinator, "SetRunHeroFilter", "TheDragons");
    Assert(
        GetNullableString(state, "SelectedRunHero") == null,
        "Selecting either alias again should toggle the canonical filter off."
    );
}

void TestStateSelectedRunUsesFilteredRunList()
{
    var state =
        Activator.CreateInstance(stateType)
        ?? throw new InvalidOperationException("HistoryPanelState should construct.");
    stateType.GetProperty("SelectedRunIndex")!.SetValue(state, 1);

    var runs = CreateRunList(CreateRun("raw-1", "Vanessa"), CreateRun("raw-3", "Vanessa"));
    var selected = Invoke(stateType, state, "GetSelectedRun", runs);

    Assert(GetString(selected, "RunId") == "raw-3", "Selected run should come from filtered runs.");
}

void TestCoordinatorRunSelectionUsesFilteredSpace()
{
    var state =
        Activator.CreateInstance(stateType)
        ?? throw new InvalidOperationException("HistoryPanelState should construct.");
    GetList(state, "Runs").Add(CreateRun("raw-1", "Vanessa"));
    GetList(state, "Runs").Add(CreateRun("raw-2", "Mak"));
    GetList(state, "Runs").Add(CreateRun("raw-3", "Vanessa"));
    stateType.GetProperty("SelectedRunHero")!.SetValue(state, "Vanessa");

    var dataService = Construct(dataServiceType, null, null);
    // Single 7-arg ctor arity match (issue #167): runState, dataService, replayService,
    // serverHealthProbe, accountLinkClient, isBazaarDbAccountLinkAvailable, combatReplayDirectoryPath.
    var dependencies = Construct(dependenciesType, null, dataService, null, null, null, null, null);
    var coordinator =
        Activator.CreateInstance(
            coordinatorType,
            state,
            dependencies,
            (Action)(() => { }),
            (Action)(() => { }),
            (Action<bool>)(_ => { })
        ) ?? throw new InvalidOperationException("HistoryPanelCoordinator should construct.");

    Invoke(coordinatorType, coordinator, "SelectRun", 1);
    var selected = Invoke(coordinatorType, coordinator, "GetSelectedRun");

    Assert(
        GetString(selected, "RunId") == "raw-3",
        "SelectRun should interpret indexes in filtered run space, not raw run space."
    );
}

void TestReplayReturnPreservesSelectionAndFilters()
{
    var state = Activator.CreateInstance(stateType)!;
    var firstRun = CreateRun("run-1", "Vanessa");
    var selectedRun = CreateRun("run-2", "Vanessa");
    var selectedBattle = CreateBattle("battle-2", 12, "Lost");
    GetList(state, "Runs").Add(firstRun);
    GetList(state, "Runs").Add(selectedRun);
    GetList(state, "Battles").Add(CreateBattle("battle-1", 11, "Won"));
    GetList(state, "Battles").Add(selectedBattle);
    stateType.GetProperty("SelectedRunIndex")!.SetValue(state, 1);
    stateType.GetProperty("SelectedBattleIndex")!.SetValue(state, 1);
    stateType.GetProperty("SelectedGhostBattleIndex")!.SetValue(state, 2);
    stateType.GetProperty("SelectedRunHero")!.SetValue(state, "Vanessa");
    stateType.GetProperty("GhostDayMin10")!.SetValue(state, true);
    stateType
        .GetProperty("GhostBattleFilter")!
        .SetValue(state, Enum.Parse(ghostFilterType, "ILost"));
    var dataService = Construct(dataServiceType, null, null);
    var dependencies = Construct(dependenciesType, null, dataService, null, null, null, null, null);
    var previewRequests = 0;
    using var coordinator = (IDisposable)
        Activator.CreateInstance(
            coordinatorType,
            state,
            dependencies,
            (Action)(() => { }),
            (Action)(() => previewRequests++),
            (Action<bool>)(_ => { })
        )!;

    // This capsule has no Unity PlayerPrefs; exercise the panel as a signed-out client.
    var bridge = RequireType("BazaarPlusPlus.GameInterop.BppClientCacheBridge");
    var cacheType = bridge.GetField(
        "_clientCacheType",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;
    var resolved = bridge.GetField(
        "_clientCacheTypeResolved",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;
    var previousType = cacheType.GetValue(null);
    var previousResolved = resolved.GetValue(null);
    cacheType.SetValue(null, null);
    resolved.SetValue(null, true);
    try
    {
        Invoke(coordinatorType, coordinator, "OnPanelHidden");
        previewRequests = 0;
        Invoke(coordinatorType, coordinator, "OnPanelShown", true);

        Assert(
            GetList(state, "Runs").Count == 2
                && ReferenceEquals(GetList(state, "Runs")[1], selectedRun),
            "Returning from replay must retain the loaded run list."
        );
        Assert(
            ReferenceEquals(Invoke(stateType, state, "GetSelectedBattle"), selectedBattle),
            "Returning from replay must retain the selected battle, not the latest battle."
        );
        Assert(
            (int)stateType.GetProperty("SelectedRunIndex")!.GetValue(state)! == 1,
            "Returning from replay must retain the selected run index."
        );
        Assert(
            GetNullableString(state, "SelectedRunHero") == "Vanessa"
                && (bool)stateType.GetProperty("GhostDayMin10")!.GetValue(state)!
                && stateType.GetProperty("GhostBattleFilter")!.GetValue(state)!.ToString()
                    == "ILost"
                && (int)stateType.GetProperty("SelectedGhostBattleIndex")!.GetValue(state)! == 2,
            "Returning from replay must retain both run and ghost filters and selection."
        );
        Assert(
            previewRequests == 1,
            "Returning from replay must refresh the selected preview once."
        );

        Invoke(coordinatorType, coordinator, "OnPanelHidden");
        Invoke(coordinatorType, coordinator, "OnPanelShown", false);
        Assert(
            GetList(state, "Runs").Count == 0,
            "A normal panel open must reload from storage instead of reusing replay-origin state."
        );
    }
    finally
    {
        cacheType.SetValue(null, previousType);
        resolved.SetValue(null, previousResolved);
    }
}

void TestGhostDayFilterRequiresDayTenOrLaterAndKeepsOutcomeFilter()
{
    var all = Enum.Parse(ghostFilterType, "All");
    var won = Enum.Parse(ghostFilterType, "IWon");
    var lost = Enum.Parse(ghostFilterType, "ILost");

    Assert(
        GhostMatches(all, true, CreateBattle("day-10-win", 10, "Won")),
        "Day >= 10 should pass the day filter."
    );
    Assert(
        GhostMatches(all, true, CreateBattle("day-12-loss", 12, "Lost")),
        "Day > 10 should pass the day filter."
    );
    Assert(
        !GhostMatches(all, true, CreateBattle("day-9-win", 9, "Won")),
        "Day 9 should fail the day filter."
    );
    Assert(
        !GhostMatches(all, true, CreateBattle("day-null-win", null, "Won")),
        "Null day should fail the day filter."
    );
    Assert(
        GhostMatches(won, true, CreateBattle("day-10-win", 10, "Won")),
        "Day filter should preserve IWon matches."
    );
    Assert(
        !GhostMatches(won, true, CreateBattle("day-10-loss", 10, "Lost")),
        "Day filter should still exclude losses from IWon."
    );
    Assert(
        GhostMatches(lost, true, CreateBattle("day-10-loss", 10, "Lost")),
        "Day filter should preserve ILost matches."
    );
}

bool RunHeroMatches(string? selectedHero, object run) =>
    (bool)InvokeStatic(runHeroFilterType, "Matches", selectedHero, run)!;

bool GhostMatches(object filter, bool dayMin10, object battle) =>
    (bool)InvokeStatic(ghostBattleFilterType, "Matches", filter, dayMin10, battle)!;

object CreateRun(string runId, string hero)
{
    return Activator.CreateInstance(
            runRecordType,
            runId,
            hero,
            "Ranked",
            DateTimeOffset.Parse("2026-06-19T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-19T00:10:00Z"),
            DateTimeOffset.Parse("2026-06-19T00:10:00Z"),
            10,
            1,
            40,
            0,
            5,
            1,
            10,
            "Bronze",
            100,
            1,
            0,
            "finished",
            2
        ) ?? throw new InvalidOperationException("HistoryRunRecord should construct.");
}

object CreateBattle(string battleId, int? day, string result)
{
    var counts =
        Activator.CreateInstance(snapshotCountsType, 0, 0, 0, 0)
        ?? throw new InvalidOperationException("HistoryBattleSnapshotCounts should construct.");

    return Activator.CreateInstance(
            battleRecordType,
            battleId,
            "run-1",
            DateTimeOffset.Parse("2026-06-19T00:00:00Z"),
            day,
            1,
            null,
            "Vanessa",
            null,
            null,
            null,
            null,
            null,
            "Opponent",
            "Mak",
            null,
            null,
            null,
            null,
            null,
            null,
            "PVPCombat",
            result,
            null,
            null,
            counts,
            null,
            false,
            Enum.Parse(battleSourceType, "Ghost"),
            false,
            false
        ) ?? throw new InvalidOperationException("HistoryBattleRecord should construct.");
}

IList CreateRunList(params object[] runs)
{
    var listType = typeof(List<>).MakeGenericType(runRecordType);
    var list = (IList)(Activator.CreateInstance(listType) ?? throw new InvalidOperationException());
    foreach (var run in runs)
        list.Add(run);
    return list;
}

IList GetList(object instance, string propertyName) =>
    (IList)(
        instance.GetType().GetProperty(propertyName)?.GetValue(instance)
        ?? throw new InvalidOperationException($"{propertyName} should be a list.")
    );

object? Invoke(Type type, object instance, string methodName, params object?[] args)
{
    var method =
        type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SingleOrDefault(method =>
                method.Name == methodName && method.GetParameters().Length == args.Length
            )
        ?? throw new MissingMethodException(type.FullName, methodName);
    return method.Invoke(instance, args);
}

object? InvokeStatic(Type type, string methodName, params object?[] args)
{
    var method =
        type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(method =>
                method.Name == methodName && method.GetParameters().Length == args.Length
            )
        ?? throw new MissingMethodException(type.FullName, methodName);
    return method.Invoke(null, args);
}

string? GetString(object? instance, string propertyName) =>
    (string?)(
        instance?.GetType().GetProperty(propertyName)?.GetValue(instance)
        ?? throw new InvalidOperationException($"{propertyName} should exist.")
    );

string? GetNullableString(object instance, string propertyName)
{
    var property =
        instance.GetType().GetProperty(propertyName)
        ?? throw new InvalidOperationException($"{propertyName} should exist.");
    return (string?)property.GetValue(instance);
}

Type RequireType(string fullName) =>
    modAssembly.GetType(fullName)
    ?? throw new InvalidOperationException($"{fullName} should exist.");

object Construct(Type type, params object?[] args)
{
    var constructor =
        type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SingleOrDefault(ctor => ctor.GetParameters().Length == args.Length)
        ?? throw new MissingMethodException(type.FullName, ".ctor");
    return constructor.Invoke(args);
}

void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
