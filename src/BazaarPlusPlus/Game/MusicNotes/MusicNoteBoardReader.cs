#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Socket;
using BazaarGameShared.Domain.Core.Types;
using TheBazaar;

namespace BazaarPlusPlus.Game.MusicNotes;

internal enum MusicNoteSocketBadgeKind
{
    /// <summary>A music note occupies this socket.</summary>
    Active,

    /// <summary>The socket is empty (or holds a non-note effect) but its letter is fixed by
    /// the anchor rule — a note placed here will be this letter.</summary>
    Implied,
}

internal readonly struct MusicNoteSocketBadge
{
    internal MusicNoteSocketBadge(
        int socketIndex,
        MusicNoteSocketBadgeKind kind,
        EMusicNote letter,
        TCardMusicNoteSocketEffect? placedTemplate
    )
    {
        SocketIndex = socketIndex;
        Kind = kind;
        Letter = letter;
        PlacedTemplate = placedTemplate;
    }

    internal int SocketIndex { get; }
    internal MusicNoteSocketBadgeKind Kind { get; }
    internal EMusicNote Letter { get; }
    internal TCardMusicNoteSocketEffect? PlacedTemplate { get; }
}

/// <summary>
/// Main-thread read of the player board's music-note state into badge models. Placed notes come
/// from the same source the board visuals use (player-owned SocketEffect entities keyed by
/// LeftSocketId); locked sockets come from the player's socket container. Returns null whenever
/// the board is not readable or no anchor note exists — this is a per-frame path, so failures
/// stay silent and render as "nothing to show".
/// </summary>
internal static class MusicNoteBoardReader
{
    internal static List<MusicNoteSocketBadge>? Read()
    {
        try
        {
            return ReadCore();
        }
        catch
        {
            // Touches game statics per frame; never log here. Hide if unsure.
            return null;
        }
    }

    private static List<MusicNoteSocketBadge>? ReadCore()
    {
        var player = Data.Run?.Player;
        var container = player?.Socket?.Container;
        if (player == null || container == null)
            return null;

        var socketCount = container.Sockets?.Length ?? 0;
        if (socketCount <= 0)
            return null;

        var placedTemplates = new TCardMusicNoteSocketEffect?[socketCount];
        foreach (var entity in Data.Entities.Values)
        {
            if (
                entity is SocketEffect socketEffect
                && ReferenceEquals(socketEffect.Owner, player)
                && socketEffect.Section == EInventorySection.Hand
                && socketEffect.LeftSocketId is EContainerSocketId socketId
                && (int)socketId >= 0
                && (int)socketId < socketCount
                && socketEffect.Template is TCardMusicNoteSocketEffect noteTemplate
            )
            {
                placedTemplates[(int)socketId] = noteTemplate;
            }
        }

        var placedLetters = new int?[socketCount];
        for (var i = 0; i < socketCount; i++)
        {
            if (placedTemplates[i] is TCardMusicNoteSocketEffect note)
                placedLetters[i] = (int)note.MusicNote;
        }

        var letters = MusicNoteLetterMath.InferLetters(placedLetters);

        List<MusicNoteSocketBadge>? badges = null;
        for (var i = 0; i < socketCount; i++)
        {
            if (letters[i] is not int letter || container.IsSocketLocked(i))
                continue;

            badges ??= new List<MusicNoteSocketBadge>(socketCount);
            var placed = placedTemplates[i];
            badges.Add(
                new MusicNoteSocketBadge(
                    i,
                    placed != null
                        ? MusicNoteSocketBadgeKind.Active
                        : MusicNoteSocketBadgeKind.Implied,
                    (EMusicNote)letter,
                    placed
                )
            );
        }

        return badges;
    }
}
