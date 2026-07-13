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

    [Fact]
    public void UiFontConfig_defaults_to_sans_serif_and_uses_appearance_key()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-ui-font-default-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();

            config.Initialize(configFile);

            var entry = config.UiFontKindConfig;
            Assert.NotNull(entry);
            Assert.Equal(BppConfig.DefaultUiFontKind, entry.Value);
            Assert.Equal(BppUiFontKind.SansSerif, entry.Value);
            Assert.Equal("Appearance", entry.Definition.Section);
            Assert.Equal("UiFont", entry.Definition.Key);
            Assert.Contains(
                "Font for BazaarPlusPlus panels and preview tooltip sections",
                entry.Description.Description
            );
            Assert.Contains(
                "SansSerif = Unity built-in sans with OS fallback for CJK",
                entry.Description.Description
            );
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void UiFontConfig_persists_sans_serif_choice()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-ui-font-persistence-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);

            config.UiFontKindConfig!.Value = BppUiFontKind.SansSerif;
            configFile.Save();

            var reloadedConfigFile = new ConfigFile(configPath, saveOnInit: false);
            var reloadedConfig = new BppConfig();
            reloadedConfig.Initialize(reloadedConfigFile);

            Assert.Equal(BppUiFontKind.SansSerif, reloadedConfig.UiFontKindConfig!.Value);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }
}
