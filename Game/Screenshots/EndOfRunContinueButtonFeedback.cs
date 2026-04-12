#nullable enable
using System.Reflection;

namespace BazaarPlusPlus.Game.Screenshots;

internal static class EndOfRunContinueButtonFeedback
{
    private const string ContinueButtonFieldName = "continueButton";
    private const string SetInteractableMethodName = "SetInteractable";
    private const string SetUnInteractableMethodName = "SetUnInteractable";

    public static void SyncInteractivity(object? screenController, bool shouldAllowContinue)
    {
        if (!TryGetContinueButton(screenController, out var continueButton) || continueButton == null)
            return;

        var methodName = shouldAllowContinue
            ? SetInteractableMethodName
            : SetUnInteractableMethodName;
        var method = continueButton.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
        method?.Invoke(continueButton, []);
    }

    private static bool TryGetContinueButton(object? screenController, out object? continueButton)
    {
        continueButton = null;
        if (screenController == null)
            return false;

        var field = screenController.GetType().GetField(
            ContinueButtonFieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
        if (field == null)
            return false;

        continueButton = field.GetValue(screenController);
        return true;
    }
}
