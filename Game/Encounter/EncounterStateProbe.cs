#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core;
using BazaarPlusPlus.Core.GameState;
using TheBazaar;

namespace BazaarPlusPlus.Game.Encounter;

/// <summary>Aggregates the encounter-state probes into a single snapshot consumers can
/// pull on demand. One <c>PedestalState.ValidateCards()</c> invocation per call, only
/// when the player is currently on a pedestal.</summary>
internal sealed class EncounterStateProbe : IEncounterStateProbe
{
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

        var choice = ResolveChoiceScreenPedestal(appState, runState);

        return new EncounterStateSnapshot
        {
            CurrentEncounterId = currentEncounterId,
            CurrentEncounterType = currentEncounterType,
            InteractionFilterTemplateIds = filter,
            PedestalEligibleInstanceIds = pedestalEligible,
            ChoiceScreenPedestalKind = choice.Kind,
            ChoiceScreenEnchantmentTypeNames = choice.EnchantmentTypeNames,
        };
    }

    private static ChoiceScreenPedestalResult ResolveChoiceScreenPedestal(
        AppState? appState,
        BazaarGameClient.Domain.Models.RunState? runState
    )
    {
        if (appState is not ChoiceState)
            return ChoiceScreenPedestalResult.None;

        return ChoiceScreenPedestalResolver.ResolveDetailed(
            runState?.SelectionSet,
            ResolveTemplateId
        );
    }

    // SelectionSet entries are live instance ids (e.g. "ped_XtbgKux"); resolve each
    // through Data.Entities to its stable template id, which PedestalEnchantCatalog
    // classifies. The client cannot read the obfuscated pedestal Behavior, so the
    // template id is the only stable handle. Falls through to a raw template GUID.
    private static Guid? ResolveTemplateId(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        var entities = Data.Entities;
        if (
            entities != null
            && entities.TryGetValue(new InstanceId(id), out var card)
            && card != null
        )
        {
            return card.TemplateId;
        }

        return Guid.TryParse(id, out var guid) ? guid : null;
    }
}
