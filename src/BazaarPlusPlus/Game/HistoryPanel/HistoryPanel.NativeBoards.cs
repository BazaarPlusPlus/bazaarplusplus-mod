#nullable enable
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.GameInterop.MonsterBoardPreview;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
    private OwnedMonsterBoardPreview? _nativePlayerBoard;
    private OwnedMonsterBoardPreview? _nativeOpponentBoard;
    private Rect _opponentBounds;

    private void RefreshNativeHistoryBoards()
    {
        if (!IsVisible || _uiView == null)
            return;
        _nativePlayerBoard ??= CreateNativeHistoryBoard(false);
        if (_hasPreviewContainerBounds)
            _nativePlayerBoard.SetBounds(_previewContainerBounds);
        var battle = ActiveSelectedBattle;
        var snapshots =
            _state.SectionMode == HistorySectionMode.Ghost && battle != null
                ? ResolveGhostSnapshots(battle)
                : battle?.Snapshots;
        var player = HistoryBattlePreviewProjection.BuildPlayer(
            snapshots,
            $"player:{battle?.BattleId}"
        );
        var playerSkills = snapshots?.PlayerSkills.Items;
        _nativePlayerBoard.Render(
            $"{player.Signature}:{player.Board.Cards.Count}:{playerSkills?.Count}",
            NativeHistoryItems(player.Board),
            NativeHistorySkills(playerSkills)
        );
        if (_uiView.ShowsBothBoards)
        {
            _nativeOpponentBoard ??= CreateNativeHistoryBoard(true);
            if (_opponentBounds.width > 1)
                _nativeOpponentBoard.SetBounds(_opponentBounds);
            var opponent = HistoryBattlePreviewProjection.BuildOpponent(
                snapshots,
                $"opponent:{battle?.BattleId}"
            );
            var skills = snapshots?.OpponentSkills.Items;
            _nativeOpponentBoard.Render(
                $"{opponent.Signature}:{opponent.Board.Cards.Count}:{skills?.Count}",
                NativeHistoryItems(opponent.Board),
                NativeHistorySkills(skills)
            );
        }
        else
        {
            _nativeOpponentBoard?.Dispose();
            _nativeOpponentBoard = null;
        }
    }

    private OwnedMonsterBoardPreview CreateNativeHistoryBoard(bool opponent) =>
        new(
            transform,
            BppOverlaySorting.NativeCardPreview,
            !opponent,
            (status, exception) =>
            {
                var message = status switch
                {
                    NativeMonsterBoardStatus.Loading => HistoryPanelText.LoadingPreview(),
                    NativeMonsterBoardStatus.Empty => HistoryPanelText.NoLocallyRenderableCards(),
                    NativeMonsterBoardStatus.Failed => HistoryPanelText.PreviewRendererInitFailed(),
                    _ => string.Empty,
                };
                if (opponent)
                    _uiView?.SetOpponentStatus(message);
                else
                    SetPreviewStatus(message, message.Length > 0);
                if (exception != null)
                    HistoryPanelPreviewLogWriter.ReportCardPreview(
                        new NativeCardPreviewFailure(
                            NativeCardPreviewOperation.SetUp,
                            NativeCardPreviewFailureReason.SetUpException,
                            null,
                            exception
                        )
                    );
            }
        );

    private static List<TCardInstanceItem> NativeHistoryItems(BppItemBoard board)
    {
        var planned = BppItemBoardSlotPlanner.Plan(board);
        return BppItemBoardPreviewMapper
            .Map(planned)
            .Where(card =>
                card.SocketId.HasValue
                && (int)card.SocketId.Value >= 0
                && (int)card.SocketId.Value + card.DisplaySpan <= 10
            )
            .Select(
                (card, index) =>
                    new TCardInstanceItem
                    {
                        TemplateId = card.TemplateId,
                        TemplateVersion = string.Empty,
                        InstanceId = $"history-item-{index}",
                        Tier = card.Tier,
                        SocketId = card.SocketId,
                        EnchantmentType = card.EnchantmentType,
                        Attributes = card.Attributes == null ? new() : new(card.Attributes),
                    }
            )
            .ToList();
    }

    private static List<TCardInstanceSkill> NativeHistorySkills(
        IList<PvpBattleCardSnapshot>? snapshots
    )
    {
        var skills = new List<TCardInstanceSkill>();
        foreach (var snapshot in snapshots ?? Array.Empty<PvpBattleCardSnapshot>())
        {
            if (
                snapshot.Type != ECardType.Skill
                || !Guid.TryParse(snapshot.TemplateId, out var templateId)
            )
                continue;
            var attributes = new Dictionary<ECardAttributeType, int>();
            foreach (var pair in snapshot.Attributes)
                if (Enum.TryParse<ECardAttributeType>(pair.Key, out var key))
                    attributes[key] = pair.Value;
            skills.Add(
                new TCardInstanceSkill
                {
                    TemplateId = templateId,
                    TemplateVersion = string.Empty,
                    InstanceId = $"history-skill-{skills.Count}",
                    Tier = Enum.TryParse<ETier>(snapshot.Tier, out var tier) ? tier : ETier.Bronze,
                    Attributes = attributes,
                }
            );
        }
        return skills;
    }

    private void DisposeNativeHistoryBoards()
    {
        _nativePlayerBoard?.Dispose();
        _nativeOpponentBoard?.Dispose();
        _nativePlayerBoard = _nativeOpponentBoard = null;
    }
}
