#nullable enable
using BazaarPlusPlus.Game.EncounterTracking;

namespace BazaarPlusPlus.Core.Events;

internal sealed class SelectionObserved
{
    public string? StateName { get; set; }

    public EncounterSelectionSnapshot Snapshot { get; set; } = new();
}
