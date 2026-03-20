using TheBazaar;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed partial class CombatStatusBar : MonoBehaviour
{
    private float _visualBlend;

    private void OnEnable()
    {
        Events.CombatStarted.AddListener(OnCombatStarted, this);
        Events.CombatEnded.AddListener(OnCombatEnded, this);
        EnsureConfigStateInitialized();
        EnsureUi();
        RefreshUi();
    }

    private void OnDisable()
    {
        Events.CombatStarted.RemoveListener(OnCombatStarted);
        Events.CombatEnded.RemoveListener(OnCombatEnded);
        SetUiVisible(false);
    }

    private void OnDestroy()
    {
        DisposeUi();
    }

    private void Update()
    {
        EnsureConfigStateInitialized();

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard[KeyBindings.Toggle.CombatStatusBar].wasPressedThisFrame)
            ToggleOverlayVisibility();

        _visualBlend = AdvanceVisualBlend(
            _visualBlend,
            IsCombatPlaybackActive,
            Time.unscaledDeltaTime
        );

        EnsureUi();
        RefreshUi();
    }

    private bool ShouldDraw()
    {
        EnsureConfigStateInitialized();
        return ShouldRenderForState(IsOverlayVisible, IsEnabled());
    }

    private static void OnCombatStarted()
    {
        BeginCombatPlayback();
    }

    private static void OnCombatEnded()
    {
        EndCombatPlayback();
    }
}
