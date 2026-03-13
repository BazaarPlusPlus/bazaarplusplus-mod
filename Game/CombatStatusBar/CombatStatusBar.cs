using TheBazaar;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus;

internal sealed partial class CombatStatusBar : MonoBehaviour
{
    private bool _visible = true;
    private float _visualBlend;

    private void OnEnable()
    {
        Events.CombatStarted.AddListener(OnCombatStarted, this);
        Events.CombatEnded.AddListener(OnCombatEnded, this);
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
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard[KeyBindings.Toggle.CombatStatusBar].wasPressedThisFrame)
            _visible = !_visible;

        _visualBlend = AdvanceVisualBlend(_visualBlend, IsCombatPlaybackActive, Time.unscaledDeltaTime);

        EnsureUi();
        RefreshUi();
    }

    private bool ShouldDraw()
    {
        return ShouldRenderForState(_visible, IsEnabled());
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
