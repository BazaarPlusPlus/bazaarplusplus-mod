#nullable enable
using System.Net;
using BazaarPlusPlus.BazaarAgent;
using Xunit;

public sealed class BazaarAgentActivityFeedTests
{
    [Fact]
    public async Task WaitForEventsAsync_ReturnsAPublicationWithoutPollingDelay()
    {
        var feed = new BazaarAgentActivityFeed();
        var waiting = feed.WaitForEventsAsync(0, maximumEvents: 10, waitMilliseconds: 1000);

        feed.Publish("context.sent", "req-1", "/v1/context", "Context Choice at tick 1");

        var result = await waiting;
        var activity = Assert.Single(result.Events);
        Assert.Equal(1, activity.Sequence);
        Assert.Equal("context.sent", activity.Kind);
        Assert.Equal(1, result.LatestSequence);
    }

    [Fact]
    public async Task DashboardBootstrapAndContextTraffic_AreAvailableOverTheExistingListener()
    {
        using var fixture = new ServerFixture();
        fixture.SetSnapshot(
            new BazaarAgentContextSnapshot(
                new BazaarAgentContext
                {
                    TickId = 7,
                    StateName = BazaarAgentRunStateName.Choice,
                    PlayerGold = 10,
                }
            )
        );
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var context = await http.GetAsync($"http://127.0.0.1:{fixture.Port}/v1/context");
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);

        var bootstrap = await http.GetAsync(
            $"http://127.0.0.1:{fixture.Port}/dashboard/api/bootstrap"
        );
        Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);
        var body = await bootstrap.Content.ReadAsStringAsync();
        Assert.Contains("\"kind\":\"context.sent\"", body);
        Assert.Contains("\"tickId\":7", body);
        Assert.Contains("\"currentContextJson\":\"{", body);
    }

    [Fact]
    public async Task DashboardAssets_UseTheEmbeddedViteResourceNames()
    {
        using var fixture = new ServerFixture();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var dashboard = await http.GetAsync($"http://127.0.0.1:{fixture.Port}/dashboard/");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        var html = await dashboard.Content.ReadAsStringAsync();
        var assetStart = html.IndexOf("src=\"/dashboard/assets/", StringComparison.Ordinal);
        Assert.True(
            assetStart >= 0,
            "Dashboard HTML did not reference its JavaScript entry point."
        );
        var assetEnd = html.IndexOf('"', assetStart + 5);
        var assetPath = html.Substring(assetStart + 5, assetEnd - assetStart - 5);

        var asset = await http.GetAsync($"http://127.0.0.1:{fixture.Port}{assetPath}");
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal("text/javascript", asset.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void PayloadsOverTheSafetyLimit_AreTruncatedBeforeTheyEnterTheRingBuffer()
    {
        var feed = new BazaarAgentActivityFeed();
        var oversized = new string('x', 300 * 1024);

        feed.Publish(
            "action.received",
            "req-1",
            "/v1/actions",
            "Action Wait",
            requestJson: oversized
        );

        var activity = Assert.Single(feed.GetSince(0, maximumEvents: 10).Events);
        Assert.NotNull(activity.RequestJson);
        Assert.True(activity.RequestJson!.Length < oversized.Length);
        Assert.EndsWith("[truncated by BazaarAgent activity feed]", activity.RequestJson);
    }
}
