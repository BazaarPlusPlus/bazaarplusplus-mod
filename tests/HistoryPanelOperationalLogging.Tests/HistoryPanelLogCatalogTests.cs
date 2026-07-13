#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace HistoryPanelOperationalLogging.Tests;

public sealed class HistoryPanelLogCatalogTests
{
    [Fact]
    public void History_events_match_the_locked_D28_through_D42_schemas()
    {
        var actual = Definitions().ToDictionary(item => item.EventId, Describe);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["history_panel.mount.failed"] =
                "dependency:Public:Low:None|reason_code:Public:Low:None",
            ["history_panel.data.load_failed"] = "dataset:Public:Low:None|run_id:Public:High:Short",
            ["history_panel.replay.preflight_completed"] =
                "request_id:Public:High:Short|battle_id:Public:High:Short|record_video:Public:Low:None|can_record:Public:Low:None|reason_code:Public:Low:None",
            ["history_panel.replay.failed"] =
                "request_id:Public:High:Short|battle_id:Public:High:Short|record_video:Public:Low:None|reason_code:Public:Low:None",
            ["history_panel.replay.accepted"] =
                "request_id:Public:High:Short|battle_id:Public:High:Short|record_video:Public:Low:None",
            ["history_panel.run_delete.failed"] = DeleteSchema,
            ["history_panel.run_delete.degraded"] = DeleteSchema,
            ["history_panel.run_delete.succeeded"] = DeleteSchema,
            ["history_panel.server_health.failed"] = HealthSchema,
            ["history_panel.server_health.succeeded"] = HealthSchema,
            ["history_panel.ghost_sync.failed"] = SyncSchema,
            ["history_panel.ghost_sync.succeeded"] = SyncSchema,
            ["history_panel.ghost_identity.read_failed"] = "reason_code:Public:Low:None",
            ["history_panel.preview.socket_effect_degraded"] =
                "template_id:Public:High:None|reason_code:Public:Low:None",
            ["history_panel.preview.static_data_degraded"] = "reason_code:Public:Low:None",
            ["history_panel.row.skipped"] =
                "battle_id:Public:High:Short|reason_code:Public:Low:None",
            ["history_panel.open.failed"] = "reason_code:Public:Low:None",
            ["history_panel.open.skipped"] = "reason_code:Public:Low:None",
        };

        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected, actual);
        Assert.All(
            Definitions(),
            definition => Assert.Same(BppLogFeatureScope.HistoryPanel, definition.Scope)
        );
    }

    [Fact]
    public void Only_preview_and_row_degradations_have_reason_only_storm_keys()
    {
        var stormed = Definitions()
            .Where(definition => definition.StormPolicy != null)
            .ToDictionary(definition => definition.EventId);

        Assert.Equal(
            new[]
            {
                "history_panel.preview.socket_effect_degraded",
                "history_panel.preview.static_data_degraded",
                "history_panel.row.skipped",
            },
            stormed.Keys.OrderBy(value => value, StringComparer.Ordinal)
        );
        Assert.All(
            stormed.Values,
            definition =>
            {
                var key = Assert.Single(definition.StormPolicy!.KeyFields);
                Assert.Equal("reason_code", key.Name);
                Assert.Equal(BppLogCardinality.Low, key.Cardinality);
                Assert.Equal(BppLogCorrelationPolicy.None, key.Correlation);
            }
        );
    }

    [Fact]
    public void Event_source_is_discoverable_and_valid()
    {
        Assert.NotNull(
            typeof(HistoryPanelLogEvents).GetCustomAttribute<BppLogEventSourceAttribute>()
        );
        var validation = BppLogEventCatalog.FromDefinitions(Definitions().ToArray()).Validate();
        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Violations.Select(violation => violation.ToString()))
        );
    }

    private const string DeleteSchema =
        "request_id:Public:High:Short|run_id:Public:High:Short|battle_count:Public:High:None|cleanup_failed_count:Public:High:None|reason_code:Public:Low:None";
    private const string HealthSchema =
        "request_id:Public:High:Short|duration_ms:Public:High:None|reason_code:Public:Low:None";
    private const string SyncSchema =
        "request_id:Public:High:Short|imported_count:Public:High:None|reason_code:Public:Low:None";

    private static IEnumerable<BppLogEventDefinition> Definitions() =>
        typeof(HistoryPanelLogEvents)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .Select(field => (BppLogEventDefinition)field.GetValue(null)!);

    private static string Describe(BppLogEventDefinition definition) =>
        string.Join(
            "|",
            definition.Fields.Select(field =>
                $"{field.Name}:{field.Privacy}:{field.Cardinality}:{field.Correlation}"
            )
        );
}
