#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace Architecture.Tests;

public sealed class SettingsLogEventCatalogTests
{
    [Fact]
    public void Settings_events_match_the_locked_E15_E23_E28_manifest()
    {
        var definitions = Definitions(typeof(SettingsLogEvents))
            .Concat(Definitions(typeof(CombatStatusBarLogEvents)))
            .ToArray();
        var actual = definitions.ToDictionary(x => x.EventId, Describe, StringComparer.Ordinal);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["combat_status_bar.config.loaded"] =
                "enabled:Public:Low:None|speed_multiplier:Public:Low:None",
            ["settings.hotkey.degraded"] =
                "action_id:Public:Low:None|binding_path:UntrustedText:High:Hash|reason_code:Public:Low:None",
            ["settings.hotkey.modifier_disagreement_observed"] =
                "binding_path:UntrustedText:High:Hash|legacy_pressed:Public:Low:None|action_pressed:Public:Low:None",
            ["settings.dock_sprite.degraded"] =
                "reason_code:Public:Low:None|resource_id:Public:Low:None",
            ["settings.native_section.degraded"] =
                "stage:Public:Low:None|reason_code:Public:Low:None",
            ["settings.native_section.recovered"] = "stage:Public:Low:None",
            ["settings.native_section.layout_observed"] =
                "operation:Public:Low:None|outcome:Public:Low:None|affected_count:Public:High:None|growth_units:Public:High:None",
            ["settings.native_section.layout_degraded"] =
                "operation:Public:Low:None|reason_code:Public:Low:None",
            ["settings.native_section.layout_recovered"] = "operation:Public:Low:None",
            ["settings.native_button.cloned"] = "button_id:Public:Low:None",
            ["settings.keybind_rows.degraded"] =
                "stage:Public:Low:None|reason_code:Public:Low:None",
            ["settings.patch.degraded"] = "operation:Public:Low:None|reason_code:Public:Low:None",
            ["settings.row.layout_applied"] =
                "layout_mode:Public:Low:None|row_id:Public:Low:None|additional_index:Public:High:None|step_px:Public:High:None|position_x_px:Public:High:None|position_y_px:Public:High:None",
            ["settings.ui_font.loaded"] = "font_kind:Public:Low:None|path:LocalPath:High:None",
            ["settings.ui_font.degraded"] =
                "font_kind:Public:Low:None|reason_code:Public:Low:None|path:LocalPath:High:None",
        };

        Assert.Equal(expected.Count, actual.Count);
        foreach (var pair in expected)
            Assert.Equal(pair.Value, actual[pair.Key]);
        var validation = BppLogEventCatalog.FromDefinitions(definitions).Validate();
        Assert.True(validation.IsValid, string.Join("; ", validation.Violations));
    }

    [Fact]
    public void Settings_warning_keys_are_closed_low_cardinality_fields()
    {
        AssertStorm(SettingsLogEvents.HotkeyDegraded, "reason_code");
        AssertStorm(SettingsLogEvents.DockSpriteDegraded, "reason_code");
        AssertStorm(SettingsLogEvents.NativeSectionDegraded, "stage", "reason_code");
        AssertStorm(SettingsLogEvents.NativeSectionLayoutDegraded, "operation", "reason_code");
        AssertStorm(SettingsLogEvents.KeybindRowsDegraded, "reason_code");
        AssertStorm(SettingsLogEvents.PatchDegraded, "operation", "reason_code");
        AssertStorm(SettingsLogEvents.UiFontDegraded, "font_kind", "reason_code");
    }

    [Fact]
    public void Health_tracker_recovers_only_the_same_operation_once()
    {
        var tracker = new OperationalHealthTracker<string, string>();

        Assert.True(tracker.ObserveFailure("footer", "missing"));
        Assert.False(tracker.ObserveFailure("footer", "different"));
        Assert.True(tracker.ObserveFailure("buffer", "missing"));

        Assert.True(tracker.ObserveSuccess("footer", out var footerReason));
        Assert.Equal("missing", footerReason);
        Assert.False(tracker.ObserveSuccess("footer", out _));
        Assert.True(tracker.ObserveSuccess("buffer", out var bufferReason));
        Assert.Equal("missing", bufferReason);

        Assert.True(tracker.ObserveFailure("footer", "missing"));
        tracker.Reset();
        Assert.True(tracker.ObserveFailure("footer", "missing"));
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

    private static void AssertStorm(BppLogEventDefinition definition, params string[] fields)
    {
        var policy = Assert.IsType<BppLogStormPolicy>(definition.StormPolicy);
        Assert.Equal(fields, policy.KeyFields.Select(field => field.Name));
        Assert.All(
            policy.KeyFields,
            field => Assert.Equal(BppLogCardinality.Low, field.Cardinality)
        );
    }
}
