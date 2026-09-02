#nullable enable
using System.Net;
using System.Text;
using BazaarPlusPlus.BazaarAgent;
using Newtonsoft.Json.Linq;
using Xunit;

public sealed class BazaarAgentHttpServerTests
{
    [Fact]
    public async Task V3_context_returns_a_full_snapshot_then_a_delta_for_the_returned_session()
    {
        using var fixture = new ServerFixture();
        fixture.SetSnapshot(Snapshot(1, BoardCard("one", "Socket_0")));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var first = await http.GetAsync($"http://127.0.0.1:{fixture.Port}/v3/context");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var session = Assert.Single(first.Headers.GetValues(BazaarAgentProtocolV3.SessionHeader));
        var firstJson = JObject.Parse(await first.Content.ReadAsStringAsync());
        Assert.True(firstJson.Value<bool>("full"));
        Assert.NotNull(firstJson["selection"]);
        Assert.Equal(
            "Test Item#00001",
            firstJson["board"]!["upsert"]![0]!["item"]!.Value<string>()
        );
        Assert.Null(firstJson["schemaVersion"]);
        Assert.Null(firstJson["cardKnowledge"]);

        fixture.SetSnapshot(Snapshot(2, BoardCard("one", "Socket_1")));
        using var nextRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{fixture.Port}/v3/context"
        );
        nextRequest.Headers.Add(BazaarAgentProtocolV3.SessionHeader, session);
        nextRequest.Headers.Add(BazaarAgentProtocolV3.RevisionHeader, "1");
        var next = await http.SendAsync(nextRequest);
        var nextJson = JObject.Parse(await next.Content.ReadAsStringAsync());

        Assert.Null(nextJson["full"]);
        Assert.Null(nextJson["selection"]);
        Assert.Equal(2UL, nextJson.Value<ulong>("revision"));
        Assert.Equal("Test Item#00001", nextJson["board"]!["upsert"]![0]!["item"]!.Value<string>());
        Assert.Equal(1, nextJson["board"]!["upsert"]![0]!["slots"]![0]!.Value<int>());
    }

    [Fact]
    public async Task V3_cards_query_route_is_not_registered()
    {
        using var fixture = new ServerFixture();
        fixture.SetSnapshot(Snapshot(1, BoardCard("one", "Socket_0")));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var response = await http.PostAsync(
            $"http://127.0.0.1:{fixture.Port}/v3/cards/query",
            new StringContent("{}", Encoding.UTF8, "application/json")
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task V3_context_requires_session_and_revision_together_and_rejects_stale_revisions()
    {
        using var fixture = new ServerFixture();
        fixture.SetSnapshot(Snapshot(1, BoardCard("one", "Socket_0")));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var partial = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{fixture.Port}/v3/context"
        );
        partial.Headers.Add(BazaarAgentProtocolV3.SessionHeader, "missing-revision");
        var partialResponse = await http.SendAsync(partial);
        Assert.Equal(HttpStatusCode.Conflict, partialResponse.StatusCode);
        Assert.Equal(
            "resync-required",
            JObject.Parse(await partialResponse.Content.ReadAsStringAsync()).Value<string>("error")
        );

        var first = await http.GetAsync($"http://127.0.0.1:{fixture.Port}/v3/context");
        var session = Assert.Single(first.Headers.GetValues(BazaarAgentProtocolV3.SessionHeader));
        using var stale = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{fixture.Port}/v3/context"
        );
        stale.Headers.Add(BazaarAgentProtocolV3.SessionHeader, session);
        stale.Headers.Add(BazaarAgentProtocolV3.RevisionHeader, "0");
        var staleResponse = await http.SendAsync(stale);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal(
            "resync-required",
            JObject.Parse(await staleResponse.Content.ReadAsStringAsync()).Value<string>("error")
        );
    }

    [Fact]
    public async Task V3_actions_require_a_current_session_even_for_card_free_operations()
    {
        using var fixture = new ServerFixture();
        fixture.SetSnapshot(Snapshot(1, BoardCard("one", "Socket_0")));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var response = await http.PostAsync(
            $"http://127.0.0.1:{fixture.Port}/v3/actions",
            new StringContent(
                "{\"op\":\"start\",\"revision\":1}",
                Encoding.UTF8,
                "application/json"
            )
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "resync-required",
            JObject.Parse(await response.Content.ReadAsStringAsync()).Value<string>("error")
        );
    }

    [Fact]
    public async Task V3_select_requires_board_at_and_automatically_chooses_a_chest_slot()
    {
        using var fixture = new ServerFixture();
        var offer = new BazaarAgentCardSnapshot
        {
            InstanceId = "offer",
            Kind = BazaarAgentCardKind.Item,
            TemplateId = "offer-template",
            DisplayName = "Offer",
            Size = "Small",
            Location = BazaarAgentCardLocation.Selection,
            CanSelect = true,
            CanAfford = true,
            CanFit = true,
        };
        fixture.SetSnapshot(
            new BazaarAgentContextSnapshot(
                new BazaarAgentContext
                {
                    TickId = 1,
                    StateName = BazaarAgentRunStateName.Choice,
                    SelectionOptions = new[] { offer },
                    AvailableActions = new[]
                    {
                        new BazaarAgentDecisionOption
                        {
                            ActionKind = BazaarAgentActionKind.SelectItem,
                            CardInstanceId = "offer",
                            TargetSection = BazaarAgentTargetSection.Stash,
                            TargetSockets = new[] { "Socket_0" },
                            Card = offer,
                        },
                    },
                }
            )
        );
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var context = await http.GetAsync($"http://127.0.0.1:{fixture.Port}/v3/context");
        var session = Assert.Single(context.Headers.GetValues(BazaarAgentProtocolV3.SessionHeader));

        var invalid = await PostAction(
            http,
            fixture.Port,
            session,
            "{\"op\":\"select\",\"id\":\"Offer#00001\",\"target\":\"board\",\"revision\":1}"
        );
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);

        var posted = PostAction(
            http,
            fixture.Port,
            session,
            "{\"op\":\"select\",\"id\":\"Offer#00001\",\"target\":\"chest\",\"revision\":1}"
        );
        var pending = await Dequeue(fixture.Queue);
        Assert.Equal(BazaarAgentActionKind.SelectItem, pending.Command.ActionKind);
        Assert.Equal(new[] { "Socket_0" }, pending.Command.TargetSockets);
        pending.SetResponse(
            new BazaarAgentServerResponse(200, "{\"status\":\"confirmed\"}", "confirmed")
        );
        var response = await posted;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("confirmed", body.Value<string>("status"));
        Assert.NotNull(body["next"]);
        Assert.Null(body["result"]);
    }

    [Theory]
    [InlineData("/v1/context")]
    [InlineData("/v2/context")]
    [InlineData("/v2/cards/query")]
    [InlineData("/v1/actions")]
    public async Task Legacy_decision_routes_are_not_registered(string path)
    {
        using var fixture = new ServerFixture();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var response = await http.GetAsync($"http://127.0.0.1:{fixture.Port}{path}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostAction(
        HttpClient http,
        int port,
        string session,
        string json
    )
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/v3/actions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(BazaarAgentProtocolV3.SessionHeader, session);
        return await http.SendAsync(request);
    }

    private static async Task<BazaarAgentPendingCommand<BazaarAgentAction>> Dequeue(
        BazaarAgentCommandQueue<BazaarAgentAction> queue
    )
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var pending = queue.TryDequeue();
            if (pending is not null)
                return pending;
            await Task.Delay(10);
        }
        throw new TimeoutException("Expected an action on the command queue.");
    }

    private static BazaarAgentContextSnapshot Snapshot(ulong tick, BazaarAgentCardSnapshot card) =>
        new(
            new BazaarAgentContext
            {
                TickId = tick,
                StateName = BazaarAgentRunStateName.Choice,
                PlayerGold = 10,
                BoardItems = new[] { card },
            }
        );

    private static BazaarAgentCardSnapshot BoardCard(string id, string socket) =>
        new()
        {
            InstanceId = id,
            Kind = BazaarAgentCardKind.Item,
            TemplateId = "template",
            DisplayName = "Test Item",
            Size = "Small",
            Location = BazaarAgentCardLocation.Board,
            SocketId = socket,
            Description = "Deal 10 damage.",
        };
}
