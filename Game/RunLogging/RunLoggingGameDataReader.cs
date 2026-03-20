#pragma warning disable CS0436
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Players;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.RunLogging.Models;
using TheBazaar;

namespace BazaarPlusPlus.Game.RunLogging;

internal static class RunLoggingGameDataReader
{
    public static string? GetParentEncounterId(string? state)
    {
        return state switch
        {
            "Choice" => Data.CurrentEncounterId?.ToString(),
            "Loot" => Data.CurrentEncounterId?.ToString(),
            "Pedestal" => Data.CurrentEncounterId?.ToString(),
            _ => null,
        };
    }

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

    public static bool TryBuildRunLogPlayerStats(out RunLogPlayerStatsSnapshot stats)
    {
        stats = null!;
        if (Data.Run?.Player == null)
            return false;

        stats = new RunLogPlayerStatsSnapshot
        {
            MaxHealth = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.HealthMax),
            Prestige = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Prestige),
            Level = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Level),
            Income = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Income),
            Gold = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Gold),
        };
        return true;
    }

    public static IList<string> GetCurrentSelectionSetInstanceIds()
    {
        var state = Data.CurrentState;
        if (state?.SelectionSet == null || state.SelectionSet.Count == 0)
            return new List<string>();

        return state
            .SelectionSet.SelectMany(ResolveSelectionInstanceIds)
            .Distinct(StringComparer.Ordinal)
            .ToList();
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
            ParentEncounterId = GetParentEncounterId(state.StateName.ToString()),
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
            ParentEncounterId = GetParentEncounterId(state.StateName.ToString()),
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
        TryBuildRunLogPlayerStats(out var stats);
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
            MaxHealth = stats?.MaxHealth,
            Prestige = stats?.Prestige,
            Level = stats?.Level,
            Income = stats?.Income,
            Gold = stats?.Gold,
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

    private static IEnumerable<string> ResolveSelectionInstanceIds(string selectionId)
    {
        if (string.IsNullOrWhiteSpace(selectionId))
            yield break;

        yield return selectionId;

        var entity = Data.Entities.GetValueOrDefault(new InstanceId(selectionId));
        if (entity is not Card card)
            yield break;

        var cardInstanceId = card.GetInstanceId().ToString();
        if (!string.IsNullOrWhiteSpace(cardInstanceId))
            yield return cardInstanceId;
    }
}
