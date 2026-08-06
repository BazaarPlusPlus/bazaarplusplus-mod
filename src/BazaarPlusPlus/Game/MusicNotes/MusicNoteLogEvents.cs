#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.MusicNotes;

[BppLogEventSource]
internal static class MusicNoteLogEvents
{
    internal static readonly BppLogFieldDefinition BadgeVisualChangedSocket = Public(
        0,
        "socket",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition BadgeVisualChangedLetter = Public(
        1,
        "letter",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition BadgeVisualChangedVisual = Public(
        2,
        "visual",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition BadgeVisualChangedBoosted = Public(
        3,
        "boosted",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition BadgeVisualChangedHoverFit = Public(
        4,
        "hover_fit",
        BppLogCardinality.Low
    );

    /// <summary>
    /// A socket badge resolved to a different visual than last rendered — placed/boosted
    /// transitions and hover fit/miss verdicts. Transition-gated, so the strip logs once per
    /// state change, not per frame; the first Shift-hold snapshots the whole strip.
    /// </summary>
    internal static readonly BppLogEventDefinition BadgeVisualChanged = new(
        BppLogFeatureScope.MusicNotes,
        "music_notes.badge.visual_changed",
        [
            BadgeVisualChangedSocket,
            BadgeVisualChangedLetter,
            BadgeVisualChangedVisual,
            BadgeVisualChangedBoosted,
            BadgeVisualChangedHoverFit,
        ]
    );

    private static BppLogFieldDefinition Public(
        int order,
        string name,
        BppLogCardinality cardinality
    ) => new(order, name, BppLogFieldPrivacy.Public, BppLogCorrelationPolicy.None, cardinality);
}
