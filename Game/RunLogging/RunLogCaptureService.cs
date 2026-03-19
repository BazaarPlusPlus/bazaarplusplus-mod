#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.RunLogging.Models;

namespace BazaarPlusPlus.Game.RunLogging;

public sealed class RunLogCaptureService
{
    public RunLogEvent BuildRunProgressEvent(RunLogRunProgressInput input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        return new RunLogEvent
        {
            Kind = "run_progress",
            Day = input.Day,
            Hour = input.Hour,
            Victories = input.Victories,
            Losses = input.Losses,
            CurrentHourXp = input.CurrentHourXp,
        };
    }

    public RunLogEvent BuildStateSeenEvent(RunLogStateSnapshotInput input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        return new RunLogEvent
        {
            Kind = "state_seen",
            Day = input.Day,
            Hour = input.Hour,
            State = input.State,
            EncounterId = input.EncounterId,
            RerollCost = input.RerollCost,
            RerollsRemaining = input.RerollsRemaining,
            StateFingerprint = RunLogSnapshotBuilder.ComputeStateFingerprint(input),
        };
    }

    public RunLogEvent BuildSelectionSeenEvent(RunLogSelectionSnapshotInput input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        return new RunLogEvent
        {
            Kind = "selection_seen",
            Day = input.Day,
            Hour = input.Hour,
            State = input.State,
            EncounterId = input.EncounterId,
            SelectionFingerprint = RunLogSnapshotBuilder.ComputeSelectionFingerprint(input),
            SelectionContextRules = new Dictionary<string, object?>(input.SelectionContextRules),
            Options = RunLogSnapshotBuilder.ProjectOptions(input.Options),
        };
    }

    public RunLogEvent BuildCombatReplayRecordedEvent(RunLogCombatReplayInput input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        return new RunLogEvent
        {
            Kind = "pvp_combat_recorded",
            Day = input.Day,
            Hour = input.Hour,
            State = input.CombatKind,
            EncounterId = input.EncounterId,
            CombatKind = input.CombatKind,
            ReplayId = input.ReplayId,
            OpponentName = input.OpponentName,
        };
    }
}

public sealed class RunLogRunProgressInput
{
    public int? Day { get; set; }

    public int? Hour { get; set; }

    public int? Victories { get; set; }

    public int? Losses { get; set; }

    public int? CurrentHourXp { get; set; }
}

public sealed class RunLogStateSnapshotInput
{
    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string? State { get; set; }

    public string? EncounterId { get; set; }

    public int? RerollCost { get; set; }

    public int? RerollsRemaining { get; set; }
}

public sealed class RunLogSelectionSnapshotInput
{
    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string? State { get; set; }

    public string? EncounterId { get; set; }

    public IDictionary<string, object?> SelectionContextRules { get; set; } =
        new Dictionary<string, object?>();

    public IList<RunLogSelectionOptionInput> Options { get; set; } =
        new List<RunLogSelectionOptionInput>();
}

public sealed class RunLogSelectionOptionInput
{
    public int Index { get; set; }

    public string? InstanceId { get; set; }

    public string? TemplateId { get; set; }

    public string? Name { get; set; }

    public string? Tier { get; set; }

    public string? Enchant { get; set; }

    public IList<string> Tags { get; set; } = new List<string>();

    public IDictionary<string, object?> Attributes { get; set; } =
        new Dictionary<string, object?>();
}

public sealed class RunLogCombatReplayInput
{
    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string? EncounterId { get; set; }

    public string? CombatKind { get; set; }

    public string? ReplayId { get; set; }

    public string? OpponentName { get; set; }
}
