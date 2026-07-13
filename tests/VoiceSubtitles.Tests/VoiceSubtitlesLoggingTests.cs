using BazaarPlusPlus.Game.VoiceSubtitles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BepInEx.Logging;
using Xunit;

namespace VoiceSubtitles.Tests;

public sealed class VoiceSubtitlesLoggingTests
{
    [Fact]
    public void Display_event_catalog_matches_the_locked_manifest()
    {
        var actual = typeof(VoiceSubtitlesDisplayLogEvents)
            .GetFields(
                System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.DeclaredOnly
            )
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .Select(field => (BppLogEventDefinition)field.GetValue(null)!)
            .ToDictionary(definition => definition.EventId, DescribeFields, StringComparer.Ordinal);

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["voice_subtitles.display.hidden"] =
                "display_id:Public:High:Full|attempt_id:Public:High:Full|stem:Public:High:None|reason_code:Public:Low:None|elapsed_ms:Public:High:None|playback_state:UntrustedText:Low:None",
            ["voice_subtitles.display.rendered"] =
                "display_id:Public:High:Full|attempt_id:Public:High:Full|stem:Public:High:None|event_duration_ms:Public:High:None|line_duration_ms:Public:High:None|display_duration_ms:Public:High:None|renderer:UntrustedText:High:None|active_before:Public:Low:None|playback_state:UntrustedText:Low:None|english_text:UntrustedText:High:None|chinese_text:UntrustedText:High:None",
            ["voice_subtitles.display.skipped"] =
                "display_id:Public:High:Full|attempt_id:Public:High:Full|stem:Public:High:None|reason_code:Public:Low:None",
            ["voice_subtitles.font.failed"] = "reason_code:Public:Low:None",
            ["voice_subtitles.font.selected"] =
                "font_name:UntrustedText:High:None|resolved_names:UntrustedText:High:None|candidate_names:UntrustedText:High:None",
            ["voice_subtitles.font_environment.observed"] =
                "reason_code:Public:Low:None|anchor_path:UntrustedText:High:None|source_font:UntrustedText:High:None|source_coverage:Public:Low:None|default_font:UntrustedText:High:None|fallback_fonts:UntrustedText:High:None",
            ["voice_subtitles.font_inventory.observed"] =
                "font_count:Public:High:None|fonts:UntrustedText:High:None",
            ["voice_subtitles.mount_anchor.selected"] =
                "anchor_path:UntrustedText:High:None|label_text:UntrustedText:High:None",
            ["voice_subtitles.overlay.failed"] =
                "stage:Public:Low:None|anchor_path:UntrustedText:High:Hash|anchor_text:UntrustedText:High:None|reason_code:Public:Low:None",
            ["voice_subtitles.overlay.mounted"] =
                "renderer:UntrustedText:High:None|anchor_path:UntrustedText:High:None",
            ["voice_subtitles.playback_tracking.degraded"] =
                "display_id:Public:High:Full|attempt_id:Public:High:Full|reason_code:Public:Low:None",
            ["voice_subtitles.settings.degraded"] =
                "phase:Public:Low:None|reason_code:Public:Low:None",
            ["voice_subtitles.settings.recovered"] = "phase:Public:Low:None",
        };

        Assert.Equal(expected, actual);
        Assert.Empty(VoiceSubtitlesDisplayLogEvents.OverlayFailed.StormPolicy!.KeyFields);
        Assert.Equal(
            ["phase", "reason_code"],
            VoiceSubtitlesDisplayLogEvents.SettingsDegraded.StormPolicy!.KeyFields.Select(field =>
                field.Name
            )
        );
        Assert.Equal(
            ["reason_code"],
            VoiceSubtitlesDisplayLogEvents.PlaybackTrackingDegraded.StormPolicy!.KeyFields.Select(
                field => field.Name
            )
        );
    }

    [Fact]
    public void Settings_degradation_is_one_warning_episode_with_one_later_recovery()
    {
        using var capture = new LogCapture();
        var state = new VoiceSubtitlesSettingsLogState();

        state.ReportDegraded(
            VoiceSubtitlesSettingsPhase.Mount,
            new InvalidOperationException("first")
        );
        state.ReportDegraded(
            VoiceSubtitlesSettingsPhase.Mount,
            new InvalidOperationException("repeat")
        );
        state.ReportSucceeded(VoiceSubtitlesSettingsPhase.Mount);
        state.ReportSucceeded(VoiceSubtitlesSettingsPhase.Mount);

        Assert.Equal(1, capture.Count("event=voice_subtitles.settings.degraded"));
        Assert.Equal(1, capture.Count("event=voice_subtitles.settings.recovered"));
        Assert.Equal(2, capture.Count("phase=mount"));
        Assert.True(capture.Contains("reason_code=settings_apply_exception"));

        state.ReportDegraded(
            VoiceSubtitlesSettingsPhase.Show,
            new InvalidOperationException("new episode")
        );

        Assert.Equal(2, capture.Count("event=voice_subtitles.settings.degraded"));
    }

    [Fact]
    public void Font_failure_is_one_error_for_the_process_owner()
    {
        using var capture = new LogCapture();
        var state = new VoiceSubtitlesFontFailureLogState();

        state.Report(VoiceSubtitlesLogReasonCode.FontUnavailable);
        state.Report(
            VoiceSubtitlesLogReasonCode.FontCreationException,
            new InvalidOperationException("repeated")
        );

        Assert.Equal(1, capture.Count("event=voice_subtitles.font.failed"));
        Assert.True(capture.Contains("reason_code=font_unavailable"));
        Assert.Null(VoiceSubtitlesDisplayLogEvents.FontFailed.StormPolicy);
    }

    private static string DescribeFields(BppLogEventDefinition definition) =>
        string.Join(
            "|",
            definition.Fields.Select(field =>
                $"{field.Name}:{field.Privacy}:{field.Cardinality}:{field.Correlation}"
            )
        );

    private sealed class LogCapture : IDisposable
    {
        private readonly ManualLogSource _source = new("VoiceSubtitles.Tests");
        private readonly List<LogEventArgs> _events = new();

        internal LogCapture()
        {
            _source.LogEvent += OnLogEvent;
            BppLog.Install(_source);
        }

        internal int Count(string text) =>
            _events.Count(entry =>
                entry.Data?.ToString()?.Contains(text, StringComparison.Ordinal) == true
            );

        internal bool Contains(string text) =>
            _events.Any(entry =>
                entry.Data?.ToString()?.Contains(text, StringComparison.Ordinal) == true
            );

        public void Dispose()
        {
            _source.LogEvent -= OnLogEvent;
            _source.Dispose();
        }

        private void OnLogEvent(object? sender, LogEventArgs args)
        {
            _events.Add(args);
        }
    }
}
