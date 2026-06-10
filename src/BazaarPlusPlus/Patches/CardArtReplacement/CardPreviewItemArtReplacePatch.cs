#nullable enable
#pragma warning disable CS0436
using System;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.CardArtReplacement;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.GameInterop.CardArtReplacement;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar.UI;

namespace BazaarPlusPlus.Patches.CardArtReplacement;

[HarmonyPatch(typeof(CardPreviewItem), "LoadArt")]
internal static class CardPreviewItemArtReplacePatch
{
    private const string LogCategory = "CardArtReplacement";

    [HarmonyPostfix]
    private static void Postfix(CardPreviewItem __instance, ref Task __result)
    {
        var marker = __instance.GetComponent<CollectionPanelOwnedMarker>();
        if (marker == null || __result == null)
            return;

        __result = ApplyAfterLoad(__instance, __result);
    }

    private static async Task ApplyAfterLoad(CardPreviewItem instance, Task loadArtTask)
    {
        await loadArtTask;

        try
        {
            if (instance == null || instance.gameObject == null)
                return;

            var services = BppPatchHost.Services;
            if (!PackageCardArtReplacementPolicy.IsEnabled(services.Config))
                return;

            var card = instance._cardData;
            if (!CardArtInjector.IsPackageTemplate(card))
                return;

            var baseMaterial = instance._cardMaterial;
            if (baseMaterial == null)
                return;

            var feature = CardArtReplacementFeature.Current;
            if (
                feature == null
                || !feature.TryGetPreviewMaterial(
                    card.Id,
                    baseMaterial,
                    out var customMaterial,
                    out _
                )
                || customMaterial == null
            )
                return;

            instance._cardMaterial = customMaterial;
            if (instance._cardImage != null)
                instance._cardImage.material = customMaterial;
        }
        catch (Exception ex)
        {
            BppLog.Warn(LogCategory, $"Preview postfix failed: {ex.Message}");
        }
    }
}
