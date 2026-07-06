#nullable enable
using System;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.GameInterop.VoiceSubtitles;
using BazaarPlusPlus.Patches;
using BepInEx.Configuration;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VoiceSubtitlesModule : IBppFeature
{
    private readonly VoiceLinesRepository _repository = new();
    private ConfigEntry<bool>? _enabledConfig;

    public void Start()
    {
        VoiceLineCatalog.Reset();
        VoiceLineDisplay.Reset();
        VoiceLineVoObserverBridge.Configure(
            new VoiceSubtitleObserverCallbacks(ResolveLine, VoiceSubtitlesGate.IsEnabled, QueueShow)
        );
        _enabledConfig = BppPatchHost.Services.Config.EnableVoiceSubtitlesConfig;
        if (_enabledConfig != null)
            _enabledConfig.SettingChanged += OnEnabledSettingChanged;

        if (VoiceSubtitlesGate.IsEnabled())
            _repository.BeginLoad();
    }

    public void Stop()
    {
        if (_enabledConfig != null)
        {
            _enabledConfig.SettingChanged -= OnEnabledSettingChanged;
            _enabledConfig = null;
        }

        VoiceLineVoObserverBridge.Configure(VoiceSubtitleObserverCallbacks.Empty);
        VoiceLineDisplay.Reset();
        VoiceLineCatalog.Reset();
    }

    private void OnEnabledSettingChanged(object sender, EventArgs args)
    {
        if (VoiceSubtitlesGate.IsEnabled())
        {
            _repository.BeginLoad();
            return;
        }

        VoiceLineDisplay.Reset();
    }

    private static VoiceSubtitleLookupResult ResolveLine(VoiceSubtitleLookupRequest request)
    {
        var resolution = VoiceLineCatalog.ResolveDetailed(
            request.LookupText,
            request.SourceLabel,
            request.HookName
        );
        var line = resolution.Line;
        return new VoiceSubtitleLookupResult(
            new VoiceSubtitleLine(line.Stem, line.English, line.Chinese, line.DurationSeconds),
            hasLine: !string.IsNullOrEmpty(line.Stem),
            resolution.Strategy,
            resolution.MatchedToken,
            resolution.CatalogName,
            resolution.CandidateCount
        );
    }

    private static void QueueShow(VoiceSubtitlePlaybackCue cue)
    {
        var line = cue.Line;
        VoiceLineDisplay.QueueShow(
            new VoiceSubtitleCue(
                new VoiceLine(line.Stem, line.English, line.Chinese, line.DurationSeconds),
                cue.EventDurationSeconds,
                cue.AttemptId,
                cue.IsPlaybackStoppedOrStopping,
                cue.PlaybackStateText
            )
        );
    }
}
