using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Achievements;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.GameInterop.CustomCards;
using BazaarPlusPlus.Localization;
using Xunit;

namespace BazaarPlusPlus.Tests.BppCustomCard;

public sealed class BppCustomCardTests : IDisposable
{
    public void Dispose()
    {
        BppCustomCardRegistry.Current = null;
    }

    [Fact]
    public void Uuid_v5_achievement_ids_match_the_frozen_catalog_value()
    {
        Assert.Equal(
            new Guid("5351d91d-2b5c-5f44-8349-bbf334a9bbc5"),
            BppCustomCardIds.ForAchievement("cosmic_ray")
        );
    }

    [Fact]
    public void Registry_registers_and_rejects_empty_duplicate_and_native_colliding_ids()
    {
        var descriptor = Descriptor(Guid.NewGuid());
        var registry = new BppCustomCardRegistry(id => id == descriptor.Id);

        Assert.Throws<InvalidOperationException>(() => registry.Register(descriptor));

        registry = new BppCustomCardRegistry(_ => false);
        Assert.Throws<InvalidOperationException>(() => registry.Register(Descriptor(Guid.Empty)));

        registry.Register(descriptor);

        Assert.True(registry.IsBppCard(descriptor.Id));
        Assert.True(registry.TryGet(descriptor.Id, out var actual));
        Assert.Same(descriptor, actual);
        Assert.Throws<InvalidOperationException>(() => registry.Register(descriptor));
    }

    [Fact]
    public void Descriptor_mapper_projects_achievement_catalog_rows_to_custom_cards()
    {
        var catalog = AchievementCardCatalog.Build(EmbeddedCosmicRayJson);
        var mapper = new AchievementCardDescriptorMapper(_ => true);

        var descriptor = mapper.Map(catalog[0]);

        Assert.Equal(new Guid("5351d91d-2b5c-5f44-8349-bbf334a9bbc5"), descriptor.Id);
        Assert.Equal(ECardType.Item, descriptor.Type);
        Assert.Equal(ECardSize.Medium, descriptor.Size);
        Assert.Equal(ETier.Legendary, descriptor.StartingTier);
        Assert.True(descriptor.HasBundledArt);
        Assert.Equal("CosmicRay", descriptor.InternalName);
    }

    [Fact]
    public void Catalog_rejects_uuid_drift_and_missing_traditional_chinese_text()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AchievementCardCatalog.Build(
                EmbeddedCosmicRayJson.Replace(
                    "5351d91d-2b5c-5f44-8349-bbf334a9bbc5",
                    "00000000-0000-5000-8000-000000000000"
                )
            )
        );
        Assert.Throws<InvalidOperationException>(() =>
            AchievementCardCatalog.Build(
                EmbeddedCosmicRayJson.Replace("\"zhHant\": \"宇宙射線\"", "\"zhHant\": \"\"")
            )
        );
    }

    [Fact]
    public void Template_factory_builds_native_item_template_for_tooltip_and_frame_setup()
    {
        var descriptor = Descriptor(new Guid("5351d91d-2b5c-5f44-8349-bbf334a9bbc5"));

        var template = Assert.IsType<TCardItem>(BppCustomCardTemplateFactory.Build(descriptor));

        Assert.Equal(descriptor.Id, template.Id);
        Assert.Equal(ECardType.Item, template.Type);
        Assert.Equal(ECardSize.Medium, template.Size);
        Assert.Equal(ETier.Legendary, template.StartingTier);
        Assert.Empty(template.ArtKey);
        Assert.Empty(template.Tiers);
        Assert.NotNull(template.Heroes);
        Assert.NotNull(template.Tags);
        Assert.NotNull(template.HiddenTags);
        Assert.Empty(template.HiddenTags);
        Assert.Equal("Cosmic Ray", template.Localization.Title.Text);
        Assert.Equal("Deal 9999+ damage in a single hit", template.Localization.Description?.Text);
    }

    [Fact]
    public void Collection_projection_builds_filterable_non_package_vms_from_registry()
    {
        var registry = new BppCustomCardRegistry(_ => false);
        var descriptor = Descriptor(Guid.NewGuid());
        registry.Register(descriptor);

        var vms = BppCustomCardCollectionProjection.BuildVms(registry);

        var vm = Assert.Single(vms);
        Assert.Equal(descriptor.Id, vm.Id);
        Assert.Equal(ECardType.Item, vm.Type);
        Assert.Equal(ECardSize.Medium, vm.Size);
        Assert.Equal(ETier.Legendary, vm.StartingTier);
        Assert.Equal("Cosmic Ray", vm.DisplayName);
        Assert.Equal("bpp-custom", vm.ArtKey);
        Assert.False(vm.IsPackage);
    }

    [Fact]
    public void Card_factory_distinguishes_static_data_not_ready_from_hard_template_misses()
    {
        var vm = new CollectionCardVm
        {
            Id = Guid.NewGuid(),
            Type = ECardType.Item,
            Size = ECardSize.Medium,
            StartingTier = ETier.Bronze,
            InternalName = "Unknown",
        };

        var notReadyFactory = new CollectionCardFactory(
            null!,
            null!,
            staticDataProvider: () => null,
            templateResolver: (_, _) => throw new InvalidOperationException("not called")
        );
        var notReady = notReadyFactory.TryBind(vm);

        Assert.Equal(CollectionCardBindStatus.NotReady, notReady.Status);
        Assert.Null(notReady.Binding);

        var hardMissFactory = new CollectionCardFactory(
            null!,
            null!,
            staticDataProvider: () => new object(),
            templateResolver: (_, _) => null
        );
        var hardMiss = hardMissFactory.TryBind(vm);

        Assert.Equal(CollectionCardBindStatus.HardMiss, hardMiss.Status);
        Assert.Null(hardMiss.Binding);
    }

    [Fact]
    public void Embedded_catalog_contains_cosmic_ray_and_bundled_art_resource()
    {
        var catalog = AchievementCardCatalog.LoadEmbedded();
        var mapper = new AchievementCardDescriptorMapper();

        var descriptor = mapper.Map(Assert.Single(catalog.Cards));

        Assert.Equal("CosmicRay", descriptor.InternalName);
        Assert.True(descriptor.HasBundledArt);
    }

    private static BppCustomCardDescriptor Descriptor(Guid id) =>
        new()
        {
            Id = id,
            Type = ECardType.Item,
            Size = ECardSize.Medium,
            StartingTier = ETier.Legendary,
            Title = new LocalizedTextSet("Cosmic Ray", "宇宙射线", "宇宙射線", "宇宙射線"),
            Description = new LocalizedTextSet(
                "Deal 9999+ damage in a single hit",
                "单次伤害达到 9999",
                "單次傷害達到 9999",
                "單次傷害達到 9999"
            ),
            HasBundledArt = true,
            InternalName = "CosmicRay",
            SortKey = 10,
        };

    private const string EmbeddedCosmicRayJson = """
        {
          "schemaVersion": 1,
          "cards": [
            {
              "achievementId": "cosmic_ray",
              "templateId": "5351d91d-2b5c-5f44-8349-bbf334a9bbc5",
              "internalName": "CosmicRay",
              "title": {
                "en": "Cosmic Ray",
                "zhHans": "宇宙射线",
                "zhHant": "宇宙射線"
              },
              "description": {
                "en": "Deal 9999+ damage in a single hit",
                "zhHans": "单次伤害达到 9999",
                "zhHant": "單次傷害達到 9999"
              },
              "category": "combat",
              "ruleKind": "combat_single_damage",
              "ruleParams": {},
              "target": 9999,
              "displayTier": "Legendary",
              "displaySize": "Medium",
              "sortKey": 10,
              "hiddenUntilUnlocked": false
            }
          ]
        }
        """;
}
