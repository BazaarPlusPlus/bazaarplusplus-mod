#nullable enable

using System.Reflection;
using BazaarPlusPlus.Game.VoiceSubtitles;
using BazaarPlusPlus.GameInterop.VoiceSubtitles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Patches.VoiceSubtitles;
using BepInEx.Logging;
using Xunit;

namespace VoiceSubtitles.Tests;

public sealed class VoiceSubtitlesEventCatalogTests
{
    private static readonly Type[] EventSources =
    [
        typeof(VoiceCatalogLogEvents),
        typeof(VoiceSubtitlesDisplayLogEvents),
        typeof(VoiceObserverLogEvents),
        typeof(VoiceSubtitleDisplayLogEvents),
        typeof(VoicePatchLogEvents),
    ];

    [Fact]
    public void Event_catalog_has_exactly_34_unique_voice_subtitle_definitions()
    {
        var definitions = EventSources.SelectMany(Definitions).ToArray();

        Assert.Equal(34, definitions.Length);
        Assert.Equal(
            34,
            definitions
                .Select(definition => definition.EventId)
                .Distinct(StringComparer.Ordinal)
                .Count()
        );
        Assert.All(
            definitions,
            definition => Assert.Equal(BppLogFeatureScope.VoiceSubtitles, definition.Scope)
        );
    }

    [Fact]
    public void Observer_patch_and_shared_display_schemas_match_the_locked_manifest()
    {
        var actual = new[]
        {
            typeof(VoiceObserverLogEvents),
            typeof(VoiceSubtitleDisplayLogEvents),
            typeof(VoicePatchLogEvents),
        }
            .SelectMany(Definitions)
            .ToDictionary(definition => definition.EventId, DescribeFields, StringComparer.Ordinal);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["voice_subtitles.observer.installed"] = "player_instance:Public:High:None",
            ["voice_subtitles.attempt.started"] =
                "attempt_id:Public:High:Full|origin:Public:Low:None|player_instance:Public:High:None|source:Public:Low:None|hook:Public:Low:None|event_ref:UntrustedText:High:None|event_path:UntrustedText:High:None|event_duration_ms:Public:High:None",
            ["voice_subtitles.observer.degraded"] =
                "reason_code:Public:Low:None|origin:Public:Low:None|hook:Public:Low:None|event_ref:UntrustedText:High:None|callback_event:UntrustedText:High:None",
            ["voice_subtitles.attempt.cleared"] =
                "attempt_id:Public:High:Full|reason_code:Public:Low:None|age_ms:Public:High:None",
            ["voice_subtitles.sound.observed"] =
                "attempt_id:Public:High:Full|player_instance:Public:High:None|context_player_instance:Public:High:None|source:Public:Low:None|hook:Public:Low:None|sound_name:UntrustedText:High:None|sound_duration_ms:Public:High:None|event_path:UntrustedText:High:None",
            ["voice_subtitles.lookup.skipped"] =
                "attempt_id:Public:High:Full|origin:Public:Low:None|strategy:Public:Low:None|hook:Public:Low:None|sound_name:UntrustedText:High:None|reason_code:Public:Low:None",
            ["voice_subtitles.lookup.resolved"] =
                "attempt_id:Public:High:Full|origin:Public:Low:None|strategy:Public:Low:None|catalog:Public:Low:None|matched_token:UntrustedText:High:None|candidate_count:Public:High:None|stem:Public:High:None|sound_duration_ms:Public:High:None|event_duration_ms:Public:High:None|line_duration_ms:Public:High:None|display_duration_ms:Public:High:None|english_text:UntrustedText:High:None|chinese_text:UntrustedText:High:None",
            ["voice_subtitles.attempt.stopped"] =
                "attempt_id:Public:High:Full|player_instance:Public:High:None|age_ms:Public:High:None",
            ["voice_subtitles.callback.observed"] =
                "attempt_id:Public:High:Full|origin:Public:Low:None|age_ms:Public:High:None|source:Public:Low:None|hook:Public:Low:None|context_matches_callback:Public:Low:None|callback_event:UntrustedText:High:None|context_event_ref:UntrustedText:High:None|context_event_path:UntrustedText:High:None",
            ["voice_subtitles.lookup.failed"] =
                "attempt_id:Public:High:Full|reason_code:Public:Low:None",
            ["voice_subtitles.gate.degraded"] = "reason_code:Public:Low:None",
            ["voice_subtitles.display.failed"] =
                "display_id:Public:High:Full|attempt_id:Public:High:Full|stem:Public:High:None|reason_code:Public:Low:None",
            ["voice_subtitles.observer.failed"] = "reason_code:Public:Low:None",
            ["voice_subtitles.callback_patch.degraded"] =
                "reason_code:Public:Low:None|actual_count:Public:High:None|expected_count:Public:Low:None",
            ["voice_subtitles.callback_patch.ready"] =
                "actual_count:Public:High:None|expected_count:Public:Low:None",
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Debug_field_factory_is_not_evaluated_in_release()
    {
        using var capture = new LogCapture();
        var evaluated = false;

        BppLog.DebugEvent(
            VoiceCatalogLogEvents.CatalogStarted,
            () =>
            {
                evaluated = true;
                return [];
            }
        );

#if DEBUG
        Assert.True(evaluated);
        Assert.Contains("event=voice_subtitles.catalog.started", capture.Joined);
#else
        Assert.False(evaluated);
        Assert.DoesNotContain("event=voice_subtitles.catalog.started", capture.Joined);
#endif
    }

    [Fact]
    public void Structured_exception_projection_hides_multiline_path_and_url_text()
    {
        using var capture = new LogCapture();

        BppLog.ErrorEvent(
            VoiceSubtitleDisplayLogEvents.DisplayFailed,
            new InvalidOperationException(
                "private line\n/Users/example/private.json https://secret.example/catalog"
            ),
            VoiceSubtitleDisplayLogEvents.DisplayId.Bind(7),
            VoiceSubtitleDisplayLogEvents.AttemptId.Bind(9),
            VoiceSubtitleDisplayLogEvents.Stem.Bind("中文台词"),
            VoiceSubtitleDisplayLogEvents.ReasonCode.Bind(
                VoiceSubtitleDisplayLogReasonCode.QueueFailed
            )
        );

        Assert.Contains("stem=中文台词", capture.Joined);
        Assert.DoesNotContain("/Users/example", capture.Joined);
        Assert.DoesNotContain("secret.example", capture.Joined);
        Assert.DoesNotContain('\n', capture.Joined);
    }

    private static IEnumerable<BppLogEventDefinition> Definitions(Type source) =>
        source
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .Select(field => (BppLogEventDefinition)field.GetValue(null)!);

    private static string DescribeFields(BppLogEventDefinition definition) =>
        string.Join(
            "|",
            definition.Fields.Select(field =>
                $"{field.Name}:{field.Privacy}:{field.Cardinality}:{field.Correlation}"
            )
        );

    private sealed class LogCapture : IDisposable
    {
        private readonly ManualLogSource _source = new("VoiceSubtitlesEventCatalog.Tests");
        private readonly List<string> _events = [];

        internal LogCapture()
        {
            _source.LogEvent += OnLogEvent;
            BppLog.Install(_source);
        }

        internal string Joined => string.Join("\n", _events);

        public void Dispose()
        {
            BppLog.Flush();
            _source.LogEvent -= OnLogEvent;
            _source.Dispose();
        }

        private void OnLogEvent(object? sender, LogEventArgs args)
        {
            _events.Add(args.Data?.ToString() ?? string.Empty);
        }
    }
}
