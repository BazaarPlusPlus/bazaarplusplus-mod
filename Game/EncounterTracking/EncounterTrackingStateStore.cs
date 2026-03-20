#nullable enable
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.EncounterTracking;

internal sealed class EncounterTrackingStateStore : IEncounterSelectionQuery
{
    private List<RunInfo.CardInfo>? _availableEncounters;
    private List<RunInfo.CardInfo>? _currentEncounterChoices;
    private List<RunInfo.MonsterPreview>? _encounterMonsterPreviews;

    public void UpdateMapEncounters(
        List<RunInfo.CardInfo> availableEncounters,
        List<RunInfo.MonsterPreview>? encounterMonsterPreviews
    )
    {
        _availableEncounters = availableEncounters;
        _currentEncounterChoices = null;
        _encounterMonsterPreviews = encounterMonsterPreviews;
    }

    public void UpdateEncounterChoices(
        List<RunInfo.CardInfo> currentEncounterChoices,
        List<RunInfo.MonsterPreview>? encounterMonsterPreviews
    )
    {
        _availableEncounters = null;
        _currentEncounterChoices = currentEncounterChoices;
        _encounterMonsterPreviews = encounterMonsterPreviews;
    }

    public void Clear()
    {
        _availableEncounters = null;
        _currentEncounterChoices = null;
        _encounterMonsterPreviews = null;
    }

    public EncounterSelectionSnapshot GetSnapshot()
    {
        return new EncounterSelectionSnapshot
        {
            AvailableEncounters = CloneCards(_availableEncounters),
            CurrentEncounterChoices = CloneCards(_currentEncounterChoices),
            EncounterMonsterPreviews = CloneMonsterPreviews(_encounterMonsterPreviews),
        };
    }

    private static List<RunInfo.CardInfo>? CloneCards(List<RunInfo.CardInfo>? cards)
    {
        return cards == null ? null : new List<RunInfo.CardInfo>(cards);
    }

    private static List<RunInfo.MonsterPreview>? CloneMonsterPreviews(
        List<RunInfo.MonsterPreview>? previews
    )
    {
        return previews == null ? null : new List<RunInfo.MonsterPreview>(previews);
    }
}
