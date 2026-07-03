#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using BazaarPlusPlus.Game.VoiceSubtitles;
using BazaarPlusPlus.GameInterop.VoiceSubtitles;
using BazaarPlusPlus.Infrastructure;
using FMOD.Studio;
using FMODUnity;
using HarmonyLib;

namespace BazaarPlusPlus.Patches.VoiceSubtitles;

[HarmonyPatch(typeof(SoundManager), "OnSystemsInitialized")]
internal static class SoundManagerInitializedPatch
{
    [HarmonyPostfix]
    private static void Postfix(SoundManager __instance)
    {
        try
        {
            VoiceLineVoObserverBridge.Install(__instance.VOPlayer);
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn($"Failed to install VO observer: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(VOPlayer), nameof(VOPlayer.PlayVO))]
internal static class VOPlayerPlayVOPatch
{
    private const int StoppedCallbackMask = 0x20;

    [HarmonyPrefix]
    private static void Prefix(
        VOPlayer __instance,
        bool isHero,
        CardAudio.AudioHookType audioHookType
    )
    {
        VoiceLineVoObserverBridge.BeginVoiceAttempt(
            VoiceLineVoObserverBridge.CreateVoiceAttempt(__instance, isHero, audioHookType)
        );
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        VoiceLineVoObserverBridge.ClearVoiceAttempt("PlayVO postfix");
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions
    )
    {
        var codes = instructions.ToList();
        var setCallback = AccessTools.Method(
            typeof(EventInstance),
            nameof(EventInstance.setCallback),
            new[] { typeof(EVENT_CALLBACK), typeof(EVENT_CALLBACK_TYPE) }
        );
        if (setCallback == null)
        {
            VoiceSubtitlesLog.Warn(
                "VOPlayer.PlayVO callback mask patch skipped: EventInstance.setCallback not found"
            );
            return codes;
        }

        var callbackMask = (int)(EVENT_CALLBACK_TYPE.STOPPED | EVENT_CALLBACK_TYPE.SOUND_PLAYED);
        var patched = 0;

        for (var i = 0; i < codes.Count; i++)
        {
            if (!codes[i].Calls(setCallback) || i == 0)
                continue;

            if (!codes[i - 1].LoadsConstant(StoppedCallbackMask))
            {
                VoiceSubtitlesLog.Warn(
                    "VOPlayer.PlayVO callback mask candidate skipped because previous instruction "
                        + $"does not load STOPPED=0x20 index={i - 1} instruction={codes[i - 1]}"
                );
                continue;
            }

            var replacement = new CodeInstruction(OpCodes.Ldc_I4, callbackMask);
            replacement.labels.AddRange(codes[i - 1].labels);
            replacement.blocks.AddRange(codes[i - 1].blocks);
            codes[i - 1] = replacement;
            patched++;
        }

        if (patched == 1)
            VoiceSubtitlesLog.Info($"VOPlayer.PlayVO callback mask patched count={patched}");
        else
            VoiceSubtitlesLog.Warn(
                $"VOPlayer.PlayVO callback mask patched count={patched}, expected=1"
            );
        return codes;
    }
}

[HarmonyPatch(typeof(VOPlayer), nameof(VOPlayer.PlayTutorialVO))]
internal static class VOPlayerPlayTutorialVOPatch
{
    [HarmonyPrefix]
    private static void Prefix(VOPlayer __instance, EventReference eventRef)
    {
        VoiceLineVoObserverBridge.BeginVoiceAttempt(
            VoiceLineVoObserverBridge.CreateVoiceAttempt(
                __instance,
                "PlayTutorialVO",
                "Hero",
                "Tutorial",
                eventRef
            )
        );
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        VoiceLineVoObserverBridge.ClearVoiceAttempt("PlayTutorialVO postfix");
    }
}

[HarmonyPatch(typeof(VOPlayer), "OnVOStopInternal")]
internal static class VOPlayerOnVOStopInternalPatch
{
    [HarmonyPrefix]
    private static void Prefix(VOPlayer __instance, EVENT_CALLBACK_TYPE type, IntPtr parameters)
    {
        if (type == EVENT_CALLBACK_TYPE.SOUND_PLAYED)
            VoiceLineVoObserverBridge.OnVoSoundPlayed(__instance, parameters);
    }

    [HarmonyPostfix]
    private static void Postfix(VOPlayer __instance, EVENT_CALLBACK_TYPE type)
    {
        if (type == EVENT_CALLBACK_TYPE.STOPPED)
            VoiceLineVoObserverBridge.OnVoPlaybackStopped(__instance);
    }
}
