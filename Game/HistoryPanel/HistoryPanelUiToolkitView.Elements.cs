#nullable enable
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanelUiToolkitView
{
    private static VisualElement CreateInfoChipRow(VisualElement parent, float spacing, float marginTop)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginTop = marginTop;
        row.style.flexWrap = Wrap.NoWrap;
        parent.Add(row);
        return row;
    }

    private static Label CreateInfoChip(VisualElement row, string label, float minWidth)
    {
        var chip = CreateLabel(Sizes.FontTiny, FontStyle.Bold, Colors.White);
        chip.text = label;
        chip.style.minWidth = minWidth;
        chip.style.height = Sizes.InfoChipHeight;
        chip.style.marginRight = UiSpacing.Sm;
        UiStyle.HorizontalPadding(chip.style, UiSpacing.Md);
        chip.style.unityTextAlign = TextAnchor.MiddleCenter;
        UiStyle.Radius(chip.style, Radii.InfoChip);
        row.Add(chip);
        return chip;
    }

    private static Label CreateInlinePill(VisualElement row, float minWidth)
    {
        var pill = CreateLabel(Sizes.FontTiny, FontStyle.Bold, Colors.White);
        pill.style.minWidth = minWidth;
        pill.style.height = Sizes.InlinePillHeight;
        UiStyle.HorizontalPadding(pill.style, UiSpacing.Md);
        pill.style.marginRight = UiSpacing.Sm;
        pill.style.unityTextAlign = TextAnchor.MiddleCenter;
        UiStyle.Radius(pill.style, Radii.Md);
        row.Add(pill);
        return pill;
    }

    private static void SetFixedPillWidth(Label pill, float width) => UiStyle.FixedWidth(pill.style, width);

    private static void SetEqualChipWidth(Label chip, bool isLast = false)
    {
        chip.style.flexGrow = 1f;
        chip.style.flexShrink = 1f;
        chip.style.flexBasis = 0f;
        chip.style.minWidth = UiSpacing.None;
        chip.style.marginRight = isLast ? UiSpacing.None : UiSpacing.Sm;
    }

    private static Label CreateDayBubble(VisualElement parent)
    {
        var bubble = CreateLabel(Sizes.FontCorner, FontStyle.Bold, Colors.White);
        UiStyle.FixedSize(bubble.style, Sizes.DayBubbleSize, Sizes.DayBubbleSize);
        bubble.style.unityTextAlign = TextAnchor.MiddleCenter;
        UiStyle.Radius(bubble.style, Radii.DayBubble);
        bubble.style.backgroundColor = Colors.HistoryDayBubbleBackground;
        UiStyle.Border(bubble.style, Borders.Thin, Colors.HistoryDayBubbleBorder);
        parent.Add(bubble);
        return bubble;
    }
}
