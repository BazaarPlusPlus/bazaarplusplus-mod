#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.ModApi.Models;
using Newtonsoft.Json.Linq;

internal static class BazaarDbSnapshotClientTests
{
    public static void Run()
    {
        UploadsSnapshotDtoToIdPathWithJsonContentType();
        SerializesBattleProjectionWinnerLoserFields();
        DoesNotSerializeRunProjectionBattles();
        Console.WriteLine("BazaarDbSnapshotClientTests passed.");
    }

    private static void UploadsSnapshotDtoToIdPathWithJsonContentType()
    {
        var routes =
            ModApiRoutes.TryCreate("https://example.invalid")
            ?? throw new InvalidOperationException("TryCreate returned null for valid URL");
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"ok\"}"),
        });
        var client = new BazaarDbSnapshotClient(new HttpClient(handler), routes);
        var result = client
            .UploadSnapshotAsync(
                new BazaarDbSnapshotUploadRequest
                {
                    SchemaVersion = 2,
                    Snapshot = new BazaarDbSnapshotMetadata
                    {
                        Id = "snap-1",
                        Source = "end_of_run_auto",
                        CapturedAtUtc = "2026-06-03T12:34:56.000Z",
                    },
                    Player = new BazaarDbSnapshotPlayer
                    {
                        AccountId = "acct-1",
                        DisplayName = "Player",
                        Rank = "Gold",
                        Rating = 1234,
                        LeaderboardPosition = 12,
                    },
                    Run = new BazaarDbSnapshotRun
                    {
                        Id = "run-1",
                        Day = 10,
                        Wins = 9,
                        Losses = null,
                        Hero = new BazaarDbSnapshotHero { Id = null, Name = "Vanessa" },
                    },
                    Image = new BazaarDbSnapshotImage
                    {
                        ContentType = "image/png",
                        Encoding = "base64",
                        DataBase64 = "AAECAw==",
                    },
                    Client = new BazaarDbSnapshotClientInfo
                    {
                        SubmittedAtUtc = "2026-06-03T12:35:00.000Z",
                    },
                },
                CancellationToken.None
            )
            .GetAwaiter()
            .GetResult();

        Assert(result.Succeeded, "Successful snapshot upload response should succeed.");
        Assert(handler.Requests.Count == 1, "Snapshot client should send one POST.");
        var request = handler.Requests[0];
        Assert(request.Method == HttpMethod.Post, "Snapshot upload should use POST.");
        Assert(
            request.RequestUri?.ToString() == "https://example.invalid/bazaardb/snapshots/snap-1",
            "Snapshot upload should put the snapshot id in the route path."
        );
        Assert(
            handler.ContentTypes[0] == "application/json",
            "Snapshot upload should send application/json."
        );

        var json = JObject.Parse(handler.Bodies[0]);
        Assert((int?)json["schema_version"] == 2, "DTO schema_version should be 2.");
        Assert((string?)json["snapshot"]?["id"] == "snap-1", "DTO should include snapshot.id.");
        Assert(
            (string?)json["player"]?["account_id"] == "acct-1",
            "DTO should include player.account_id."
        );
        Assert(
            (string?)json["run"]?["hero"]?["name"] == "Vanessa",
            "DTO should include run.hero.name."
        );
        Assert(
            (string?)json["image"]?["data_base64"] == "AAECAw==",
            "DTO should include image data."
        );
        Assert(json["screenshot_id"] == null, "DTO should not emit old screenshot_id.");
        Assert(json["final_victories"] == null, "DTO should not emit old final_victories.");
        Assert(json["image_bytes_base64"] == null, "DTO should not emit old image_bytes_base64.");
    }

    private static void SerializesBattleProjectionWinnerLoserFields()
    {
        var json = JObject.FromObject(
            new BattleProjection
            {
                BattleId = "battle-1",
                RecordedAtUtc = "2026-06-03T12:00:00.000Z",
                WinnerCombatantId = "Player",
                LoserCombatantId = "Opponent",
                IsFinalBattle = true,
            },
            Newtonsoft.Json.JsonSerializer.Create(ModApiSerialization.SerializerSettings)
        );
        Assert(
            (string?)json["winner_combatant_id"] == "Player",
            "BattleProjection should serialize winner_combatant_id."
        );
        Assert(
            (string?)json["loser_combatant_id"] == "Opponent",
            "BattleProjection should serialize loser_combatant_id."
        );
        Assert(
            (bool?)json["is_final_battle"] == true,
            "BattleProjection should serialize is_final_battle."
        );
    }

    private static void DoesNotSerializeRunProjectionBattles()
    {
        var json = JObject.FromObject(
            new RunBundleUploadRequest
            {
                SchemaVersion = 1,
                PlayerAccountId = "acct-1",
                SubmittedAtUtc = "2026-06-03T12:00:00.000Z",
                ArtifactCodec = RunBundleArtifactCodec.ContentType,
                RunProjection = new RunProjection
                {
                    RunId = "run-1",
                    Status = "completed",
                    EndedAtUtc = "2026-06-03T12:30:00.000Z",
                },
                BattleProjections =
                [
                    new BattleProjection { BattleId = "battle-top-level", RunId = "run-1" },
                ],
            },
            Newtonsoft.Json.JsonSerializer.Create(ModApiSerialization.SerializerSettings)
        );

        Assert(
            json["run_projection"]?["battles"] == null,
            "Nested run_projection.battles is not part of the V4 wire contract."
        );
        Assert(
            json["battle_projections"] is JArray,
            "Top-level battle_projections should remain the upload projection source."
        );
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
