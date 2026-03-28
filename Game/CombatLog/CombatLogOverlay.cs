#nullable enable
using BazaarPlusPlus.Game.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using CombatStatusBarState = BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBar;

namespace BazaarPlusPlus.Game.CombatLog;

internal sealed class CombatLogOverlay : MonoBehaviour
{
    private readonly CombatLogPanel _panel = new CombatLogPanel();

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard[KeyBindings.Toggle.CombatLogPanel].wasPressedThisFrame)
            _panel.ToggleVisibility();
    }

    private void OnGUI()
    {
        var timeline = CombatLogController.Instance?.Runtime.CurrentTimeline;
        _panel.DrawStandalone(timeline, CombatStatusBarState.ProcessedCombatFrames);
    }
}
