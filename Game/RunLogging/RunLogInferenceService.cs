#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Game.RunLogging.Models;

namespace BazaarPlusPlus.Game.RunLogging;

public sealed class RunLogInferenceService
{
    public RunLogEvent? InferChoice(RunLogChoiceInferenceInput input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        if (input.Options.Count == 1 && input.TransitionedAway)
            return CreateChoiceEvent(input, input.Options[0], "single_option_transition", 0.95);

        var matchedOptions = input
            .Options.Where(option => MatchesResultingState(option, input.ResultingInstanceIds))
            .ToList();
        if (matchedOptions.Count == 1)
            return CreateChoiceEvent(input, matchedOptions[0], "resulting_state_match", 0.8);

        return null;
    }

    private static bool MatchesResultingState(
        RunLogOptionSnapshot option,
        IEnumerable<string> resultingInstanceIds
    )
    {
        if (string.IsNullOrWhiteSpace(option.InstanceId))
            return false;

        return resultingInstanceIds.Any(id =>
            string.Equals(id, option.InstanceId, StringComparison.Ordinal)
        );
    }

    private static RunLogEvent CreateChoiceEvent(
        RunLogChoiceInferenceInput input,
        RunLogOptionSnapshot option,
        string inferredFrom,
        double confidence
    )
    {
        return new RunLogEvent
        {
            Kind = ResolveChoiceMadeKind(input.State),
            Day = input.Day,
            Hour = input.Hour,
            State = input.State,
            EncounterId = input.EncounterId,
            ParentEncounterId = input.ParentEncounterId,
            SelectionSeq = input.SelectionSeq,
            SelectedInstanceId = option.InstanceId,
            SelectedTemplateId = option.TemplateId,
            SelectedEncounterId = ResolveSelectedEncounterId(input.State, option.TemplateId),
            SelectedName = option.Name,
            SelectedTier = option.Tier,
            SelectedEnchant = option.Enchant,
            InferredFrom = inferredFrom,
            Confidence = confidence,
        };
    }

    private static string ResolveChoiceMadeKind(string? state)
    {
        return state switch
        {
            "Encounter" => "encounter_selected",
            "Choice" => "choice_selected",
            "Loot" => "loot_selected",
            "Pedestal" => "pedestal_selected",
            _ => "choice_made",
        };
    }

    private static string? ResolveSelectedEncounterId(string? state, string? selectedTemplateId)
    {
        if (string.Equals(state, "Encounter", StringComparison.Ordinal))
            return selectedTemplateId;

        return null;
    }
}

public sealed class RunLogChoiceInferenceInput
{
    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string? State { get; set; }

    public string? EncounterId { get; set; }

    public string? ParentEncounterId { get; set; }

    public long SelectionSeq { get; set; }

    public bool TransitionedAway { get; set; }

    public IList<RunLogOptionSnapshot> Options { get; set; } = new List<RunLogOptionSnapshot>();

    public IList<string> ResultingInstanceIds { get; set; } = new List<string>();
}
