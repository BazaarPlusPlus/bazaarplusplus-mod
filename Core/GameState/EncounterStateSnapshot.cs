#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.Encounter;

namespace BazaarPlusPlus.Core.GameState;

internal readonly struct EncounterStateSnapshot
{
    public string? CurrentEncounterId { get; init; }
    public string? CurrentEncounterType { get; init; }

    /// <summary>Template IDs the game's interaction filter currently restricts
    /// SelectItem to. Empty when no upgrade/enchant target-selection is active.</summary>
    public IReadOnlyList<string> InteractionFilterTemplateIds { get; init; }

    /// <summary>Owned-card InstanceIds the active PedestalState would accept.
    /// Empty when not on a pedestal or no card satisfies the criteria.
    /// HashSet&lt;string&gt; (concrete) instead of IReadOnlySet&lt;string&gt; because
    /// netstandard2.1 does not expose the latter; callers treat it as read-only.</summary>
    public HashSet<string> PedestalEligibleInstanceIds { get; init; }

    /// <summary>The kind of pedestal currently offered on the choice screen,
    /// or <see cref="ChoiceScreenPedestalKind.None"/> when the player is not
    /// in ChoiceState or the offered SelectionSet contains no relevant pedestal.</summary>
    public ChoiceScreenPedestalKind ChoiceScreenPedestalKind { get; init; }

    public static EncounterStateSnapshot Empty { get; } = new()
    {
        CurrentEncounterId = null,
        CurrentEncounterType = null,
        InteractionFilterTemplateIds = Array.Empty<string>(),
        PedestalEligibleInstanceIds = new HashSet<string>(),
        ChoiceScreenPedestalKind = ChoiceScreenPedestalKind.None,
    };
}
