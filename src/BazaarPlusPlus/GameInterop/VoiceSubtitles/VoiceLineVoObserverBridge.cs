#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using TheBazaar.AppFramework;
using UnityEngine;
using VoiceSubtitlesLog = BazaarPlusPlus.GameInterop.VoiceSubtitles.VoiceSubtitlesInteropLog;

namespace BazaarPlusPlus.GameInterop.VoiceSubtitles;

internal static class VoiceLineVoObserverBridge
{
    private static readonly HashSet<int> InstalledPlayers = new();
    private static int _nextAttemptId;
    private static VoiceAttemptContext _pendingContext = VoiceAttemptContext.Unknown;
    private static VoiceAttemptContext _activeContext = VoiceAttemptContext.Unknown;
    private static VoiceSubtitleObserverCallbacks _callbacks = VoiceSubtitleObserverCallbacks.Empty;

    internal static void Configure(VoiceSubtitleObserverCallbacks callbacks)
    {
        _callbacks = callbacks ?? VoiceSubtitleObserverCallbacks.Empty;
    }

    internal static void Install(VOPlayer? player)
    {
        if (player == null)
            return;

        var key = RuntimeHelpers.GetHashCode(player);
        if (!InstalledPlayers.Add(key))
            return;

        player.VODebugPrint -= OnVoDebugPrint;
        player.VODebugPrint += OnVoDebugPrint;
        VoiceSubtitlesLog.Info(
            $"VO debug listener installed player={VoiceSubtitlesLog.ObjectId(player)}"
        );
    }

    internal static void BeginVoiceAttempt(VoiceAttemptContext context)
    {
        _pendingContext = context;
        VoiceSubtitlesLog.Info(
            "VO attempt begin "
                + $"attempt={context.AttemptId} "
                + $"origin={context.Origin} "
                + $"player={VoiceSubtitlesLog.ObjectId(context.Player)} "
                + $"source={context.SourceLabel} "
                + $"hook={context.HookName} "
                + $"eventRef={VoiceSubtitlesLog.Field(context.EventReferenceText)} "
                + $"path={VoiceSubtitlesLog.Field(context.EventPath)} "
                + $"eventDuration={context.DurationSeconds:F3}s"
        );
    }

    internal static VoiceAttemptContext CreateVoiceAttempt(
        VOPlayer? player,
        bool isHero,
        CardAudio.AudioHookType audioHookType
    )
    {
        var sourceLabel = isHero ? "Hero" : "Merchant";
        var hookName = audioHookType.ToString();
        var eventRef = default(EventReference);

        try
        {
            var handler = Services.Get<SoundManager>().CardAudioHandler;
            var cardAudio =
                isHero ? handler.HeroCardAudio
                : audioHookType == CardAudio.AudioHookType.OnChoiceSelect ? handler.RewardCardAudio
                : handler.ActiveCardAudio;
            var hook = cardAudio?.GetAudioHook(audioHookType);
            if (hook != null)
                eventRef = hook.EventRef;
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn($"Failed to inspect VO CardAudio hook={hookName}: {ex.Message}");
        }

        return CreateVoiceAttempt(player, "PlayVO", sourceLabel, hookName, eventRef);
    }

    internal static VoiceAttemptContext CreateVoiceAttempt(
        VOPlayer? player,
        string origin,
        string sourceLabel,
        string hookName,
        EventReference eventRef
    )
    {
        var attemptId = Interlocked.Increment(ref _nextAttemptId);
        var eventReferenceText = eventRef.IsNull ? null : eventRef.ToString();
        var eventPath = default(string);
        var durationSeconds = 0f;

        if (!eventRef.IsNull)
        {
            try
            {
                var description = RuntimeManager.GetEventDescription(eventRef);
                if (description.getPath(out var path) == RESULT.OK)
                    eventPath = path;
                if (description.getLength(out var lengthMs) == RESULT.OK && lengthMs > 0)
                    durationSeconds = lengthMs / 1000f;
            }
            catch (Exception ex)
            {
                VoiceSubtitlesLog.Warn(
                    $"Failed to resolve VO event metadata {eventRef}: {ex.Message}"
                );
            }
        }

        return new VoiceAttemptContext(
            attemptId,
            player,
            string.IsNullOrWhiteSpace(origin) ? "Unknown" : origin,
            sourceLabel,
            string.IsNullOrWhiteSpace(hookName) ? "Unknown" : hookName,
            eventReferenceText,
            eventPath,
            durationSeconds,
            Time.unscaledTime
        );
    }

    internal static void ClearVoiceAttempt(string reason)
    {
        if (_pendingContext.IsKnown)
        {
            VoiceSubtitlesLog.Info(
                "VO attempt clear "
                    + $"attempt={_pendingContext.AttemptId} "
                    + $"reason={reason} "
                    + $"age={AgeSeconds(_pendingContext):F3}s"
            );
        }

        _pendingContext = VoiceAttemptContext.Unknown;
    }

    internal static void Reset()
    {
        InstalledPlayers.Clear();
        _pendingContext = VoiceAttemptContext.Unknown;
        _activeContext = VoiceAttemptContext.Unknown;
        _nextAttemptId = 0;
    }

    internal static void OnVoSoundPlayed(VOPlayer? player, IntPtr soundPtr)
    {
        var context = _activeContext;
        if (!context.IsKnown && _pendingContext.IsKnown)
        {
            _activeContext = _pendingContext;
            context = _activeContext;
        }

        if (!context.IsKnown)
        {
            VoiceSubtitlesLog.Warn(
                "VO sound played has no active attempt "
                    + $"player={VoiceSubtitlesLog.ObjectId(player)} "
                    + $"soundPtr=0x{soundPtr.ToInt64():X}"
            );
        }

        var sound = new Sound(soundPtr);
        var soundName = ResolveSoundName(sound);
        var soundDurationSeconds = ResolveSoundDurationSeconds(sound);
        var sourceLabel = context.SourceLabel == "Unknown" ? "Hero" : context.SourceLabel;
        var hookName = context.HookName;
        var lookupText =
            $"{soundName ?? string.Empty} {context.EventPath ?? string.Empty} {context.EventReferenceText ?? string.Empty}";
        var resolution = ResolveLine(lookupText, sourceLabel, hookName);
        var line = resolution.Line;
        var durationSeconds =
            soundDurationSeconds > 0f ? soundDurationSeconds
            : context.DurationSeconds > 0f ? context.DurationSeconds
            : line.DurationSeconds;

        VoiceSubtitlesLog.Info(
            "VO sound played "
                + $"attempt={context.AttemptId} "
                + $"player={VoiceSubtitlesLog.ObjectId(player)} "
                + $"contextPlayer={VoiceSubtitlesLog.ObjectId(context.Player)} "
                + $"source={sourceLabel} "
                + $"hook={hookName} "
                + $"sound={VoiceSubtitlesLog.Field(soundName)} "
                + $"soundDuration={soundDurationSeconds:F3}s "
                + $"contextPath={VoiceSubtitlesLog.Field(context.EventPath)}"
        );

        var enabled = IsEnabled();
        if (enabled)
            QueueHideCurrent($"superseded-by-voice-attempt-{context.AttemptId}");

        if (!HasResolvedLine(resolution))
        {
            VoiceSubtitlesLog.Info(
                "VO subtitle skipped because no voice line matched "
                    + $"attempt={context.AttemptId} "
                    + $"strategy={resolution.Strategy} "
                    + $"hook={hookName} "
                    + $"sound={VoiceSubtitlesLog.Field(soundName)}"
            );
            return;
        }

        if (!enabled)
            return;

        QueueShow(CreateCue(line, player ?? context.Player, durationSeconds, context.AttemptId));
        VoiceSubtitlesLog.Info(
            "VO subtitle resolved "
                + $"attempt={context.AttemptId} "
                + $"strategy={resolution.Strategy} "
                + $"catalog={resolution.CatalogName} "
                + $"matched={VoiceSubtitlesLog.Field(resolution.MatchedToken)} "
                + $"candidates={resolution.CandidateCount} "
                + $"stem={line.Stem} "
                + $"soundDuration={soundDurationSeconds:F3}s "
                + $"eventDuration={context.DurationSeconds:F3}s "
                + $"lineDuration={line.DurationSeconds:F3}s "
                + $"displayDuration={durationSeconds:F3}s "
                + $"english={VoiceSubtitlesLog.Field(line.English)} "
                + $"chinese={VoiceSubtitlesLog.Field(line.Chinese)}"
        );
    }

    internal static void OnVoPlaybackStopped(VOPlayer? player)
    {
        if (_activeContext.IsKnown)
        {
            VoiceSubtitlesLog.Info(
                "VO playback stopped "
                    + $"attempt={_activeContext.AttemptId} "
                    + $"player={VoiceSubtitlesLog.ObjectId(player)} "
                    + $"age={AgeSeconds(_activeContext):F3}s"
            );
        }

        _activeContext = VoiceAttemptContext.Unknown;
    }

    private static void OnVoDebugPrint(string eventReferenceText)
    {
        var context = _pendingContext;
        if (!context.IsKnown)
        {
            VoiceSubtitlesLog.Warn(
                "VO debug callback has no pending attempt "
                    + $"callbackEvent={VoiceSubtitlesLog.Field(eventReferenceText)}"
            );
        }

        if (context.IsKnown)
            _activeContext = context;

        var sourceLabel = context.SourceLabel == "Unknown" ? "Hero" : context.SourceLabel;
        var hookName = context.HookName;
        var lookupText =
            $"{context.EventPath ?? string.Empty} {context.EventReferenceText ?? string.Empty} {eventReferenceText}";
        var resolution = ResolveLine(lookupText, sourceLabel, hookName);
        var line = resolution.Line;
        var contextMatchesCallback = ContextMatchesCallback(context, eventReferenceText);

        VoiceSubtitlesLog.Info(
            "VO debug callback deferred "
                + $"attempt={context.AttemptId} "
                + $"origin={context.Origin} "
                + $"age={AgeSeconds(context):F3}s "
                + $"source={sourceLabel} "
                + $"hook={hookName} "
                + $"contextMatchesCallback={contextMatchesCallback} "
                + $"callbackEvent={VoiceSubtitlesLog.Field(eventReferenceText)} "
                + $"contextEventRef={VoiceSubtitlesLog.Field(context.EventReferenceText)} "
                + $"contextPath={VoiceSubtitlesLog.Field(context.EventPath)}"
        );

        VoiceSubtitlesLog.Info(
            "VO debug category resolution "
                + $"attempt={context.AttemptId} "
                + $"strategy={resolution.Strategy} "
                + $"catalog={resolution.CatalogName} "
                + $"matched={VoiceSubtitlesLog.Field(resolution.MatchedToken)} "
                + $"candidates={resolution.CandidateCount} "
                + $"stem={line.Stem} "
                + $"eventDuration={context.DurationSeconds:F3}s "
                + $"lineDuration={line.DurationSeconds:F3}s "
                + $"english={VoiceSubtitlesLog.Field(line.English)} "
                + $"chinese={VoiceSubtitlesLog.Field(line.Chinese)}"
        );

        if (string.Equals(context.Origin, "PlayVO", StringComparison.Ordinal))
            return;

        var enabled = IsEnabled();
        if (enabled)
            QueueHideCurrent($"superseded-by-voice-attempt-{context.AttemptId}");

        if (!HasResolvedLine(resolution))
        {
            VoiceSubtitlesLog.Info(
                "VO subtitle skipped because no voice line matched "
                    + $"attempt={context.AttemptId} "
                    + $"origin={context.Origin} "
                    + $"strategy={resolution.Strategy} "
                    + $"hook={hookName}"
            );
            return;
        }

        if (!enabled)
            return;

        var durationSeconds =
            context.DurationSeconds > 0f ? context.DurationSeconds : line.DurationSeconds;
        QueueShow(CreateCue(line, context.Player, durationSeconds, context.AttemptId));
        VoiceSubtitlesLog.Info(
            "VO subtitle resolved from debug callback "
                + $"attempt={context.AttemptId} "
                + $"origin={context.Origin} "
                + $"strategy={resolution.Strategy} "
                + $"stem={line.Stem} "
                + $"displayDuration={durationSeconds:F3}s"
        );
    }

    private static VoiceSubtitlePlaybackCue CreateCue(
        VoiceSubtitleLine line,
        VOPlayer? player,
        float durationSeconds,
        int attemptId
    )
    {
        Func<bool>? isPlaybackStoppedOrStopping = null;
        Func<string>? playbackStateText = null;
        if (player != null)
        {
            isPlaybackStoppedOrStopping = () =>
            {
                var state = player.GetVOPlaybackState();
                return state == PLAYBACK_STATE.STOPPED || state == PLAYBACK_STATE.STOPPING;
            };
            playbackStateText = () => player.GetVOPlaybackState().ToString();
        }

        return new VoiceSubtitlePlaybackCue(
            line,
            durationSeconds,
            attemptId,
            isPlaybackStoppedOrStopping,
            playbackStateText
        );
    }

    private static string? ResolveSoundName(Sound sound)
    {
        try
        {
            if (sound.getName(out var name, 512) == RESULT.OK && !string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn($"Failed to read FMOD sound name: {ex.Message}");
        }

        return null;
    }

    private static float ResolveSoundDurationSeconds(Sound sound)
    {
        try
        {
            if (sound.getLength(out var lengthMs, TIMEUNIT.MS) == RESULT.OK && lengthMs > 0)
                return lengthMs / 1000f;
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn($"Failed to read FMOD sound length: {ex.Message}");
        }

        return 0f;
    }

    private static float AgeSeconds(VoiceAttemptContext context)
    {
        if (!context.IsKnown)
            return 0f;

        return Mathf.Max(0f, Time.unscaledTime - context.CreatedAtSeconds);
    }

    private static bool ContextMatchesCallback(
        VoiceAttemptContext context,
        string eventReferenceText
    )
    {
        if (!context.IsKnown || string.IsNullOrWhiteSpace(eventReferenceText))
            return false;

        return ContainsEither(eventReferenceText, context.EventReferenceText)
            || ContainsEither(eventReferenceText, context.EventPath);
    }

    private static bool ContainsEither(string callbackText, string? contextText)
    {
        if (string.IsNullOrWhiteSpace(contextText))
            return false;

        return callbackText.IndexOf(contextText!, StringComparison.OrdinalIgnoreCase) >= 0
            || contextText!.IndexOf(callbackText, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static VoiceSubtitleLookupResult ResolveLine(
        string lookupText,
        string sourceLabel,
        string hookName
    )
    {
        try
        {
            return _callbacks.ResolveLine(
                new VoiceSubtitleLookupRequest(lookupText, sourceLabel, hookName)
            );
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn($"Voice subtitle lookup failed: {ex.Message}");
            return VoiceSubtitleLookupResult.Empty;
        }
    }

    private static bool IsEnabled()
    {
        try
        {
            return _callbacks.IsEnabled();
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn($"Voice subtitle enabled check failed: {ex.Message}");
            return false;
        }
    }

    private static void QueueShow(VoiceSubtitlePlaybackCue cue)
    {
        try
        {
            _callbacks.QueueShow(cue);
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn($"Voice subtitle queue failed: {ex.Message}");
        }
    }

    private static void QueueHideCurrent(string reason)
    {
        try
        {
            _callbacks.QueueHideCurrent(reason);
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn($"Voice subtitle hide queue failed: {ex.Message}");
        }
    }

    private static bool HasResolvedLine(VoiceSubtitleLookupResult resolution)
    {
        return resolution.HasLine && !string.IsNullOrEmpty(resolution.Line.Stem);
    }

    internal readonly struct VoiceAttemptContext
    {
        public static readonly VoiceAttemptContext Unknown = new(
            0,
            null,
            "Unknown",
            "Unknown",
            "Unknown",
            null,
            null,
            0f,
            0f
        );

        public VoiceAttemptContext(
            int attemptId,
            VOPlayer? player,
            string origin,
            string sourceLabel,
            string hookName,
            string? eventReferenceText,
            string? eventPath,
            float durationSeconds,
            float createdAtSeconds
        )
        {
            AttemptId = attemptId;
            Player = player;
            Origin = origin;
            SourceLabel = sourceLabel;
            HookName = hookName;
            EventReferenceText = eventReferenceText;
            EventPath = eventPath;
            DurationSeconds = durationSeconds;
            CreatedAtSeconds = createdAtSeconds;
        }

        public int AttemptId { get; }

        public VOPlayer? Player { get; }

        public string Origin { get; }

        public string SourceLabel { get; }

        public string HookName { get; }

        public string? EventReferenceText { get; }

        public string? EventPath { get; }

        public float DurationSeconds { get; }

        public float CreatedAtSeconds { get; }

        public bool IsKnown => AttemptId > 0;
    }
}
