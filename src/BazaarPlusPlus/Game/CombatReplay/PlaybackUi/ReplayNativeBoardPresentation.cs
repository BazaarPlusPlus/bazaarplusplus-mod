#nullable enable

using BazaarPlusPlus.Infrastructure;
using TheBazaar;

namespace BazaarPlusPlus.Game.CombatReplay.PlaybackUi;

internal static class ReplayNativeBoardPresentation
{
    internal static async Task RebuildOpponentCollectiblesAsync()
    {
        var boardManager = Singleton<BoardManager>.Instance;
        if (boardManager == null || Data.SimPvpOpponent == null)
            return;

        // A saved replay skips PVPCombatState, including its transition-owned cleanup and
        // collectible load. LoadOpponentCollectibles only checks whether its private cache is
        // non-null, so an old presentation can otherwise make this call a successful no-op.
        boardManager.SetPortraitFrame(isCombat: true, force: true);
        boardManager.TryClearOpponentCollectables();
        await Task.Yield();
        Data.UpdateOpponentCollectibles();
        await boardManager.LoadOpponentCollectibles();
        SetOpponentStashVisible(boardManager, isVisible: true);
    }

    internal static bool ShouldShowOpponentBank(bool replayControlsVisible)
    {
        return !replayControlsVisible;
    }

    internal static void Normalize(bool replayControlsVisible)
    {
        var boardManager = Singleton<BoardManager>.Instance;
        if (boardManager == null)
            return;

        boardManager.SetPortraitFrame(isCombat: true, force: true);
        AlignTemporaryOpponentPortrait(boardManager);
        boardManager.HideBoardButtons();
        SetOpponentStashVisible(boardManager, isVisible: true);
        SetOpponentBankVisible(boardManager, ShouldShowOpponentBank(replayControlsVisible));
    }

    internal static void Observe(string? battleId)
    {
        var boardManager = Singleton<BoardManager>.Instance;
        var loadout = Data.SimPvpOpponent?.PlayerLoadout;
        var stash = FindOpponentStash(boardManager);
        var bank = FindOpponentBank(boardManager);
        var portrait = PlaybackUiState.ActiveOpponentPortrait;
        var portraitAnchor = boardManager?.GetAnchor(AnchorSide.Opponent, AnchorType.Portrait);

        BppLog.DebugEvent(
            CombatReplayLogEvents.NativePvpPresentationObserved,
            () =>
                [
                    CombatReplayLogEvents.NativePvpPresentationBattleId.Bind(battleId),
                    CombatReplayLogEvents.NativePvpPresentationHasStashId.Bind(
                        !string.IsNullOrWhiteSpace(loadout?.stashId)
                    ),
                    CombatReplayLogEvents.NativePvpPresentationHasBankId.Bind(
                        !string.IsNullOrWhiteSpace(loadout?.bankId)
                    ),
                    CombatReplayLogEvents.NativePvpPresentationCollectionCount.Bind(
                        Data.SimPvpOpponent?.PlayerCollection?.Count ?? 0
                    ),
                    CombatReplayLogEvents.NativePvpPresentationStashLoaded.Bind(stash != null),
                    CombatReplayLogEvents.NativePvpPresentationStashActive.Bind(
                        stash?.gameObject.activeInHierarchy == true
                    ),
                    CombatReplayLogEvents.NativePvpPresentationBankLoaded.Bind(bank != null),
                    CombatReplayLogEvents.NativePvpPresentationBankActive.Bind(
                        bank?.gameObject.activeInHierarchy == true
                    ),
                    CombatReplayLogEvents.NativePvpPresentationPortraitLoaded.Bind(
                        portrait != null
                    ),
                    CombatReplayLogEvents.NativePvpPresentationPortraitActive.Bind(
                        portrait?.gameObject.activeInHierarchy == true
                    ),
                    CombatReplayLogEvents.NativePvpPresentationPortraitAnchored.Bind(
                        portrait != null && portrait.transform.parent == portraitAnchor
                    ),
                ]
        );
    }

    private static void AlignTemporaryOpponentPortrait(BoardManager boardManager)
    {
        var portrait = PlaybackUiState.ActiveOpponentPortrait;
        var combatAnchor = boardManager.GetAnchor(AnchorSide.Opponent, AnchorType.Portrait);
        if (portrait == null || combatAnchor == null)
            return;

        // BoardBuilder.LoadHeroPortraitAsync uses the same worldPositionStays=false contract.
        // Normally this portrait is now created after ReplayState enters; keep the reparent as
        // a defensive normalization for delayed frame changes and older in-flight starts.
        if (portrait.transform.parent != combatAnchor)
            portrait.transform.SetParent(combatAnchor, worldPositionStays: false);

        if (Data.CurrentEncounterController != null)
            Data.CurrentEncounterController.ShowCard(show: false);

        portrait.gameObject.SetActive(true);
        portrait.ShowCard(show: true);
    }

    private static void SetOpponentBankVisible(BoardManager boardManager, bool isVisible)
    {
        var opponentBank = FindOpponentBank(boardManager);
        if (opponentBank != null && opponentBank.gameObject.activeSelf != isVisible)
            opponentBank.gameObject.SetActive(isVisible);
    }

    private static void SetOpponentStashVisible(BoardManager boardManager, bool isVisible)
    {
        var opponentStash = FindOpponentStash(boardManager);
        if (opponentStash != null && opponentStash.gameObject.activeSelf != isVisible)
            opponentStash.gameObject.SetActive(isVisible);
    }

    private static BankToyController? FindOpponentBank(BoardManager? boardManager)
    {
        var bankAnchor = boardManager?.GetAnchor(AnchorSide.Opponent, AnchorType.Bank);
        return bankAnchor?.GetComponentInChildren<BankToyController>(includeInactive: true);
    }

    private static StorageToy? FindOpponentStash(BoardManager? boardManager)
    {
        var stashAnchor = boardManager?.GetAnchor(AnchorSide.Opponent, AnchorType.Stash);
        return stashAnchor?.GetComponentInChildren<StorageToy>(includeInactive: true);
    }
}
