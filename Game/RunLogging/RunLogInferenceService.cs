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
            Kind = "choice_made",
            SelectionSeq = input.SelectionSeq,
            SelectedInstanceId = option.InstanceId,
            SelectedTemplateId = option.TemplateId,
            InferredFrom = inferredFrom,
            Confidence = confidence,
        };
    }
}

public sealed class RunLogChoiceInferenceInput
{
    public long SelectionSeq { get; set; }

    public bool TransitionedAway { get; set; }

    public IList<RunLogOptionSnapshot> Options { get; set; } = new List<RunLogOptionSnapshot>();

    public IList<string> ResultingInstanceIds { get; set; } = new List<string>();
}
