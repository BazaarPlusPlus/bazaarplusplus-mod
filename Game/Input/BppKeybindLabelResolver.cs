using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus;

internal static class BppKeybindLabelResolver
{
    internal static string ResolveActionLabel(BppHotkeyActionId actionId, string languageCode)
    {
        var isChinese = SimplifiedChineseLanguage.Matches(languageCode);
        return actionId switch
        {
            BppHotkeyActionId.HoldEnchantPreview => isChinese
                ? "显示附魔预览"
                : "Show Enchant Preview",
            BppHotkeyActionId.HoldUpgradePreview => isChinese
                ? "显示升级预览"
                : "Show Upgrade Preview",
            _ => actionId.ToString(),
        };
    }

    internal static string ResolveRebindPrompt(string languageCode)
    {
        return SimplifiedChineseLanguage.Matches(languageCode) ? "按下一个按键" : "Press a key";
    }

    internal static string ResolveUnsupportedKey(string languageCode)
    {
        return SimplifiedChineseLanguage.Matches(languageCode) ? "不支持该按键" : "Unsupported key";
    }
}
