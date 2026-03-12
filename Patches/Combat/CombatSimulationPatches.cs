#pragma warning disable CS0436
using System.Threading;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus;

// Combat sim: capture win/loss result
[HarmonyPatch(typeof(CombatSimHandler), "Simulate")]
class CombatSimPatch
{
    [HarmonyPrefix]
    static void Prefix(NetMessageCombatSim message, CancellationTokenSource cancellationToken)
    {
        if (ModState.LastMessageId == message.MessageId)
            return;
        ModState.LastMessageId = message.MessageId;
        ModState.SetCombatFrameTotal(message.Data?.Frames?.Count ?? 0);
        ModState.LastVictoryCondition =
            message.Data.Winner == ECombatantId.Player
                ? EVictoryCondition.Win
                : EVictoryCondition.Lose;
    }
}

[HarmonyPatch(typeof(FinalBlowSlowDownController), nameof(FinalBlowSlowDownController.Process))]
class CombatFrameAdvancePatch
{
    [HarmonyPostfix]
    static void Postfix()
    {
        ModState.AdvanceCombatFrame();
    }
}
