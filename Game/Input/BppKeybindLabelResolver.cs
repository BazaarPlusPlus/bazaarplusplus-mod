using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus;

internal static class BppKeybindLabelResolver
{
    internal static string ResolveActionLabel(BppHotkeyActionId actionId, string languageCode)
    {
        return actionId switch
        {
            BppHotkeyActionId.HoldEnchantPreview => ResolveLocalizedLabel(
                languageCode,
                "Show Enchant Preview",
                "显示附魔预览",
                "Verzauberungsvorschau anzeigen",
                "Mostrar previa de encantamento",
                "마법부여 미리보기 표시",
                "Mostra anteprima incantamento"
            ),
            BppHotkeyActionId.HoldUpgradePreview => ResolveLocalizedLabel(
                languageCode,
                "Show Upgrade Preview",
                "显示升级预览",
                "Upgrade-Vorschau anzeigen",
                "Mostrar previa de upgrade",
                "업그레이드 미리보기 표시",
                "Mostra anteprima upgrade"
            ),
            _ => actionId.ToString(),
        };
    }

    internal static string ResolveRebindPrompt(string languageCode)
    {
        return ResolveLocalizedLabel(
            languageCode,
            "Press a key",
            "按下一个按键",
            "Taste druecken",
            "Pressione uma tecla",
            "키를 누르세요",
            "Premi un tasto"
        );
    }

    internal static string ResolveUnsupportedKey(string languageCode)
    {
        return ResolveLocalizedLabel(
            languageCode,
            "Unsupported key",
            "不支持该按键",
            "Nicht unterstuetzte Taste",
            "Tecla nao suportada",
            "지원되지 않는 키",
            "Tasto non supportato"
        );
    }

    private static string ResolveLocalizedLabel(
        string languageCode,
        string english,
        string simplifiedChinese,
        string german,
        string portuguese,
        string korean,
        string italian
    )
    {
        if (LanguageCodeMatcher.IsSimplifiedChinese(languageCode))
            return simplifiedChinese;
        if (LanguageCodeMatcher.IsGerman(languageCode))
            return german;
        if (LanguageCodeMatcher.IsPortuguese(languageCode))
            return portuguese;
        if (LanguageCodeMatcher.IsKorean(languageCode))
            return korean;
        if (LanguageCodeMatcher.IsItalian(languageCode))
            return italian;

        return english;
    }
}
