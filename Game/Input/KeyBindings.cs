using UnityEngine.InputSystem;

namespace BazaarPlusPlus.Game.Input;

internal static class KeyBindings
{
    internal static class Modifiers
    {
        public static bool IsCtrlPressed(Keyboard keyboard)
        {
            return keyboard != null
                && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
        }

        public static bool IsShiftPressed(Keyboard keyboard)
        {
            return keyboard != null
                && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        }
    }

    internal static class Toggle
    {
        public const string DebugPanel = "<Keyboard>/f2";
    }

    internal static class DebugPanel
    {
        public const string SelectSummary = "<Keyboard>/digit1";
        public const string SelectRun = "<Keyboard>/digit2";
        public const string SelectEncounters = "<Keyboard>/digit3";
        public const string SelectReplays = "<Keyboard>/digit4";
        public const string ToggleViewMode = "<Keyboard>/tab";
    }
}
