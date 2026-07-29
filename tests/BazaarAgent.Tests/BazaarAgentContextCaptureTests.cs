#nullable enable
using BazaarPlusPlus.BazaarAgent;
using Newtonsoft.Json.Linq;
using Xunit;

public sealed class BazaarAgentContextCaptureTests
{
    [Fact]
    public void Capture_writes_raw_snapshot_and_duplication_metrics()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var capture = new BazaarAgentContextCapture(temporaryDirectory.Path, "session-a");
        var card = new BazaarAgentCardSnapshot
        {
            InstanceId = "item-1",
            Kind = BazaarAgentCardKind.Item,
            TemplateId = "template-1",
            DisplayName = "Test Item",
            Tags = new[] { "Weapon" },
            HiddenTags = new[] { "Hidden" },
            Attributes = new Dictionary<string, int> { ["Damage"] = 5 },
            ActiveAbilities = new[]
            {
                new BazaarAgentCardAbilitySnapshot { Id = "ability-1", Action = "DealDamage" },
            },
        };
        var context = new BazaarAgentContext
        {
            TickId = 7,
            RunId = "run-1",
            StateName = BazaarAgentRunStateName.Choice,
            BoardItems = new[] { card },
            SellableItems = new[] { card },
            AvailableActions = new[]
            {
                new BazaarAgentDecisionOption
                {
                    ActionKind = BazaarAgentActionKind.MoveItem,
                    CardInstanceId = card.InstanceId,
                    TargetSection = BazaarAgentTargetSection.Hand,
                    TargetSockets = new[] { "Socket_1" },
                    Card = card,
                },
            },
        };

        capture.Capture(new BazaarAgentContextSnapshot(context));

        var snapshotPath = System.IO.Path.Combine(
            capture.SessionDirectory,
            "snapshots",
            "00000007-choice.json"
        );
        Assert.True(File.Exists(snapshotPath));
        Assert.Contains("\"displayName\":\"Test Item\"", File.ReadAllText(snapshotPath));

        var metricsPath = System.IO.Path.Combine(capture.SessionDirectory, "metrics.jsonl");
        var metrics = JObject.Parse(Assert.Single(File.ReadAllLines(metricsPath)));
        Assert.Equal(3, metrics.Value<int>("totalFullCardCopies"));
        Assert.Equal(1, metrics.Value<int>("uniqueCardInstanceIds"));
        Assert.Equal(2, metrics.Value<int>("redundantFullCardCopies"));
        Assert.Equal(1, metrics.Value<int>("placementActionCount"));
        Assert.Equal(3, metrics.Value<int>("activeAbilityCount"));
        Assert.True(metrics["sectionBytes"]!["availableActions"]!.Value<int>() > 0);
    }

    [Fact]
    public void Capture_sanitizes_explicit_session_id()
    {
        using var temporaryDirectory = new TemporaryDirectory();

        var capture = new BazaarAgentContextCapture(temporaryDirectory.Path, "../session/escape");
        capture.Capture(new BazaarAgentContextSnapshot(new BazaarAgentContext { TickId = 1 }));

        Assert.StartsWith(
            System.IO.Path.GetFullPath(temporaryDirectory.Path)
                + System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.GetFullPath(capture.SessionDirectory),
            StringComparison.Ordinal
        );
        Assert.True(File.Exists(System.IO.Path.Combine(capture.SessionDirectory, "metrics.jsonl")));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch { }
        }
    }
}
