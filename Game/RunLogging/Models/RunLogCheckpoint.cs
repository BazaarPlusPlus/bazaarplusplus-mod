#nullable enable
using System;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;

namespace BazaarPlusPlus.Game.RunLogging.Models;

public sealed class RunLogCheckpoint
{
    public int SchemaVersion { get; set; } = RunLogSqliteSchema.RowSchemaVersion;

    public string RunId { get; set; } = string.Empty;

    public long LastSeq { get; set; }

    public DateTimeOffset LastSeenAtUtc { get; set; }

    public int? Day { get; set; }

    public int? Hour { get; set; }

    public int? MaxHealth { get; set; }

    public int? Prestige { get; set; }

    public int? Level { get; set; }

    public int? Income { get; set; }

    public int? Gold { get; set; }

    public string? State { get; set; }

    public string? CurrentEncounterId { get; set; }

    public string? LastStateFingerprint { get; set; }

    public string? LastSelectionFingerprint { get; set; }

    public long? PendingSelectionSeq { get; set; }

    public RunLogPendingSelectionState? PendingSelection { get; set; }

    public bool Completed { get; set; }
}
