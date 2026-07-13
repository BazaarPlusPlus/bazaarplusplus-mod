#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace Architecture.Tests;

public sealed class LobbyLogEventCatalogTests
{
    [Fact]
    public void Lobby_events_match_the_locked_E10_E14_manifest()
    {
        var definitions = typeof(LobbyLogEvents)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .Select(field => (BppLogEventDefinition)field.GetValue(null)!)
            .ToArray();
        var actual = definitions.ToDictionary(x => x.EventId, Describe, StringComparer.Ordinal);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["lobby.version_check.degraded"] =
                "reason_code:Public:Low:None|http_status:Public:Low:None|timeout_ms:Public:Low:None",
            ["lobby.version_check.completed"] =
                "current_version:Public:High:None|latest_version:UntrustedText:High:None|update_available:Public:Low:None",
            ["lobby.version_label.degraded"] = "reason_code:Public:Low:None",
            ["lobby.random_pool_preferences.degraded"] =
                "pool_kind:Public:Low:None|reason_code:Public:Low:None",
            ["lobby.hero_pool.degraded"] = "operation:Public:Low:None|reason_code:Public:Low:None",
            ["lobby.collectible_pool.degraded"] =
                "operation:Public:Low:None|collection_kind:Public:Low:None|reason_code:Public:Low:None",
        };

        Assert.Equal(expected.Count, actual.Count);
        foreach (var pair in expected)
            Assert.Equal(pair.Value, actual[pair.Key]);
        Assert.All(
            definitions,
            definition => Assert.Same(BppLogFeatureScope.Lobby, definition.Scope)
        );
        var validation = BppLogEventCatalog.FromDefinitions(definitions).Validate();
        Assert.True(validation.IsValid, string.Join("; ", validation.Violations));
    }

    private static string Describe(BppLogEventDefinition definition) =>
        string.Join(
            "|",
            definition.Fields.Select(field =>
                $"{field.Name}:{field.Privacy}:{field.Cardinality}:{field.Correlation}"
            )
        );
}
