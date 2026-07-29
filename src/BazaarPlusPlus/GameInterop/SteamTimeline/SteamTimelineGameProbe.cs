#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Runs;
using BazaarGameShared.Infra.Messages;
using TheBazaar;

namespace BazaarPlusPlus.GameInterop.SteamTimeline;

internal sealed class SteamTimelineGameProbe
{
    internal bool IsReplayPlayback => AppState.CurrentState is ReplayState;

    internal SteamTimelineBattleContext? TryCreateBattleContext(NetMessageGameSim message)
    {
        if (message?.Data?.CurrentState?.StateName != ERunState.PVPCombat)
            return null;

        try
        {
            var day = unchecked((int)message.Data.Run.Day);
            var playerHero = Data.Run?.Player?.Hero.ToString();
            var opponent = message.Data.CurrentState.PvpOpponent;
            var opponentHero = opponent?.Hero.ToString() ?? Data.Run?.Opponent?.Hero.ToString();
            return new SteamTimelineBattleContext(
                day > 0 ? $"pvp-day-{day}" : Guid.NewGuid().ToString("N"),
                day > 0 ? day : null,
                playerHero,
                opponentHero
            );
        }
        catch
        {
            return new SteamTimelineBattleContext(
                Guid.NewGuid().ToString("N"),
                day: null,
                playerHero: null,
                opponentHero: null
            );
        }
    }

    internal SteamTimelineBattleResult ResolveBattleResult(NetMessageCombatSim message)
    {
        if (message?.Data?.Winner == ECombatantId.Player)
            return SteamTimelineBattleResult.Victory;
        if (message?.Data?.Loser == ECombatantId.Player)
            return SteamTimelineBattleResult.Defeat;
        return SteamTimelineBattleResult.Unknown;
    }

    internal SteamTimelineRunSummary ReadRunSummary()
    {
        try
        {
            var run = Data.Run;
            if (run == null)
                return default;

            return new SteamTimelineRunSummary(
                unchecked((int)run.Day),
                unchecked((int)run.Victories),
                run.Player?.Hero.ToString()
            );
        }
        catch
        {
            return default;
        }
    }
}
