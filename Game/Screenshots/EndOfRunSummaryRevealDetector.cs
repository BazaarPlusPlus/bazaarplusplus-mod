#nullable enable
using System;
using System.Collections;
using System.Reflection;

namespace BazaarPlusPlus.Game.Screenshots;

internal enum EndOfRunSummaryRevealState
{
    NotSummary,
    RevealInProgress,
    RevealComplete,
    DetectionFailed,
}

internal static class EndOfRunSummaryRevealDetector
{
    private const string SummaryControllerTypeName = "TheBazaar.UI.EndOfRun.EndOfRunSummaryController";
    private const string ActiveControllerFieldName = "_activeController";
    private const string LoadedCardsFieldName = "loadedCards";
    private const string AnimatorPropertyName = "Animator";
    private const string GetBoolMethodName = "GetBool";
    private const string FaceUpParamName = "FaceUp";
    private static bool _warnedMissingActiveControllerField;
    private static bool _warnedMissingLoadedCardsField;
    private static bool _warnedMissingAnimatorProperty;
    private static bool _warnedMissingAnimatorGetBool;

    public static bool IsSummaryRevealInProgress(object? screenController)
    {
        return GetRevealState(screenController) == EndOfRunSummaryRevealState.RevealInProgress;
    }

    public static EndOfRunSummaryRevealState GetRevealState(object? screenController)
    {
        if (!TryGetFieldValue(
                screenController,
                ActiveControllerFieldName,
                out var activeController,
                ref _warnedMissingActiveControllerField,
                "Failed to resolve EndOfRunScreenController._activeController; continue button gating will fall back to the game's default behavior."
            ))
        {
            return EndOfRunSummaryRevealState.DetectionFailed;
        }
        if (activeController == null)
            return EndOfRunSummaryRevealState.NotSummary;
        if (!string.Equals(activeController.GetType().FullName, SummaryControllerTypeName, StringComparison.Ordinal))
            return EndOfRunSummaryRevealState.NotSummary;
        if (!TryGetFieldValue(
                activeController,
                LoadedCardsFieldName,
                out var loadedCardsValue,
                ref _warnedMissingLoadedCardsField,
                "Failed to resolve EndOfRunSummaryController.loadedCards; continue button gating will fall back to the game's default behavior."
            ))
        {
            return EndOfRunSummaryRevealState.DetectionFailed;
        }
        if (loadedCardsValue is not IEnumerable loadedCards)
            return EndOfRunSummaryRevealState.DetectionFailed;

        foreach (var loadedCard in loadedCards)
        {
            if (loadedCard == null)
                continue;
            if (!TryGetPropertyValue(
                    loadedCard,
                    AnimatorPropertyName,
                    out var animator,
                    ref _warnedMissingAnimatorProperty,
                    "Failed to resolve summary card Animator; continue button gating will fall back to the game's default behavior."
                ))
            {
                return EndOfRunSummaryRevealState.DetectionFailed;
            }
            if (animator == null)
                return EndOfRunSummaryRevealState.RevealInProgress;
            if (!TryInvokeAnimatorGetBool(
                    animator,
                    FaceUpParamName,
                    out var isFaceUp,
                    ref _warnedMissingAnimatorGetBool,
                    "Failed to resolve Animator.GetBool(string) for summary reveal detection; continue button gating will fall back to the game's default behavior."
                ))
            {
                return EndOfRunSummaryRevealState.DetectionFailed;
            }
            if (!isFaceUp)
                return EndOfRunSummaryRevealState.RevealInProgress;
        }

        return EndOfRunSummaryRevealState.RevealComplete;
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

        var field = instance.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
        if (field == null)
        {
            WarnOnce(ref warned, warningMessage);
            return false;
        }

        value = field.GetValue(instance);
        return true;
    }

    private static bool TryGetPropertyValue(
        object instance,
        string propertyName,
        out object? value,
        ref bool warned,
        string warningMessage
    )
    {
        value = null;
        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
        if (property == null)
        {
            WarnOnce(ref warned, warningMessage);
            return false;
        }

        value = property.GetValue(instance);
        return true;
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
        var method = animator.GetType().GetMethod(
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
