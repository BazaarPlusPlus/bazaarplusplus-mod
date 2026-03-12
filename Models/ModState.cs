#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace BazaarPlusPlus;

internal static class ModState
{
    public static ManualLogSource Logger;

    // Config entries
    public static ConfigEntry<bool> EnableNameOverrideConfig;
    public static ConfigEntry<bool> EnableCombatStatusBarConfig;
    public static ConfigEntry<float> DefaultCombatSpeedConfig;

    // Runtime state
    public static EVictoryCondition LastVictoryCondition;
    public static string LastMessageId = "";
    public static DateTime LastSentTime = DateTime.MinValue;
    public static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(2);
    public static readonly float[] CombatSpeedSteps = { 0.25f, 0.5f, 1f, 2f, 3f, 4f,};
    public static bool CombatPlaybackActive;
    public static float CombatSpeedMultiplier = 1f;
    public static int ProcessedCombatFrames;
    public static int TotalCombatFrames;

    // Encounter selection tracking
    public static List<RunInfo.CardInfo> AvailableEncounters; // map path options (ERunState.Encounter)
    public static List<RunInfo.CardInfo> CurrentEncounterChoices; // choices inside an encounter (Choice/Loot/Pedestal)
    public static List<RunInfo.MonsterPreview> EncounterMonsterPreviews; // combat encounter monster info from local DB

    public static List<EEnchantmentType> AvailableEnchantments = new List<EEnchantmentType>();
    public static string CardsJsonPath;

    public static void Initialize(ConfigFile config)
    {
        EnableNameOverrideConfig = config.Bind(
            "StreamerMode",
            "EnableNameOverride",
            false,
            "Whether to replace the local player's displayed username"
        );
        EnableCombatStatusBarConfig = config.Bind(
            "Combat",
            "EnableStatusBar",
            true,
            "Whether to show the combat status bar with elapsed time and speed controls"
        );
        DefaultCombatSpeedConfig = config.Bind(
            "Combat",
            "DefaultSpeedMultiplier",
            1f,
            new ConfigDescription("Default combat playback speed multiplier", new AcceptableValueRange<float>(0.25f, 8f))
        );
        CombatSpeedMultiplier = ClampSpeed(DefaultCombatSpeedConfig.Value);
        BppLog.Info(
            "ModState",
            $"Configuration initialized: enableNameOverride={EnableNameOverrideConfig.Value}, combatStatusBar={EnableCombatStatusBarConfig.Value}, combatSpeed={CombatSpeedMultiplier:F2}x"
        );
        CardsJsonPath = CardJsonPathResolver.GetCardsJsonPath();
        if (string.IsNullOrWhiteSpace(CardsJsonPath))
        {
            BppLog.Error(
                "ModState",
                "Failed to resolve cards.json path from BepInEx game root; cards.json will be unavailable"
            );
        }
        else
        {
            BppLog.Debug("ModState", $"cards.json path initialized: {CardsJsonPath}");
        }
    }

    public static void BeginCombatPlayback()
    {
        CombatPlaybackActive = true;
        ProcessedCombatFrames = 0;
    }

    public static void EndCombatPlayback()
    {
        CombatPlaybackActive = false;
    }

    public static void SetCombatFrameTotal(int totalFrames)
    {
        TotalCombatFrames = Math.Max(totalFrames, 0);
        ProcessedCombatFrames = 0;
    }

    public static void AdvanceCombatFrame()
    {
        if (!CombatPlaybackActive)
            return;

        ProcessedCombatFrames++;
        if (TotalCombatFrames > 0 && ProcessedCombatFrames > TotalCombatFrames)
            ProcessedCombatFrames = TotalCombatFrames;
    }

    public static TimeSpan GetCombatLogicalElapsed()
    {
        return TimeSpan.FromMilliseconds(ProcessedCombatFrames * 50d);
    }

    public static float StepCombatSpeed(int direction)
    {
        var currentIndex = 0;
        var smallestDelta = float.MaxValue;
        for (var i = 0; i < CombatSpeedSteps.Length; i++)
        {
            var delta = Math.Abs(CombatSpeedSteps[i] - CombatSpeedMultiplier);
            if (delta < smallestDelta)
            {
                smallestDelta = delta;
                currentIndex = i;
            }
        }

        currentIndex = Math.Clamp(currentIndex + direction, 0, CombatSpeedSteps.Length - 1);
        return SetCombatSpeed(CombatSpeedSteps[currentIndex]);
    }

    public static float SetCombatSpeed(float speed)
    {
        CombatSpeedMultiplier = ClampSpeed(speed);
        if (DefaultCombatSpeedConfig != null)
            DefaultCombatSpeedConfig.Value = CombatSpeedMultiplier;
        return CombatSpeedMultiplier;
    }

    private static float ClampSpeed(float speed)
    {
        return Math.Clamp(speed, 0.25f, 8f);
    }
}
