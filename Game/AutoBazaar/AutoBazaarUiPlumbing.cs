#nullable enable
using System;
using System.Reflection;
using BazaarPlusPlus.Game.CombatReplay;
using TheBazaar;

namespace BazaarPlusPlus.Game.AutoBazaar;

/// <summary>
/// Handles UI-level plumbing that the AutoBazaar runtime needs each tick:
/// auto-advancing the replay state when replay playback has finished, and
/// dismissing known one-shot overlay dialogs (v1: PvP first-victory tutorial).
/// </summary>
/// <remarks>Main thread only. All methods are idempotent. Errors are logged and swallowed.</remarks>
internal static class AutoBazaarUiPlumbing
{
    // -------------------------------------------------------------------------
    // Reflection cache — filled on first successful lookup to avoid per-tick cost
    // -------------------------------------------------------------------------

    // ReplayState._exitRequested (private bool)
    private static FieldInfo? _exitRequestedField;

    // -------------------------------------------------------------------------
    // Entry point
    // -------------------------------------------------------------------------

    /// <summary>Main thread only. Idempotent. Errors are logged and swallowed.</summary>
    public static void Tick()
    {
        TryAdvanceReplay();
        TryDismissKnownOverlays();
    }

    // -------------------------------------------------------------------------
    // Replay auto-advance
    // -------------------------------------------------------------------------

    private static void TryAdvanceReplay()
    {
        try
        {
            var state = AppState.CurrentState;
            if (state is not ReplayState replay) return;
            if (CombatReplayRuntime.Instance?.IsReplayStartInProgress == true) return;

            // Guard 1: replay animation must have finished.
            // IsReplaying is a public property; it is true while the combat sim
            // animation is playing and flips to false when Replay() completes.
            if (replay.IsReplaying) return;

            // Guard 2: Exit() must not have already been called.
            // ReplayState.Exit() is idempotent via the private _exitRequested flag;
            // we check it via reflection so subsequent ticks don't redundantly
            // invoke Exit() (no-op but adds log noise).
            if (ExitAlreadyRequested(replay)) return;

            // Advance out of the replay state. ReplayState.Exit() sets
            // AppState._cachedGameSim and calls AppState._gameSimHandler.Handle(),
            // which triggers the run-state machine to transition.
            //
            // The BoardManager.OnBoardRecapReplayButtonsContinueClicked path also
            // calls ExitRecapReplayState() when the underlying RunState is LevelUp;
            // calling that first via reflection avoids a recap-overlay leak on
            // LevelUp transitions. If reflection misses it, Exit() alone still works.
            TryExitRecapReplayState();
            replay.Exit();
        }
        catch (Exception ex)
        {
            BppLog.Error("AutoBazaar", "TryAdvanceReplay failed", ex);
        }
    }

    private static MethodInfo? _exitRecapReplayStateMethod;

    private static void TryExitRecapReplayState()
    {
        try
        {
            // Only meaningful when the underlying RunState is LevelUp — mirrors
            // BoardManager.OnBoardRecapReplayButtonsContinueClicked's own guard.
            var runState = Data.CurrentState;
            if (runState is null) return;
            // ERunState.LevelUp is value 4 across builds we've seen; compare by name
            // to avoid taking a hard dependency on the enum's integer layout.
            if (runState.StateName.ToString() != "LevelUp") return;

            var boardManagerType = HarmonyLib.AccessTools.TypeByName("TheBazaar.BoardManager");
            if (boardManagerType is null) return;

            _exitRecapReplayStateMethod ??= boardManagerType.GetMethod(
                "ExitRecapReplayState",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (_exitRecapReplayStateMethod is null) return;

            // Singleton<BoardManager>.Instance — get the singleton via FindObjectOfType
            // as a fallback that doesn't depend on the generic Singleton helper.
            var instance = UnityEngine.Object.FindObjectOfType(boardManagerType) as UnityEngine.Object;
            if (instance is null) return;

            _exitRecapReplayStateMethod.Invoke(instance, null);
        }
        catch (Exception ex)
        {
            BppLog.Error("AutoBazaar", "TryExitRecapReplayState failed (non-fatal)", ex);
        }
    }

    // -------------------------------------------------------------------------
    // Known-overlay dismissal
    // -------------------------------------------------------------------------

    private static void TryDismissKnownOverlays()
    {
        try
        {
            // TODO (v1 gap): PvP first-victory tutorial dialog.
            //
            // Goal §9 specifies dismissing the PvP "首胜" (first-victory) tutorial
            // dialog when it is visible.  A comprehensive search of the decompiled
            // assemblies (TheBazaarRuntime, TheBazaar.SequenceFramework,
            // Assembly-CSharp) found no C# class that represents this dialog:
            //
            //   • BoardVFXController exposes PVP4thVictory and PVP7thVictory static
            //     events, and PVPFirstLoss — but NO PVPFirstVictory event.
            //   • TheBazaar.SequenceFramework contains PVP4thVictoryEventCondition,
            //     PVP7thVictoryEventCondition, and PVPFirstLossEventCondition — but
            //     no PVPFirstVictoryEventCondition.
            //   • The tutorial dialog for PvP first win appears to be configured
            //     entirely as a Unity scene/prefab data asset (NodeSequenceDataModel
            //     SO) with no uniquely-named C# MonoBehaviour we can target via
            //     FindObjectOfType<>.
            //   • SequenceArrowDialogController and SequenceDialogController are
            //     generic; any active instance could be an unrelated tutorial step.
            //
            // To implement this safely we would need the specific prefab name or
            // the ActivationCondition subclass that drives it.  Skipping for v1 to
            // avoid accidentally dismissing unrelated tutorial dialogs.
        }
        catch (Exception ex)
        {
            BppLog.Error("AutoBazaar", "TryDismissKnownOverlays failed", ex);
        }
    }

    // -------------------------------------------------------------------------
    // Reflection helpers (cached)
    // -------------------------------------------------------------------------

    private static bool ExitAlreadyRequested(ReplayState replay)
    {
        try
        {
            _exitRequestedField ??= typeof(ReplayState).GetField(
                "_exitRequested",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (_exitRequestedField is null) return false; // can't tell — be safe and proceed
            return _exitRequestedField.GetValue(replay) is true;
        }
        catch
        {
            return false; // reflection failed — let Exit() guard itself
        }
    }

}
