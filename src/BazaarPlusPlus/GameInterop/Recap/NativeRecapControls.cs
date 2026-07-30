#nullable enable
using System.Reflection;
using HarmonyLib;
using TheBazaar;
using UnityEngine.UI;

namespace BazaarPlusPlus.GameInterop.Recap;

internal static class NativeRecapControls
{
    private static readonly FieldInfo? BackButtonField = AccessTools.Field(
        typeof(BoardRecapReplayButtonsController),
        "BackButton"
    );
    private static Button? _backButton;

    internal static void Observe(BoardRecapReplayButtonsController controller)
    {
        _backButton = BackButtonField?.GetValue(controller) as Button;
    }

    internal static bool TryInvokeBack()
    {
        var manager = Singleton<BoardManager>.Instance;
        if (manager?.IsRecapViewOpen != true || _backButton == null)
            return false;

        _backButton.onClick.Invoke();
        return manager.IsRecapViewOpen == false;
    }
}
