#pragma warning disable CS0436
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus;

// Combat speed: set speed to the combat speed multiplier
[HarmonyPatch(typeof(CombatSimHandler), "SetSpeed")]
class CombatSpeedPatch
{
    [HarmonyPrefix]
    static void Prefix(ref float speed)
    {
        if (!CombatStatusBar.IsCombatPlaybackActive)
            return;

        speed = CombatStatusBar.CombatSpeedMultiplier;
    }
}
