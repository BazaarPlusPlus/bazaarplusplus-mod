#nullable enable

using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static partial class HistoryPanelText
{
    private static readonly LocalizedTextSet UnknownOpponentText = new(
        "Unknown Opponent",
        "未知对手"
    );

    private static readonly LocalizedTextSet SelectBattleForFooterText = new(
        "Select a battle to view both boards.",
        "选择一场战斗，查看双方阵容。",
        "選擇一場戰鬥，查看雙方陣容。"
    );

    private static readonly LocalizedTextSet SelectedBattleText = new(
        "Selected",
        "当前战斗",
        "當前戰鬥"
    );

    private static readonly LocalizedTextSet WinText = new("Win", "胜利", "勝利");

    private static readonly LocalizedTextSet LossText = new("Loss", "失败", "失敗");

    private static readonly LocalizedTextSet GhostOpponentEliminatedNoticeText = new(
        "After this battle, the challenger is eliminated.",
        "打完这场战斗后，挑战者直接出局。",
        "打完這場戰鬥後，挑戰者直接出局。"
    );

    private static readonly LocalizedTextSet GhostOpponentEliminatedShortText = new(
        "Challenger Out",
        "挑战者出局",
        "挑戰者出局"
    );

    internal static string UnknownOpponent() => Resolve(UnknownOpponentText);

    internal static string SelectBattleForFooter() => Resolve(SelectBattleForFooterText);

    internal static string SelectedBattle() => Resolve(SelectedBattleText);

    internal static string Win() => Resolve(WinText);

    internal static string Loss() => Resolve(LossText);

    internal static string GhostOpponentEliminatedNotice() =>
        Resolve(GhostOpponentEliminatedNoticeText);

    internal static string GhostOpponentEliminatedShort() =>
        Resolve(GhostOpponentEliminatedShortText);

    internal static string PlayerSideShort() => FormatSimple("YOU", "我方", "我方");

    internal static string OpponentSideShort() => FormatSimple("OPP", "对手", "對手");

    internal static string GhostChallengerSideShort() => FormatSimple("CHA", "挑战者", "挑戰者");

    internal static string GhostDefenderSideShort() => FormatSimple("YOU", "你", "你");

    internal static string GhostChallengedYou(string name) =>
        FormatSimple($"{name} challenged you", $"{name} 挑战了你", $"{name} 挑戰了你");

    internal static string LoadedGhostBattles(int count)
    {
        return FormatSimple($"{count} ghost battles loaded.", $"已载入 {count} 场幽灵对战。");
    }

    internal static string GhostHistoryLoadFailed(string details)
    {
        return FormatSimple(
            $"Ghost history load failed: {details}",
            $"幽灵历史加载失败：{details}"
        );
    }

    internal static string GhostSyncUnavailable()
    {
        return FormatSimple("Ghost sync is unavailable right now.", "幽灵同步暂不可用。");
    }

    internal static string GhostSyncFailed(string details)
    {
        return FormatSimple($"Couldn't sync ghost battles: {details}", $"幽灵同步失败：{details}");
    }

    internal static string GhostSyncSucceeded(int count)
    {
        return FormatSimple($"{count} ghost battles synced.", $"已同步 {count} 场幽灵对战。");
    }

    internal static string GhostDeleteUnavailable()
    {
        return FormatSimple(
            "Ghost battles cannot be deleted from this panel yet.",
            "暂时不能在这个面板里删除幽灵战斗。"
        );
    }

    internal static string ReplayActionAlreadyRunning()
    {
        return FormatSimple("Replay is already being prepared.", "正在准备回放。");
    }

    internal static string DownloadingGhostReplay()
    {
        return FormatSimple("Fetching replay data...", "正在获取回放数据...");
    }

    internal static string StartingReplay()
    {
        return FormatSimple("Opening replay...", "正在启动回放...");
    }

    internal static string ReplayFailed(string details)
    {
        return FormatSimple($"Couldn't start replay: {details}", $"回放失败：{details}");
    }

    internal static string GhostSyncAlreadyRunning()
    {
        return FormatSimple("Ghost sync is already in progress.", "幽灵同步进行中。");
    }

    internal static string SyncingGhostBattles()
    {
        return FormatSimple("Syncing ghost battles...", "正在同步幽灵对战...");
    }

    internal static string BattleLoadFailed(string details)
    {
        return FormatSimple($"Couldn't load battles: {details}", $"载入战斗失败：{details}");
    }

    internal static string SelectBattleToReplay()
    {
        return FormatSimple("Select a battle to replay.", "选择一场战斗进行回放。");
    }

    internal static string CombatReplayRuntimeUnavailable()
    {
        return FormatSimple("Combat replay runtime is unavailable.", "战斗回放运行时不可用。");
    }

    internal static string RecordingUnavailable()
    {
        return FormatSimple(
            "Native video recorder is unavailable.",
            "原生视频录制器不可用。",
            "原生影片錄製器不可用。"
        );
    }

    internal static string GhostReplayPayloadUnavailable()
    {
        return FormatSimple(
            "Replay payload for the selected ghost battle is unavailable.",
            "所选幽灵战斗的回放负载不可用。"
        );
    }

    internal static string ReplayRejectedForBattle(string battleId)
    {
        return FormatSimple(
            $"Replay rejected for battle {battleId}.",
            $"战斗 {battleId} 的回放被拒绝。"
        );
    }

    internal static string StartingReplayForBattle(string battleId)
    {
        return FormatSimple($"Starting replay for {battleId}.", $"正在为 {battleId} 启动回放。");
    }

    internal static string CombatReplayDirectoryUnavailable()
    {
        return FormatSimple(
            "Combat replay directory path is unavailable.",
            "战斗回放目录路径不可用。"
        );
    }

    internal static string GhostReplayDownloadUnavailable()
    {
        return FormatSimple("Ghost replay download is unavailable.", "幽灵回放下载不可用。");
    }

    internal static string FailedToDownloadGhostReplay(string details)
    {
        return FormatSimple(
            $"Failed to download ghost replay: {details}",
            $"下载幽灵回放失败：{details}"
        );
    }

    internal static string GhostManifestUnavailable(string battleId)
    {
        return FormatSimple(
            $"Ghost manifest for battle {battleId} is unavailable.",
            $"战斗 {battleId} 的 ghost manifest 不可用。"
        );
    }

    internal static string ReplayPayloadUnavailable(string battleId)
    {
        return FormatSimple(
            $"Replay payload for battle {battleId} is unavailable.",
            $"战斗 {battleId} 的回放负载不可用。"
        );
    }

    internal static string ReplayRejectedForGhostBattle(string battleId)
    {
        return FormatSimple(
            $"Replay rejected for ghost battle {battleId}.",
            $"幽灵战斗 {battleId} 的回放被拒绝。"
        );
    }

    internal static string DownloadedAndStartingReplay(string battleId)
    {
        return FormatSimple(
            $"Downloaded and starting replay for {battleId}.",
            $"已下载并开始回放 {battleId}。"
        );
    }

    internal static string NoLocallyRenderableCards()
    {
        return FormatSimple(
            "No locally renderable cards were recorded for this selection.",
            "这个选择没有记录可在本地渲染的卡牌。"
        );
    }

    internal static string PreviewRendererInitFailed()
    {
        return FormatSimple("Preview renderer failed to initialize.", "预览渲染器初始化失败。");
    }

    internal static string LoadingPreview()
    {
        return FormatSimple("Loading preview...", "正在加载预览...");
    }
}
