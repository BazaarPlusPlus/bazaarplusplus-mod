#nullable enable
#pragma warning disable CS0436
using System;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.Game.CardArtReplacement;
using BazaarPlusPlus.GameInterop.CardArtReplacement;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar.Game.CardFrames;

namespace BazaarPlusPlus.Patches.CardArtReplacement;

[HarmonyPatch(
    typeof(ItemVisualsController),
    "Setup",
    new[] { typeof(Card), typeof(BazaarCollectionLoadout) }
)]
internal static class ItemVisualsSetupCardArtIdentityPatch
{
    [HarmonyPrefix]
    private static void Prefix(ItemVisualsController __instance, Card card)
    {
        CardArtInjector.TrackCard(__instance, card);
    }
}

[HarmonyPatch(typeof(ItemVisualsController), "SetCardFrameMaterial")]
internal static class ItemVisualsArtReplacePatch
{
    private const string LogCategory = "CardArtReplacement";

    [HarmonyPostfix]
    private static void Postfix(ItemVisualsController __instance)
    {
        try
        {
            var services = BppPatchHost.Services;
            if (!PackageCardArtReplacementPolicy.IsEnabled(services.Config))
                return;

            if (__instance == null || __instance.gameObject == null)
                return;
            if (!__instance.gameObject.activeInHierarchy)
                return;

            if (!CardArtInjector.TryResolveCard(__instance, out var card) || card == null)
                return;

            if (!CardArtInjector.IsPackageCard(card))
                return;

            var feature = CardArtReplacementFeature.Current;
            UnityEngine.Texture2D? texture = null;
            var hasTexture =
                feature != null && feature.TryGetTexture(card.TemplateId, out texture, out _);

            if (!hasTexture || texture == null)
                return;

            CardArtInjector.Apply(__instance, texture);
        }
        catch (Exception ex)
        {
            BppLog.Warn(LogCategory, $"Postfix failed: {ex.Message}");
        }
    }
}
