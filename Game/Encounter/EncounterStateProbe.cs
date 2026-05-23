#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.Core.GameState;
using TheBazaar;

namespace BazaarPlusPlus.Game.Encounter;

/// <summary>Aggregates the three encounter-state probes into a single snapshot
/// consumers can pull on demand. One <c>PedestalState.ValidateCards()</c>
/// invocation per call, only when the player is currently on a pedestal.</summary>
internal sealed class EncounterStateProbe : IEncounterStateProbe
{
    private IReadOnlyList<string>? _cachedSelectionSet;
    private ChoiceScreenPedestalKind _cachedChoiceScreenPedestalKind;

    public EncounterStateSnapshot GetCurrent()
    {
        var runState = Data.CurrentState;
        var appState = AppState.CurrentState;

        var currentEncounterId = runState?.CurrentEncounterId;
        var currentEncounterType = EncounterTypeResolver.Resolve(currentEncounterId);

        var filter = InteractionFilterProbe.ReadCurrentFilter();

        var pedestalEligible = appState is PedestalState ped
            ? PedestalEligibilityProbe.ReadEligibleInstanceIds(ped)
            : new HashSet<string>();

        var choiceScreenKind = ResolveChoiceScreenPedestalKind(appState, runState);

        return new EncounterStateSnapshot
        {
            CurrentEncounterId = currentEncounterId,
            CurrentEncounterType = currentEncounterType,
            InteractionFilterTemplateIds = filter,
            PedestalEligibleInstanceIds = pedestalEligible,
            ChoiceScreenPedestalKind = choiceScreenKind,
        };
    }

    private ChoiceScreenPedestalKind ResolveChoiceScreenPedestalKind(
        AppState? appState,
        BazaarGameClient.Domain.Models.RunState? runState
    )
    {
        if (appState is not ChoiceState)
        {
            _cachedSelectionSet = null;
            _cachedChoiceScreenPedestalKind = ChoiceScreenPedestalKind.None;
            return ChoiceScreenPedestalKind.None;
        }

        var selectionSet = runState?.SelectionSet;
        if (ReferenceEquals(selectionSet, _cachedSelectionSet))
            return _cachedChoiceScreenPedestalKind;

        if (!Data.IsManagerCreated())
        {
            _cachedSelectionSet = null;
            _cachedChoiceScreenPedestalKind = ChoiceScreenPedestalKind.None;
            return ChoiceScreenPedestalKind.None;
        }

        var manager = Data.GetStatic().GetAwaiter().GetResult();
        var kind = ChoiceScreenPedestalResolver.Resolve(selectionSet, manager.GetCardById);

        _cachedSelectionSet = selectionSet;
        _cachedChoiceScreenPedestalKind = kind;
        return kind;
    }
}
