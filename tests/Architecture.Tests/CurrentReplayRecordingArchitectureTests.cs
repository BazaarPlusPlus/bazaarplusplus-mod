using Xunit;

namespace Architecture.Tests;

public sealed class CurrentReplayRecordingArchitectureTests
{
    [Fact]
    public void Ordinary_saved_replay_reuses_the_native_replay_action_for_recording()
    {
        var repoRoot = RepoRoot();
        var runtimeSource = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "src",
                "BazaarPlusPlus",
                "Game",
                "CombatReplay",
                "CombatReplayRuntime.cs"
            )
        );
        var controllerSource = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "src",
                "BazaarPlusPlus",
                "Game",
                "CombatReplay",
                "CurrentReplayRecordingButtonController.cs"
            )
        );

        Assert.Contains(
            "ReplayRecordingButtonSnapshotPolicy.OrdinaryManagedReplay(",
            runtimeSource
        );
        Assert.Contains("TryStartManagedReplayRecording(", runtimeSource);
        Assert.Contains("operation.TryPromoteToRecording()", runtimeSource);
        Assert.Contains(
            "publisher.TryPromoteActiveSessionToRecording(operation.BattleId)",
            runtimeSource
        );
        Assert.Contains("invokeNativeReplay();", runtimeSource);
        Assert.Contains("if (!replay.IsReplaying)", runtimeSource);
        Assert.Contains(
            "_button.interactable = snapshot.CanReveal || (nativeActionsBound && snapshot.CanStart)",
            controllerSource
        );
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
