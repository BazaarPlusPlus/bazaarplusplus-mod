using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.Input;

internal static class BppKeybindLabelResolver
{
    private static readonly LocalizedTextSet EnchantPreviewLabel = new(
        "Show Enchant Preview",
        "\u663e\u793a\u9644\u9b54\u9884\u89c8",
        "Verzauberungsvorschau anzeigen",
        "Mostrar previa de encantamento",
        "\ub9c8\ubc95\ubd80\uc5ec \ubbf8\ub9ac\ubcf4\uae30 \ud45c\uc2dc",
        "Mostra anteprima incantamento"
    );

    private static readonly LocalizedTextSet UpgradePreviewLabel = new(
        "Show Upgrade Preview",
        "\u663e\u793a\u5347\u7ea7\u9884\u89c8",
        "Upgrade-Vorschau anzeigen",
        "Mostrar previa de upgrade",
        "\uac15\ud654 \ubbf8\ub9ac\ubcf4\uae30 \ud45c\uc2dc",
        "Mostra anteprima upgrade"
    );

    private static readonly LocalizedTextSet RebindPrompt = new(
        "Press a key or mouse button",
        "\u6309\u4e0b\u4e00\u4e2a\u952e\u6216\u9f20\u6807\u6309\u94ae",
        "Taste oder Maustaste druecken",
        "Pressione uma tecla ou botao do mouse",
        "\ud0a4 \ub610\ub294 \ub9c8\uc6b0\uc2a4 \ubc84\ud2bc\uc744 \ub204\ub974\uc138\uc694",
        "Premi un tasto o un pulsante del mouse"
    );

    private static readonly LocalizedTextSet UnsupportedKey = new(
        "Unsupported key",
        "\u4e0d\u652f\u6301\u8be5\u6309\u952e",
        "Nicht unterstuetzte Taste",
        "Tecla nao suportada",
        "\uc9c0\uc6d0\ub418\uc9c0 \uc54a\ub294 \ud0a4",
        "Tasto non supportato"
    );

    internal static string ResolveActionLabel(BppHotkeyActionId actionId, string languageCode)
    {
        return actionId switch
        {
            BppHotkeyActionId.HoldEnchantPreview => EnchantPreviewLabel.Resolve(languageCode),
            BppHotkeyActionId.HoldUpgradePreview => UpgradePreviewLabel.Resolve(languageCode),
            _ => actionId.ToString(),
        };
    }

    internal static string ResolveRebindPrompt(string languageCode)
    {
        return RebindPrompt.Resolve(languageCode);
    }

    internal static string ResolveUnsupportedKey(string languageCode)
    {
        return UnsupportedKey.Resolve(languageCode);
    }
}
