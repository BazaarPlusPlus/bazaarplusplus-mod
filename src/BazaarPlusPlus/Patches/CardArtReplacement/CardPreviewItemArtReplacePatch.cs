#nullable enable
#pragma warning disable CS0436
using System;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
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

            if (
                instance.GetComponent<CollectionPanelOwnedMarker>() != null
                && TryApplyBppCustomCardMaterial(instance)
            )
                return;

            if (
                !PackageCardArtPatchGate.TryGetReplacementPreviewMaterial(
                    instance._cardData,
                    instance._cardMaterial,
                    out var customMaterial
                )
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

    private static bool TryApplyBppCustomCardMaterial(CardPreviewItem instance)
    {
        // Base material is the authored donor material the LoadArt prefix already cloned onto
        // _cardMaterial (the donor ArtKey lives on the synthetic template). Clone it and swap
        // _BaseMap to the achievement art via the shared package clone cache. Do NOT touch
        // marker.CurrentArtKey: it holds the donor key, owned and released by the prefix /
        // destroy patch. Do NOT clear enchantment keywords here — that is exactly what made
        // the bare path strobe.
        if (instance._cardMaterial == null || instance._cardImage == null)
            return false;

        if (
            !PackageCardArtPatchGate.TryGetBppCustomCardPreviewMaterial(
                instance._cardData,
                instance._cardMaterial,
                out var material
            )
            || material == null
        )
            return false;

        instance._cardMaterial = material;
        instance._cardImage.material = material;
        return true;
    }
}
