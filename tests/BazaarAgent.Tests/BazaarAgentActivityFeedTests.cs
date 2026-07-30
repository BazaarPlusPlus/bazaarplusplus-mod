#nullable enable
using System.Net;
using System.Text;
using BazaarPlusPlus.BazaarAgent;
using Xunit;

public sealed class BazaarAgentActivityFeedTests
{
    [Fact]
    public async Task WaitForEventsAsync_ReturnsAPublicationWithoutPollingDelay()
    {
        var feed = new BazaarAgentActivityFeed();
        var waiting = feed.WaitForEventsAsync(0, maximumEvents: 10, waitMilliseconds: 1000);

        feed.Publish("agent.view.sent", "req-1", "/v3/context", "Agent v3 context");

        var result = await waiting;
        Assert.Equal("agent.view.sent", Assert.Single(result.Events).Kind);
    }

    [Fact]
    public async Task DashboardBootstrap_ContainsOnlyV3ProtocolTraffic()
    {
        using var fixture = new ServerFixture();
        fixture.SetSnapshot(new BazaarAgentContextSnapshot(new BazaarAgentContext { TickId = 7 }));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var context = await http.GetAsync($"http://127.0.0.1:{fixture.Port}/v3/context");
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        var bootstrap = await http.GetStringAsync(
            $"http://127.0.0.1:{fixture.Port}/dashboard/api/bootstrap"
        );

        Assert.Contains("\"kind\":\"agent.view.sent\"", bootstrap);
        Assert.Contains("\"route\":\"/v3/context\"", bootstrap);
        Assert.DoesNotContain("currentContextJson", bootstrap);
    }

    [Fact]
    public async Task DashboardEvents_IncludeCompletedV3Actions()
    {
        using var fixture = new ServerFixture();
        fixture.SetSnapshot(
            new BazaarAgentContextSnapshot(
                new BazaarAgentContext
                {
                    TickId = 8,
                    AvailableActions = new[]
                    {
                        new BazaarAgentDecisionOption { ActionKind = BazaarAgentActionKind.Reroll },
                    },
                }
            )
        );
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var context = await http.GetAsync($"http://127.0.0.1:{fixture.Port}/v3/context");
        var session = Assert.Single(context.Headers.GetValues(BazaarAgentProtocolV3.SessionHeader));

        var post = new HttpRequestMessage(
            HttpMethod.Post,
            $"http://127.0.0.1:{fixture.Port}/v3/actions"
        )
        {
            Content = new StringContent(
                "{\"op\":\"reroll\",\"revision\":8}",
                Encoding.UTF8,
                "application/json"
            ),
        };
        post.Headers.Add(BazaarAgentProtocolV3.SessionHeader, session);
        var responding = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var pending = fixture.Queue.TryDequeue();
                if (pending is not null)
                {
                    pending.SetResponse(new BazaarAgentServerResponse(200, "{\"confirmed\":true}"));
                    return;
                }
                await Task.Delay(10);
            }
            throw new TimeoutException("The v3 action was not queued.");
        });

        var response = await http.SendAsync(post);
        await responding;
        var bootstrap = await http.GetStringAsync(
            $"http://127.0.0.1:{fixture.Port}/dashboard/api/bootstrap"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"kind\":\"agent.action.received\"", bootstrap);
        Assert.Contains("\"kind\":\"agent.action.completed\"", bootstrap);
        Assert.Contains("\"route\":\"/v3/actions\"", bootstrap);
    }

    [Fact]
    public void PayloadsOverTheSafetyLimit_AreTruncatedBeforeTheyEnterTheRingBuffer()
    {
        var feed = new BazaarAgentActivityFeed();
        var oversized = new string('x', 300 * 1024);

        feed.Publish(
            "agent.action.received",
            "req-1",
            "/v3/actions",
            "Action",
            requestJson: oversized
        );

        var activity = Assert.Single(feed.GetSince(0, maximumEvents: 10).Events);
        Assert.EndsWith("[truncated by BazaarAgent activity feed]", activity.RequestJson);
    }
}
