#pragma warning disable CS0436
#nullable enable
using System;
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches.Lobby;

[HarmonyPatch(typeof(CosmeticsListManager), "RefreshView")]
internal static class RandomHeroSkinPoolRefreshViewPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        CosmeticsListManager __instance,
        BazaarInventoryTypes.ECollectionType cosmeticType,
        EHero hero
    )
    {
        try
        {
            RandomHeroSkinPoolPanelController.Attach(__instance, cosmeticType, hero);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "RandomHeroSkinPool",
                $"Failed to refresh random collectible pool UI: {ex}"
            );
        }
    }
}

[HarmonyPatch(typeof(CosmeticsListManager), "OnRandomizeToggleChanged")]
internal static class RandomHeroSkinPoolTogglePatch
{
    // PTR removed this method (the randomize toggle moved to CosmeticsPanelController),
    // so probe before patching and skip cleanly instead of aborting with
    // "Undefined target method". Prepare must not throw: a throwing Prepare is a hard
    // patch-class failure, not a skip.
    [HarmonyPrepare]
    private static bool Prepare()
    {
        try
        {
            if (
                AccessTools.DeclaredMethod(typeof(CosmeticsListManager), "OnRandomizeToggleChanged")
                != null
            )
                return true;
        }
        catch (Exception ex)
        {
            BppLog.Warn("RandomHeroSkinPool", $"Toggle patch target probe failed: {ex}");
            return false;
        }

        BppLog.Warn(
            "RandomHeroSkinPool",
            "CosmeticsListManager.OnRandomizeToggleChanged not found (PTR build); random skin pool toggle disabled."
        );
        return false;
    }

    [HarmonyPostfix]
    private static void Postfix(CosmeticsListManager __instance)
    {
        try
        {
            RandomHeroSkinPoolPanelController.NotifyRandomizeChanged(__instance);
        }
        catch (Exception ex)
        {
            BppLog.Warn("RandomHeroSkinPool", $"Failed to update random collectible pool UI: {ex}");
        }
    }
}

[HarmonyPatch(typeof(CosmeticsListManager), "EquipItem")]
internal static class RandomHeroSkinPoolEquipItemPatch
{
    [HarmonyPostfix]
    private static void Postfix(CosmeticsListManager __instance, object[] __args)
    {
        try
        {
            if (__args.Length == 0 || __args[0] is not EquipableItem item)
                return;

            var itemData = item.itemData;
            var hero = item.hero;
            var collectionType = itemData.CollectionType;
            if (!RandomHeroSkinPoolRuntime.IsSupported(collectionType))
                return;

            var collectionManager = TheBazaar.AppFramework.Services.Get<CollectionManager>();
            if (collectionManager == null)
                return;

            RandomHeroSkinPoolRuntime.EnsureSelected(
                hero,
                collectionType,
                itemData.CollectionItemID,
                collectionManager
            );
            RandomHeroSkinPoolPanelController.NotifyCollectibleSelected(
                __instance,
                collectionType,
                hero,
                itemData.CollectionItemID
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "RandomHeroSkinPool",
                $"Failed to sync clicked collectible into random pool selection: {ex}"
            );
        }
    }
}

[HarmonyPatch(typeof(CollectionManager), "GetRandomizedLoadout")]
internal static class RandomHeroSkinPoolGetRandomizedLoadoutPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        CollectionManager __instance,
        EHero hero,
        ref EquipLoadoutRequest __result
    )
    {
        try
        {
            __result ??= new EquipLoadoutRequest();
            RandomHeroSkinPoolRuntime.ApplyToRandomizedLoadout(hero, __instance, __result);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "RandomHeroSkinPool",
                $"Failed to apply random collectible pool selection: {ex}"
            );
        }
    }
}
