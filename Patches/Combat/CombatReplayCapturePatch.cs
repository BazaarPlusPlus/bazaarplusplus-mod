#pragma warning disable CS0436
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Game.CombatReplay;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(NetMessageProcessor), "ReceiveOrQueue")]
internal static class CombatReplayCapturePatch
{
    [HarmonyPostfix]
    private static void Postfix(INetMessage message)
    {
        CombatReplayRuntime.Instance?.ObserveMessage(message);
    }
}
