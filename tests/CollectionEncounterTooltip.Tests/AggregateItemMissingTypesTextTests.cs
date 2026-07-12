using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Targeting;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.Patches.Tooltips;
using Xunit;

namespace CollectionEncounterTooltip.Tests;

public sealed class AggregateItemMissingTypesTextTests
{
    public AggregateItemMissingTypesTextTests()
    {
        L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
    }

    [Fact]
    public void FindMissing_preserves_player_facing_order_and_excludes_system_tags()
    {
        var missing = AggregateItemMissingTypesText.FindMissing(
            new[] { ECardTag.Weapon, ECardTag.Tool, ECardTag.Apparel }
        );

        Assert.DoesNotContain(ECardTag.Weapon, missing);
        Assert.DoesNotContain(ECardTag.Tool, missing);
        Assert.DoesNotContain(ECardTag.Apparel, missing);
        Assert.DoesNotContain(ECardTag.Event, missing);
        Assert.DoesNotContain(ECardTag.Merchant, missing);
        Assert.Equal(ECardTag.Friend, missing[0]);
    }

    [Fact]
    public void Build_uses_native_keyword_colorizer_for_the_complete_line()
    {
        var content = AggregateItemMissingTypesText.Build(
            AggregateItemMissingTypesText.ItemTypes.Where(tag => tag != ECardTag.Food),
            text => $"colored({text})"
        );

        Assert.Equal("colored(Missing Types: Food)", content);
    }

    [Fact]
    public void Build_returns_null_when_every_type_is_present()
    {
        Assert.Null(AggregateItemMissingTypesText.Build(AggregateItemMissingTypesText.ItemTypes));
    }

    [Fact]
    public void Build_localizes_the_heading_for_simplified_chinese()
    {
        L.Install(new TestLanguageProvider("zh-CN"), new TestLocaleModeProvider());

        var content = AggregateItemMissingTypesText.Build(
            AggregateItemMissingTypesText.ItemTypes.Where(tag => tag != ECardTag.Relic)
        );

        Assert.Equal("尚缺类型： Relic", content);
    }

    [Fact]
    public void AppendToPassiveText_places_missing_types_inside_the_native_passive_block()
    {
        var content = AggregateItemMissingTypesText.AppendToPassiveText(
            "This has the Types of items you have.\n",
            "Missing Types: Food, Tool"
        );

        Assert.Equal(
            "This has the Types of items you have.\n<size=65%>Missing Types: Food, Tool</size>",
            content
        );
    }

    [Fact]
    public void Source_resolver_recognizes_persistent_action_aggregators()
    {
        var template = new TCardItem
        {
            Abilities = new Dictionary<string, TCardAbility>
            {
                ["gain-types"] = new() { Action = new TActionCardAddTagsBySource() },
            },
        };

        Assert.True(
            AggregateItemMissingTypesTooltipPatch.TryResolveTypeSource(template, out var source)
        );
        Assert.Null(source.Section);
    }

    [Fact]
    public void Source_resolver_recognizes_live_aura_aggregators()
    {
        var template = new TCardItem
        {
            Auras = new Dictionary<string, TCardAura>
            {
                ["copy-types"] = new() { Action = new TAuraActionCardAddTagsBySource() },
            },
        };

        Assert.True(
            AggregateItemMissingTypesTooltipPatch.TryResolveTypeSource(template, out var source)
        );
        Assert.Null(source.Section);
    }

    [Fact]
    public void Source_resolver_recognizes_external_distinct_type_aggregators()
    {
        var template = new TCardItem
        {
            Auras = new Dictionary<string, TCardAura>
            {
                ["count-types"] = new()
                {
                    Action = new TAuraActionCardModifyAttribute
                    {
                        Value = new TReferenceValueCardTagCount
                        {
                            Distinct = true,
                            Target = new TTargetCardSection
                            {
                                TargetSection = ETargetCardSectionTargetSection.SelfHand,
                                ExcludeSelf = true,
                            },
                        },
                    },
                },
            },
        };

        Assert.True(
            AggregateItemMissingTypesTooltipPatch.TryResolveTypeSource(template, out var source)
        );
        Assert.Equal(ETargetCardSectionTargetSection.SelfHand, source.Section);
        Assert.True(source.ExcludeSelf);
    }

    private sealed class TestLanguageProvider(string languageCode = "en") : ILanguageProvider
    {
        public string CurrentLanguageCode => languageCode;
    }

    private sealed class TestLocaleModeProvider : ILocaleModeProvider
    {
        public BppChineseLocaleMode CurrentMode => BppChineseLocaleMode.Mainland;
    }
}
