#nullable enable
using System;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal class RunSummaryUploadPayload
{
    public int SchemaVersion { get; set; } = RunLogSqliteSchema.UploadPayloadSchemaVersion;

    public string InstallId { get; set; } = string.Empty;

    public string? ClientId { get; set; }

    public string PluginVersion { get; set; } = string.Empty;

    public DateTimeOffset SubmittedAtUtc { get; set; }

    public string RunId { get; set; } = string.Empty;

    public JObject Meta { get; set; } = new();

    public JObject? Checkpoint { get; set; }

    public JObject? Status { get; set; }
}

internal class RunSummaryUploadSnapshot
{
    public RunSummaryUploadPayload Payload { get; set; } = new();

    public long LastSeq { get; set; }

    public string? UploadedStatus { get; set; }
}

internal readonly struct RunSummaryUploadCycleResult
{
    public RunSummaryUploadCycleResult(int uploadedCount, bool hasMorePending)
    {
        UploadedCount = uploadedCount;
        HasMorePending = hasMorePending;
    }

    public int UploadedCount { get; }

    public bool HasMorePending { get; }
}
