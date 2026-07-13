using BazaarPlusPlus.Core.Config;
using BepInEx.Configuration;
using Xunit;

namespace BazaarPlusPlus.Tests.SettingsDockRegistry;

public class BppConfigTests
{
    [Fact]
    public void QuestRewardPreviewConfig_defaults_off_with_its_own_key()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-quest-reward-preview-default-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();

            config.Initialize(configFile);

            var entry = config.EnableQuestRewardPreviewConfig;
            Assert.NotNull(entry);
            Assert.False(entry.Value);
            Assert.Equal("QuestRewardPreview", entry.Definition.Section);
            Assert.Equal("Enabled", entry.Definition.Key);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }
}
