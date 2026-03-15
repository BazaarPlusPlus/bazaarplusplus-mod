#pragma warning disable CS0436
using BazaarGameShared.Infra.Messages;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(NetMessageProcessor), "ReceiveOrQueue")]
internal static class RunInitializedPatch
{
    [HarmonyPrefix]
    private static void Prefix(INetMessage message)
    {
        if (message is not NetMessageRunInitialized runInitialized)
            return;

        ModState.CurrentServerRunId = runInitialized.RunId;
        BppLog.Info("RunLogging", $"Captured server run id: {runInitialized.RunId}");
    }
}
