using BazaarPlusPlus.Game.CollectionPanel.Ui;
using Xunit;

namespace CollectionEncounterTooltip.Tests;

public sealed class CollectionTooltipMarkupTests
{
    [Fact]
    public void Structural_breaks_use_current_font_size_units()
    {
        Assert.Equal("<line-height=1.6em>\n", CollectionTooltipMarkup.BlockBreak);
        Assert.Equal(
            "<line-height=1.35em>\n<line-height=1.3em>",
            CollectionTooltipMarkup.SubItemBreak
        );
        Assert.Equal(
            "<line-height=1.4em>\n<line-height=1.3em>",
            CollectionTooltipMarkup.BulletBreak
        );
    }

    [Fact]
    public void Scaled_blocks_compute_baseline_and_trailing_break_inside_their_size_scope()
    {
        var text = CollectionTooltipMarkup.JoinBlocks(
            new[]
            {
                new CollectionTooltipMarkup.Block("Full size"),
                new CollectionTooltipMarkup.Block("Small one", fontSizePercent: 85),
                new CollectionTooltipMarkup.Block("Small two", fontSizePercent: 85),
            }
        );

        Assert.Equal(
            "<line-height=1.3em>Full size"
                + "<line-height=1.6em>\n"
                + "<size=85%><line-height=1.3em>Small one"
                + "<line-height=1.6em>\n</size>"
                + "<size=85%><line-height=1.3em>Small two</size>",
            text
        );
    }

    [Fact]
    public void Native_colorizer_line_height_does_not_override_shared_vertical_rhythm()
    {
        Assert.Equal(
            "Gain <color=green>Health</color>",
            CollectionTooltipMarkup.NormalizeInlineFragment(
                "<line-height=1.6em>Gain <color=green>Health</color></line-height>"
            )
        );
        Assert.Equal(
            "prefix<line-height=1.6em>nested</line-height>",
            CollectionTooltipMarkup.NormalizeInlineFragment(
                "prefix<line-height=1.6em>nested</line-height>"
            )
        );
    }

    [Fact]
    public void Lists_keep_item_breaks_inside_the_containing_font_size_scope()
    {
        var list =
            "Choose one:"
            + CollectionTooltipMarkup.BulletBreak
            + "· first"
            + CollectionTooltipMarkup.SubItemBreak
            + "- nested";

        var text = CollectionTooltipMarkup.JoinBlocks(
            new[] { new CollectionTooltipMarkup.Block(list, fontSizePercent: 85) }
        );

        Assert.Equal(
            "<size=85%><line-height=1.3em>Choose one:"
                + "<line-height=1.4em>\n<line-height=1.3em>· first"
                + "<line-height=1.35em>\n<line-height=1.3em>- nested</size>",
            text
        );
    }
}
