#nullable enable
using System;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;

namespace BazaarPlusPlus.Game.CombatReplay.Upload;

internal sealed class BattleUploadPayload
{
    public int SchemaVersion { get; set; } = RunLogSqliteSchema.UploadPayloadSchemaVersion;

    public string InstallId { get; set; } = string.Empty;

    public string? ClientId { get; set; }

    public string PluginVersion { get; set; } = string.Empty;

    public DateTimeOffset SubmittedAtUtc { get; set; }

    public string BattleId { get; set; } = string.Empty;

    public string? RunId { get; set; }

    public PvpBattleManifest BattleManifest { get; set; } = new();

    public PvpReplayPayload ReplayPayload { get; set; } = new();
}

internal sealed class BattleUploadSnapshot
{
    public BattleUploadPayload Payload { get; set; } = new();

    public string Json { get; set; } = string.Empty;

    public string PayloadSha256 { get; set; } = string.Empty;
}

internal readonly struct BattleUploadCycleResult
{
    public BattleUploadCycleResult(int uploadedCount, bool hasMorePending)
    {
        UploadedCount = uploadedCount;
        HasMorePending = hasMorePending;
    }

    public int UploadedCount { get; }

    public bool HasMorePending { get; }
}
