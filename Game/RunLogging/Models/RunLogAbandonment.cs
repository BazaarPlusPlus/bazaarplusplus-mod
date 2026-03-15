#nullable enable
using System;

namespace BazaarPlusPlus.Game.RunLogging.Models;

public sealed class RunLogAbandonment
{
    public int SchemaVersion { get; set; } = 1;

    public string RunId { get; set; } = string.Empty;

    public string Status { get; set; } = "abandoned";

    public DateTimeOffset EndedAtUtc { get; set; }

    public int? FinalDay { get; set; }

    public int? FinalHour { get; set; }

    public string? Reason { get; set; }
}
