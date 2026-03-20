#nullable enable
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.EncounterTracking;

internal sealed class EncounterSelectionSnapshot
{
    public List<RunInfo.CardInfo>? AvailableEncounters { get; set; }

    public List<RunInfo.CardInfo>? CurrentEncounterChoices { get; set; }

    public List<RunInfo.MonsterPreview>? EncounterMonsterPreviews { get; set; }
}
