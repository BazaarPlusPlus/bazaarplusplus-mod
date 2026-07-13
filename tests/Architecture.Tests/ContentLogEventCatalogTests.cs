#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.EventPreview;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.GameInterop.Localization;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Patches.NameOverride;
using Xunit;

namespace Architecture.Tests;

public sealed class ContentLogEventCatalogTests
{
    [Fact]
    public void Supporter_catalog_is_one_aggregate_degradation_episode()
    {
        var tracker =
            new OperationalHealthTracker<SupporterCatalogOperation, SupporterCatalogFailure>();
        var first = new SupporterCatalogFailure(
            SupporterCatalogSource.DiskCache,
            SupporterLogReasonCode.ReadException
        );
        var repeated = new SupporterCatalogFailure(
            SupporterCatalogSource.Remote,
            SupporterLogReasonCode.RefreshException
        );

        Assert.True(tracker.ObserveFailure(SupporterCatalogOperation.Load, first));
        Assert.False(tracker.ObserveFailure(SupporterCatalogOperation.Load, repeated));
        Assert.True(tracker.ObserveSuccess(SupporterCatalogOperation.Load, out var recovered));
        Assert.Equal(first, recovered);
        Assert.False(tracker.ObserveSuccess(SupporterCatalogOperation.Load, out _));
    }

    [Fact]
    public void Content_events_match_the_locked_E24_E31_manifest()
    {
        var definitions = Definitions(typeof(SupporterLogEvents))
            .Concat(Definitions(typeof(BilingualItemNamesLogEvents)))
            .Concat(Definitions(typeof(EventPreviewLogEvents)))
            .Concat(Definitions(typeof(NameOverrideLogEvents)))
            .ToArray();
        var actual = definitions.ToDictionary(x => x.EventId, Describe, StringComparer.Ordinal);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["supporters.catalog.degraded"] =
                "source:Public:Low:None|reason_code:Public:Low:None|cache_path:LocalPath:High:None",
            ["supporters.catalog.recovered"] =
                "source:Public:Low:None|entry_count:Public:High:None",
            ["supporters.catalog.loaded"] = "source:Public:Low:None|entry_count:Public:High:None",
            ["supporters.cache.write_degraded"] =
                "path:LocalPath:High:None|reason_code:Public:Low:None",
            ["supporters.cache.write_recovered"] = "path:LocalPath:High:None",
            ["bilingual_item_names.catalog.degraded"] =
                "locale:Public:Low:None|reason_code:Public:Low:None",
            ["bilingual_item_names.catalog.recovered"] = "locale:Public:Low:None",
            ["bilingual_item_names.catalog.loaded"] = "locale:Public:Low:None",
            ["bilingual_item_names.tooltip.degraded"] = "reason_code:Public:Low:None",
            ["name_override.value.applied"] =
                "operation:Public:Low:None|reason_code:Public:Low:None",
            ["name_override.value.skipped"] =
                "operation:Public:Low:None|reason_code:Public:Low:None",
        };

        foreach (
            var eventId in new[]
            {
                "event_preview.plans.load_failed",
                "event_preview.plans.degraded",
                "event_preview.plans.ready",
                "event_preview.plans.recovered",
            }
        )
        {
            expected[eventId] =
                "source:Public:Low:None|reason_code:Public:Low:None|event_count:Public:High:None|level_up_count:Public:High:None|template_count:Public:High:None|event_failure_count:Public:High:None|level_up_failure_count:Public:High:None|unsupported_level_up_part_count:Public:High:None|missing_template_count:Public:High:None|size_bytes:Public:High:None|load_duration_ms:Public:High:None|compile_duration_ms:Public:High:None|write_duration_ms:Public:High:None|cache_path:LocalPath:High:None";
        }

        Assert.Equal(expected.Count, actual.Count);
        foreach (var pair in expected)
            Assert.Equal(pair.Value, actual[pair.Key]);
        var validation = BppLogEventCatalog.FromDefinitions(definitions).Validate();
        Assert.True(validation.IsValid, string.Join("; ", validation.Violations));
    }

    private static IEnumerable<BppLogEventDefinition> Definitions(Type type) =>
        type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
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
