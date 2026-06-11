using System.Runtime.CompilerServices;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.CardArtReplacement;
using BazaarPlusPlus.GameInterop.CardArtReplacement;
using BepInEx.Configuration;
using UnityEngine;
using Xunit;

namespace BazaarPlusPlus.Tests.CardArtReplacement;

public sealed class CardArtReplacementTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        $"bpp-card-art-{Guid.NewGuid():N}"
    );

    public CardArtReplacementTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Catalog_maps_guid_jpg_filenames_and_ignores_png_and_non_guid_files()
    {
        var templateId = Guid.NewGuid();
        var jpgPath = Path.Combine(_tempDir, $"{templateId}.jpg");
        File.WriteAllBytes(jpgPath, Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(_tempDir, $"{Guid.NewGuid()}.png"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(_tempDir, "not-a-guid.jpg"), Array.Empty<byte>());

        var catalog = new CustomCardArtCatalog(_tempDir);

        Assert.Equal(1, catalog.Count);
        Assert.True(catalog.TryGetArtPath(templateId, out var actualPath));
        Assert.Equal(jpgPath, actualPath);
    }

    [Fact]
    public void Catalog_refresh_picks_up_new_placeholder_files()
    {
        var catalog = new CustomCardArtCatalog(_tempDir);
        var templateId = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(_tempDir, $"{templateId}.jpg"), Array.Empty<byte>());

        catalog.Refresh();

        Assert.True(catalog.TryGetArtPath(templateId, out _));
    }

    [Fact]
    public void Texture_cache_loads_jpg_once_per_template_id()
    {
        var templateId = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(_tempDir, $"{templateId}.jpg"), OnePixelImageBytes);
        var catalog = new CustomCardArtCatalog(_tempDir);
        var fakeTexture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
        var loadCount = 0;
        var cache = new CustomCardArtTextureCache(
            catalog,
            (string _, Guid __, out Texture2D? texture) =>
            {
                loadCount++;
                texture = fakeTexture;
                return true;
            }
        );

        Assert.True(cache.TryGetTexture(templateId, out var first, out var firstPath));
        Assert.Same(fakeTexture, first);
        Assert.True(cache.TryGetTexture(templateId, out var second, out var secondPath));
        Assert.Same(first, second);
        Assert.Equal(1, loadCount);
        Assert.Equal(firstPath, secondPath);
        Assert.NotNull(firstPath);
    }

    [Fact]
    public void Main_assembly_embeds_default_placeholder_package_art()
    {
        var resources = typeof(CardArtReplacementFeature)
            .Assembly.GetManifestResourceNames()
            .Where(name =>
                name.StartsWith("BazaarPlusPlus.Resources.CustomCardArt.", StringComparison.Ordinal)
                && name.EndsWith(
                    CustomCardArtImageFormats.Extension,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToArray();

        Assert.Equal(120, resources.Length);
        foreach (var resource in resources)
        {
            var fileName = resource.Substring("BazaarPlusPlus.Resources.CustomCardArt.".Length);
            Assert.True(CustomCardArtImageFormats.TryGetTemplateId(fileName, out _));
        }
    }

    [Fact]
    public void Bundled_installer_writes_missing_defaults_without_overwriting_custom_files()
    {
        var existingTemplateId = Guid.NewGuid();
        var missingTemplateId = Guid.NewGuid();
        var existingPath = Path.Combine(_tempDir, $"{existingTemplateId}.jpg");
        File.WriteAllBytes(existingPath, [0x01, 0x02, 0x03]);

        var resources = new[]
        {
            $"BazaarPlusPlus.Resources.CustomCardArt.{existingTemplateId}.jpg",
            $"BazaarPlusPlus.Resources.CustomCardArt.{missingTemplateId}.jpg",
        };
        var installer = new BundledCustomCardArtInstaller(
            () => resources,
            _ => new MemoryStream([0x09, 0x08, 0x07])
        );

        var result = installer.InstallMissing(_tempDir);

        Assert.Equal(2, result.ResourceCount);
        Assert.Equal(1, result.ExistingCount);
        Assert.Equal(1, result.WrittenCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal([0x01, 0x02, 0x03], File.ReadAllBytes(existingPath));
        Assert.Equal(
            [0x09, 0x08, 0x07],
            File.ReadAllBytes(Path.Combine(_tempDir, $"{missingTemplateId}.jpg"))
        );
    }

    [Fact]
    public void Package_identity_accepts_runtime_hidden_tag()
    {
        var card = new BazaarGameClient.Domain.Models.Cards.Card
        {
            TemplateId = Guid.NewGuid(),
            HiddenTags = new HashSet<EHiddenTag> { EHiddenTag.Package },
        };

        Assert.True(CardArtInjector.IsPackageCard(card));
    }

    [Fact]
    public void Package_identity_accepts_template_hidden_tag_when_runtime_tags_are_empty()
    {
        var templateId = Guid.NewGuid();
        var card = new BazaarGameClient.Domain.Models.Cards.Card
        {
            TemplateId = templateId,
            HiddenTags = new HashSet<EHiddenTag>(),
            Template = new TCardItem
            {
                Id = templateId,
                Type = ECardType.Item,
                ArtKey = "Assets/Cards/Package.png",
                InternalName = "Package Template",
                HiddenTags = new HashSet<EHiddenTag> { EHiddenTag.Package },
            },
        };

        Assert.True(CardArtInjector.IsPackageCard(card));
    }

    [Fact]
    public void Package_identity_rejects_template_without_package_hidden_tag()
    {
        var templateId = Guid.NewGuid();
        var card = new BazaarGameClient.Domain.Models.Cards.Card
        {
            TemplateId = templateId,
            HiddenTags = new HashSet<EHiddenTag>(),
            Template = new TCardItem
            {
                Id = templateId,
                Type = ECardType.Item,
                ArtKey = "Assets/Cards/Package.png",
                InternalName = "Package Name Without Hidden Tag",
                HiddenTags = new HashSet<EHiddenTag>(),
            },
        };

        Assert.False(CardArtInjector.IsPackageCard(card));
    }

    [Fact]
    public void Package_art_replacement_policy_defaults_disabled_when_config_is_missing()
    {
        Assert.False(PackageCardArtReplacementPolicy.IsEnabled(null));
    }

    [Fact]
    public void Package_art_replacement_policy_reads_and_writes_config()
    {
        var configPath = Path.Combine(_tempDir, "BazaarPlusPlus.cfg");
        var configFile = new ConfigFile(configPath, saveOnInit: false);
        var config = new BppConfig();
        config.Initialize(configFile);

        Assert.False(PackageCardArtReplacementPolicy.IsEnabled(config));

        PackageCardArtReplacementPolicy.SetEnabled(config, true);

        Assert.True(PackageCardArtReplacementPolicy.IsEnabled(config));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }

    private static readonly byte[] OnePixelImageBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII="
    );
}
