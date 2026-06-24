#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.ModApi.Clients;
using Newtonsoft.Json.Linq;

internal static class BazaarDbLinkClientTests
{
    public static void Run()
    {
        LinkedResponsePostsCasePreservedCodeAndSnakeCaseAccountId();
        InvalidOrExpiredErrorClassifiesAsInvalidOrExpired();
        AlreadyLinkedErrorClassifiesAsAlreadyLinked();
        ServerErrorClassifiesAsServerError();
        EmptyCodeReturnsMissingFieldsWithoutHttpCall();
        Console.WriteLine("BazaarDbLinkClientTests passed.");
    }

    private static void LinkedResponsePostsCasePreservedCodeAndSnakeCaseAccountId()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new BazaarDbLinkClient(
            new HttpClient(handler),
            new Uri("https://example.invalid/api/profile/link/redeem")
        );

        var result = client
            .RedeemAsync(" AbcD23 ", "acct-1", CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert(result.Outcome == BazaarDbLinkOutcome.Linked, "200 response should link profile.");
        Assert(result.Succeeded, "Linked result should succeed.");
        Assert(handler.Requests.Count == 1, "Link client should send one POST.");
        var request = handler.Requests[0];
        Assert(request.Method == HttpMethod.Post, "Link redeem should use POST.");
        Assert(
            request.RequestUri?.ToString() == "https://example.invalid/api/profile/link/redeem",
            "Link redeem should post to the configured absolute endpoint."
        );
        Assert(
            handler.ContentTypes[0] == "application/json",
            "Link redeem should send application/json."
        );

        var json = JObject.Parse(handler.Bodies[0]);
        Assert((string?)json["code"] == "AbcD23", "Code should be trimmed but case-preserved.");
        Assert((string?)json["account_id"] == "acct-1", "DTO should include account_id.");
        Assert(json["accountId"] == null, "DTO should not emit camelCase accountId.");
    }

    private static void InvalidOrExpiredErrorClassifiesAsInvalidOrExpired()
    {
        var result = RedeemWithResponse(
            HttpStatusCode.BadRequest,
            "{\"error\":\"invalid_or_expired\"}"
        );
        Assert(
            result.Outcome == BazaarDbLinkOutcome.InvalidOrExpired,
            "invalid_or_expired should classify as InvalidOrExpired."
        );
    }

    private static void AlreadyLinkedErrorClassifiesAsAlreadyLinked()
    {
        var result = RedeemWithResponse(HttpStatusCode.Conflict, "{\"error\":\"already_linked\"}");
        Assert(
            result.Outcome == BazaarDbLinkOutcome.AlreadyLinked,
            "already_linked should classify as AlreadyLinked."
        );
    }

    private static void ServerErrorClassifiesAsServerError()
    {
        var result = RedeemWithResponse(
            HttpStatusCode.InternalServerError,
            "{\"error\":\"Redeem failed\"}"
        );
        Assert(
            result.Outcome == BazaarDbLinkOutcome.ServerError,
            "500 should classify as ServerError."
        );
    }

    private static void EmptyCodeReturnsMissingFieldsWithoutHttpCall()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new BazaarDbLinkClient(
            new HttpClient(handler),
            new Uri("https://example.invalid/api/profile/link/redeem")
        );

        var result = client
            .RedeemAsync("  ", "acct-1", CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert(
            result.Outcome == BazaarDbLinkOutcome.MissingFields,
            "Empty code should classify as MissingFields."
        );
        Assert(handler.Requests.Count == 0, "Empty code should not send an HTTP request.");
    }

    private static BazaarDbLinkResult RedeemWithResponse(HttpStatusCode statusCode, string body)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body),
        });
        var client = new BazaarDbLinkClient(
            new HttpClient(handler),
            new Uri("https://example.invalid/api/profile/link/redeem")
        );

        var result = client
            .RedeemAsync("AbcD23", "acct-1", CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert(handler.Requests.Count == 1, "Link client should send one POST.");
        return result;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Bodies { get; } = new();

        public List<string?> ContentTypes { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            Bodies.Add(
                request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult()
                    ?? string.Empty
            );
            ContentTypes.Add(request.Content?.Headers.ContentType?.MediaType);
            return Task.FromResult(_responder(request));
        }
    }
}
