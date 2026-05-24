#nullable enable
using System;
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryBattleRecord
{
    public HistoryBattleRecord(
        string battleId,
        string runId,
        DateTimeOffset recordedAtUtc,
        int? day,
        int? hour,
        string? encounterId,
        string? playerHero,
        string? playerRank,
        int? playerRating,
        int? playerLevel,
        string? opponentName,
        string? opponentHero,
        string? opponentRank,
        int? opponentRating,
        int? opponentLevel,
        string? opponentAccountId,
        string? combatKind,
        string? result,
        string? winnerCombatantId,
        string? loserCombatantId,
        HistoryBattleSnapshotCounts snapshotCounts,
        PvpBattleSnapshots? snapshots,
        bool isBundleFinalBattle,
        HistoryBattleSource source,
        bool replayAvailable,
        bool replayDownloaded
    )
    {
        BattleId = battleId;
        RunId = runId;
        RecordedAtUtc = recordedAtUtc;
        Day = day;
        Hour = hour;
        EncounterId = encounterId;
        PlayerHero = playerHero;
        PlayerRank = playerRank;
        PlayerRating = playerRating;
        PlayerLevel = playerLevel;
        OpponentName = opponentName;
        OpponentHero = opponentHero;
        OpponentRank = opponentRank;
        OpponentRating = opponentRating;
        OpponentLevel = opponentLevel;
        OpponentAccountId = opponentAccountId;
        CombatKind = combatKind;
        Result = result;
        WinnerCombatantId = winnerCombatantId;
        LoserCombatantId = loserCombatantId;
        SnapshotCounts = snapshotCounts;
        Snapshots = snapshots;
        IsBundleFinalBattle = isBundleFinalBattle;
        Source = source;
        ReplayAvailable = replayAvailable;
        ReplayDownloaded = replayDownloaded;
    }

    public string BattleId { get; }

    public string RunId { get; }

    public DateTimeOffset RecordedAtUtc { get; }

    public int? Day { get; }

    public int? Hour { get; }

    public string? EncounterId { get; }

    public string? PlayerHero { get; }

    public string? PlayerRank { get; }

    public int? PlayerRating { get; }

    public int? PlayerLevel { get; }

    public string? OpponentName { get; }

    public string? OpponentHero { get; }

    public string? OpponentRank { get; }

    public int? OpponentRating { get; }

    public int? OpponentLevel { get; }

    public string? OpponentAccountId { get; }

    public string? CombatKind { get; }

    public string? Result { get; }

    public string? WinnerCombatantId { get; }

    public string? LoserCombatantId { get; }

    public HistoryBattleSnapshotCounts SnapshotCounts { get; }

    public int PlayerHandItemCount => SnapshotCounts.PlayerHandItemCount;

    public int PlayerSkillCount => SnapshotCounts.PlayerSkillCount;

    public int OpponentHandItemCount => SnapshotCounts.OpponentHandItemCount;

    public int OpponentSkillCount => SnapshotCounts.OpponentSkillCount;

    // The raw card-set captures used to project a card preview; null for ghost-list rows where
    // snapshots live in a separate payload file the repository does not read.
    public PvpBattleSnapshots? Snapshots { get; }

    public bool IsBundleFinalBattle { get; }

    public HistoryBattleSource Source { get; }

    public bool ReplayAvailable { get; }

    public bool ReplayDownloaded { get; }
}

internal enum HistoryBattleSource
{
    Local,
    Ghost,
}
