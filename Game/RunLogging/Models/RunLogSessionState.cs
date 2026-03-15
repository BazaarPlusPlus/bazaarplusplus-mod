#nullable enable
using System;

namespace BazaarPlusPlus.Game.RunLogging.Models;

public sealed class RunLogSessionState
{
    public string RunId { get; set; } = string.Empty;

    public int SchemaVersion { get; set; } = 1;

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset LastSeenAtUtc { get; set; }

    public long LastSeq { get; set; }

    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string? State { get; set; }

    public string? CurrentEncounterId { get; set; }

    public string? LastStateFingerprint { get; set; }

    public string? LastSelectionFingerprint { get; set; }

    public long? PendingSelectionSeq { get; set; }

    public bool Completed { get; set; }
}
