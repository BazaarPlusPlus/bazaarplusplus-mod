using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine.UIElements;

internal static class TokenizedSamplePanel
{
    public static VisualElement Create()
    {
        var panel = new VisualElement();
        panel.style.width = Sizes.ChipMinWidth;
        panel.style.backgroundColor = Colors.HistorySectionBackground;
        UiStyle.Padding(panel.style, UiSpacing.Xxl);
        UiStyle.Radius(panel.style, Radii.Md);
        UiStyle.Border(panel.style, Borders.Thin, Colors.HistoryListFrameBorder);
        return panel;
    }
}
