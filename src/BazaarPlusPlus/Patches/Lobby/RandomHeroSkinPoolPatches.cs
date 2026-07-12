#pragma warning disable CS0436
#nullable enable
using System;
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches.Lobby;

[HarmonyPatch(typeof(CosmeticsListManager), "FetchCosmetics")]
internal static class RandomHeroSkinPoolFetchPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        CosmeticsListManager __instance,
        BazaarInventoryTypes.ECollectionType cosmeticType,
        EHero hero,
        out RandomHeroSkinPoolNativeController.FetchScope __state
    )
    {
        try
        {
            __state = RandomHeroSkinPoolNativeController.BeginFetch(__instance, cosmeticType, hero);
        }
        catch (Exception ex)
        {
            __state = default;
            BppLog.Warn("RandomHeroSkinPool", $"Failed to begin native collectible session: {ex}");
        }
    }

    [HarmonyPostfix]
    private static void Postfix(RandomHeroSkinPoolNativeController.FetchScope __state)
    {
        try
        {
            RandomHeroSkinPoolNativeController.CompleteFetch(__state);
        }
        catch (Exception ex)
        {
            BppLog.Warn("RandomHeroSkinPool", $"Failed to project native collectible cards: {ex}");
        }
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(
        RandomHeroSkinPoolNativeController.FetchScope __state,
        Exception? __exception
    )
    {
        try
        {
            RandomHeroSkinPoolNativeController.RestoreFetchScope(__state);
        }
        catch (Exception ex)
        {
            BppLog.Warn("RandomHeroSkinPool", $"Failed to close native collectible session: {ex}");
        }
        return __exception;
    }
}

[HarmonyPatch(typeof(CosmeticItem), nameof(CosmeticItem.SetData))]
internal static class RandomHeroSkinPoolSetDataPatch
{
    [HarmonyPostfix]
    private static void Postfix(CosmeticItem __instance, BazaarSaleItem data, EHero hero)
    {
        try
        {
            RandomHeroSkinPoolNativeController.RegisterActiveFetchItem(__instance, data, hero);
        }
        catch (Exception ex)
        {
            BppLog.Warn("RandomHeroSkinPool", $"Failed to register native collectible card: {ex}");
        }
    }
}

[HarmonyPatch(typeof(CosmeticItem), "SetEquipState")]
internal static class RandomHeroSkinPoolSetEquipStatePatch
{
    [HarmonyPrefix]
    private static void Prefix(CosmeticItem __instance, ref bool state)
    {
        try
        {
            RandomHeroSkinPoolNativeController.OverrideEquipVisual(__instance, ref state);
        }
        catch (Exception ex)
        {
            BppLog.Warn("RandomHeroSkinPool", $"Failed to project native collectible visual: {ex}");
        }
    }
}

[HarmonyPatch(typeof(CosmeticsListManager), "EquipItem")]
internal static class RandomHeroSkinPoolEquipItemPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CosmeticsListManager __instance, object[] __args, out bool __state)
    {
        __state = false;
        try
        {
            if (__args.Length == 0 || __args[0] is not EquipableItem item)
                return true;

            var route = RandomHeroSkinPoolNativeController.RouteClick(__instance, item);
            if (!NativePoolInteractionRouting.ShouldRunNativeAction(route))
                return false;

            __state = RandomHeroSkinPoolRuntime.IsSupported(item.itemData.CollectionType);
            return true;
        }
        catch (Exception ex)
        {
            BppLog.Warn("RandomHeroSkinPool", $"Failed to route native collectible click: {ex}");
            return true;
        }
    }

    [HarmonyPostfix]
    private static void Postfix(object[] __args, bool __state)
    {
        if (!__state || __args.Length == 0 || __args[0] is not EquipableItem item)
            return;

        try
        {
            var collectionManager = TheBazaar.AppFramework.Services.Get<CollectionManager>();
            if (collectionManager == null)
                return;

            RandomHeroSkinPoolRuntime.EnsureSelected(
                item.hero,
                item.itemData.CollectionType,
                item.itemData.CollectionItemID,
                collectionManager
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "RandomHeroSkinPool",
                $"Failed to keep the normally equipped collectible in its random pool: {ex}"
            );
        }
    }
}

[HarmonyPatch(
    typeof(CollectionManager),
    nameof(CollectionManager.SetRandomizeLoadout),
    [typeof(EHero), typeof(bool)]
)]
internal static class RandomHeroSkinPoolTogglePatch
{
    [HarmonyPostfix]
    private static void Postfix(EHero hero)
    {
        try
        {
            RandomHeroSkinPoolNativeController.NotifyRandomizeChanged(hero);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "RandomHeroSkinPool",
                $"Failed to restore native collectible visuals: {ex}"
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
