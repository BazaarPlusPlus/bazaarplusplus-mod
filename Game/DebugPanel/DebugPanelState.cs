using System.Collections.Generic;

namespace BazaarPlusPlus;

internal enum DebugPanelSection
{
    Summary,
    Preview,
    Run,
    Encounters,
}

internal sealed class DebugPanelState
{
    private readonly HashSet<string> _expandedEncounterKeys = new HashSet<string>();

    public DebugPanelSection ActiveSection { get; private set; } = DebugPanelSection.Summary;

    public bool ShowAllSections { get; private set; }

    public void SelectSection(DebugPanelSection section)
    {
        ActiveSection = section;
    }

    public void ToggleViewMode()
    {
        ShowAllSections = !ShowAllSections;
    }

    public void ShowOnlySelectedSection()
    {
        ShowAllSections = false;
    }

    public void Reset()
    {
        ActiveSection = DebugPanelSection.Summary;
        ShowAllSections = false;
        _expandedEncounterKeys.Clear();
    }

    public void ToggleEncounter(string encounterKey)
    {
        if (string.IsNullOrEmpty(encounterKey))
            return;

        if (!_expandedEncounterKeys.Add(encounterKey))
            _expandedEncounterKeys.Remove(encounterKey);
    }

    public bool IsEncounterExpanded(string encounterKey)
    {
        return !string.IsNullOrEmpty(encounterKey) && _expandedEncounterKeys.Contains(encounterKey);
    }
}
