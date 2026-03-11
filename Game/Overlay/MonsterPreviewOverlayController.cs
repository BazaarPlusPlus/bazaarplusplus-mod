#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class MonsterPreviewOverlayController : MonoBehaviour
{
    private readonly List<PreviewCardSpec> _cards = new List<PreviewCardSpec>();

    private MonsterPreviewBoard _board;
    private IPreviewCardFactory _factory;
    private IOverlayAnchorSource _anchorSource;
    private PreviewBoardLayout _layout;
    private bool _visible;
    private bool _syncInFlight;
    private bool _syncPending;
    private int _syncVersion;

    public bool Visible => _visible;

    private void Awake()
    {
        _factory = new MonsterPreviewCardFactory();
        _layout = new PreviewBoardLayout();
        EnsureBoard();
    }

    private void LateUpdate()
    {
        EnsureBoard();
        if (_board == null)
            return;

        if (!_visible)
        {
            _board.SetVisible(false);
            return;
        }

        if (_anchorSource != null && _anchorSource.TryGetAnchor(out var position, out var rotation))
        {
            _board.UpdateAnchor(position, rotation);
            _board.SetVisible(true);
        }
        else
        {
            _board.SetVisible(false);
        }

        if (_syncPending && !_syncInFlight)
        {
            _syncInFlight = true;
            var version = _syncVersion;
            var snapshot = CloneCards(_cards);
            _ = SyncCardsAsync(version, snapshot);
        }
    }

    public void SetAnchorSource(IOverlayAnchorSource anchorSource)
    {
        _anchorSource = anchorSource;
        ModState.Logger?.LogInfo(
            $"[MonsterPreviewOverlayController] Anchor source set: {anchorSource?.GetType().Name ?? "null"}"
        );
    }

    public void SetLayout(PreviewBoardLayout layout)
    {
        _layout = layout ?? new PreviewBoardLayout();
        _board?.SetLayout(_layout);
        ModState.Logger?.LogDebug(
            $"[MonsterPreviewOverlayController] Layout updated: size={_layout.BoardSize}, offset={_layout.LocalOffset}, spacing={_layout.CardSpacing}, scale={_layout.CardScale}"
        );
    }

    public void SetCards(IReadOnlyList<PreviewCardSpec> cards)
    {
        _cards.Clear();
        if (cards != null)
            _cards.AddRange(CloneCards(cards));

        ModState.Logger?.LogInfo(
            $"[MonsterPreviewOverlayController] SetCards count={_cards.Count}, visible={_visible}"
        );
        QueueSync();
    }

    public void ClearCards()
    {
        _cards.Clear();
        ModState.Logger?.LogInfo("[MonsterPreviewOverlayController] ClearCards");
        QueueSync();
    }

    public void SetVisible(bool visible)
    {
        if (_visible == visible)
        {
            ModState.Logger?.LogDebug(
                $"[MonsterPreviewOverlayController] SetVisible ignored: already {visible}"
            );
            return;
        }

        _visible = visible;
        _syncVersion++;
        ModState.Logger?.LogInfo(
            $"[MonsterPreviewOverlayController] Visible={_visible}, syncVersion={_syncVersion}"
        );

        if (!_visible)
        {
            _board?.Clear();
            _board?.SetVisible(false);
            _syncPending = false;
            return;
        }

        _syncPending = true;
    }

    public void Refresh()
    {
        QueueSync();
    }

    private async Task SyncCardsAsync(int version, IReadOnlyList<PreviewCardSpec> snapshot)
    {
        try
        {
            EnsureBoard();
            if (_board == null)
                return;

            ModState.Logger?.LogInfo(
                $"[MonsterPreviewOverlayController] Sync start: version={version}, cards={snapshot.Count}, visible={_visible}"
            );
            await _board.RebuildAsync(snapshot, () => version != _syncVersion || !_visible || _board == null || !_board.IsAlive);

            if (version == _syncVersion && _visible)
            {
                _syncPending = false;
                ModState.Logger?.LogInfo(
                    $"[MonsterPreviewOverlayController] Sync complete: version={version}, cards={snapshot.Count}"
                );
            }
            else
            {
                ModState.Logger?.LogDebug(
                    $"[MonsterPreviewOverlayController] Sync skipped completion: requestedVersion={version}, currentVersion={_syncVersion}, visible={_visible}"
                );
            }
        }
        catch (Exception ex)
        {
            ModState.Logger?.LogWarning($"[MonsterPreviewOverlayController] Sync failed: {ex}");
        }
        finally
        {
            _syncInFlight = false;
        }
    }

    private void EnsureBoard()
    {
        if (_board != null && _board.IsAlive)
            return;

        _board?.Dispose();
        _board = new MonsterPreviewBoard("MonsterPreviewBoard", _factory);
        _board.SetLayout(_layout ?? new PreviewBoardLayout());
        _syncPending = true;
        ModState.Logger?.LogInfo("[MonsterPreviewOverlayController] Recreated preview board");
    }

    private void QueueSync()
    {
        _syncVersion++;
        _syncPending = true;
        ModState.Logger?.LogDebug(
            $"[MonsterPreviewOverlayController] QueueSync version={_syncVersion}, pending={_syncPending}"
        );
    }

    private void OnDestroy()
    {
        _board?.Dispose();
        _board = null;
    }

    private static List<PreviewCardSpec> CloneCards(IReadOnlyList<PreviewCardSpec> cards)
    {
        return cards
            .Select(card => new PreviewCardSpec
            {
                TemplateId = card.TemplateId,
                Tier = card.Tier,
                SourceName = card.SourceName,
                Enchant = card.Enchant,
                Size = card.Size,
                Attributes = card.Attributes != null
                    ? new Dictionary<int, int>(card.Attributes)
                    : new Dictionary<int, int>(),
            })
            .ToList();
    }
}
