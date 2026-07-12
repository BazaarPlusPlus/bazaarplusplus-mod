using BazaarPlusPlus.Game.CollectionPanel.Ui;
using Xunit;

namespace CollectionEncounterTooltip.Tests;

public sealed class CollectionTooltipMarkupTests
{
    [Fact]
    public void Prose_renderer_distinguishes_paragraphs_lists_and_nested_lists()
    {
        var text = CollectionTooltipMarkup.Render(
            new CollectionTooltipMarkup.Block[]
            {
                new CollectionTooltipMarkup.Paragraph("Intro"),
                new CollectionTooltipMarkup.ListBlock(
                    "Choose one:",
                    new[]
                    {
                        new CollectionTooltipMarkup.ListItem(
                            "first",
                            new[] { new CollectionTooltipMarkup.ListItem("detail") }
                        ),
                        new CollectionTooltipMarkup.ListItem("second"),
                    }
                ),
            }
        );

        Assert.Equal(
            "<line-height=1.4em>Intro"
                + "<line-height=1.9em>\n"
                + "<line-height=1.4em>Choose one:"
                + "<line-height=1.5em>\n<line-height=1.4em>· <indent=1em>first</indent>"
                + "<line-height=1.4em>\n<line-height=1.4em><indent=2.2em>- detail</indent>"
                + "<line-height=1.5em>\n<line-height=1.4em>· <indent=1em>second</indent>",
            text
        );
    }

    [Fact]
    public void Font_scaling_wraps_the_complete_semantic_element()
    {
        var text = CollectionTooltipMarkup.Render(
            new CollectionTooltipMarkup.Block[]
            {
                new CollectionTooltipMarkup.Paragraph("Full size"),
                new CollectionTooltipMarkup.ListBlock(
                    null,
                    new[] { new CollectionTooltipMarkup.ListItem("dimmed") },
                    fontSizePercent: 85
                ),
            }
        );

        Assert.Equal(
            "<line-height=1.4em>Full size"
                + "<line-height=1.9em>\n"
                + "<size=85%><line-height=1.4em>"
                + "<line-height=1.4em>· <indent=1em>dimmed</indent></size>",
            text
        );
    }

    [Fact]
    public void Paragraph_gap_uses_the_preceding_elements_font_size()
    {
        var text = CollectionTooltipMarkup.Render(
            new CollectionTooltipMarkup.Block[]
            {
                new CollectionTooltipMarkup.Paragraph("Small", fontSizePercent: 85),
                new CollectionTooltipMarkup.Paragraph("Full"),
            }
        );

        Assert.Equal(
            "<size=85%><line-height=1.4em>Small<line-height=1.9em>\n</size>"
                + "<line-height=1.4em>Full",
            text
        );
    }

    [Fact]
    public void Native_colorizer_line_height_does_not_override_prose_rhythm()
    {
        Assert.Equal(
            "Gain <color=green>Health</color>",
            CollectionTooltipMarkup.NormalizeInlineFragment(
                "<line-height=1.6em>Gain <color=green>Health</color></line-height>"
            )
        );
    }
}
