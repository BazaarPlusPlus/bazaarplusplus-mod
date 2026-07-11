#nullable enable
using System;
using System.Collections;
using System.Reflection;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.Screenshots;

internal enum EndOfRunSummaryRevealState
{
    TargetDetectionFailed,
    NotSummary,
    NoLoadedCards,
    RevealInProgress,
    RevealComplete,
    DetectionFailed,
}

internal static class EndOfRunSummaryRevealDetector
{
    private const string SummaryControllerTypeName =
        "TheBazaar.UI.EndOfRun.EndOfRunSummaryController";
    private const string ActiveControllerFieldName = "_activeController";
    private const string LoadedCardsFieldName = "loadedCards";
    private const string SkillSequenceFieldName = "_skillSequence";
    private const string TweenDurationFieldName = "duration";
    private const string TweenIsCompleteFieldName = "isComplete";
    private const string AnimatorPropertyName = "Animator";
    private const string GetBoolMethodName = "GetBool";
    private const string FaceUpParamName = "FaceUp";
    private static bool _warnedMissingActiveControllerField;
    private static bool _warnedMissingLoadedCardsField;
    private static bool _warnedMissingAnimatorProperty;
    private static bool _warnedMissingAnimatorGetBool;
    private static bool _warnedMissingSkillSequenceField;
    private static bool _warnedMissingTweenDurationField;
    private static bool _warnedMissingTweenCompleteField;

    public static EndOfRunSummaryRevealState GetRevealState(object? screenController)
    {
        if (!TryGetSummaryController(screenController, out var activeController))
            return EndOfRunSummaryRevealState.TargetDetectionFailed;
        if (activeController == null)
            return EndOfRunSummaryRevealState.NotSummary;
        if (!IsSummaryController(activeController))
            return EndOfRunSummaryRevealState.NotSummary;
        if (
            !TryGetFieldValue(
                activeController,
                LoadedCardsFieldName,
                out var loadedCardsValue,
                ref _warnedMissingLoadedCardsField,
                "Failed to resolve EndOfRunSummaryController.loadedCards; automatic capture will use the bounded fallback."
            )
        )
        {
            return EndOfRunSummaryRevealState.DetectionFailed;
        }
        if (loadedCardsValue is not IEnumerable loadedCards)
            return EndOfRunSummaryRevealState.DetectionFailed;

        var loadedCardCount = 0;
        foreach (var loadedCard in loadedCards)
        {
            if (loadedCard == null)
                continue;
            loadedCardCount++;
            if (
                !TryGetMemberValue(
                    loadedCard,
                    AnimatorPropertyName,
                    out var animator,
                    ref _warnedMissingAnimatorProperty,
                    "Failed to resolve summary card Animator; automatic capture will use the bounded fallback."
                )
            )
            {
                return EndOfRunSummaryRevealState.DetectionFailed;
            }
            if (animator == null)
                return EndOfRunSummaryRevealState.RevealInProgress;
            if (
                !TryInvokeAnimatorGetBool(
                    animator,
                    FaceUpParamName,
                    out var isFaceUp,
                    ref _warnedMissingAnimatorGetBool,
                    "Failed to resolve Animator.GetBool(string) for summary reveal detection; automatic capture will use the bounded fallback."
                )
            )
            {
                return EndOfRunSummaryRevealState.DetectionFailed;
            }
            if (!isFaceUp)
                return EndOfRunSummaryRevealState.RevealInProgress;
        }

        if (
            !TryGetFieldValue(
                activeController,
                SkillSequenceFieldName,
                out var skillSequence,
                ref _warnedMissingSkillSequenceField,
                "Failed to resolve EndOfRunSummaryController._skillSequence; automatic capture will use the bounded fallback."
            )
        )
        {
            return EndOfRunSummaryRevealState.DetectionFailed;
        }

        // DisplaySkills runs immediately after DisplayCardsAsync is invoked. Until its sequence
        // exists, the summary display has started but has not reached a settled frame.
        if (skillSequence == null)
            return EndOfRunSummaryRevealState.RevealInProgress;

        if (
            !TryGetFieldValue(
                skillSequence,
                TweenDurationFieldName,
                out var durationValue,
                ref _warnedMissingTweenDurationField,
                "Failed to resolve the end-of-run skill sequence duration; automatic capture will use the bounded fallback."
            ) || durationValue is not float duration
        )
        {
            return EndOfRunSummaryRevealState.DetectionFailed;
        }

        if (duration > 0f)
        {
            if (
                !TryGetFieldValue(
                    skillSequence,
                    TweenIsCompleteFieldName,
                    out var isCompleteValue,
                    ref _warnedMissingTweenCompleteField,
                    "Failed to resolve end-of-run skill animation completion; automatic capture will use the bounded fallback."
                ) || isCompleteValue is not bool isComplete
            )
            {
                return EndOfRunSummaryRevealState.DetectionFailed;
            }

            if (!isComplete)
                return EndOfRunSummaryRevealState.RevealInProgress;
        }

        return loadedCardCount == 0
            ? EndOfRunSummaryRevealState.NoLoadedCards
            : EndOfRunSummaryRevealState.RevealComplete;
    }

    private static bool TryGetSummaryController(
        object? screenController,
        out object? activeController
    )
    {
        activeController = null;
        if (
            !TryGetFieldValue(
                screenController,
                ActiveControllerFieldName,
                out activeController,
                ref _warnedMissingActiveControllerField,
                "Failed to resolve EndOfRunScreenController._activeController; automatic capture will use the bounded fallback."
            )
        )
        {
            return false;
        }

        return true;
    }

    private static bool IsSummaryController(object activeController)
    {
        return string.Equals(
            activeController.GetType().FullName,
            SummaryControllerTypeName,
            StringComparison.Ordinal
        );
    }

    private static bool TryGetFieldValue(
        object? instance,
        string fieldName,
        out object? value,
        ref bool warned,
        string warningMessage
    )
    {
        value = null;
        if (instance == null)
            return false;

        FieldInfo? field = null;
        for (var type = instance.GetType(); type != null && field == null; type = type.BaseType)
        {
            field = type.GetField(
                fieldName,
                BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly
            );
        }
        if (field == null)
        {
            WarnOnce(ref warned, warningMessage);
            return false;
        }

        value = field.GetValue(instance);
        return true;
    }

    private static bool TryGetMemberValue(
        object instance,
        string memberName,
        out object? value,
        ref bool warned,
        string warningMessage
    )
    {
        value = null;
        var property = instance
            .GetType()
            .GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (property != null)
        {
            value = property.GetValue(instance);
            return true;
        }

        var field = instance
            .GetType()
            .GetField(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (field != null)
        {
            value = field.GetValue(instance);
            return true;
        }

        WarnOnce(ref warned, warningMessage);
        return false;
    }

    private static bool TryInvokeAnimatorGetBool(
        object animator,
        string parameterName,
        out bool value,
        ref bool warned,
        string warningMessage
    )
    {
        value = false;
        var method = animator
            .GetType()
            .GetMethod(
                GetBoolMethodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(string)],
                modifiers: null
            );
        if (method == null)
        {
            WarnOnce(ref warned, warningMessage);
            return false;
        }

        value = (bool?)method.Invoke(animator, [parameterName]) == true;
        return true;
    }

    private static void WarnOnce(ref bool warned, string message)
    {
        if (warned)
            return;

        warned = true;
        BppLog.Warn("EndOfRunScreenshot", message);
    }
}
