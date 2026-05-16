#nullable enable
using System;
using System.Reflection;
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

    // AppState._cachedGameSim (protected static NetMessageGameSim)
    private static FieldInfo? _cachedGameSimField;
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

            // Guard 1: replay animation must have finished.
            // IsReplaying is a public property; it is true while the combat sim
            // animation is playing and flips to false when Replay() completes.
            if (replay.IsReplaying) return;

            // Guard 2: Exit() must not have already been called.
            // ReplayState.Exit() sets the private _exitRequested flag and is
            // idempotent, but we check it via reflection so we do not call Exit()
            // on every tick after the first call (which would be a no-op but noisy).
            if (ExitAlreadyRequested(replay)) return;

            // Guard 3: the sequence's DespawnMessage (the "cached game sim") must
            // be available. ReplayState.Exit() reads _sequence.DespawnMessage
            // internally; if _sequence is null (e.g. state entered but
            // OnCombatSequenceCreated hasn't fired) Exit() would NRE. We rely on
            // Exit()'s own guard (_exitRequested) for true idempotency, but this
            // check avoids a noisy stack trace on an edge case.
            //
            // _cachedGameSim on AppState is protected static. We reflect on it as
            // a proxy: if it is already non-null, another code path already called
            // Exit() (or GoToNextState on a CombatState), so skip.
            if (CachedGameSimNonNull()) return;

            // All guards passed — advance out of the replay state.
            // ReplayState.Exit() is public. It sets AppState._cachedGameSim and
            // calls AppState._gameSimHandler.Handle(), which triggers the run-state
            // machine to transition to the next state (Choice / LevelUp / etc.).
            replay.Exit();
        }
        catch (Exception ex)
        {
            BppLog.Error("AutoBazaar", "TryAdvanceReplay failed", ex);
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

    private static bool CachedGameSimNonNull()
    {
        try
        {
            _cachedGameSimField ??= typeof(AppState).GetField(
                "_cachedGameSim",
                BindingFlags.Static | BindingFlags.NonPublic);

            if (_cachedGameSimField is null) return false;
            return _cachedGameSimField.GetValue(null) is not null;
        }
        catch
        {
            return false;
        }
    }
}
