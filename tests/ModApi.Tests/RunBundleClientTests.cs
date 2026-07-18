#nullable enable
using System.Net;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.ModApi.Models;

internal static class RunBundleClientTests
{
    public static void Run()
    {
        UploadsRunBundleAsMultipart().GetAwaiter().GetResult();
        ClassifiesPermanentClientFailures().GetAwaiter().GetResult();
        KeepsRetryableFailuresTransient().GetAwaiter().GetResult();
        Console.WriteLine("RunBundleClientTests passed.");
    }

    private static async Task UploadsRunBundleAsMultipart()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var routes =
            ModApiRoutes.TryCreate("https://example.invalid")
            ?? throw new InvalidOperationException("TryCreate returned null for valid URL");
        var uploadClient = new RunBundleClient(client, routes);

        var request = new RunBundleUploadRequest
        {
            SchemaVersion = 5,
            PlayerAccountId = "acct-1",
            SubmittedAtUtc = "2026-06-04T00:00:00.000Z",
            ArtifactCodec = RunBundleArtifactCodec.ContentType,
            RunProjection = new RunProjection
            {
                RunId = "run-1",
                Status = "completed",
                EndedAtUtc = "2026-06-04T00:30:00.000Z",
            },
            BattleProjections = [new BattleProjection { BattleId = "battle-1", RunId = "run-1" }],
        };

        var result = await uploadClient.UploadRunBundleAsync(
            request,
            [1, 2, 3, 4],
            CancellationToken.None
        );

        Assert(result.Succeeded, "Expected multipart run bundle upload to succeed.");
        Assert(handler.Request != null, "Expected one HTTP request.");
        Assert(handler.Request!.Method == HttpMethod.Post, "Run bundle upload should use POST.");
        Assert(
            handler.Request.RequestUri?.ToString() == "https://example.invalid/run-bundles",
            "Run bundle upload should POST to /run-bundles."
        );
        Assert(
            handler.ContentType == "multipart/form-data",
            "Run bundle upload should use multipart/form-data."
        );
        Assert(
            handler.Body.Contains("\"battle_projections\"", StringComparison.Ordinal),
            "Multipart metadata should include battle_projections."
        );
        Assert(
            handler.Body.Contains(RunBundleArtifactCodec.ContentType, StringComparison.Ordinal),
            "Multipart artifact should include the run bundle artifact content type."
        );
        Assert(
            !handler.Body.Contains("artifact_bytes", StringComparison.Ordinal),
            "Multipart metadata should not include artifact_bytes."
        );
    }

    private static async Task ClassifiesPermanentClientFailures()
    {
        var result = await UploadWithResponse(
            HttpStatusCode.Conflict,
            "{\"error\":\"run_bundle_conflict\"}"
        );

        Assert(!result.Succeeded, "A 409 run bundle conflict must not succeed.");
        Assert(result.Permanent, "A 409 run bundle conflict must be terminal, not retryable.");
        Assert(
            result.Error == "http_409:run_bundle_conflict",
            "The structured conflict reason should be retained for local diagnostics."
        );
    }

    private static async Task KeepsRetryableFailuresTransient()
    {
        foreach (
            var status in new[]
            {
                HttpStatusCode.RequestTimeout,
                (HttpStatusCode)429,
                HttpStatusCode.InternalServerError,
            }
        )
        {
            var result = await UploadWithResponse(status, "{\"error\":\"temporary\"}");
            Assert(!result.Succeeded, $"HTTP {(int)status} must not succeed.");
            Assert(!result.Permanent, $"HTTP {(int)status} must remain retryable.");
        }
    }

    private static async Task<RunBundleUploadResult> UploadWithResponse(
        HttpStatusCode status,
        string responseBody
    )
    {
        using var client = new HttpClient(new FixedResponseHandler(status, responseBody));
        var routes = ModApiRoutes.TryCreate("https://example.invalid")!;
        return await new RunBundleClient(client, routes).UploadRunBundleAsync(
            new RunBundleUploadRequest
            {
                PlayerAccountId = "acct-1",
                RunProjection = new RunProjection { RunId = "run-1" },
            },
            [1],
            CancellationToken.None
        );
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string ContentType { get; private set; } = string.Empty;

        public string Body { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Request = request;
            ContentType = request.Content?.Headers.ContentType?.MediaType ?? string.Empty;
            Body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"accepted\"}"),
                }
            );
        }
    }

    private sealed class FixedResponseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _responseBody;

        internal FixedResponseHandler(HttpStatusCode status, string responseBody)
        {
            _status = status;
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(_status) { Content = new StringContent(_responseBody) }
            );
    }
}
