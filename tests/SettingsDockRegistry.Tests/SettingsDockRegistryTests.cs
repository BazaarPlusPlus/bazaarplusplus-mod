using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using Xunit;

namespace BazaarPlusPlus.Tests.SettingsDockRegistry;

public class SettingsDockRegistryTests
{
    private sealed class FakeEntry : ISettingsDockEntry
    {
        public string Key { get; }
        public int Order { get; init; }
        public int BuildCount { get; private set; }

        public FakeEntry(string key)
        {
            Key = key;
        }

        public BppSettingsDockDefinition Build(IBppConfig config)
        {
            BuildCount++;
            return new BppSettingsDockDefinition(
                Key,
                resolveLabel: _ => $"Label-{Key}",
                resolveStatus: _ => "ON",
                isActive: () => true,
                activate: () => { },
                collapseAfterActivate: false
            );
        }
    }

    [Fact]
    public void MaterializeAll_returns_one_definition_per_registered_entry()
    {
        var registry = new SettingsDockEntryRegistry();
        var a = new FakeEntry("A");
        var b = new FakeEntry("B");

        registry.Register(a);
        registry.Register(b);

        var defs = registry.MaterializeAll(config: null!);

        Assert.Equal(2, defs.Count);
        Assert.Contains(defs, d => d.Key == "A");
        Assert.Contains(defs, d => d.Key == "B");
        Assert.Equal(1, a.BuildCount);
        Assert.Equal(1, b.BuildCount);
    }

    [Fact]
    public void Register_null_throws()
    {
        var registry = new SettingsDockEntryRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void MaterializeWithOrder_returns_entries_paired_with_their_Order()
    {
        var registry = new SettingsDockEntryRegistry();
        registry.Register(new FakeEntry("A") { Order = 5 });
        registry.Register(new FakeEntry("B") { Order = 1 });

        var pairs = registry.MaterializeWithOrder(config: null!);

        Assert.Equal(2, pairs.Count);
        // Materialization preserves registration order; sorting is the caller's job.
        Assert.Equal(5, pairs[0].Order);
        Assert.Equal("A", pairs[0].Definition.Key);
        Assert.Equal(1, pairs[1].Order);
        Assert.Equal("B", pairs[1].Definition.Key);
    }

    [Theory]
    [InlineData(0, "zh-CN", "按键显示")]
    [InlineData(1, "zh-CN", "智能切换")]
    [InlineData(2, "zh-CN", "常驻显示")]
    [InlineData(0, "en", "OFF")]
    [InlineData(1, "en", "AUTO")]
    [InlineData(2, "en", "ON")]
    public void ResolvePreviewVisibilityModeStatus_returns_localized_dock_status(
        int modeValue,
        string languageCode,
        string expected
    )
    {
        var mode = (PreviewVisibilityMode)modeValue;
        var result = BppSettingsDockCatalog.ResolvePreviewVisibilityModeStatus(mode, languageCode);

        Assert.Equal(expected, result);
    }
}
