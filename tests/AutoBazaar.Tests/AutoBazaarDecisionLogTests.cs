using System;
using System.IO;
using BazaarPlusPlus.Game.AutoBazaar;
using Xunit;

public class AutoBazaarDecisionLogTests
{
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch { }
        }
    }

    [Fact]
    public void Append_NullRunId_WritesToFallbackPath()
    {
        using var tmp = new TempDir();
        var log = new AutoBazaarDecisionLog(tmp.Path);
        log.Append(
            new AutoBazaarDecisionLogEntry
            {
                TickId = 1,
                DecisionId = "01H",
                RunId = null,
                State = "Choice",
                Action = new AutoBazaarAction { ActionKind = AutoBazaarActionKind.Wait },
                Executed = true,
            }
        );
        Assert.True(File.Exists(System.IO.Path.Combine(tmp.Path, "decisions.jsonl")));
    }

    [Fact]
    public void Append_NonNullRunId_WritesToRunsSubdir()
    {
        using var tmp = new TempDir();
        var log = new AutoBazaarDecisionLog(tmp.Path);
        log.Append(
            new AutoBazaarDecisionLogEntry
            {
                RunId = "abc123",
                State = "Choice",
                Action = new AutoBazaarAction { ActionKind = AutoBazaarActionKind.Wait },
                Executed = true,
            }
        );
        Assert.True(
            File.Exists(System.IO.Path.Combine(tmp.Path, "runs", "abc123", "decisions.jsonl"))
        );
    }

    [Fact]
    public void Append_PathTraversalRunId_SanitizesNoEscape()
    {
        using var tmp = new TempDir();
        var log = new AutoBazaarDecisionLog(tmp.Path);
        log.Append(
            new AutoBazaarDecisionLogEntry
            {
                RunId = "../evil/run",
                State = "Choice",
                Action = new AutoBazaarAction { ActionKind = AutoBazaarActionKind.Wait },
                Executed = true,
            }
        );
        // No directory escape: the only directory created under tmp.Path is "runs/<sanitized>"
        Assert.False(Directory.Exists(System.IO.Path.Combine(tmp.Path, "..", "evil")));
        // Some sanitized dir exists under runs/
        var runsDir = System.IO.Path.Combine(tmp.Path, "runs");
        Assert.True(Directory.Exists(runsDir));
        var children = Directory.GetDirectories(runsDir);
        Assert.Single(children);
    }

    [Fact]
    public void Append_EmptyAfterSanitize_FallsBackToUnderscoreDir()
    {
        using var tmp = new TempDir();
        var log = new AutoBazaarDecisionLog(tmp.Path);
        log.Append(
            new AutoBazaarDecisionLogEntry
            {
                RunId = "....",
                State = "X",
                Action = new AutoBazaarAction { ActionKind = AutoBazaarActionKind.Wait },
                Executed = true,
            }
        );
        Assert.True(
            Directory.Exists(System.IO.Path.Combine(tmp.Path, "runs", "____"))
                || Directory.Exists(System.IO.Path.Combine(tmp.Path, "runs", "_"))
        );
        // Either form is acceptable depending on how aggressively you sanitize.
    }

    [Fact]
    public void Append_MultipleEntries_AppendsLineByLine()
    {
        using var tmp = new TempDir();
        var log = new AutoBazaarDecisionLog(tmp.Path);
        log.Append(
            new AutoBazaarDecisionLogEntry
            {
                RunId = "r1",
                DecisionId = "D1",
                State = "S",
                Action = new AutoBazaarAction { ActionKind = AutoBazaarActionKind.Wait },
                Executed = true,
            }
        );
        log.Append(
            new AutoBazaarDecisionLogEntry
            {
                RunId = "r1",
                DecisionId = "D2",
                State = "S",
                Action = new AutoBazaarAction { ActionKind = AutoBazaarActionKind.Reroll },
                Executed = false,
                Error = "stale-or-unavailable",
            }
        );
        var lines = File.ReadAllLines(
            System.IO.Path.Combine(tmp.Path, "runs", "r1", "decisions.jsonl")
        );
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"decisionId\":\"D1\"", lines[0]);
        Assert.Contains("\"decisionId\":\"D2\"", lines[1]);
        Assert.Contains("\"executed\":true", lines[0]);
        Assert.Contains("\"executed\":false", lines[1]);
        Assert.Contains("\"actionKind\":\"Reroll\"", lines[1]); // StringEnumConverter
    }

    [Fact]
    public void Append_AutoFillsTimestampIfBlank()
    {
        using var tmp = new TempDir();
        var log = new AutoBazaarDecisionLog(tmp.Path);
        log.Append(
            new AutoBazaarDecisionLogEntry
            {
                RunId = "r2",
                State = "X",
                Action = new AutoBazaarAction { ActionKind = AutoBazaarActionKind.Wait },
                Executed = true,
            }
        );
        var line = File.ReadAllText(
            System.IO.Path.Combine(tmp.Path, "runs", "r2", "decisions.jsonl")
        );
        Assert.Contains("\"ts\":\"", line);
        Assert.DoesNotContain("\"ts\":\"\"", line); // must not have written empty ts
    }
}
