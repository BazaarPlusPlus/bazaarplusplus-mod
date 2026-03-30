using System;
using BazaarPlusPlus.Core.Runtime;
using TheBazaar;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed partial class CombatStatusBar
{
    internal static bool IsCombatPlaybackActive { get; private set; }
    internal static bool IsCombatPaused { get; private set; }
    internal static int ProcessedCombatFrames { get; private set; }
    internal static int TotalCombatFrames { get; private set; }
    internal static TimeSpan LastCombatLogicalElapsed { get; private set; }
    internal static bool HasCompletedCombatPlayback { get; private set; }

    internal static void BeginCombatPlayback()
    {
        IsCombatPlaybackActive = true;
        ProcessedCombatFrames = 0;
    }

    internal static void EndCombatPlayback()
    {
        LastCombatLogicalElapsed = GetCombatLogicalElapsed();
        HasCompletedCombatPlayback = true;
        SetCombatPaused(false);
        IsCombatPlaybackActive = false;
    }

    internal static void SetCombatFrameTotal(int totalFrames)
    {
        TotalCombatFrames = Math.Max(totalFrames, 0);
        ProcessedCombatFrames = 0;
    }

    internal static void AdvanceCombatFrame()
    {
        if (!IsCombatPlaybackActive)
            return;

        ProcessedCombatFrames++;
        if (TotalCombatFrames > 0 && ProcessedCombatFrames > TotalCombatFrames)
            ProcessedCombatFrames = TotalCombatFrames;
    }

    internal static TimeSpan GetCombatLogicalElapsed()
    {
        return TimeSpan.FromMilliseconds(ProcessedCombatFrames * 50d);
    }

    internal static bool ShouldRenderForState(bool enabled)
    {
        return enabled && BppRuntimeHost.RunContext.IsInGameRun;
    }

    internal static string GetDisplayedTimeLabel()
    {
        return IsCombatPlaybackActive ? "Time" : "LastCombat";
    }

    internal static string GetDisplayedTimeText()
    {
        return IsCombatPlaybackActive ? FormatElapsed(GetCombatLogicalElapsed())
            : HasCompletedCombatPlayback ? FormatElapsed(LastCombatLogicalElapsed)
            : "-:--:--";
    }

    internal static string GetDisplayedFrameText()
    {
        return IsCombatPlaybackActive ? ProcessedCombatFrames.ToString() : "Standby";
    }

    internal static float AdvanceVisualBlend(float current, bool active, float deltaTime)
    {
        var target = active ? 1f : 0f;
        var maxStep = Math.Max(deltaTime, 0f) * 5f;
        if (current < target)
            return Math.Min(current + maxStep, target);

        if (current > target)
            return Math.Max(current - maxStep, target);

        return current;
    }

    internal static void ResetStateForTests()
    {
        IsCombatPlaybackActive = false;
        IsCombatPaused = false;
        ProcessedCombatFrames = 0;
        TotalCombatFrames = 0;
        LastCombatLogicalElapsed = TimeSpan.Zero;
        HasCompletedCombatPlayback = false;
    }

    internal static bool CanToggleCombatPause()
    {
        return IsCombatPlaybackActive && Singleton<GameServiceManager>.Instance != null;
    }

    internal static bool ToggleCombatPause()
    {
        return SetCombatPaused(!IsCombatPaused);
    }

    internal static bool SetCombatPaused(bool paused)
    {
        var gameServiceManager = Singleton<GameServiceManager>.Instance;
        if (gameServiceManager == null)
            return IsCombatPaused;

        gameServiceManager.PauseOrUnpauseGame(paused);
        IsCombatPaused = gameServiceManager.GamePaused;
        return IsCombatPaused;
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        var minutes = (int)elapsed.TotalMinutes;
        return $"{minutes}:{elapsed.Seconds:00}:{elapsed.Milliseconds / 10:00}";
    }
}
