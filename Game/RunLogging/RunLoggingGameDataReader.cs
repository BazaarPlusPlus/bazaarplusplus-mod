#pragma warning disable CS0436
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.RunLogging.Models;
using TheBazaar;

namespace BazaarPlusPlus.Game.RunLogging;

internal static class RunLoggingGameDataReader
{
    public static bool TryCreateRunLogCreateRequest(out RunLogCreateRequest request)
    {
        request = null!;
        if (Data.Run?.Player == null)
            return false;

        var serverRunId = BppRuntimeHost.RunContext.CurrentServerRunId;
        if (string.IsNullOrWhiteSpace(serverRunId))
            return false;

        request = new RunLogCreateRequest
        {
            SchemaVersion = 1,
            RunId = serverRunId,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Hero = Data.Run.Player.Hero.ToString(),
            GameMode = Data.SelectedPlayMode.ToString(),
            Day = (int?)Data.Run.Day,
            Hour = unchecked((int)(Data.Run.Victories + Data.Run.Losses + 1)),
        };
        return true;
    }

    public static bool TryBuildRunLogRunProgressInput(out RunLogRunProgressInput input)
    {
        input = null!;
        if (Data.Run == null)
            return false;

        input = new RunLogRunProgressInput
        {
            Day = (int?)Data.Run.Day,
            Hour = unchecked((int)(Data.Run.Victories + Data.Run.Losses + 1)),
            Victories = unchecked((int)Data.Run.Victories),
            Losses = unchecked((int)Data.Run.Losses),
        };
        return true;
    }

    public static bool TryBuildRunLogStateSnapshot(out RunLogStateSnapshotInput input)
    {
        input = null!;
        var state = Data.CurrentState;
        if (state == null)
            return false;

        input = new RunLogStateSnapshotInput
        {
            Day = Data.Run == null ? null : (int?)Data.Run.Day,
            Hour =
                Data.Run == null
                    ? null
                    : unchecked((int)(Data.Run.Victories + Data.Run.Losses + 1)),
            State = state.StateName.ToString(),
            EncounterId = Data.CurrentEncounterId?.ToString(),
        };
        return true;
    }

    public static bool TryBuildRunLogSelectionSnapshot(out RunLogSelectionSnapshotInput input)
    {
        input = null!;
        var state = Data.CurrentState;
        if (state == null || !EncounterTracker.IsSupportedSelectionState(state.StateName))
            return false;

        var selectionSnapshot = EncounterTracker.SelectionQuery.GetSnapshot();
        var source =
            selectionSnapshot.AvailableEncounters ?? selectionSnapshot.CurrentEncounterChoices;
        if (source == null || source.Count == 0)
            return false;

        input = new RunLogSelectionSnapshotInput
        {
            Day = Data.Run == null ? null : (int?)Data.Run.Day,
            Hour =
                Data.Run == null
                    ? null
                    : unchecked((int)(Data.Run.Victories + Data.Run.Losses + 1)),
            State = state.StateName.ToString(),
            EncounterId = Data.CurrentEncounterId?.ToString(),
            Options = source.Select(ToSelectionOption).ToList(),
        };
        return true;
    }

    public static RunLogCompletion BuildRunLogCompletion(string reason)
    {
        var status =
            BppRuntimeHost.RunContext.LastRunExitKind == RunExitKind.Interrupted
                ? "abandoned"
                : "completed";
        return new RunLogCompletion
        {
            SchemaVersion = 1,
            Status = status,
            EndedAtUtc = DateTimeOffset.UtcNow,
            FinalDay = Data.Run == null ? null : (int?)Data.Run.Day,
            FinalHour =
                Data.Run == null
                    ? null
                    : unchecked((int)(Data.Run.Victories + Data.Run.Losses + 1)),
            Victories = Data.Run == null ? null : unchecked((int)Data.Run.Victories),
            Losses = Data.Run == null ? null : unchecked((int)Data.Run.Losses),
            Reason = reason,
        };
    }

    private static RunLogSelectionOptionInput ToSelectionOption(RunInfo.CardInfo card)
    {
        return new RunLogSelectionOptionInput
        {
            InstanceId = card.Instance.ToString(),
            TemplateId = card.TemplateId.ToString(),
            Name = card.Name,
            Tier = card.Tier.ToString(),
            Enchant = card.Enchant,
            Tags = card.Tags?.Select(tag => tag.ToString()).ToList() ?? new List<string>(),
            Attributes =
                card.Attributes?.ToDictionary(
                    entry => entry.Key.ToString(),
                    entry => (object?)entry.Value
                )
                ?? new Dictionary<string, object?>(),
        };
    }
}
