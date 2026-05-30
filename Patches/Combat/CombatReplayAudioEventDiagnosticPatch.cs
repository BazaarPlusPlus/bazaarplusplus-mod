#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Infrastructure;
using FMOD.Studio;
using FMODUnity;
using HarmonyLib;
using UnityEngine;

namespace BazaarPlusPlus.Patches.Combat;

internal static class CombatReplayAudioEventDiagnostic
{
    private const string Component = "CombatReplayAudio";
    private static readonly object s_lock = new();
    private static readonly HashSet<string> s_logged = new(StringComparer.Ordinal);
    private static readonly HashSet<string> s_instanceProbes = new(StringComparer.Ordinal);

    internal static void LogSfxEvent(string source, EventReference eventRef)
    {
        if (!IsReplayPlaybackActive() || eventRef.IsNull)
            return;

        var path = ResolveEventPath(eventRef);
        LogOnce(
            $"event|{source}|{eventRef.Guid}|{path}",
            $"Replay audio event diagnostic: source={source} guid={eventRef.Guid} path={path}"
        );
        ScheduleDelayedInstanceProbe(source, eventRef, path);
    }

    internal static void LogSfxEventInstance(
        string source,
        EventReference eventRef,
        EventInstance instance
    )
    {
        if (!IsReplayPlaybackActive() || eventRef.IsNull || !instance.isValid())
            return;

        var path = ResolveEventPath(eventRef);
        var channelGroup = ResolveEventChannelGroup(instance);
        LogOnce(
            $"instance|{source}|{eventRef.Guid}|{path}|{channelGroup}",
            $"Replay audio event diagnostic: source={source} guid={eventRef.Guid} path={path} eventChannelGroup={channelGroup}"
        );
        ScheduleDelayedInstanceProbe(source, eventRef, path);
    }

    private static bool IsReplayPlaybackActive() =>
        CombatReplayRuntime.Instance?.IsReplayPlaybackActive == true;

    private static string ResolveEventPath(EventReference eventRef)
    {
        try
        {
            var description = RuntimeManager.GetEventDescription(eventRef);
            if (
                description.isValid()
                && description.getPath(out var path) == FMOD.RESULT.OK
                && !string.IsNullOrWhiteSpace(path)
            )
            {
                return path;
            }
        }
        catch (Exception ex)
        {
            return $"<unavailable:{ex.GetType().Name}>";
        }

        return "<unavailable>";
    }

    private static string ResolveEventChannelGroup(EventInstance instance)
    {
        try
        {
            var result = instance.getChannelGroup(out var group);
            if (result != FMOD.RESULT.OK || group.handle == IntPtr.Zero)
                return $"<unavailable:{result}>";

            return FormatChannelGroupTree(group);
        }
        catch (Exception ex)
        {
            return $"<unavailable:{ex.GetType().Name}>";
        }
    }

    private static void ScheduleDelayedInstanceProbe(
        string source,
        EventReference eventRef,
        string path
    )
    {
        if (!ShouldProbeDelayedInstances(path))
            return;

        var runtime = CombatReplayRuntime.Instance;
        if (runtime == null)
            return;

        var key = $"{eventRef.Guid}|{path}";
        lock (s_lock)
        {
            if (!s_instanceProbes.Add(key))
                return;
        }

        runtime.StartCoroutine(ProbeEventInstances(source, eventRef, path));
    }

    private static bool ShouldProbeDelayedInstances(string path) =>
        path.StartsWith("event:/SFX/Combat/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("event:/SFX/Board/Cards/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("event:/SFX/Board/LifeBar/", StringComparison.OrdinalIgnoreCase);

    private static IEnumerator ProbeEventInstances(
        string source,
        EventReference eventRef,
        string path
    )
    {
        yield return null;
        LogEventInstances(source, eventRef, path, "frame+1");

        yield return null;
        LogEventInstances(source, eventRef, path, "frame+2");

        yield return null;
        yield return null;
        LogEventInstances(source, eventRef, path, "frame+4");
    }

    private static void LogEventInstances(
        string source,
        EventReference eventRef,
        string path,
        string phase
    )
    {
        if (!IsReplayPlaybackActive() || eventRef.IsNull)
            return;

        try
        {
            RuntimeManager.StudioSystem.flushCommands();
            var description = RuntimeManager.GetEventDescription(eventRef);
            var countResult = description.getInstanceCount(out var count);
            if (countResult != FMOD.RESULT.OK)
            {
                BppLog.Info(
                    Component,
                    $"Replay audio event instance diagnostic: phase={phase} source={source} guid={eventRef.Guid} path={path} getInstanceCount={countResult}"
                );
                return;
            }

            var listResult = description.getInstanceList(out var instances);
            if (listResult != FMOD.RESULT.OK || instances == null)
            {
                BppLog.Info(
                    Component,
                    $"Replay audio event instance diagnostic: phase={phase} source={source} guid={eventRef.Guid} path={path} instanceCount={count} getInstanceList={listResult}"
                );
                return;
            }

            BppLog.Info(
                Component,
                $"Replay audio event instance diagnostic: phase={phase} source={source} guid={eventRef.Guid} path={path} instanceCount={count} listed={instances.Length}"
            );

            for (var i = 0; i < instances.Length && i < 4; i++)
            {
                var instance = instances[i];
                var playback =
                    instance.getPlaybackState(out var playbackState) == FMOD.RESULT.OK
                        ? playbackState.ToString()
                        : "<unavailable>";
                var virtualLabel =
                    instance.isVirtual(out var isVirtual) == FMOD.RESULT.OK
                        ? isVirtual.ToString()
                        : "<unavailable>";
                var volumeLabel =
                    instance.getVolume(out var volume, out var finalVolume) == FMOD.RESULT.OK
                        ? $"{volume:0.###}/{finalVolume:0.###}"
                        : "<unavailable>";
                var channelGroup = ResolveEventChannelGroup(instance);
                BppLog.Info(
                    Component,
                    $"Replay audio event instance diagnostic: phase={phase} index={i} path={path} playback={playback} virtual={virtualLabel} volume={volumeLabel} channelGroup={channelGroup}"
                );
            }
        }
        catch (Exception ex)
        {
            BppLog.Info(
                Component,
                $"Replay audio event instance diagnostic: phase={phase} source={source} guid={eventRef.Guid} path={path} failed={ex.GetType().Name}: {ex.Message}"
            );
        }
    }

    private static string FormatChannelGroupTree(FMOD.ChannelGroup group)
    {
        var names = new List<string>();
        var current = group;
        for (var i = 0; i < 8 && current.handle != IntPtr.Zero; i++)
        {
            names.Add(GetChannelGroupName(current));
            var result = current.getParentGroup(out var parent);
            if (
                result != FMOD.RESULT.OK
                || parent.handle == IntPtr.Zero
                || parent.handle == current.handle
            )
                break;
            current = parent;
        }

        return string.Join(" <- ", names);
    }

    private static string GetChannelGroupName(FMOD.ChannelGroup group)
    {
        try
        {
            return
                group.getName(out var name, 256) == FMOD.RESULT.OK
                && !string.IsNullOrWhiteSpace(name)
                ? name
                : "<unnamed>";
        }
        catch
        {
            return "<unnamed>";
        }
    }

    private static void LogOnce(string key, string message)
    {
        lock (s_lock)
        {
            if (!s_logged.Add(key))
                return;
        }

        BppLog.Info(Component, message);
    }
}

[HarmonyPatch(
    typeof(SFXPlayer),
    nameof(SFXPlayer.PlayOneShotSfx),
    typeof(EventReference),
    typeof(Vector3)
)]
internal static class CombatReplayPlayOneShotSfxPositionDiagnosticPatch
{
    [HarmonyPrefix]
    private static void Prefix(EventReference eventRef)
    {
        CombatReplayAudioEventDiagnostic.LogSfxEvent(
            "SFXPlayer.PlayOneShotSfx(position)",
            eventRef
        );
    }
}

[HarmonyPatch(
    typeof(SFXPlayer),
    nameof(SFXPlayer.PlayOneShotSfx),
    typeof(EventReference),
    typeof(GameObject),
    typeof(string),
    typeof(float)
)]
internal static class CombatReplayPlayOneShotSfxFloatDiagnosticPatch
{
    [HarmonyPrefix]
    private static void Prefix(EventReference eventRef, string parameterName, float parameterValue)
    {
        CombatReplayAudioEventDiagnostic.LogSfxEvent(
            $"SFXPlayer.PlayOneShotSfx(float {parameterName}={parameterValue})",
            eventRef
        );
    }
}

[HarmonyPatch(
    typeof(SFXPlayer),
    nameof(SFXPlayer.PlayOneShotSfx),
    typeof(EventReference),
    typeof(GameObject),
    typeof(string),
    typeof(string)
)]
internal static class CombatReplayPlayOneShotSfxStringDiagnosticPatch
{
    [HarmonyPrefix]
    private static void Prefix(EventReference eventRef, string parameterName, string parameterValue)
    {
        CombatReplayAudioEventDiagnostic.LogSfxEvent(
            $"SFXPlayer.PlayOneShotSfx(string {parameterName}={parameterValue})",
            eventRef
        );
    }
}

[HarmonyPatch(
    typeof(SFXPlayer),
    nameof(SFXPlayer.PlayOneShotSfxAttached),
    typeof(EventReference),
    typeof(GameObject)
)]
internal static class CombatReplayPlayOneShotSfxAttachedDiagnosticPatch
{
    [HarmonyPrefix]
    private static void Prefix(EventReference eventRef)
    {
        CombatReplayAudioEventDiagnostic.LogSfxEvent("SFXPlayer.PlayOneShotSfxAttached", eventRef);
    }
}

[HarmonyPatch(
    typeof(SFXPlayer),
    nameof(SFXPlayer.PlaySfx),
    typeof(EventReference),
    typeof(GameObject),
    typeof(string)
)]
internal static class CombatReplayPlaySfxDiagnosticPatch
{
    [HarmonyPrefix]
    private static void Prefix(EventReference eventRef, string instanceName)
    {
        CombatReplayAudioEventDiagnostic.LogSfxEvent(
            $"SFXPlayer.PlaySfx(instanceName={instanceName})",
            eventRef
        );
    }
}

[HarmonyPatch(
    typeof(SFXPlayer),
    nameof(SFXPlayer.PlaySfx),
    typeof(EventReference),
    typeof(GameObject),
    typeof(string),
    typeof(float)
)]
internal static class CombatReplayPlaySfxFloatDiagnosticPatch
{
    [HarmonyPrefix]
    private static void Prefix(EventReference eventRef, string parameterName, float parameterValue)
    {
        CombatReplayAudioEventDiagnostic.LogSfxEvent(
            $"SFXPlayer.PlaySfx(float {parameterName}={parameterValue})",
            eventRef
        );
    }
}

[HarmonyPatch(
    typeof(SFXPlayer),
    nameof(SFXPlayer.PlaySfx),
    typeof(EventReference),
    typeof(GameObject),
    typeof(string),
    typeof(string)
)]
internal static class CombatReplayPlaySfxStringDiagnosticPatch
{
    [HarmonyPrefix]
    private static void Prefix(EventReference eventRef, string parameterName, string parameterValue)
    {
        CombatReplayAudioEventDiagnostic.LogSfxEvent(
            $"SFXPlayer.PlaySfx(string {parameterName}={parameterValue})",
            eventRef
        );
    }
}

[HarmonyPatch(
    typeof(SFXPlayer),
    "PlaySfxInternal",
    typeof(EventReference),
    typeof(EventInstance),
    typeof(GameObject),
    typeof(string)
)]
internal static class CombatReplayPlaySfxInternalDiagnosticPatch
{
    [HarmonyPostfix]
    private static void Postfix(EventReference eventRef, EventInstance soundEvent)
    {
        CombatReplayAudioEventDiagnostic.LogSfxEventInstance(
            "SFXPlayer.PlaySfxInternal",
            eventRef,
            soundEvent
        );
    }
}

[HarmonyPatch(
    typeof(SFXPlayer),
    "PlaySfxInternal",
    typeof(EventReference),
    typeof(EventInstance),
    typeof(GameObject),
    typeof(string),
    typeof(float),
    typeof(string)
)]
internal static class CombatReplayPlaySfxInternalFloatDiagnosticPatch
{
    [HarmonyPostfix]
    private static void Postfix(EventReference eventRef, EventInstance soundEvent)
    {
        CombatReplayAudioEventDiagnostic.LogSfxEventInstance(
            "SFXPlayer.PlaySfxInternal(float)",
            eventRef,
            soundEvent
        );
    }
}

[HarmonyPatch(
    typeof(SFXPlayer),
    "PlaySfxInternal",
    typeof(EventReference),
    typeof(EventInstance),
    typeof(GameObject),
    typeof(string),
    typeof(string),
    typeof(string)
)]
internal static class CombatReplayPlaySfxInternalStringDiagnosticPatch
{
    [HarmonyPostfix]
    private static void Postfix(EventReference eventRef, EventInstance soundEvent)
    {
        CombatReplayAudioEventDiagnostic.LogSfxEventInstance(
            "SFXPlayer.PlaySfxInternal(string)",
            eventRef,
            soundEvent
        );
    }
}

[HarmonyPatch(
    typeof(SFXPlayer),
    nameof(SFXPlayer.SfxTriggerCue),
    typeof(EventReference),
    typeof(bool),
    typeof(string)
)]
internal static class CombatReplaySfxTriggerCueDiagnosticPatch
{
    [HarmonyPrefix]
    private static void Prefix(EventReference eventRef, bool isEnding, string instanceName)
    {
        CombatReplayAudioEventDiagnostic.LogSfxEvent(
            $"SFXPlayer.SfxTriggerCue(isEnding={isEnding}, instanceName={instanceName})",
            eventRef
        );
    }
}

[HarmonyPatch(typeof(VOPlayer), "CreateEventInstance")]
internal static class CombatReplayVoCreateEventInstanceDiagnosticPatch
{
    [HarmonyPrefix]
    private static void Prefix(EventReference eventRef)
    {
        CombatReplayAudioEventDiagnostic.LogSfxEvent("VOPlayer.CreateEventInstance", eventRef);
    }
}
