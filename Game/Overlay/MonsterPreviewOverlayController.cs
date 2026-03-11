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
        _board = new MonsterPreviewBoard("MonsterPreviewBoard", _factory);
        _board.SetLayout(_layout);
    }

    private void LateUpdate()
    {
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
    }

    public void SetLayout(PreviewBoardLayout layout)
    {
        _layout = layout ?? new PreviewBoardLayout();
        _board?.SetLayout(_layout);
    }

    public void SetCards(IReadOnlyList<PreviewCardSpec> cards)
    {
        _cards.Clear();
        if (cards != null)
            _cards.AddRange(CloneCards(cards));

        QueueSync();
    }

    public void ClearCards()
    {
        _cards.Clear();
        QueueSync();
    }

    public void SetVisible(bool visible)
    {
        if (_visible == visible)
            return;

        _visible = visible;
        _syncVersion++;

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
            await _board.RebuildAsync(snapshot, () => version != _syncVersion || !_visible);

            if (version == _syncVersion && _visible)
                _syncPending = false;
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

    private void QueueSync()
    {
        _syncVersion++;
        _syncPending = true;
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
                Enchant = card.Enchant,
                Attributes = card.Attributes != null
                    ? new Dictionary<int, int>(card.Attributes)
                    : new Dictionary<int, int>(),
            })
            .ToList();
    }
}
