#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.BilingualItemNames;

internal static class BilingualItemNamesSettingsDockEntry
{
    private static readonly LocalizedTextSet Labels = new(
        "Bilingual Item Names",
        "双语物品名",
        "雙語物品名",
        "Zweisprachige Gegenstandsnamen",
        "Nomes Bilíngues de Itens",
        "이중 언어 아이템 이름",
        "Nomi Oggetto Bilingue"
    );

    internal static CyclingSettingsDockEntry<bool> Create() =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.BilingualItemNames,
            "BilingualItemNames",
            languageCode => Labels.Resolve(languageCode, L.CurrentMode),
            config => config.EnableBilingualItemNamesConfig?.Value ?? false,
            (config, enabled) =>
            {
                var entry = config.EnableBilingualItemNamesConfig;
                if (entry != null)
                    entry.Value = enabled;
            }
        );
}
