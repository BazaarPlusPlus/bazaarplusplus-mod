using UnityEngine.InputSystem;

namespace BazaarPlusPlus;

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
        public static Key DebugPanel => Key.F2;
        public static Key CombatStatusBar => Key.F6;
    }

    internal static class DebugPanel
    {
        public static Key SelectSummary => Key.Digit1;
        public static Key SelectPreview => Key.Digit2;
        public static Key SelectRun => Key.Digit3;
        public static Key SelectEncounters => Key.Digit4;
        public static Key ToggleViewMode => Key.Tab;
    }
}
