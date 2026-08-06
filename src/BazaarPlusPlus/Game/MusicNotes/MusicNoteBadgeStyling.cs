#nullable enable
namespace BazaarPlusPlus.Game.MusicNotes;

/// <summary>How the card under the pointer relates to a socket's note gate.</summary>
internal enum MusicNoteHoverFit
{
    /// <summary>No card hovered, or the socket's gate is unknown (catalog still loading).</summary>
    None,

    /// <summary>The hovered card satisfies this socket's note gate.</summary>
    Fits,

    /// <summary>The hovered card fails this socket's note gate.</summary>
    Misses,
}

/// <summary>Resolved visual for one badge; the overlay maps each to concrete colors.</summary>
internal enum MusicNoteBadgeVisual
{
    /// <summary>Implied letter, no hover filter: dim neutral chip.</summary>
    Ghost,

    /// <summary>Placed note, no hover filter: soft accent plate.</summary>
    Plate,

    /// <summary>Placed note whose occupant passes the gate: brightest accent + match ring.</summary>
    Boosted,

    /// <summary>Implied letter the hovered card fits: lit letter + match ring.</summary>
    GhostHighlighted,

    /// <summary>Placed note the hovered card fits: plate + match ring.</summary>
    PlateHighlighted,

    /// <summary>Implied letter the hovered card misses: faded out of consideration.</summary>
    GhostDimmed,

    /// <summary>Placed note the hovered card misses: faded out of consideration.</summary>
    PlateDimmed,
}

/// <summary>
/// Pure badge-state resolution: combines note presence, live boost, and the hover filter into
/// one visual. Boosted is hover-immune — an actually-working note never dims or restyles under
/// a hover comparison, so live board state always outranks the what-if layer. This file stays
/// free of Unity/game types so the precedence rule is testable in a plain test project.
/// </summary>
internal static class MusicNoteBadgeStyling
{
    internal static MusicNoteBadgeVisual Resolve(
        bool notePlaced,
        bool isBoosted,
        MusicNoteHoverFit hoverFit
    )
    {
        if (notePlaced && isBoosted)
            return MusicNoteBadgeVisual.Boosted;

        return hoverFit switch
        {
            MusicNoteHoverFit.Fits => notePlaced
                ? MusicNoteBadgeVisual.PlateHighlighted
                : MusicNoteBadgeVisual.GhostHighlighted,
            MusicNoteHoverFit.Misses => notePlaced
                ? MusicNoteBadgeVisual.PlateDimmed
                : MusicNoteBadgeVisual.GhostDimmed,
            _ => notePlaced ? MusicNoteBadgeVisual.Plate : MusicNoteBadgeVisual.Ghost,
        };
    }
}
