using BazaarPlusPlus.GameInterop.Cards;
using Xunit;

namespace BazaarPlusPlus.BazaarAgent.Tests;

public sealed class CardDescriptionTextNormalizerTests
{
    [Fact]
    public void Normalize_removes_TMP_rendering_tags_but_keeps_the_mechanic_text()
    {
        const string description =
            "<line-height=1.6em>己方最右侧的<size=145%><voffset=-4><sprite name=Health><font=\"BazaarNumbers_Regular_SDF\" material=\"BazaarNumbers_Regular_SDF_OutlineTooltip_M\"><color=#8FE931FF></font></size></voffset>治疗</color>物品+</line-height><size=145%><voffset=-4><sprite name=Health><font=\"BazaarNumbers_Regular_SDF\" material=\"BazaarNumbers_Regular_SDF_OutlineTooltip_M\"><color=#8FE931FF>20</color></font></size></voffset>";

        var result = CardDescriptionTextNormalizer.Normalize(description);

        Assert.Equal("己方最右侧的治疗物品+20", result);
    }

    [Fact]
    public void Normalize_preserves_non_empty_lines()
    {
        var result = CardDescriptionTextNormalizer.Normalize(
            "  <b>造成 30 伤害</b>\r\n\r\n  <color=#FF0000>灼烧 5</color>  "
        );

        Assert.Equal("造成 30 伤害\n灼烧 5", result);
    }
}
