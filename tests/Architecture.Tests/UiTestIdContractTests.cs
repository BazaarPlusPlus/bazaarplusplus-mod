#nullable enable
using System.Reflection;
using System.Runtime.CompilerServices;
using BazaarPlusPlus.Infrastructure.UiTesting;
using Xunit;

namespace Architecture.Tests;

public sealed class UiTestIdContractTests
{
    [Fact]
    public void Plugin_ui_test_ids_are_nonempty_and_unique()
    {
        var ids = typeof(BppUiTestIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void History_ui_toolkit_ids_use_web_like_kebab_case()
    {
        var historyElementIds = typeof(BppUiTestIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.Name.StartsWith("History", StringComparison.Ordinal))
            .Where(field => field.Name != nameof(BppUiTestIds.HistoryRootObject))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.All(historyElementIds, id => Assert.StartsWith("bpp-history-", id));
    }

    [Fact]
    public void Runtime_surfaces_bind_the_shared_test_ids()
    {
        var repoRoot = RepoRoot();
        var mainSource = Path.Combine(repoRoot, "src", "BazaarPlusPlus");
        var historyView = File.ReadAllText(
            Path.Combine(mainSource, "Game", "HistoryPanel", "Ui", "HistoryPanelUiToolkitView.cs")
        );
        var historyTree = File.ReadAllText(
            Path.Combine(
                mainSource,
                "Game",
                "HistoryPanel",
                "Ui",
                "HistoryPanelUiToolkitView.Tree.cs"
            )
        );
        var currentReplay = File.ReadAllText(
            Path.Combine(
                mainSource,
                "Game",
                "CombatReplay",
                "CurrentReplayRecordingButtonController.cs"
            )
        );
        Assert.Contains("BppUiTestIds.HistoryRoot", historyView);
        Assert.Contains("BppUiTestIds.HistoryRecord", historyTree);
        Assert.Contains("BppUiTestIds.HistoryReplay", historyTree);
        Assert.Contains("BppUiTestIds.HistoryDetailedReport", historyTree);
        Assert.Contains("BppUiTestIds.CurrentReplayRecord", currentReplay);
        Assert.Contains("BppUiTestIds.CurrentReplayRecordAgain", currentReplay);
    }

    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        var testDirectory = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testDirectory, "..", ".."));
    }
}
