#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatLog;

internal sealed class CombatLogController : MonoBehaviour
{
    public static CombatLogController? Instance { get; private set; }

    public CombatLogRuntime Runtime { get; private set; } = null!;

    private void Awake()
    {
        Instance = this;
        Runtime = new CombatLogRuntime();
        Events.CombatSimReceived.AddListener(OnCombatSimReceived, this);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        Events.CombatSimReceived.RemoveListener(OnCombatSimReceived);
    }

    private void OnCombatSimReceived(
        BazaarGameShared.Infra.Messages.CombatSimEvents.CombatSim combatSim
    )
    {
        if (combatSim == null)
        {
            Runtime.Clear();
            return;
        }

        Runtime.ReplaceCombat(combatSim, ResolvePlaybackPass());
    }

    private static CombatLogPlaybackPass ResolvePlaybackPass()
    {
        return CombatReplayRuntime.Instance?.IsReplayPlaybackActive == true
            ? CombatLogPlaybackPass.Replay
            : CombatLogPlaybackPass.FirstPlay;
    }
}
