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
            ParentEncounterId = input.ParentEncounterId,
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
            Kind = ResolveSelectionSeenKind(input.State),
            Day = input.Day,
            Hour = input.Hour,
            State = input.State,
            EncounterId = input.EncounterId,
            ParentEncounterId = input.ParentEncounterId,
            SelectionFingerprint = RunLogSnapshotBuilder.ComputeSelectionFingerprint(input),
            SelectionContextRules = new Dictionary<string, object?>(input.SelectionContextRules),
            Options = RunLogSnapshotBuilder.ProjectOptions(input.Options),
        };
    }

    public RunLogEvent BuildPvpBattleRecordedEvent(RunLogPvpBattleInput input)
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
            BattleId = input.BattleId,
            OpponentName = input.OpponentName,
        };
    }

    private static string ResolveSelectionSeenKind(string? state)
    {
        return state switch
        {
            "Encounter" => "encounter_options_seen",
            "Choice" => "choice_options_seen",
            "Loot" => "loot_options_seen",
            "Pedestal" => "pedestal_options_seen",
            _ => "selection_seen",
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

    public string? ParentEncounterId { get; set; }

    public int? RerollCost { get; set; }

    public int? RerollsRemaining { get; set; }
}

public sealed class RunLogSelectionSnapshotInput
{
    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string? State { get; set; }

    public string? EncounterId { get; set; }

    public string? ParentEncounterId { get; set; }

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

public sealed class RunLogPvpBattleInput
{
    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string? EncounterId { get; set; }

    public string? CombatKind { get; set; }

    public string? BattleId { get; set; }

    public string? OpponentName { get; set; }
}

public sealed class RunLogPlayerStatsSnapshot
{
    public int? MaxHealth { get; set; }

    public int? Prestige { get; set; }

    public int? Level { get; set; }

    public int? Income { get; set; }

    public int? Gold { get; set; }
}
