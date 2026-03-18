using BazaarPlusPlus.Game.Input;

namespace BazaarPlusPlus;

internal static class BppKeybindLabelResolver
{
    internal static string ResolveActionLabel(BppHotkeyActionId actionId, string languageCode)
    {
        var isChinese = languageCode == "zh-CN";
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
        return languageCode == "zh-CN" ? "按下一个按键" : "Press a key";
    }

    internal static string ResolveUnsupportedKey(string languageCode)
    {
        return languageCode == "zh-CN" ? "不支持该按键" : "Unsupported key";
    }
}
