#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

// Vertical rhythm for BPP tooltip sections is controlled exclusively through
// <line-height> tags kept active across the whole text. TMP only honours an
// explicit line height at the moment it processes a line break, and — crucially —
// when none is set it retroactively pushes a line down if a taller element (inline
// sprite, styled keyword) appears after the line's first character, which made
// gaps depend on WHERE colored keywords happened to sit on each line. Spacer
// lines (shrunken NBSP) are gone for the same reason: their rendered height
// depended on neighbouring line content.
internal static class CollectionTooltipMarkup
{
    private const string BaseLine = "<line-height=100%>";

    // Between top-level blocks (choices, outcome groups, level-reward sections).
    public const string BlockBreak = "<line-height=175%>\n" + BaseLine;

    // Between "- entry" lines inside an expanded pool.
    public const string SubItemBreak = "<line-height=140%>\n" + BaseLine;

    // Between bulleted candidates in a "Choose one:" list.
    public const string BulletBreak = "<line-height=120%>\n" + BaseLine;

    // The base line height must already be set when the first break is reached,
    // so every built text starts with it.
    public static string Wrap(string content) =>
        content.Length == 0 ? content : BaseLine + content;
}
