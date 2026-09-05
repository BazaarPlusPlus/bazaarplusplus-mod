#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Socket;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Input;
using HarmonyLib;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.MusicNotes;

// Preview all unlocked sockets, with brighter native effects for placed, matching notes.
internal sealed class NativeMusicNotePreviewController : MonoBehaviour
{
    private static readonly System.Reflection.FieldInfo? CatalogField = AccessTools.Field(
        typeof(BoardManager),
        "_socketEffectVfxCatalog"
    );
    private BoardManager? _board;
    private NativeMusicNotePreviewLayer? _implied;
    private NativeMusicNotePreviewLayer? _placed;
    private NativeMusicNotePreviewLayer? _active;
    private float _nextRefresh;
    private readonly NativeMusicNoteVisualSuppression _nativeVisuals = new();

    private void Update()
    {
        var board = Singleton<BoardManager>.Instance;
        if (board != _board)
        {
            Release();
            _board = board;
        }

        if (
            board == null
            || !board.isActiveAndEnabled
            || !Application.isFocused
            || !BppHotkeyService.IsActive(BppHotkeyActionId.HoldUpgradePreview)
            || Data.Run?.Player?.Socket?.Container == null
            || Data.IsInCombat
            || AppState.CurrentState is ReplayState
            || board.IsRecapViewOpen
            || Data.NewDayTransitionController?.IsActive == true
            || SearchOpponentTransition.IsInTransition
        )
        {
            Clear();
            return;
        }

        if (Time.unscaledTime < _nextRefresh)
            return;
        _nextRefresh = Time.unscaledTime + 0.15f;

        if (_implied == null)
        {
            if (
                CatalogField?.GetValue(board) is not SocketEffectVfxCatalog catalog
                || catalog == null
            )
                return;
            // Do not borrow BoardManager's presenter: native card hover clears that instance.
            _implied = new NativeMusicNotePreviewLayer(catalog, 0.7f);
            _placed = new NativeMusicNotePreviewLayer(catalog, 0.9f);
            _active = new NativeMusicNotePreviewLayer(catalog, 1f);
        }

        _implied.Placements.Clear();
        _placed!.Placements.Clear();
        _active!.Placements.Clear();
        var player = Data.Run.Player;
        var container = player.Socket.Container;
        var placed = new int?[container.Sockets.Length];
        var active = new bool[placed.Length];
        var nativeNotes = new SocketEffectController?[placed.Length];
        foreach (var entity in Data.Entities.Values)
        {
            if (
                entity is SocketEffect effect
                && ReferenceEquals(effect.Owner, player)
                && effect.Section == EInventorySection.Hand
                && effect.LeftSocketId is EContainerSocketId socket
                && (int)socket >= 0
                && (int)socket < placed.Length
                && effect.Template is TCardMusicNoteSocketEffect note
            )
            {
                placed[(int)socket] = (int)note.MusicNote;
                nativeNotes[(int)socket] =
                    Data.CardAndSkillLookup.GetCardController(effect) as SocketEffectController;
                var hand = player.Hand?.Container?.Sockets;
                var item =
                    hand != null && (int)socket < hand.Length ? hand[(int)socket] as ICard : null;
                active[(int)socket] = MusicNoteActivation.IsSatisfied(note, item, Data.Run, effect);
            }
        }
        var letters = MusicNoteSocketInference.Resolve(placed);
        _nativeVisuals.BeginRefresh();
        for (var i = 0; i < letters.Length; i++)
        {
            if (letters[i] is not int letter || container.IsSocketLocked(i))
                continue;
            var target = board.GetItemSocketController((EContainerSocketId)i, ECombatantId.Player);
            if (target != null && target.gameObject.activeInHierarchy)
            {
                var layer =
                    !placed[i].HasValue ? _implied
                    : active[i] ? _active
                    : _placed;
                layer.Placements.Add(
                    new MusicNoteSpawnPlacement((EMusicNote)letter, target.transform)
                );
                _nativeVisuals.Suppress(nativeNotes[i]);
            }
        }
        _nativeVisuals.EndRefresh();

        _implied.Refresh();
        _placed.Refresh();
        _active.Refresh();
    }

    private void LateUpdate()
    {
        _implied?.ApplyBrightness();
        _placed?.ApplyBrightness();
        _active?.ApplyBrightness();
    }

    private void Clear()
    {
        _implied?.Clear();
        _placed?.Clear();
        _active?.Clear();
        _nativeVisuals.Restore();
        _nextRefresh = 0f;
    }

    private void Release()
    {
        _implied?.Dispose();
        _placed?.Dispose();
        _active?.Dispose();
        _nativeVisuals.Restore();
        _implied = null;
        _placed = null;
        _active = null;
        _board = null;
        _nextRefresh = 0f;
    }

    private void OnDisable() => Release();

    private void OnDestroy() => Release();
}
