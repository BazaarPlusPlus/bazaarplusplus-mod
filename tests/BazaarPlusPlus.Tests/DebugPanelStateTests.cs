using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class DebugPanelStateTests
{
    [Fact]
    public void Defaults_to_summary_section_and_section_view()
    {
        var state = new DebugPanelState();

        Assert.Equal(DebugPanelSection.Summary, state.ActiveSection);
        Assert.False(state.ShowAllSections);
    }

    [Fact]
    public void Can_switch_sections_directly()
    {
        var state = new DebugPanelState();

        state.SelectSection(DebugPanelSection.Encounters);

        Assert.Equal(DebugPanelSection.Encounters, state.ActiveSection);
    }

    [Fact]
    public void Toggle_view_mode_switches_between_single_and_all_sections()
    {
        var state = new DebugPanelState();

        state.ToggleViewMode();
        Assert.True(state.ShowAllSections);

        state.ToggleViewMode();
        Assert.False(state.ShowAllSections);
    }

    [Fact]
    public void Reset_restores_defaults_and_clears_expanded_encounters()
    {
        var state = new DebugPanelState();
        state.SelectSection(DebugPanelSection.Run);
        state.ToggleViewMode();
        state.ToggleEncounter("map:one");

        state.Reset();

        Assert.Equal(DebugPanelSection.Summary, state.ActiveSection);
        Assert.False(state.ShowAllSections);
        Assert.False(state.IsEncounterExpanded("map:one"));
    }

    [Fact]
    public void Encounter_toggle_flips_individual_keys_only()
    {
        var state = new DebugPanelState();

        state.ToggleEncounter("map:one");
        Assert.True(state.IsEncounterExpanded("map:one"));
        Assert.False(state.IsEncounterExpanded("choice:two"));

        state.ToggleEncounter("map:one");
        Assert.False(state.IsEncounterExpanded("map:one"));
    }
}
