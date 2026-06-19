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
var coordinatorType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelCoordinator");
var dependenciesType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelDependencies");
var dataServiceType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.Storage.HistoryPanelDataService"
);

TestRunHeroFilterShowsAllWhenEmpty();
TestRunHeroFilterMatchesCaseInsensitiveHeroOnly();
TestStateSelectedRunUsesFilteredRunList();
TestCoordinatorRunSelectionUsesFilteredSpace();
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
    var dependencies = Construct(dependenciesType, null, dataService, null, null);
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
