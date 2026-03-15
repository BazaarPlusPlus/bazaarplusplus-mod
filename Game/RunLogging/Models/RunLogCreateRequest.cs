#nullable enable
using System;

namespace BazaarPlusPlus.Game.RunLogging.Models;

public sealed class RunLogCreateRequest
{
    public int SchemaVersion { get; set; } = 1;

    public string RunId { get; set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; set; }

    public string Hero { get; set; } = string.Empty;

    public string GameMode { get; set; } = string.Empty;

    public int? Day { get; set; }

    public int? Hour { get; set; }

    public int? Seed { get; set; }

    public string Status { get; set; } = "active";
}
