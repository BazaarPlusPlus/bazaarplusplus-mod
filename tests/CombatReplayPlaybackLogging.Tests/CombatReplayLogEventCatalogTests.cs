#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace CombatReplayPlaybackLogging.Tests;

public sealed class CombatReplayLogEventCatalogTests
{
    private const string PlaybackTerminalSchema =
        "battle_id:Public:High:Short|source:Public:Low:None|end_reason_code:Public:Low:None|duration_ms:Public:High:None|reason_code:Public:Low:None|degradation_count:Public:Low:None|rollback_status:Public:Low:None";

    [Fact]
    public void Playback_persistence_and_warmup_events_match_the_locked_manifest_schemas()
    {
        var actual = Definitions()
            .ToDictionary(definition => definition.EventId, DescribeFields, StringComparer.Ordinal);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["combat_replay.capture.failed"] =
                "run_id:Public:High:Short|reason_code:Public:Low:None",
            ["combat_replay.current_recording_ui.observed"] =
                "phase:Public:Low:None|snapshot_visible:Public:Low:None|layout_available:Public:Low:None|layout_reason_code:Public:Low:None|clone_active:Public:Low:None|native_replay_bound:Public:Low:None|icon_available:Public:Low:None",
            ["combat_replay.playback.request_rejected"] =
                "source:Public:Low:None|reason_code:Public:Low:None|battle_id:Public:High:Short",
            ["combat_replay.external_record.accepted"] =
                "request_id:Public:High:Short|battle_id:Public:High:Short|source:Public:Low:None",
            ["combat_replay.playback.started"] =
                "battle_id:Public:High:Short|source:Public:Low:None|record_video:Public:Low:None",
            ["combat_replay.playback.succeeded"] = PlaybackTerminalSchema,
            ["combat_replay.playback.degraded"] = PlaybackTerminalSchema,
            ["combat_replay.playback.failed"] = PlaybackTerminalSchema,
            ["combat_replay.persistence.failed"] =
                "battle_id:Public:High:Short|run_id:Public:High:Short|reason_code:Public:Low:None",
            ["combat_replay.persistence.succeeded"] =
                "battle_id:Public:High:Short|run_id:Public:High:Short|reason_code:Public:Low:None",
            ["combat_replay.persistence.orphan_cleanup_degraded"] =
                "reason_code:Public:Low:None|failed_count:Public:High:None",
            ["combat_replay.persistence.shutdown_incomplete"] =
                "pending_count:Public:High:None|in_flight:Public:Low:None|timeout_ms:Public:High:None",
            ["combat_replay.persistence.rollback_cleanup_failed"] = "battle_id:Public:High:Short",
            ["combat_replay.playback.cleanup_observed"] =
                "stage:Public:Low:None|removed_count:Public:High:None|battle_id:Public:High:Short",
            ["combat_replay.native_pvp_presentation.observed"] =
                "battle_id:Public:High:Short|has_stash_id:Public:Low:None|has_bank_id:Public:Low:None|collection_count:Public:High:None|stash_loaded:Public:Low:None|stash_active:Public:Low:None|bank_loaded:Public:Low:None|bank_active:Public:Low:None|portrait_loaded:Public:Low:None|portrait_active:Public:Low:None|portrait_anchored:Public:Low:None",
            ["combat_replay.warmup.completed"] =
                "stage:Public:Low:None|battle_id:Public:High:Short|duration_ms:Public:High:None|board_bank_loaded_count:Public:High:None|board_bank_already_loaded_count:Public:High:None|board_bank_failed_count:Public:High:None|board_bank_skipped_count:Public:High:None|soundtrack_bank_loaded_count:Public:High:None|soundtrack_bank_already_loaded_count:Public:High:None|soundtrack_bank_failed_count:Public:High:None|soundtrack_bank_skipped_count:Public:High:None|shared_asset_preloaded_count:Public:High:None|shared_asset_skipped_count:Public:High:None|card_preloaded_count:Public:High:None|card_skipped_count:Public:High:None|card_failed_count:Public:High:None|override_asset_preloaded_count:Public:High:None|override_asset_skipped_count:Public:High:None|override_asset_failed_count:Public:High:None|vfx_prewarmed_count:Public:High:None|vfx_skipped_count:Public:High:None|vfx_failed_count:Public:High:None",
            ["combat_replay.warmup.asset_skipped"] =
                "stage:Public:Low:None|asset_key:UntrustedText:High:None|reason_code:Public:Low:None",
        };

        Assert.Equal(expected.Count, actual.Count);
        foreach (var (eventId, schema) in expected)
        {
            Assert.True(actual.TryGetValue(eventId, out var actualSchema), $"Missing {eventId}.");
            Assert.Equal(schema, actualSchema);
        }

        Assert.All(
            Definitions(),
            definition => Assert.Same(BppLogFeatureScope.CombatReplay, definition.Scope)
        );
    }

    [Fact]
    public void Event_source_is_discoverable_and_every_definition_is_a_direct_readonly_field()
    {
        var source = typeof(CombatReplayLogEvents);
        Assert.NotNull(source.GetCustomAttribute<BppLogEventSourceAttribute>());

        var fields = source
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .ToArray();
        Assert.NotEmpty(fields);
        Assert.All(
            fields,
            field => Assert.True(field.IsInitOnly, $"{field.Name} must be readonly.")
        );

        var validation = BppLogEventCatalog
            .FromDefinitions(
                fields.Select(field => (BppLogEventDefinition)field.GetValue(null)!).ToArray()
            )
            .Validate();
        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Violations.Select(violation => violation.ToString()))
        );
    }

    [Fact]
    public void Orphan_cleanup_storm_key_is_only_the_low_cardinality_reason()
    {
        var policy = Assert.IsType<BppLogStormPolicy>(
            CombatReplayLogEvents.OrphanCleanupDegraded.StormPolicy
        );
        var key = Assert.Single(policy.KeyFields);
        Assert.Same(CombatReplayLogEvents.OrphanCleanupReasonCode, key);
        Assert.Equal(BppLogCardinality.Low, key.Cardinality);
        Assert.Equal(BppLogCorrelationPolicy.None, key.Correlation);
    }

    private static IEnumerable<BppLogEventDefinition> Definitions() =>
        typeof(CombatReplayLogEvents)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .Select(field => (BppLogEventDefinition)field.GetValue(null)!);

    private static string DescribeFields(BppLogEventDefinition definition) =>
        string.Join(
            "|",
            definition.Fields.Select(field =>
                string.Join(":", field.Name, field.Privacy, field.Cardinality, field.Correlation)
            )
        );
}
