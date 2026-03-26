#nullable enable
using System;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunUploadPayload
{
    public int SchemaVersion { get; set; } = 1;

    public string InstallId { get; set; } = string.Empty;

    public string? ClientId { get; set; }

    public string PluginVersion { get; set; } = string.Empty;

    public DateTimeOffset SubmittedAtUtc { get; set; }

    public string RunId { get; set; } = string.Empty;

    public JObject Meta { get; set; } = new();

    public JArray Events { get; set; } = [];

    public JObject? Checkpoint { get; set; }

    public JObject? Status { get; set; }

    public JArray PvpBattles { get; set; } = [];
}

internal sealed class RunUploadSnapshot
{
    public RunUploadPayload Payload { get; set; } = new();

    public long LastSeq { get; set; }

    public string? UploadedStatus { get; set; }
}

internal readonly struct RunUploadCycleResult
{
    public RunUploadCycleResult(int uploadedCount, bool hasMorePending)
    {
        UploadedCount = uploadedCount;
        HasMorePending = hasMorePending;
    }

    public int UploadedCount { get; }

    public bool HasMorePending { get; }
}
