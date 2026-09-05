using BazaarGameClient.Domain.Models;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Socket;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Prerequisites.Conditionals;
using BazaarGameShared.Domain.Targeting;
using BazaarGameShared.Domain.Values;
using BazaarPlusPlus.Game.MusicNotes;
using Xunit;

namespace RuntimeIntegration.Tests;

public sealed class MusicNoteActivationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Occupancy_alone_does_not_activate_a_note(bool expected)
    {
        var tag = expected ? ECardTag.Weapon : ECardTag.Tool;
        var item = new ItemCard { Tags = new HashSet<ECardTag> { tag } };
        var template = Template(
            new TCardConditionalTag
            {
                Tags = new HashSet<ECardTag> { ECardTag.Weapon },
                Operator = EListComparisonOperator.Any,
            }
        );
        Assert.Equal(
            expected,
            MusicNoteActivation.IsSatisfied(template, item, new Run(), new SocketEffect())
        );
    }

    [Fact]
    public void Empty_socket_is_not_activated()
    {
        Assert.False(
            MusicNoteActivation.IsSatisfied(
                Template(new TCardConditionalTag()),
                null,
                new Run(),
                new SocketEffect()
            )
        );
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1000, true)]
    public void Non_tag_cooldown_condition_is_evaluated(int cooldown, bool expected)
    {
        var item = new ItemCard
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.CooldownMax] = cooldown,
            },
        };
        var template = Template(
            new TCardConditionalAttribute
            {
                Attribute = ECardAttributeType.CooldownMax,
                ComparisonOperator = EComparisonOperator.GreaterThanOrEqual,
                ComparisonValue = new TFixedValue { Value = 1 },
            }
        );
        Assert.Equal(
            expected,
            MusicNoteActivation.IsSatisfied(template, item, new Run(), new SocketEffect())
        );
    }

    private static TCardMusicNoteSocketEffect Template(ITCardConditional condition) =>
        new()
        {
            Auras = new Dictionary<string, TCardAura>
            {
                ["note"] = new()
                {
                    Action = new TAuraActionCardModifyAttribute
                    {
                        Target = new TTargetCardOccupying { Conditions = condition },
                    },
                },
            },
        };
}
