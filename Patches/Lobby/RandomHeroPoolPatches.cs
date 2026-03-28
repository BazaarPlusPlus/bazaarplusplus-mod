#pragma warning disable CS0436
#nullable enable
using System;
using BazaarPlusPlus.Game.Lobby.RandomHeroPool;
using HarmonyLib;
using TheBazaar.UI;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(HeroSelectButtonsView), "Awake")]
internal static class RandomHeroPoolAwakePatch
{
    [HarmonyPostfix]
    private static void Postfix(HeroSelectButtonsView __instance)
    {
        AttachWithGuard(__instance);
    }

    private static void AttachWithGuard(HeroSelectButtonsView instance)
    {
        try
        {
            RandomHeroPoolPanelController.Attach(instance);
        }
        catch (Exception ex)
        {
            BppLog.Warn("RandomHeroPool", $"Failed to attach random hero pool panel: {ex}");
        }
    }
}
