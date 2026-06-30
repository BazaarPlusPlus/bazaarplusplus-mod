using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Tooltips;
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
            new Guid("ff35bbaa-3545-5fef-b469-79a4f4592326"),
            BppCustomCardIds.ForAchievement("storm_traveler")
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
        var catalog = AchievementCardCatalog.Build(EmbeddedStormTravelerJson);
        var mapper = new AchievementCardDescriptorMapper(_ => true);

        var descriptor = mapper.Map(catalog[0]);

        Assert.Equal(new Guid("ff35bbaa-3545-5fef-b469-79a4f4592326"), descriptor.Id);
        Assert.Equal(ECardType.Item, descriptor.Type);
        Assert.Equal(ECardSize.Medium, descriptor.Size);
        Assert.Equal(ETier.Legendary, descriptor.StartingTier);
        Assert.True(descriptor.HasBundledArt);
        Assert.Equal("StormTraveler", descriptor.InternalName);
    }

    [Fact]
    public void Catalog_rejects_uuid_drift_and_missing_traditional_chinese_text()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AchievementCardCatalog.Build(
                EmbeddedStormTravelerJson.Replace(
                    "ff35bbaa-3545-5fef-b469-79a4f4592326",
                    "00000000-0000-5000-8000-000000000000"
                )
            )
        );
        Assert.Throws<InvalidOperationException>(() =>
            AchievementCardCatalog.Build(
                EmbeddedStormTravelerJson.Replace("\"zhHant\": \"風暴旅人\"", "\"zhHant\": \"\"")
            )
        );
    }

    [Fact]
    public void Chinese_locale_modes_cycle_between_mainland_and_taiwan_only()
    {
        Assert.Equal(
            BppChineseLocaleMode.Taiwan,
            ChineseScriptConverter.GetNextMode(BppChineseLocaleMode.Mainland)
        );
        Assert.Equal(
            BppChineseLocaleMode.Mainland,
            ChineseScriptConverter.GetNextMode(BppChineseLocaleMode.Taiwan)
        );
        Assert.Equal("CN", ChineseScriptConverter.ResolveModeStatus(BppChineseLocaleMode.Mainland));
        Assert.Equal("TW", ChineseScriptConverter.ResolveModeStatus(BppChineseLocaleMode.Taiwan));
    }

    [Fact]
    public void Legacy_hong_kong_mode_value_resolves_as_taiwan()
    {
        var text = new LocalizedTextSet("Database", "数据库", "資料庫");
        var legacyHongKongValue = (BppChineseLocaleMode)2;

        Assert.Equal(
            BppChineseLocaleMode.Taiwan,
            ChineseScriptConverter.NormalizeMode(legacyHongKongValue)
        );
        Assert.Equal("TW", ChineseScriptConverter.ResolveModeStatus(legacyHongKongValue));
        Assert.Equal("資料庫", text.Resolve("zh-Hant", legacyHongKongValue));
    }

    [Fact]
    public void Template_factory_builds_native_item_template_for_tooltip_and_frame_setup()
    {
        var descriptor = Descriptor(new Guid("ff35bbaa-3545-5fef-b469-79a4f4592326"));

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
        Assert.Equal("Storm Traveler", template.Localization.Title.Text);
        Assert.Equal("Outlast an entire sandstorm", template.Localization.Description?.Text);
        var passiveTooltip = Assert.Single(template.Localization.Tooltips);
        Assert.Equal(ETooltipType.Passive, passiveTooltip.TooltipType);
        Assert.Equal("Outlast an entire sandstorm", passiveTooltip.Content.Text);
    }

    [Fact]
    public void Template_factory_uses_donor_art_key_when_provided()
    {
        var descriptor = Descriptor(new Guid("ff35bbaa-3545-5fef-b469-79a4f4592326"));
        const string donorArtKey = "Addressables/CardArt/Donor.asset";

        var template = Assert.IsType<TCardItem>(
            BppCustomCardTemplateFactory.Build(descriptor, donorArtKey)
        );

        Assert.Equal(donorArtKey, template.ArtKey);
        var passiveTooltip = Assert.Single(template.Localization.Tooltips);
        Assert.Equal(ETooltipType.Passive, passiveTooltip.TooltipType);
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
    public void Card_factory_hard_misses_when_custom_template_builder_throws()
    {
        var descriptor = Descriptor(Guid.NewGuid());
        var registry = new BppCustomCardRegistry(_ => false);
        registry.Register(descriptor);
        BppCustomCardRegistry.Current = registry;
        var vm = new CollectionCardVm
        {
            Id = descriptor.Id,
            Type = ECardType.Item,
            Size = ECardSize.Medium,
            StartingTier = ETier.Bronze,
            InternalName = "BrokenCustomCard",
        };
        var factory = new CollectionCardFactory(
            null!,
            null!,
            staticDataProvider: () => new object(),
            templateResolver: (_, _) => throw new InvalidOperationException("not called"),
            customTemplateBuilder: (_, _) => throw new InvalidOperationException("bad custom"),
            customMaterialDonorResolver: new TestDonorResolver(
                BppCustomCardMaterialDonorResult.Ready("Addressables/CardArt/Donor.asset")
            )
        );

        var result = factory.TryBind(vm);

        Assert.Equal(CollectionCardBindStatus.HardMiss, result.Status);
        Assert.Null(result.Binding);
    }

    [Fact]
    public void Card_factory_reports_not_ready_until_donor_art_resolves()
    {
        var descriptor = Descriptor(Guid.NewGuid());
        var registry = new BppCustomCardRegistry(_ => false);
        registry.Register(descriptor);
        BppCustomCardRegistry.Current = registry;
        var vm = new CollectionCardVm
        {
            Id = descriptor.Id,
            Type = ECardType.Item,
            Size = ECardSize.Medium,
            StartingTier = ETier.Bronze,
            InternalName = "CustomCardWaitingForDonor",
        };
        var factory = new CollectionCardFactory(
            null!,
            null!,
            staticDataProvider: () => new object(),
            templateResolver: (_, _) => throw new InvalidOperationException("not called"),
            customTemplateBuilder: (_, _) => throw new InvalidOperationException("not called"),
            customMaterialDonorResolver: new TestDonorResolver(
                BppCustomCardMaterialDonorResult.Pending()
            )
        );

        var result = factory.TryBind(vm);

        Assert.Equal(CollectionCardBindStatus.NotReady, result.Status);
        Assert.Null(result.Binding);
    }

    [Fact]
    public void Card_factory_hard_misses_when_donor_art_is_unavailable()
    {
        var descriptor = Descriptor(Guid.NewGuid());
        var registry = new BppCustomCardRegistry(_ => false);
        registry.Register(descriptor);
        BppCustomCardRegistry.Current = registry;
        var vm = new CollectionCardVm
        {
            Id = descriptor.Id,
            Type = ECardType.Item,
            Size = ECardSize.Medium,
            StartingTier = ETier.Bronze,
            InternalName = "CustomCardWithoutDonor",
        };
        var templateResolverCalls = 0;
        var customTemplateBuilderCalls = 0;
        var factory = new CollectionCardFactory(
            null!,
            null!,
            staticDataProvider: () => new object(),
            templateResolver: (_, _) =>
            {
                templateResolverCalls++;
                throw new InvalidOperationException("not called");
            },
            customTemplateBuilder: (_, _) =>
            {
                customTemplateBuilderCalls++;
                throw new InvalidOperationException("not called");
            },
            customMaterialDonorResolver: new TestDonorResolver(
                BppCustomCardMaterialDonorResult.Unavailable()
            )
        );

        var result = factory.TryBind(vm);

        Assert.Equal(CollectionCardBindStatus.HardMiss, result.Status);
        Assert.Null(result.Binding);
        Assert.Equal(0, templateResolverCalls);
        Assert.Equal(0, customTemplateBuilderCalls);
    }

    [Fact]
    public void Card_factory_hard_misses_custom_cards_without_bundled_art()
    {
        var descriptor = Descriptor(Guid.NewGuid(), hasBundledArt: false);
        var registry = new BppCustomCardRegistry(_ => false);
        registry.Register(descriptor);
        BppCustomCardRegistry.Current = registry;
        var vm = new CollectionCardVm
        {
            Id = descriptor.Id,
            Type = ECardType.Item,
            Size = ECardSize.Medium,
            StartingTier = ETier.Bronze,
            InternalName = "CustomCardWithoutArt",
        };
        var factory = new CollectionCardFactory(
            null!,
            null!,
            staticDataProvider: () => throw new InvalidOperationException("not called"),
            templateResolver: (_, _) => throw new InvalidOperationException("not called"),
            customTemplateBuilder: (_, _) => throw new InvalidOperationException("not called"),
            customMaterialDonorResolver: new TestDonorResolver(
                BppCustomCardMaterialDonorResult.Pending()
            )
        );

        var result = factory.TryBind(vm);

        Assert.Equal(CollectionCardBindStatus.HardMiss, result.Status);
        Assert.Null(result.Binding);
    }

    [Fact]
    public void Embedded_catalog_contains_achievement_cards_and_bundled_art_resources()
    {
        var catalog = AchievementCardCatalog.LoadEmbedded();
        var mapper = new AchievementCardDescriptorMapper();

        var descriptor = mapper.Map(Assert.Single(catalog.Cards));

        Assert.Equal("StormTraveler", descriptor.InternalName);
        Assert.True(descriptor.HasBundledArt);
    }

    [Fact]
    public void Achievement_registration_disables_achievements_when_catalog_loading_fails()
    {
        var registry = new BppCustomCardRegistry(_ => false);

        AchievementCardRegistrar.Register(
            registry,
            loadCatalog: () => throw new InvalidOperationException("bad catalog")
        );

        Assert.Empty(registry.GetAll());
    }

    [Fact]
    public void Achievement_registration_preserves_registry_when_later_card_registration_fails()
    {
        var existing = Descriptor(Guid.NewGuid());
        var stagedId = Guid.NewGuid();
        var registry = new BppCustomCardRegistry(_ => false);
        registry.Register(existing);

        AchievementCardRegistrar.Register(
            registry,
            loadCatalog: () =>
                new AchievementCardCatalog(
                    new[]
                    {
                        AchievementDefinition(stagedId, "FirstAchievement", 10),
                        AchievementDefinition(stagedId, "DuplicateAchievement", 20),
                    }
                )
        );

        var descriptor = Assert.Single(registry.GetAll());
        Assert.Same(existing, descriptor);
        Assert.False(registry.IsBppCard(stagedId));
    }

    private static BppCustomCardDescriptor Descriptor(Guid id, bool hasBundledArt = true) =>
        new()
        {
            Id = id,
            Type = ECardType.Item,
            Size = ECardSize.Medium,
            StartingTier = ETier.Legendary,
            Title = new LocalizedTextSet("Storm Traveler", "风暴旅人", "風暴旅人"),
            Description = new LocalizedTextSet(
                "Outlast an entire sandstorm",
                "熬过整场沙尘暴",
                "熬過整場沙塵暴"
            ),
            HasBundledArt = hasBundledArt,
            InternalName = "StormTraveler",
            SortKey = 20,
        };

    private sealed class TestDonorResolver : BppCustomCardMaterialDonorResolver
    {
        private readonly BppCustomCardMaterialDonorResult _result;

        public TestDonorResolver(BppCustomCardMaterialDonorResult result) => _result = result;

        public override BppCustomCardMaterialDonorResult Resolve(
            object? staticData,
            ECardSize size
        ) => _result;
    }

    private static AchievementCardDefinition AchievementDefinition(
        Guid templateId,
        string internalName,
        int sortKey
    ) =>
        new()
        {
            AchievementId = internalName,
            TemplateId = templateId,
            InternalName = internalName,
            Title = new LocalizedTextSet(internalName, internalName, internalName),
            Description = new LocalizedTextSet(
                $"{internalName} description",
                $"{internalName} description",
                $"{internalName} description"
            ),
            Category = "test",
            RuleKind = "test",
            Target = 1,
            DisplayTier = ETier.Legendary,
            DisplaySize = ECardSize.Medium,
            SortKey = sortKey,
            HiddenUntilUnlocked = false,
        };

    private const string EmbeddedStormTravelerJson = """
        {
          "schemaVersion": 1,
          "cards": [
            {
              "achievementId": "storm_traveler",
              "templateId": "ff35bbaa-3545-5fef-b469-79a4f4592326",
              "internalName": "StormTraveler",
              "title": {
                "en": "Storm Traveler",
                "zhHans": "风暴旅人",
                "zhHant": "風暴旅人"
              },
              "description": {
                "en": "Outlast an entire sandstorm",
                "zhHans": "熬过整场沙尘暴",
                "zhHant": "熬過整場沙塵暴"
              },
              "category": "survival",
              "ruleKind": "sandstorm_survived",
              "ruleParams": {},
              "target": 1,
              "displayTier": "Legendary",
              "displaySize": "Medium",
              "sortKey": 20,
              "hiddenUntilUnlocked": false
            }
          ]
        }
        """;
}
