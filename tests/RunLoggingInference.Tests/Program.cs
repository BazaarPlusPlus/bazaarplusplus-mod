#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.RunLogging.Models;

var inferenceServiceType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLogInferenceService");
var inferenceInputType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLogChoiceInferenceInput");

var service =
    Activator.CreateInstance(inferenceServiceType)
    ?? throw new InvalidOperationException("RunLogInferenceService should be constructible.");

var singleOptionInput = CreateInput(
    inferenceInputType,
    day: 6,
    hour: 2,
    state: "Encounter",
    encounterId: "enc-01",
    parentEncounterId: null,
    selectionSeq: 12,
    transitionedAway: true,
    options: [CreateOption("instance-a", "template-a", "Frost Street")],
    resultingInstanceIds: Array.Empty<string>()
);
var singleOptionResult = Invoke<RunLogEvent?>(
    inferenceServiceType,
    service,
    "InferChoice",
    [singleOptionInput]
);
Assert(singleOptionResult != null, "A single-option transition should infer a choice.");
Assert(singleOptionResult!.Kind == "encounter_selected", "Encounter state should infer encounter_selected.");
Assert(singleOptionResult!.SelectionSeq == 12, "Inferred choice should preserve selection_seq.");
Assert(
    singleOptionResult.SelectedInstanceId == "instance-a",
    "Inferred choice should select the only option."
);
Assert(singleOptionResult.SelectedName == "Frost Street", "Inferred choice should keep the option name.");
Assert(singleOptionResult.Day == 6 && singleOptionResult.Hour == 2, "Inferred choice should preserve run position.");
Assert(
    singleOptionResult.State == "Encounter"
        && singleOptionResult.EncounterId == "enc-01"
        && singleOptionResult.SelectedEncounterId == "template-a",
    "Encounter selection should preserve encounter context and store the chosen encounter separately."
);
Assert(
    singleOptionResult.InferredFrom == "single_option_transition",
    "Single-option transition should record its inference source."
);
Assert(
    singleOptionResult.Confidence >= 0.9,
    "Single-option transition should produce high confidence."
);

var projectedStateInput = CreateInput(
    inferenceInputType,
    day: 6,
    hour: 3,
    state: "Choice",
    encounterId: "enc-02",
    parentEncounterId: "enc-02",
    selectionSeq: 25,
    transitionedAway: false,
    options:
    [
        CreateOption("instance-a", "template-a", "Frost Street"),
        CreateOption("instance-b", "template-b", "Amber Cove"),
    ],
    resultingInstanceIds: ["instance-b"]
);
var projectedStateResult = Invoke<RunLogEvent?>(
    inferenceServiceType,
    service,
    "InferChoice",
    [projectedStateInput]
);
Assert(projectedStateResult != null, "Projected player state should infer a matching option.");
Assert(projectedStateResult!.Kind == "choice_selected", "Choice state should infer choice_selected.");
Assert(
    projectedStateResult!.SelectedInstanceId == "instance-b",
    "Projected player state should pick the matching option."
);
Assert(
    projectedStateResult.ParentEncounterId == "enc-02",
    "Projected player state should preserve parent encounter context."
);
Assert(
    projectedStateResult.SelectedEncounterId == null,
    "Non-encounter selections should not set selected_encounter_id."
);
Assert(
    projectedStateResult.InferredFrom == "resulting_state_match",
    "Projected player state should record its inference source."
);
Assert(
    projectedStateResult.Confidence >= 0.7 && projectedStateResult.Confidence < 0.95,
    "Projected player state should be conservative but confident."
);

var ambiguousInput = CreateInput(
    inferenceInputType,
    day: 6,
    hour: 4,
    state: "Loot",
    encounterId: "enc-03",
    parentEncounterId: "enc-03",
    selectionSeq: 30,
    transitionedAway: true,
    options:
    [
        CreateOption("instance-a", "template-a", "Frost Street"),
        CreateOption("instance-b", "template-b", "Amber Cove"),
    ],
    resultingInstanceIds: Array.Empty<string>()
);
var ambiguousResult = Invoke<RunLogEvent?>(
    inferenceServiceType,
    service,
    "InferChoice",
    [ambiguousInput]
);
Assert(ambiguousResult == null, "Ambiguous transitions should not emit a precise inferred choice.");

Console.WriteLine("RunLogging inference checks passed.");

static object CreateInput(
    Type inputType,
    int? day,
    int? hour,
    string? state,
    string? encounterId,
    string? parentEncounterId,
    long selectionSeq,
    bool transitionedAway,
    RunLogOptionSnapshot[] options,
    string[] resultingInstanceIds
)
{
    var input =
        Activator.CreateInstance(inputType)
        ?? throw new InvalidOperationException(
            "RunLogChoiceInferenceInput should be constructible."
        );
    SetProperty(inputType, input, "Day", day);
    SetProperty(inputType, input, "Hour", hour);
    SetProperty(inputType, input, "State", state);
    SetProperty(inputType, input, "EncounterId", encounterId);
    SetProperty(inputType, input, "ParentEncounterId", parentEncounterId);
    SetProperty(inputType, input, "SelectionSeq", selectionSeq);
    SetProperty(inputType, input, "TransitionedAway", transitionedAway);
    SetProperty(inputType, input, "Options", options.ToList());
    SetProperty(inputType, input, "ResultingInstanceIds", resultingInstanceIds.ToList());
    return input;
}

static RunLogOptionSnapshot CreateOption(string instanceId, string templateId, string name)
{
    return new RunLogOptionSnapshot
    {
        InstanceId = instanceId,
        TemplateId = templateId,
        Name = name,
    };
}

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void SetProperty(Type type, object instance, string name, object? value)
{
    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
    if (property == null)
        throw new InvalidOperationException($"Property not found: {type.FullName}.{name}");

    property.SetValue(instance, value);
}

static T Invoke<T>(Type type, object instance, string name, object?[] args)
{
    var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.{name}");

    return (T)method.Invoke(instance, args)!;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
