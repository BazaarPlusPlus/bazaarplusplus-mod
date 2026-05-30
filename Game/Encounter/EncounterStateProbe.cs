#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using BazaarGameShared.Domain.Cards.Encounter.Pedestal;
using BazaarGameShared.Domain.Cards.Encounter.Pedestal.Behaviors;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;

namespace BazaarPlusPlus.Game.Encounter;

/// <summary>Aggregates the three encounter-state probes into a single snapshot
/// consumers can pull on demand. One <c>PedestalState.ValidateCards()</c>
/// invocation per call, only when the player is currently on a pedestal.</summary>
internal sealed class EncounterStateProbe : IEncounterStateProbe
{
    private IReadOnlyList<string>? _cachedSelectionSet;
    private ChoiceScreenPedestalResult _cachedChoiceScreenPedestal = ChoiceScreenPedestalResult.None;

    // TEMP diagnostic state — change-detected so the per-frame probe doesn't spam the log.
    private string? _diagAppStateType;
    private IReadOnlyList<string>? _diagSelectionSet;

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

    private ChoiceScreenPedestalResult ResolveChoiceScreenPedestal(
        AppState? appState,
        BazaarGameClient.Domain.Models.RunState? runState
    )
    {
        LogChoiceScreenDiagnostics(appState, runState);

        if (appState is not ChoiceState)
        {
            _cachedSelectionSet = null;
            _cachedChoiceScreenPedestal = ChoiceScreenPedestalResult.None;
            return ChoiceScreenPedestalResult.None;
        }

        var selectionSet = runState?.SelectionSet;
        if (ReferenceEquals(selectionSet, _cachedSelectionSet))
            return _cachedChoiceScreenPedestal;

        if (!Data.IsManagerCreated())
        {
            _cachedSelectionSet = null;
            _cachedChoiceScreenPedestal = ChoiceScreenPedestalResult.None;
            return ChoiceScreenPedestalResult.None;
        }

        var manager = Data.GetStatic();
        var result = ChoiceScreenPedestalResolver.ResolveDetailed(selectionSet, manager.GetCardById);

        _cachedSelectionSet = selectionSet;
        _cachedChoiceScreenPedestal = result;
        return result;
    }

    // TEMP diagnostic (remove once root cause is confirmed). Dumps the live
    // AppState + SelectionSet -> template -> pedestal behavior -> specific
    // enchant type so we can see which link of the auto-detection chain breaks
    // at runtime, and what enchant each pedestal id actually offers. Logs only
    // when AppState type or the SelectionSet instance changes, so the per-frame
    // probe does not spam.
    private void LogChoiceScreenDiagnostics(
        AppState? appState,
        BazaarGameClient.Domain.Models.RunState? runState
    )
    {
        try
        {
            var appStateType = appState?.GetType().Name ?? "<null>";
            var selectionSet = runState?.SelectionSet;
            if (
                appStateType == _diagAppStateType
                && ReferenceEquals(selectionSet, _diagSelectionSet)
            )
            {
                return;
            }

            _diagAppStateType = appStateType;
            _diagSelectionSet = selectionSet;

            var inChoiceState = appState is ChoiceState;
            var managerCreated = Data.IsManagerCreated();
            var sb = new StringBuilder();
            sb.Append("[ChoiceDiag] appState=")
                .Append(appStateType)
                .Append(" inChoiceState=")
                .Append(inChoiceState)
                .Append(" managerCreated=")
                .Append(managerCreated)
                .Append(" selectionSet=");

            if (selectionSet == null)
            {
                sb.Append("<null>");
            }
            else
            {
                sb.Append('[').Append(selectionSet.Count).Append(']');
                if (inChoiceState && managerCreated && selectionSet.Count > 0)
                {
                    var manager = Data.GetStatic();
                    foreach (var id in selectionSet)
                    {
                        sb.Append("\n  - ").Append(string.IsNullOrEmpty(id) ? "<empty>" : id);
                        if (!Guid.TryParse(id, out var guid))
                        {
                            sb.Append(" (not-a-guid)");
                            continue;
                        }

                        var template = manager.GetCardById(guid);
                        if (template == null)
                        {
                            sb.Append(" -> GetCardById=null");
                            continue;
                        }

                        sb.Append(" -> ").Append(template.GetType().Name);
                        if (template is TCardEncounterPedestal pedestal)
                        {
                            var behavior = pedestal.Behavior;
                            sb.Append(" / behavior=").Append(behavior?.GetType().Name ?? "<null>");
                            if (behavior is TPedestalBehaviorEnchant enchant)
                                sb.Append(" / enchant=").Append(enchant.Enchantment);
                            else if (behavior is TPedestalBehaviorEnchantRandom randomEnchant)
                                sb.Append(" / enchantRandom=")
                                    .Append(randomEnchant.Enchantments?.Count ?? 0);
                        }
                    }
                }
            }

            BppLog.Info("Encounter", sb.ToString());
        }
        catch (Exception ex)
        {
            BppLog.Info("Encounter", $"ChoiceDiag failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
