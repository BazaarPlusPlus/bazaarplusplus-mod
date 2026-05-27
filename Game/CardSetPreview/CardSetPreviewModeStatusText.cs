#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.CardSetPreview;

internal static class CardSetPreviewModeStatusText
{
    public static string Build(string modeLabel, string languageCode)
    {
        var resolvedModeLabel = string.IsNullOrWhiteSpace(modeLabel)
            ? string.Empty
            : modeLabel.Trim();
        return LanguageCodeMatcher.IsChinese(languageCode)
            ? $"卡组选择模式：点击物品加入/移除，CapsLock 退出 | {resolvedModeLabel}"
            : $"Card Set Selection: click items to add/remove, CapsLock to exit | {resolvedModeLabel}";
    }
}
