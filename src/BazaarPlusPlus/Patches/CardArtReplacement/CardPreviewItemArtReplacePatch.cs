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

            var marker = instance.GetComponent<CollectionPanelOwnedMarker>();
            if (marker != null && TryApplyBppCustomCardMaterial(instance, marker))
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

    private static bool TryApplyBppCustomCardMaterial(
        CardPreviewItem instance,
        CollectionPanelOwnedMarker marker
    )
    {
        if (
            !PackageCardArtPatchGate.TryGetBppCustomCardTexture(instance._cardData, out var texture)
            || texture == null
        )
            return false;

        if (instance._cardMaterialShader == null || instance._cardImage == null)
            return false;

        var materialCache = CollectionCardCacheHost.MaterialCache;
        if (materialCache == null)
            return false;

        var artKey = $"bpp-custom:{instance._cardData.Id}";
        if (!string.Equals(marker.CurrentArtKey, artKey, StringComparison.Ordinal))
        {
            ReleaseCurrentArtKey(marker);
            materialCache.Acquire(artKey);
        }

        var material = materialCache.GetOrCreate(artKey, texture, instance._cardMaterialShader);
        if (material == null)
        {
            if (string.IsNullOrEmpty(marker.CurrentArtKey))
                materialCache.Release(artKey);
            return false;
        }

        marker.CurrentArtKey = artKey;
        instance._cardMaterial = material;
        instance._cardImage.material = material;
        return true;
    }

    private static void ReleaseCurrentArtKey(CollectionPanelOwnedMarker marker)
    {
        if (string.IsNullOrEmpty(marker.CurrentArtKey))
            return;

        CollectionCardCacheHost.ArtCache?.Release(marker.CurrentArtKey!);
        CollectionCardCacheHost.MaterialCache?.Release(marker.CurrentArtKey!);
        marker.CurrentArtKey = null;
    }
}
