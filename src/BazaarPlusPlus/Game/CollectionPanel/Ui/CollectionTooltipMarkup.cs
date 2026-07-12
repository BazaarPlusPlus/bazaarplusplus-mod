#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

// Vertical rhythm for BPP tooltip sections is controlled exclusively through
// <line-height> tags expressed in em, so every value follows the font size active
// at that point. TMP resolves line-height immediately rather than keeping a CSS-like
// relative value; scaled blocks therefore open <size> before setting their baseline
// and process their trailing newline before closing it.
internal static class CollectionTooltipMarkup
{
    private const string BaseLine = "<line-height=1.15em>";
    private const string NativeLineHeightOpen = "<line-height=1.6em>";
    private const string LineHeightClose = "</line-height>";

    // Between top-level blocks (choices, outcome groups, level-reward sections).
    // JoinBlocks emits the next baseline after entering that block's size scope.
    public const string BlockBreak = "<line-height=1.6em>\n";

    // Between "- entry" lines inside an expanded pool.
    public const string SubItemBreak = "<line-height=1.35em>\n" + BaseLine;

    // Between bulleted candidates in a "Choose one:" list.
    public const string BulletBreak = "<line-height=1.25em>\n" + BaseLine;

    internal readonly struct Block
    {
        public Block(string content, int fontSizePercent = 100)
        {
            Content = content ?? string.Empty;
            FontSizePercent = fontSizePercent;
        }

        public string Content { get; }

        public int FontSizePercent { get; }

        public static implicit operator Block(string content) => new(content);
    }

    public static string JoinBlocks(IReadOnlyList<Block> blocks)
    {
        if (blocks == null || blocks.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            var isScaled = block.FontSizePercent != 100;
            if (isScaled)
                builder.Append("<size=").Append(block.FontSizePercent).Append("%>");

            builder.Append(BaseLine).Append(block.Content);
            if (index + 1 < blocks.Count)
                builder.Append(BlockBreak);

            if (isScaled)
                builder.Append("</size>");
        }
        return builder.ToString();
    }

    // The game's ColorKeywords always wraps its return value in 1.6em. These
    // fragments are embedded inside a block whose rhythm is already controlled
    // here, and TMP's closing line-height tag resets rather than restores the
    // outer value. Remove only that known outer pair; preserve any inner markup.
    public static string NormalizeInlineFragment(string content)
    {
        if (
            string.IsNullOrEmpty(content)
            || !content.StartsWith(NativeLineHeightOpen, StringComparison.Ordinal)
            || !content.EndsWith(LineHeightClose, StringComparison.Ordinal)
        )
            return content;

        return content.Substring(
            NativeLineHeightOpen.Length,
            content.Length - NativeLineHeightOpen.Length - LineHeightClose.Length
        );
    }
}
